using System.Text;
using Repliq.Core.Models;
using Repliq.Core.Playback;
using Repliq.Core.Policies;
using Repliq.Core.Testing;
using Repliq.Core.Validation;
using Repliq.Storage.Workflows;

namespace Repliq.Security.Tests;

public class MaliciousWorkflowSecurityTests
{
    private readonly MockAutomationBackend _backend = new();
    private readonly DefaultPolicyEngine _policyEngine = new();

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("powershell.exe")]
    [InlineData("pwsh.exe")]
    [InlineData("bash.exe")]
    [InlineData("wscript.exe")]
    [InlineData("mshta.exe")]
    [InlineData("rundll32.exe")]
    [InlineData("certutil.exe")]
    public void Validator_RejectsDangerousExecutablesInTargetPolicy(string dangerousProc)
    {
        var wf = new WorkflowDefinition
        {
            TargetPolicy = new TargetPolicy { AllowedProcesses = new() { dangerousProc } }
        };

        var result = WorkflowValidator.Validate(wf);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("dangerous executable"));
    }

    [Theory]
    [InlineData("invokeScript")]
    [InlineData("executeCode")]
    [InlineData("shellExec")]
    [InlineData("eval")]
    [InlineData("runProcess")]
    public void Validator_RejectsArbitraryActionTypes(string maliciousActionType)
    {
        var wf = new WorkflowDefinition();
        wf.Actions.Add(new WorkflowAction
        {
            Type = maliciousActionType,
            Parameters = new() { ["cmd"] = "whoami" }
        });

        var result = WorkflowValidator.Validate(wf);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unsupported action type"));
    }

    [Fact]
    public void Validator_RejectsOversizedJson()
    {
        // Generate > 2MB JSON
        var sb = new StringBuilder();
        sb.Append(@"{ ""schemaVersion"": 1, ""id"": ""huge"", ""name"": """);
        sb.Append(new string('A', 2 * 1024 * 1024 + 100));
        sb.Append(@""" }");

        var result = WorkflowValidator.ValidateJsonStructure(sb.ToString());
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("exceeds maximum permitted size"));
    }

    [Theory]
    [InlineData(@"..\..\..\Windows\System32")]
    [InlineData(@"..\AppData\Roaming\Secret.txt")]
    [InlineData(@"folder/../../test")]
    public void Validator_RejectsPathTraversalPatterns(string maliciousPath)
    {
        Assert.True(WorkflowValidator.HasPathTraversal(maliciousPath));

        var wf = new WorkflowDefinition();
        wf.TargetPolicy.AllowedPaths.Add(maliciousPath);

        var result = WorkflowValidator.Validate(wf);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("path traversal"));
    }

    [Fact]
    public async Task ExecutionEngine_NeverInvokesBackendOnPolicyDenial()
    {
        var controller = new ExecutionController(_backend, _policyEngine);
        var wf = new WorkflowDefinition
        {
            TargetPolicy = new TargetPolicy { AllowedProcesses = new() { "notepad.exe" } },
            Actions = new List<WorkflowAction>
            {
                new()
                {
                    Type = ActionTypes.Click,
                    Target = new TargetSelector { ProcessName = "untrusted_malicious.exe" }
                }
            }
        };

        var run = await controller.StartAsync(wf);

        Assert.Equal(ExecutionState.Failed, run.FinalState);
        Assert.Equal("DENIED_PROCESS_NOT_ALLOWLISTED", run.ErrorCode);
        Assert.Empty(_backend.ExecutedActions); // Backend never touched!
        Assert.True(_backend.InputStateReleased);
    }

    [Fact]
    public async Task WorkflowRepository_RejectsPathTraversalInId()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Repliq_Sec_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new WorkflowRepository(tempDir);
            var wf = await repo.GetByIdAsync(@"..\..\Windows\System32\cmd");
            Assert.Null(wf);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task WorkflowRepository_Import_TagsUntrustedWorkflow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Repliq_Sec_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new WorkflowRepository(tempDir);
            var json = @"{
                ""schemaVersion"": 1,
                ""id"": ""imported-wf-1"",
                ""name"": ""Third Party Workflow"",
                ""actions"": [
                    { ""id"": ""act1"", ""type"": ""wait"", ""delayMs"": 100 }
                ]
            }";

            var imported = await repo.ImportAsync(json);

            Assert.True(imported.Metadata.ContainsKey("Imported"));
            Assert.True(imported.Metadata.ContainsKey("RequiresReview"));
            Assert.Equal("true", imported.Metadata["RequiresReview"]);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
