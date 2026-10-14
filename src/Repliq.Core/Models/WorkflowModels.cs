using System.Text.Json.Serialization;

namespace Repliq.Core.Models;

public sealed class WorkflowAction
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Type { get; set; } = ActionTypes.Wait;

    public string? Description { get; set; }

    public bool Enabled { get; set; } = true;

    public int DelayMs { get; set; } = 100;

    public int TimeoutMs { get; set; } = 5000;

    public TargetSelector? Target { get; set; }

    public Dictionary<string, object?> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ActionVerification? Verification { get; set; }

    public bool RequiresConfirmation { get; set; } = false;

    public bool IsIdempotent { get; set; } = true;
}

public sealed class TargetPolicy
{
    public List<string> AllowedProcesses { get; set; } = new();

    public List<string> AllowedDomains { get; set; } = new();

    public string? WindowTitlePattern { get; set; }

    public bool RequireProcessMatch { get; set; } = true;

    public List<string> AllowedPaths { get; set; } = new();
}

public sealed class ExecutionLimits
{
    public int MaxActions { get; set; } = 500;

    public int MaxDurationSeconds { get; set; } = 300;

    public int MaxActionTimeoutMs { get; set; } = 10000;

    public int MaxRetries { get; set; } = 3;
}

public sealed class WorkflowDefinition
{
    public int SchemaVersion { get; set; } = 1;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Untitled Workflow";

    public string? Description { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    public TargetPolicy TargetPolicy { get; set; } = new();

    public ExecutionLimits Limits { get; set; } = new();

    public List<WorkflowAction> Actions { get; set; } = new();

    public Dictionary<string, string> Variables { get; set; } = new();

    public Dictionary<string, string> Metadata { get; set; } = new();
}
