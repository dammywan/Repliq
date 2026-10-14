using System.Text.Json;
using Microsoft.Data.Sqlite;
using Repliq.Core.Diagnostics;
using Repliq.Core.Models;

namespace Repliq.Storage.History;

public interface IHistoryRepository
{
    Task InitializeAsync();
    Task RecordRunAsync(WorkflowRunRecord record);
    Task<IReadOnlyList<WorkflowRunRecord>> GetRecentRunsAsync(int limit = 50);
    Task<WorkflowRunRecord?> GetRunByIdAsync(string runId);
    Task ClearHistoryAsync();
}

public sealed class SqliteHistoryRepository : IHistoryRepository
{
    private readonly string _connectionString;
    private readonly int _maxRetentionCount;

    public SqliteHistoryRepository(string? dbPath = null, int maxRetentionCount = 500)
    {
        _maxRetentionCount = maxRetentionCount;
        var path = dbPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Repliq",
            "history.db");

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            CREATE TABLE IF NOT EXISTS RunHistory (
                RunId TEXT PRIMARY KEY,
                WorkflowId TEXT NOT NULL,
                WorkflowName TEXT NOT NULL,
                StartedUtc TEXT NOT NULL,
                EndedUtc TEXT,
                FinalState TEXT NOT NULL,
                TotalActions INTEGER NOT NULL,
                ExecutedActions INTEGER NOT NULL,
                LastCheckpointId TEXT,
                FailedActionId TEXT,
                SafeErrorMessage TEXT,
                ErrorCode TEXT,
                TargetProcess TEXT,
                StepResultsJson TEXT
            );
            CREATE INDEX IF NOT EXISTS IX_RunHistory_StartedUtc ON RunHistory(StartedUtc DESC);
        ";

        using var cmd = new SqliteCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RecordRunAsync(WorkflowRunRecord record)
    {
        if (record is null) throw new ArgumentNullException(nameof(record));

        await InitializeAsync();

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        // Serialize step results, redacting any sensitive strings
        var sanitizedSteps = record.StepResults.Select(s => new
        {
            s.ActionId,
            s.ActionType,
            s.IsSuccess,
            Status = s.Status.ToString(),
            s.DurationMs,
            SafeMessage = RedactionService.RedactText(s.SafeMessage),
            s.ErrorCode,
            s.ExecutedAt
        });
        var stepJson = JsonSerializer.Serialize(sanitizedSteps);

        var sql = @"
            INSERT INTO RunHistory 
            (RunId, WorkflowId, WorkflowName, StartedUtc, EndedUtc, FinalState, TotalActions, ExecutedActions, LastCheckpointId, FailedActionId, SafeErrorMessage, ErrorCode, TargetProcess, StepResultsJson)
            VALUES 
            (@RunId, @WorkflowId, @WorkflowName, @StartedUtc, @EndedUtc, @FinalState, @TotalActions, @ExecutedActions, @LastCheckpointId, @FailedActionId, @SafeErrorMessage, @ErrorCode, @TargetProcess, @StepResultsJson);
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@RunId", record.RunId);
        cmd.Parameters.AddWithValue("@WorkflowId", record.WorkflowId);
        cmd.Parameters.AddWithValue("@WorkflowName", record.WorkflowName);
        cmd.Parameters.AddWithValue("@StartedUtc", record.StartedUtc.ToString("o"));
        cmd.Parameters.AddWithValue("@EndedUtc", (object?)record.EndedUtc?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FinalState", record.FinalState.ToString());
        cmd.Parameters.AddWithValue("@TotalActions", record.TotalActions);
        cmd.Parameters.AddWithValue("@ExecutedActions", record.ExecutedActions);
        cmd.Parameters.AddWithValue("@LastCheckpointId", (object?)record.LastCheckpointId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@FailedActionId", (object?)record.FailedActionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@SafeErrorMessage", (object?)RedactionService.RedactText(record.SafeErrorMessage) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ErrorCode", (object?)record.ErrorCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TargetProcess", (object?)record.TargetProcess ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@StepResultsJson", stepJson);

        await cmd.ExecuteNonQueryAsync();

        // Prune old records if over limit (bounded retention)
        var pruneSql = @"
            DELETE FROM RunHistory 
            WHERE RunId NOT IN (
                SELECT RunId FROM RunHistory ORDER BY StartedUtc DESC LIMIT @MaxCount
            );
        ";
        using var pruneCmd = new SqliteCommand(pruneSql, conn);
        pruneCmd.Parameters.AddWithValue("@MaxCount", _maxRetentionCount);
        await pruneCmd.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<WorkflowRunRecord>> GetRecentRunsAsync(int limit = 50)
    {
        await InitializeAsync();

        var list = new List<WorkflowRunRecord>();
        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT RunId, WorkflowId, WorkflowName, StartedUtc, EndedUtc, FinalState, TotalActions, ExecutedActions, LastCheckpointId, FailedActionId, SafeErrorMessage, ErrorCode, TargetProcess
            FROM RunHistory
            ORDER BY StartedUtc DESC
            LIMIT @Limit;
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Limit", limit);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var record = new WorkflowRunRecord
            {
                RunId = reader.GetString(0),
                WorkflowId = reader.GetString(1),
                WorkflowName = reader.GetString(2),
                StartedUtc = DateTime.Parse(reader.GetString(3)),
                EndedUtc = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)),
                FinalState = Enum.TryParse<ExecutionState>(reader.GetString(5), out var st) ? st : ExecutionState.Idle,
                TotalActions = reader.GetInt32(6),
                ExecutedActions = reader.GetInt32(7),
                LastCheckpointId = reader.IsDBNull(8) ? null : reader.GetString(8),
                FailedActionId = reader.IsDBNull(9) ? null : reader.GetString(9),
                SafeErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10),
                ErrorCode = reader.IsDBNull(11) ? null : reader.GetString(11),
                TargetProcess = reader.IsDBNull(12) ? null : reader.GetString(12)
            };
            list.Add(record);
        }

