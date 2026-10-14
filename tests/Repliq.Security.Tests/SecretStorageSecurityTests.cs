using Microsoft.Data.Sqlite;
using Repliq.Core.Diagnostics;
using Repliq.Core.Models;
using Repliq.Storage.History;
using Repliq.Storage.Secrets;

namespace Repliq.Security.Tests;

public class SecretStorageSecurityTests
{
    [Fact]
    public async Task DpapiSecretStore_ProtectsAndRetrievesSecret()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Repliq_Vault_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DpapiSecretStore(tempDir);
            const string key = "TestApiKey";
            const string secret = "AIzaSyD-SuperConfidentialKey-123456";

            await store.SetSecretAsync(key, secret);

            // Raw file on disk must NOT contain plaintext secret
            var files = Directory.GetFiles(tempDir, "*.enc");
            Assert.Single(files);
            var rawBytes = await File.ReadAllBytesAsync(files[0]);
            var rawString = System.Text.Encoding.UTF8.GetString(rawBytes);
            Assert.DoesNotContain(secret, rawString);

            // Decrypted through DPAPI should match
            var decrypted = await store.GetSecretAsync(key);
            Assert.Equal(secret, decrypted);

            // Delete
            await store.DeleteSecretAsync(key);
            Assert.False(await store.HasSecretAsync(key));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task HistoryRepository_ScrubsSeededSecretsFromDatabase()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"hist_test_{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteHistoryRepository(tempDb);
            await repo.InitializeAsync();

            const string seededSecret = "SECRET_PASSWORD_XYZ_789";
            var record = new WorkflowRunRecord
            {
                WorkflowId = "wf-sec-test",
                WorkflowName = "Secret Test",
                FinalState = ExecutionState.Failed,
                SafeErrorMessage = $"Failure while evaluating token: {seededSecret}",
                StepResults = new()
                {
                    ActionExecutionResult.Failure("step1", ActionTypes.TypeText, $"Failed with password: {seededSecret}", "ERR_AUTH")
                }
            };

            await repo.RecordRunAsync(record);

            // Clear pool to inspect file
            SqliteConnection.ClearAllPools();

            // Raw SQLite database file must NOT contain seededSecret
            var dbBytes = await File.ReadAllBytesAsync(tempDb);
            var dbText = System.Text.Encoding.UTF8.GetString(dbBytes);
            Assert.DoesNotContain(seededSecret, dbText);

            // Reading back record should contain redacted placeholder
            var retrieved = await repo.GetRunByIdAsync(record.RunId);
            Assert.NotNull(retrieved);
            Assert.Contains("***REDACTED***", retrieved.SafeErrorMessage);
            Assert.DoesNotContain(seededSecret, retrieved.SafeErrorMessage);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (File.Exists(tempDb)) File.Delete(tempDb);
            }
            catch { }
        }
    }
}
