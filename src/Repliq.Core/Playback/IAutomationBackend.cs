using Repliq.Core.Models;

namespace Repliq.Core.Playback;

/// <summary>
/// Narrow execution backend interface.
/// Encapsulates all physical or simulated input and target inspection.
/// Mocked during tests to guarantee zero unintended desktop input.
/// </summary>
public interface IAutomationBackend
{
    Task<ActionExecutionResult> ExecuteActionAsync(WorkflowAction action, CancellationToken cancellationToken);
    Task<bool> VerifyTargetAsync(TargetSelector target, CancellationToken cancellationToken);
    Task ReleaseInputStateAsync();
}
