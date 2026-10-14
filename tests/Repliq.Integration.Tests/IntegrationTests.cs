using Repliq.App;
using Repliq.Core.Models;
using Repliq.Core.Playback;
using Repliq.Core.Policies;
using Repliq.Core.Recording;
using Repliq.Core.Testing;
using Repliq.Core.Validation;
using Repliq.Integrations.Ai;
using Repliq.Integrations.Spreadsheets;
using Repliq.Storage.History;
using Repliq.Storage.Secrets;
using Repliq.Storage.Settings;
using Repliq.Storage.Workflows;

namespace Repliq.Integration.Tests;

public class IntegrationTests
{
    [Fact]
    public async Task EndToEnd_RecordingToExecutionFlow_Succeeds()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Repliq_E2E_" + Guid.NewGuid().ToString("N"));
        try
        {
            var workflowRepo = new WorkflowRepository(tempDir);
            var mockRecorder = new MockRecorderBackend();
            var recordingCtrl = new RecordingController(mockRecorder);

            // 1. User starts recording
            await recordingCtrl.StartAsync();

            // 2. Mock raw events captured
            mockRecorder.SimulateEvent(new RawRecordedEvent
            {
                EventType = "window",
                WindowTitle = "Data Entry Form",
                ProcessName = "notepad.exe"
            });
            mockRecorder.SimulateEvent(new RawRecordedEvent
            {
                EventType = "click",
                X = 120,
                Y = 240,
                Button = "left",
                ProcessName = "notepad.exe",
                WindowTitle = "Data Entry Form"
            });
            mockRecorder.SimulateEvent(new RawRecordedEvent
            {
                EventType = "key",
                Key = "Enter",
                ProcessName = "notepad.exe",
                WindowTitle = "Data Entry Form"
            });

            // 3. User stops recording
            var draft = await recordingCtrl.StopAsync("Recorded E2E Task");
            Assert.Equal(3, draft.Actions.Count);

            // 4. Save to repository
            await workflowRepo.SaveAsync(draft);

            // 5. Retrieve from repository
            var loaded = await workflowRepo.GetByIdAsync(draft.Id);
            Assert.NotNull(loaded);
            Assert.Equal("Recorded E2E Task", loaded.Name);

            // 6. Execute with ExecutionController
            var mockBackend = new MockAutomationBackend();
            var policyEngine = new DefaultPolicyEngine();
            var execController = new ExecutionController(mockBackend, policyEngine);

            var runRecord = await execController.StartAsync(loaded);

            // 7. Verify verified completion
            Assert.Equal(ExecutionState.Completed, runRecord.FinalState);
            Assert.Equal(3, runRecord.ExecutedActions);
            Assert.Equal(3, mockBackend.ExecutedActions.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task SpreadsheetService_PreviewAndSafeWrite_CreatesBackupAndPreservesOriginal()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Repliq_Spreadsheet_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var csvPath = Path.Combine(tempDir, "customers.csv");
            var originalContent = "CustomerId,CustomerName,Email\r\n101,Acme Corp,contact@acme.com\r\n102,Beta LLC,info@beta.com\r\n";
            await File.WriteAllTextAsync(csvPath, originalContent);

            var service = new SpreadsheetService();

            // Preview
            var preview = await service.PreviewFileAsync(csvPath);
            Assert.Equal(2, preview.TotalRows);
            Assert.Equal(3, preview.Headers.Count);
            Assert.Equal("CustomerId", preview.Headers[0]);

            // Safe write with new data
            var headers = new List<string> { "CustomerId", "CustomerName", "Email" };
            var rows = new List<Dictionary<string, string>>
            {
                new() { ["CustomerId"] = "103", ["CustomerName"] = "Gamma Inc", ["Email"] = "gamma@gamma.com" }
            };

            await service.WriteRowsSafelyAsync(csvPath, headers, rows, backupOriginal: true);

            // Verify original was backed up
            var backupFiles = Directory.GetFiles(tempDir, "*.bak.*");
            Assert.Single(backupFiles);
            var backupText = await File.ReadAllTextAsync(backupFiles[0]);
            Assert.Equal(originalContent, backupText);

            // Verify file has updated content
            var updatedRows = await service.ReadAllRowsAsync(csvPath);
            Assert.Single(updatedRows);
            Assert.Equal("Gamma Inc", updatedRows[0]["CustomerName"]);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GeminiWorkflowAssistant_LocalAnalysis_ValidatesSafetyWithoutCloudKey()
    {
        var assistant = new GeminiWorkflowAssistant(apiKey: null);
        Assert.False(assistant.IsConfigured);

        var safeWf = new WorkflowDefinition
        {
            TargetPolicy = new TargetPolicy { AllowedProcesses = new() { "notepad.exe" } },
            Actions = new() { new WorkflowAction { Type = ActionTypes.Wait } }
        };

        var analysis = await assistant.AnalyzeWorkflowAsync(safeWf);
        Assert.True(analysis.IsSafeToExecute);
        Assert.Empty(analysis.SecurityRisks);

        var unsafeWf = new WorkflowDefinition
        {
            TargetPolicy = new TargetPolicy { AllowedProcesses = new() { "cmd.exe" } }
        };

        var unsafeAnalysis = await assistant.AnalyzeWorkflowAsync(unsafeWf);
        Assert.False(unsafeAnalysis.IsSafeToExecute);
        Assert.NotEmpty(unsafeAnalysis.SecurityRisks);
    }

    [Fact]
    public void MainForm_Instantiation_InitializesAllControlsCleanly()
    {
        // Smoke test verifying that MainForm constructs all views and handles without exceptions
        using var form = new MainForm();
        Assert.NotNull(form);
        Assert.Equal("Repliq — Desktop Workflow Automation", form.Text);
    }

    [Fact]
    public void QuickPanelForm_Instantiation_InitializesCleanly()
    {
        var workflowRepo = new WorkflowRepository();
        var mockBackend = new MockAutomationBackend();
        var execController = new ExecutionController(mockBackend);
        var mockRecorder = new MockRecorderBackend();
        var recordingCtrl = new RecordingController(mockRecorder);

        using var panel = new QuickPanelForm(workflowRepo, execController, recordingCtrl, () => { });
        Assert.NotNull(panel);
        Assert.Equal("Repliq Quick Panel", panel.Text);
        Assert.True(panel.TopMost);
    }
}
