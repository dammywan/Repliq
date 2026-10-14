namespace Repliq.Core.Models;

public enum ExecutionState
{
    Idle,
    Starting,
    Validating,
    AwaitingConfirmation,
    Running,
    Paused,
    Stopping,
    Completed,
    Failed,
    Cancelled
}

public enum VerificationStatus
{
    Unverified,
    Verified,
    Failed,
    UnknownOutcome
}
