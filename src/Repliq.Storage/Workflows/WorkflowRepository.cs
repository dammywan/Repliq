using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Repliq.Core.Models;
using Repliq.Core.Validation;

namespace Repliq.Storage.Workflows;

public interface IWorkflowRepository
{
    Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync();
    Task<WorkflowDefinition?> GetByIdAsync(string id);
    Task SaveAsync(WorkflowDefinition workflow);
    Task DeleteAsync(string id);
    Task<WorkflowDefinition> ImportAsync(string json);
    Task<string> ExportAsync(string id);
}

public sealed class WorkflowRepository : IWorkflowRepository
{
    private readonly string _storageDir;
    private readonly JsonSerializerOptions _jsonOptions;

    public WorkflowRepository(string? customDir = null)
    {
        _storageDir = customDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Repliq",
            "Workflows");

        Directory.CreateDirectory(_storageDir);

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync()
    {
        var list = new List<WorkflowDefinition>();
        var files = Directory.GetFiles(_storageDir, "*.json");

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var validation = WorkflowValidator.ValidateJsonStructure(json);
                if (!validation.IsValid) continue;

                var wf = JsonSerializer.Deserialize<WorkflowDefinition>(json, _jsonOptions);
                if (wf is not null && WorkflowValidator.Validate(wf).IsValid)
                {
                    list.Add(wf);
                }
            }
            catch
            {
                // Fail closed: skip corrupt or unreadable files
            }
        }

        return list.OrderByDescending(w => w.ModifiedUtc).ToList();
    }

    public async Task<WorkflowDefinition?> GetByIdAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || WorkflowValidator.HasPathTraversal(id))
            return null;

        var path = Path.Combine(_storageDir, $"{id}.json");
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path);
        var validation = WorkflowValidator.ValidateJsonStructure(json);
        if (!validation.IsValid)
            return null;

        var wf = JsonSerializer.Deserialize<WorkflowDefinition>(json, _jsonOptions);
        if (wf is null || !WorkflowValidator.Validate(wf).IsValid)
            return null;

        return wf;
    }

    public async Task SaveAsync(WorkflowDefinition workflow)
    {
        if (workflow is null) throw new ArgumentNullException(nameof(workflow));

        var validation = WorkflowValidator.Validate(workflow);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("Cannot save invalid workflow: " + string.Join(", ", validation.Errors));
        }

        workflow.ModifiedUtc = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(workflow, _jsonOptions);
        var path = Path.Combine(_storageDir, $"{workflow.Id}.json");
        var tempPath = Path.Combine(_storageDir, $"{workflow.Id}.tmp");

        // Atomic write: write to temp file then replace
        await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, path, overwrite: true);
    }

    public Task DeleteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || WorkflowValidator.HasPathTraversal(id))
            return Task.CompletedTask;

        var path = Path.Combine(_storageDir, $"{id}.json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public async Task<WorkflowDefinition> ImportAsync(string json)
    {
        var structValidation = WorkflowValidator.ValidateJsonStructure(json);
        if (!structValidation.IsValid)
        {
            throw new InvalidOperationException("Import rejected: " + string.Join(", ", structValidation.Errors));
        }

        var wf = JsonSerializer.Deserialize<WorkflowDefinition>(json, _jsonOptions)
            ?? throw new InvalidOperationException("Failed to deserialize workflow.");

        var modelValidation = WorkflowValidator.Validate(wf);
        if (!modelValidation.IsValid)
        {
            throw new InvalidOperationException("Import rejected: " + string.Join(", ", modelValidation.Errors));
        }

        // Tag imported workflow as requiring review
        wf.Metadata["Imported"] = "true";
        wf.Metadata["RequiresReview"] = "true";
        wf.Metadata["ImportedUtc"] = DateTime.UtcNow.ToString("o");

        // Save safely
        await SaveAsync(wf);
        return wf;
    }

    public async Task<string> ExportAsync(string id)
    {
        var wf = await GetByIdAsync(id)
            ?? throw new KeyNotFoundException($"Workflow with ID '{id}' was not found.");

        return JsonSerializer.Serialize(wf, _jsonOptions);
    }

    public static string ComputeSha256Checksum(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