        return list;
    }

    public async Task<WorkflowRunRecord?> GetRunByIdAsync(string runId)
    {
        await InitializeAsync();

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var sql = @"
            SELECT RunId, WorkflowId, WorkflowName, StartedUtc, EndedUtc, FinalState, TotalActions, ExecutedActions, LastCheckpointId, FailedActionId, SafeErrorMessage, ErrorCode, TargetProcess, StepResultsJson
            FROM RunHistory
            WHERE RunId = @RunId;
        ";

        using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@RunId", runId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var record = new WorkflowRunRecord
            {
                RunId = reader.GetString(0),
                WorkflowId = reader.GetString(1),
                WorkflowName = reader.GetString(2),
                StartedUtc = DateTime.Parse(reader.GetString(3)),
                EndedUtc = reader.IsDBNull(4) ? null : DateTime.Parse(reader.GetString(4)),
                FinalState = Enum.TryParse<ExecutionState>(reader.GetString(5), out var st) ? st : ExecutionState.Idle,
                TotalActions = reader.GetInt32(6),
                ExecutedActions = reader.GetInt32(7),
                LastCheckpointId = reader.IsDBNull(8) ? null : reader.GetString(8),
                FailedActionId = reader.IsDBNull(9) ? null : reader.GetString(9),
                SafeErrorMessage = reader.IsDBNull(10) ? null : reader.GetString(10),
                ErrorCode = reader.IsDBNull(11) ? null : reader.GetString(11),
                TargetProcess = reader.IsDBNull(12) ? null : reader.GetString(12)
            };

            if (!reader.IsDBNull(13))
            {
                try
                {
                    var steps = JsonSerializer.Deserialize<List<ActionExecutionResult>>(reader.GetString(13));
                    if (steps is not null)
                        record.StepResults = steps;
                }
                catch { }
            }

            return record;
        }

        return null;
    }

    public async Task ClearHistoryAsync()
    {
        await InitializeAsync();

        using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        using var cmd = new SqliteCommand("DELETE FROM RunHistory;", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
