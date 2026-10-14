namespace Repliq.Core.Models;

public sealed class ActionExecutionResult
{
    public string ActionId { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public VerificationStatus Status { get; set; } = VerificationStatus.Unverified;
    public long DurationMs { get; set; }
    public string SafeMessage { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    public static ActionExecutionResult Success(string actionId, string actionType, string message, VerificationStatus status = VerificationStatus.Verified) =>
        new()
        {
            ActionId = actionId,
            ActionType = actionType,
            IsSuccess = true,
            Status = status,
            SafeMessage = message
        };

    public static ActionExecutionResult Failure(string actionId, string actionType, string message, string errorCode, VerificationStatus status = VerificationStatus.Failed) =>
        new()
        {
            ActionId = actionId,
            ActionType = actionType,
            IsSuccess = false,
            Status = status,
            SafeMessage = message,
            ErrorCode = errorCode
        };
}

public sealed class WorkflowRunRecord
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkflowId { get; set; } = string.Empty;
    public string WorkflowName { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedUtc { get; set; }
    public ExecutionState FinalState { get; set; } = ExecutionState.Idle;
    public int TotalActions { get; set; }
    public int ExecutedActions { get; set; }
    public string? LastCheckpointId { get; set; }
    public string? LastSuccessfulActionId { get; set; }
    public string? FailedActionId { get; set; }
    public string? SafeErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public string? TargetProcess { get; set; }
    public List<ActionExecutionResult> StepResults { get; set; } = new();
    public List<string> RecoveryOptions { get; set; } = new();
}

public sealed class PolicyDecision
{
    public bool IsAllowed { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? ViolationCode { get; set; }

    public static PolicyDecision Allow(string reason = "Allowed by policy", bool requiresConfirmation = false) =>
        new() { IsAllowed = true, RequiresConfirmation = requiresConfirmation, Reason = reason };

    public static PolicyDecision Deny(string reason, string violationCode) =>
        new() { IsAllowed = false, Reason = reason, ViolationCode = violationCode };
}
