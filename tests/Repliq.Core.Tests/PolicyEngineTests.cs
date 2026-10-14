using Repliq.Core.Models;
using Repliq.Core.Policies;

namespace Repliq.Core.Tests;

public class PolicyEngineTests
{
    private readonly DefaultPolicyEngine _engine = new();

    [Fact]
    public void EvaluateTarget_BlockedProcess_ReturnsDenied()
    {
        var target = new TargetSelector { ProcessName = "cmd.exe" };
        var decision = _engine.EvaluateTarget(target, null);

        Assert.False(decision.IsAllowed);
        Assert.Equal("DENIED_BLOCKED_PROCESS", decision.ViolationCode);
    }

    [Fact]
    public void EvaluateTarget_AllowedProcess_ReturnsAllowed()
    {
        var target = new TargetSelector { ProcessName = "notepad.exe" };
        var decision = _engine.EvaluateTarget(target, null);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void EvaluateTarget_UnallowlistedProcess_ReturnsDenied()
    {
        var target = new TargetSelector { ProcessName = "untrusted_app.exe" };
        var decision = _engine.EvaluateTarget(target, null);

        Assert.False(decision.IsAllowed);
        Assert.Equal("DENIED_PROCESS_NOT_ALLOWLISTED", decision.ViolationCode);
    }

    [Fact]
    public void EvaluateTarget_ProtectedWindowPattern_ReturnsDenied()
    {
        var target = new TargetSelector
        {
            ProcessName = "notepad.exe",
            WindowTitle = "User Account Control Security Dialog"
        };
        var decision = _engine.EvaluateTarget(target, null);

        Assert.False(decision.IsAllowed);
        Assert.Equal("DENIED_PROTECTED_WINDOW", decision.ViolationCode);
    }

    [Fact]
    public void EvaluateTarget_RepliqOwnWindow_ReturnsDenied()
    {
        var target = new TargetSelector
        {
            ProcessName = "repliq.app.exe",
            WindowTitle = "Repliq - Workflow Automation"
        };
        var decision = _engine.EvaluateTarget(target, null);

        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public void EvaluateAction_HighImpactSubmit_RequiresConfirmation()
    {
        var action = new WorkflowAction
        {
            Type = ActionTypes.Click,
            Description = "Submit 100 customer records",
            Parameters = new() { ["isSubmit"] = true }
        };

        var wf = new WorkflowDefinition();
        var decision = _engine.EvaluateAction(action, wf);

        Assert.True(decision.IsAllowed);
        Assert.True(decision.RequiresConfirmation);
    }

    [Fact]
    public void EvaluateAction_UnsupportedAction_ReturnsDenied()
    {
        var action = new WorkflowAction
        {
            Type = "customMaliciousAction"
        };

        var wf = new WorkflowDefinition();
        var decision = _engine.EvaluateAction(action, wf);

        Assert.False(decision.IsAllowed);
        Assert.Equal("UNSUPPORTED_ACTION_TYPE", decision.ViolationCode);
    }
}
