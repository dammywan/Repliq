using Repliq.Core.Models;
using Repliq.Core.Policies;
using Repliq.Core.Validation;

namespace Repliq.Core.Tests;

public class KeyPressActionTests
{
    [Theory]
    [InlineData("keyPress", "keyPress")]
    [InlineData("keypress", "keyPress")]
    [InlineData("KEYPRESS", "keyPress")]
    [InlineData("typeText", "typeText")]
    [InlineData("typetext", "typeText")]
    [InlineData("click", "click")]
    [InlineData("CLICK", "click")]
    [InlineData("wait", "wait")]
    [InlineData("scroll", "scroll")]
    [InlineData("focusWindow", "focusWindow")]
    [InlineData("verifyElement", "verifyElement")]
    [InlineData("checkpoint", "checkpoint")]
    public void ActionTypes_Normalize_ReturnsCanonicalName(string input, string expected)
    {
        var normalized = ActionTypes.Normalize(input);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("keyPress", true)]
    [InlineData("keypress", true)]
    [InlineData("KEYPRESS", true)]
    [InlineData("unknownAction", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ActionTypes_IsSupported_HandlesCasing(string? input, bool expected)
    {
        Assert.Equal(expected, ActionTypes.IsSupported(input));
    }

    [Fact]
    public void WorkflowValidator_KeyPressWithValidKey_ValidatesSuccessfully()
    {
        var wf = CreateBaseWorkflow();
        wf.Actions.Add(new WorkflowAction
        {
            Id = "key1",
            Type = ActionTypes.KeyPress,
            Parameters = new() { ["key"] = "A" }
        });

        var result = WorkflowValidator.Validate(wf);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("keyPress")]
    [InlineData("keypress")]
    [InlineData("KEYPRESS")]
    public void WorkflowValidator_KeyPress_CaseInsensitiveValidation(string actionType)
    {
        var wf = CreateBaseWorkflow();
        wf.Actions.Add(new WorkflowAction
        {
            Id = "key1",
            Type = actionType,
            Parameters = new() { ["key"] = "Enter" }
        });

        var result = WorkflowValidator.Validate(wf);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void WorkflowValidator_KeyPressWithoutKeyParam_ReturnsError()
    {
        var wf = CreateBaseWorkflow();
        wf.Actions.Add(new WorkflowAction
        {
            Id = "key1",
            Type = ActionTypes.KeyPress,
            Parameters = new() // Missing "key"
        });

        var result = WorkflowValidator.Validate(wf);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("must specify 'key' parameter"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void WorkflowValidator_KeyPressWithEmptyKey_ReturnsError(string emptyKey)
    {
        var wf = CreateBaseWorkflow();
        wf.Actions.Add(new WorkflowAction
        {
            Id = "key1",
            Type = ActionTypes.KeyPress,
            Parameters = new() { ["key"] = emptyKey }
        });

        var result = WorkflowValidator.Validate(wf);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("must specify 'key' parameter"));
    }

    [Fact]
    public void PolicyEngine_KeyPressAction_AllowedWhenTargetAllowed()
    {
        var policyEngine = new DefaultPolicyEngine();
        var wf = CreateBaseWorkflow();
        var action = new WorkflowAction
        {
            Id = "step1",
            Type = ActionTypes.KeyPress,
            Parameters = new() { ["key"] = "A" }
        };

        var decision = policyEngine.EvaluateAction(action, wf);
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void PolicyEngine_KeyPressAction_DeniedWhenTargetProcessBlocked()
    {
        var policyEngine = new DefaultPolicyEngine();
        var wf = CreateBaseWorkflow();
        var action = new WorkflowAction
        {
            Id = "step1",
            Type = ActionTypes.KeyPress,
            Target = new TargetSelector { ProcessName = "powershell.exe" },
            Parameters = new() { ["key"] = "A" }
        };

        var decision = policyEngine.EvaluateAction(action, wf);
        Assert.False(decision.IsAllowed);
        Assert.Equal("DENIED_BLOCKED_PROCESS", decision.ViolationCode);
    }

    private static WorkflowDefinition CreateBaseWorkflow()
    {
        return new WorkflowDefinition
        {
            SchemaVersion = 1,
            Id = "wf-test-keys",
            Name = "Keys Test",
            TargetPolicy = new TargetPolicy
            {
                AllowedProcesses = new() { "notepad.exe" }
            },
            Limits = new ExecutionLimits
            {
                MaxActions = 50,
                MaxDurationSeconds = 30,
                MaxActionTimeoutMs = 5000,
                MaxRetries = 1
            },
            Actions = new List<WorkflowAction>()
        };
    }
}
