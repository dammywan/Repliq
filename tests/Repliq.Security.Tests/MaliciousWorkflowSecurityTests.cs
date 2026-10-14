using System.Runtime.InteropServices;
using System.Text;
using Repliq.Core.Models;
using Repliq.Core.Playback;
using Repliq.Core.Policies;
using Repliq.Core.Testing;
using Repliq.Core.Validation;
using Repliq.Storage.Workflows;
using Repliq.Windows.Hotkeys;
using Repliq.Windows.Input;

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

    [Fact]
    public void InputStruct_SizeOn64Bit_Is40Bytes()
    {
        if (Environment.Is64BitProcess)
        {
            Assert.Equal(40, Marshal.SizeOf<Win32InputBackend.INPUT>());
        }
        else
        {
            Assert.Equal(28, Marshal.SizeOf<Win32InputBackend.INPUT>());
        }
    }

    [Theory]
    [InlineData(@"..\..\Windows\System32\cmd")]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    [InlineData(@"/etc/passwd")]
    [InlineData(@"sub\workflow")]
    [InlineData(@"sub/workflow")]
    [InlineData(@"wf:name")]
    [InlineData(@"wf*name")]
    [InlineData(@"")]
    [InlineData(@"   ")]
    public async Task WorkflowRepository_RejectsAllInvalidAndTraversalIds(string maliciousId)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Repliq_Sec_Test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var repo = new WorkflowRepository(tempDir);
            var wf = await repo.GetByIdAsync(maliciousId);
            Assert.Null(wf);

            // Deleting with malicious/invalid ID should safely complete without path traversal or exceptions
            await repo.DeleteAsync(maliciousId);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData(@"{ ""schemaVersion"": ""one"", ""id"": ""test"", ""name"": ""Test"" }", "schemaVersion")]
    [InlineData(@"{ ""schemaVersion"": null, ""id"": ""test"", ""name"": ""Test"" }", "schemaVersion")]
    [InlineData(@"{ ""schemaVersion"": { ""version"": 1 }, ""id"": ""test"", ""name"": ""Test"" }", "schemaVersion")]
    [InlineData(@"{ ""schemaVersion"": 1, ""id"": ""test"", ""name"": ""Test"", ""actions"": ""not-an-array"" }", "array")]
    public void Validator_JsonStructure_HandlesMalformedSchemaVersionAndActionsGracefully(string malformedJson, string expectedErrorSubstr)
    {
        var result = WorkflowValidator.ValidateJsonStructure(malformedJson);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains(expectedErrorSubstr, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PolicyEngine_DeniesUntrustedImportedWorkflow_FromAutomatingProcessesWithoutGlobalApproval()
    {
        var policyEngine = new DefaultPolicyEngine();
        var wf = new WorkflowDefinition
        {
            Id = "untrusted-imported",
            Name = "Untrusted",
            TargetPolicy = new TargetPolicy
            {
                AllowedProcesses = new List<string> { "custom_payroll.exe" }
            },
            Metadata = new Dictionary<string, string>
            {
                ["Imported"] = "true"
            }
        };

        var action = new WorkflowAction
        {
            Id = "act1",
            Type = ActionTypes.Click,
            Target = new TargetSelector { ProcessName = "custom_payroll.exe" }
        };

        var decision = policyEngine.EvaluateAction(action, wf);
        Assert.False(decision.IsAllowed);
        Assert.Equal("DENIED_UNTRUSTED_IMPORTED_PROCESS", decision.ViolationCode);
    }

    [Fact]
    public async Task ExecutionController_ResolvesVariablesAndEnvironmentInTypeText()
    {
        var backend = new MockAutomationBackend();
        var controller = new ExecutionController(backend, new DefaultPolicyEngine());

        var wf = new WorkflowDefinition
        {
            Id = "var-test",
            Name = "Variable Test",
            TargetPolicy = new TargetPolicy { AllowedProcesses = new() { "notepad.exe" } },
            Variables = new Dictionary<string, string>
            {
                ["Greeting"] = "HelloRepliq"
            },
            Actions = new List<WorkflowAction>
            {
                new()
                {
                    Id = "type1",
                    Type = ActionTypes.TypeText,
                    Target = new TargetSelector { ProcessName = "notepad.exe" },
                    Parameters = new()
                    {
                        ["text"] = "Message: ${Var:Greeting} for ${Env:USERNAME}"
                    }
                }
            }
        };

        var run = await controller.StartAsync(wf);
        Assert.Equal(ExecutionState.Completed, run.FinalState);
        Assert.Single(backend.ExecutedActions);
        var executedParam = backend.ExecutedActions[0].Parameters["text"]?.ToString();
        Assert.NotNull(executedParam);
        Assert.StartsWith("Message: HelloRepliq for ", executedParam);
        Assert.DoesNotContain("${Var:Greeting}", executedParam);
        Assert.DoesNotContain("${Env:USERNAME}", executedParam);
    }

    [Fact]
    public async Task ExecutionController_PreventsConcurrentOrReentrantExecution()
    {
        var backend = new MockAutomationBackend();
        var controller = new ExecutionController(backend, new DefaultPolicyEngine());

        var wf = new WorkflowDefinition
        {
            Id = "slow-test",
            Name = "Slow Test",
            TargetPolicy = new TargetPolicy { AllowedProcesses = new() { "notepad.exe" } },
            Actions = new List<WorkflowAction>
            {
                new()
                {
                    Id = "wait1",
                    Type = ActionTypes.Wait,
                    Target = new TargetSelector { ProcessName = "notepad.exe" },
                    Parameters = new() { ["durationMs"] = 1000 }
                }
            }
        };

        var task1 = controller.StartAsync(wf);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.StartAsync(wf));
        await task1;
    }

    [Fact]
    public async Task ExecutionController_TracksTargetProcessAndLastSuccessfulAction()
    {
        var backend = new MockAutomationBackend();
        var controller = new ExecutionController(backend, new DefaultPolicyEngine());

        var wf = new WorkflowDefinition
        {
            Id = "tracking-test",
            Name = "Tracking Test",
            TargetPolicy = new TargetPolicy { AllowedProcesses = new() { "notepad.exe" } },
            Actions = new List<WorkflowAction>
            {
                new()
                {
                    Id = "step1",
                    Type = ActionTypes.Wait,
                    Target = new TargetSelector { ProcessName = "notepad.exe" },
                    Parameters = new() { ["durationMs"] = 10 }
                },
                new()
                {
                    Id = "step2",
                    Type = ActionTypes.Wait,
                    Target = new TargetSelector { ProcessName = "notepad.exe" },
                    Parameters = new() { ["durationMs"] = 10 }
                }
            }
        };

        var run = await controller.StartAsync(wf);
        Assert.Equal(ExecutionState.Completed, run.FinalState);
        Assert.Equal("notepad.exe", run.TargetProcess);
        Assert.Equal("step2", run.LastSuccessfulActionId);
    }

    [Fact]
    public void GlobalHotkeyManager_HandlesDuplicateRegistrationsCleanly()
    {
        using var hotkeyMgr = new GlobalHotkeyManager();
        var r1 = hotkeyMgr.RegisterShortcut("Test1", "Ctrl+Shift+F12", () => { });
        var r2 = hotkeyMgr.RegisterShortcut("Test1", "Ctrl+Shift+F12", () => { });
        Assert.True(r1.Success == r2.Success);
        hotkeyMgr.UnregisterShortcut("Test1");
    }
}
