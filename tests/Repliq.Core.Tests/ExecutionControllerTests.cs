using Repliq.Core.Models;
using Repliq.Core.Playback;
using Repliq.Core.Policies;
using Repliq.Core.Testing;

namespace Repliq.Core.Tests;

public class ExecutionControllerTests
{
    private readonly MockAutomationBackend _backend = new();
    private readonly DefaultPolicyEngine _policyEngine = new();

    [Fact]
    public async Task StartAsync_ValidWorkflow_CompletesSuccessfully()
    {
        var controller = new ExecutionController(_backend, _policyEngine);
        var wf = CreateTestWorkflow();

        var run = await controller.StartAsync(wf);

        Assert.Equal(ExecutionState.Completed, run.FinalState);
        Assert.Equal(2, run.ExecutedActions);
        Assert.Equal(2, _backend.ExecutedActions.Count);
        Assert.True(_backend.InputStateReleased);
    }

    [Fact]
    public async Task StartAsync_DryRun_DoesNotInvokeBackendExecution()
    {
        var controller = new ExecutionController(_backend, _policyEngine);
        var wf = CreateTestWorkflow();

        var run = await controller.StartAsync(wf, dryRun: true);

        Assert.Equal(ExecutionState.Completed, run.FinalState);
        Assert.Equal(2, run.ExecutedActions);
        Assert.Empty(_backend.ExecutedActions); // Backend was NOT physically called
    }

    [Fact]
    public async Task EmergencyStopAsync_DuringExecution_CancelsPromptly()
    {
        _backend.ActionDelayMs = 200; // Slow actions
        var controller = new ExecutionController(_backend, _policyEngine);
        var wf = CreateTestWorkflow();
        for (int i = 0; i < 10; i++)
        {
            wf.Actions.Add(new WorkflowAction { Type = ActionTypes.Wait, DelayMs = 50 });
        }

        var runTask = controller.StartAsync(wf);
        await Task.Delay(50);
        await controller.EmergencyStopAsync();

        var run = await runTask;

        Assert.Equal(ExecutionState.Cancelled, run.FinalState);
        Assert.True(_backend.InputStateReleased);
        Assert.True(run.ExecutedActions < wf.Actions.Count);
    }

    [Fact]
    public async Task StartAsync_PolicyDenial_StopsImmediatelyWithoutExecution()
    {
        var controller = new ExecutionController(_backend, _policyEngine);
        var wf = CreateTestWorkflow();
        wf.Actions.Insert(0, new WorkflowAction
        {
            Type = ActionTypes.Click,
            Target = new TargetSelector { ProcessName = "untrusted_app.exe" } // Not allowlisted
        });

        var run = await controller.StartAsync(wf);

        Assert.Equal(ExecutionState.Failed, run.FinalState);
        Assert.Equal("DENIED_PROCESS_NOT_ALLOWLISTED", run.ErrorCode);
        Assert.Empty(_backend.ExecutedActions); // Blocked action never touched the backend
        Assert.True(_backend.InputStateReleased);
    }

    [Fact]
    public async Task StartAsync_MissingTarget_FailsSafely()
    {
        _backend.TargetExistsResult = false;
        var controller = new ExecutionController(_backend, _policyEngine);
        var wf = CreateTestWorkflow();
        wf.Actions[0].Target = new TargetSelector { ProcessName = "notepad.exe" };
        wf.Actions[0].Verification = new ActionVerification { VerifyTargetExists = true };

        var run = await controller.StartAsync(wf);

        Assert.Equal(ExecutionState.Failed, run.FinalState);
        Assert.Equal("TARGET_NOT_FOUND", run.ErrorCode);
        Assert.Empty(_backend.ExecutedActions);
        Assert.True(_backend.InputStateReleased);
    }

    [Fact]
    public async Task PauseAndResume_ExecutionResumesCorrectly()
    {
        _backend.ActionDelayMs = 50;
        var controller = new ExecutionController(_backend, _policyEngine);
        var wf = CreateTestWorkflow();
        for (int i = 0; i < 5; i++)
        {
            wf.Actions.Add(new WorkflowAction { Type = ActionTypes.Wait });
        }

        var runTask = controller.StartAsync(wf);
        await Task.Delay(30);
        await controller.PauseAsync();
        Assert.Equal(ExecutionState.Paused, controller.CurrentState);

        await Task.Delay(100);
        await controller.ResumeAsync();
        Assert.Equal(ExecutionState.Running, controller.CurrentState);

        var run = await runTask;
        Assert.Equal(ExecutionState.Completed, run.FinalState);
    }

