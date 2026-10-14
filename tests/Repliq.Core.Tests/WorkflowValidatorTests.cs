using System.Text.Json;
using Repliq.Core.Models;
using Repliq.Core.Validation;

namespace Repliq.Core.Tests;

public class WorkflowValidatorTests
{
    [Fact]
    public void Validate_ValidWorkflow_ReturnsSuccess()
    {
        var wf = CreateValidWorkflow();
        var result = WorkflowValidator.Validate(wf);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_UnsupportedSchemaVersion_ReturnsError()
    {
        var wf = CreateValidWorkflow();
        wf.SchemaVersion = 2;
        var result = WorkflowValidator.Validate(wf);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("schema version"));
    }

    [Fact]
    public void Validate_UnsupportedActionType_ReturnsError()
    {
        var wf = CreateValidWorkflow();
        wf.Actions.Add(new WorkflowAction
        {
            Type = "runShellScript",
            Parameters = new() { ["script"] = "calc.exe" }
        });

        var result = WorkflowValidator.Validate(wf);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("unsupported action type"));
    }

    [Fact]
    public void Validate_DangerousTargetExecutable_ReturnsError()
    {
        var wf = CreateValidWorkflow();
        wf.TargetPolicy.AllowedProcesses.Add("powershell.exe");

        var result = WorkflowValidator.Validate(wf);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("dangerous executable denylist"));
    }

    [Fact]
    public void Validate_PathTraversalInAllowedPaths_ReturnsError()
    {
        var wf = CreateValidWorkflow();
        wf.TargetPolicy.AllowedPaths.Add(@"C:\Windows\System32\..\..\Secret");

        var result = WorkflowValidator.Validate(wf);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("path traversal"));
    }

    [Fact]
    public void ValidateJsonStructure_UnknownProperty_ReturnsError()
    {
        var json = @"{
            ""schemaVersion"": 1,
            ""id"": ""wf1"",
            ""name"": ""Test"",
            ""unauthorizedCustomField"": ""malicious_payload""
        }";

        var result = WorkflowValidator.ValidateJsonStructure(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Unsupported root property"));
    }

    [Fact]
    public void ValidateJsonStructure_MalformedJson_ReturnsError()
    {
        var json = @"{ ""schemaVersion"": 1, broken json ";
        var result = WorkflowValidator.ValidateJsonStructure(json);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Malformed JSON"));
    }

    [Fact]
    public void Validate_ExceedsMaxActionsLimit_ReturnsError()
    {
        var wf = CreateValidWorkflow();
        wf.Limits.MaxActions = 5;
        for (int i = 0; i < 10; i++)
        {
            wf.Actions.Add(new WorkflowAction { Type = ActionTypes.Wait });
        }

        var result = WorkflowValidator.Validate(wf);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("action count"));
    }

    [Fact]
    public void Validate_DuplicateActionIds_ReturnsError()
    {
        var wf = CreateValidWorkflow();
        wf.Actions.Add(new WorkflowAction { Id = "duplicate-id", Type = ActionTypes.Wait });
        wf.Actions.Add(new WorkflowAction { Id = "duplicate-id", Type = ActionTypes.Wait });

        var result = WorkflowValidator.Validate(wf);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Duplicate action ID"));
    }

    private static WorkflowDefinition CreateValidWorkflow()
    {
        return new WorkflowDefinition
        {
            SchemaVersion = 1,
            Id = "valid-workflow-1",
            Name = "Data Entry Test",
            TargetPolicy = new TargetPolicy
            {
                AllowedProcesses = new() { "notepad.exe" },
                AllowedDomains = new() { "example.com" }
            },
            Limits = new ExecutionLimits
            {
                MaxActions = 100,
                MaxDurationSeconds = 60,
                MaxActionTimeoutMs = 5000,
                MaxRetries = 2
            },
            Actions = new List<WorkflowAction>
            {
                new()
                {
                    Id = "step1",
                    Type = ActionTypes.Wait,
                    DelayMs = 100,
                    Parameters = new() { ["durationMs"] = 100 }
                },
                new()
                {
                    Id = "step2",
                    Type = ActionTypes.TypeText,
                    Parameters = new() { ["text"] = "Hello World" }
                }
            }
        };
    }
}