    [Fact]
    public async Task StopAsync_WhenIdle_DoesNotGetStuckInStoppingAndAllowsNextStart()
    {
        var controller = new ExecutionController(_backend, _policyEngine);
        var wf = CreateTestWorkflow();

        // Calling stop when idle should not stick in Stopping
        await controller.StopAsync();
        Assert.NotEqual(ExecutionState.Stopping, controller.CurrentState);

        // Next start should succeed smoothly without throwing
        var run = await controller.StartAsync(wf);
        Assert.Equal(ExecutionState.Completed, run.FinalState);
    }

    [Fact]
    public async Task RapidStopDuringActiveRun_AllowsImmediateRestartWithoutStoppingDeadlock()
    {
        _backend.ActionDelayMs = 150;
        var controller = new ExecutionController(_backend, _policyEngine);
        var wf = CreateTestWorkflow();
        for (int i = 0; i < 8; i++)
        {
            wf.Actions.Add(new WorkflowAction { Type = ActionTypes.Wait, DelayMs = 100 });
        }

        // Start long running workflow
        var firstRunTask = controller.StartAsync(wf);
        await Task.Delay(40);

        // Trigger Stop while running
        var stopTask = controller.StopAsync();

        // Immediately attempt to start a new workflow (simulating user immediately clicking Run)
        var secondRunTask = controller.StartAsync(CreateTestWorkflow());

        await stopTask;
        var firstRun = await firstRunTask;
        var secondRun = await secondRunTask;

        Assert.Equal(ExecutionState.Cancelled, firstRun.FinalState);
        Assert.Equal(ExecutionState.Completed, secondRun.FinalState);
        Assert.Equal(ExecutionState.Completed, controller.CurrentState);
    }

    [Fact]
    public async Task MultiProcessWorkflow_ExecutesStepsAcrossMultipleTargetProcessesWithoutDenial()
    {
        var controller = new ExecutionController(_backend, _policyEngine);
        var multiWf = new WorkflowDefinition
        {
            SchemaVersion = 1,
            Id = "wf-multi-proc",
            Name = "Multi Process Workflow",
            TargetPolicy = new TargetPolicy
            {
                AllowedProcesses = new() { "WindowsTerminal.exe", "Antigravity.exe", "brave.exe", "Taskmgr.exe" }
            },
            Actions = new List<WorkflowAction>
            {
                new()
                {
                    Id = "step1",
                    Type = ActionTypes.Click,
                    Target = new TargetSelector { ProcessName = "WindowsTerminal.exe" }
                },
                new()
                {
                    Id = "step2",
                    Type = ActionTypes.TypeText,
                    Target = new TargetSelector { ProcessName = "Antigravity.exe" },
                    Parameters = new() { ["text"] = "echo test" }
                },
                new()
                {
                    Id = "step3",
                    Type = ActionTypes.Click,
                    Target = new TargetSelector { ProcessName = "brave.exe" }
                },
                new()
                {
                    Id = "step4",
                    Type = ActionTypes.Click,
                    Target = new TargetSelector { ProcessName = "Taskmgr.exe" }
                }
            }
        };

        var run = await controller.StartAsync(multiWf);

        Assert.Equal(ExecutionState.Completed, run.FinalState);
        Assert.Equal(4, run.ExecutedActions);
        Assert.Equal(4, _backend.ExecutedActions.Count);
    }

    [Fact]
    public async Task EmergencyStopAsync_WhenIdle_DoesNotGetStuckInStoppingAndAllowsNextStart()
    {
        var controller = new ExecutionController(_backend, _policyEngine);
        var wf = CreateTestWorkflow();

        // Calling emergency stop when idle
        await controller.EmergencyStopAsync();
        Assert.NotEqual(ExecutionState.Stopping, controller.CurrentState);

        // Next start should succeed smoothly without throwing
        var run = await controller.StartAsync(wf);
        Assert.Equal(ExecutionState.Completed, run.FinalState);
    }

    private static WorkflowDefinition CreateTestWorkflow()
    {
        return new WorkflowDefinition
        {
            SchemaVersion = 1,
            Id = "wf-exec-test",
            Name = "Execution Test",
            TargetPolicy = new TargetPolicy
            {
                AllowedProcesses = new() { "notepad.exe" }
            },
            Actions = new List<WorkflowAction>
            {
                new()
                {
                    Id = "step1",
                    Type = ActionTypes.Wait,
                    DelayMs = 10,
                    Parameters = new() { ["durationMs"] = 10 }
                },
                new()
                {
                    Id = "step2",
                    Type = ActionTypes.TypeText,
                    DelayMs = 10,
                    Parameters = new() { ["text"] = "Hello" }
                }
            }
        };
    }
}
