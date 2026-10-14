using Repliq.Core.Models;
using Repliq.Core.Playback;
using Repliq.Core.Recording;

namespace Repliq.Core.Testing;

public sealed class MockAutomationBackend : IAutomationBackend
{
    public List<WorkflowAction> ExecutedActions { get; } = new();
    public bool TargetExistsResult { get; set; } = true;
    public bool FailNextAction { get; set; } = false;
    public string FailReason { get; set; } = "Simulated failure in mock backend";
    public string FailErrorCode { get; set; } = "SIMULATED_MOCK_FAILURE";
    public bool InputStateReleased { get; private set; } = false;
    public int ActionDelayMs { get; set; } = 5;

    public async Task<ActionExecutionResult> ExecuteActionAsync(WorkflowAction action, CancellationToken cancellationToken)
    {
        if (ActionDelayMs > 0)
        {
            await Task.Delay(ActionDelayMs, cancellationToken);
        }

        ExecutedActions.Add(action);

        if (FailNextAction)
        {
            FailNextAction = false;
            return ActionExecutionResult.Failure(action.Id, action.Type, FailReason, FailErrorCode, VerificationStatus.Failed);
        }

        return ActionExecutionResult.Success(action.Id, action.Type, $"Mock executed {action.Type} successfully.");
    }

    public Task<bool> VerifyTargetAsync(TargetSelector target, CancellationToken cancellationToken)
    {
        return Task.FromResult(TargetExistsResult);
    }

    public Task ReleaseInputStateAsync()
    {
        InputStateReleased = true;
        return Task.CompletedTask;
    }

    public void Reset()
    {
        ExecutedActions.Clear();
        TargetExistsResult = true;
        FailNextAction = false;
        InputStateReleased = false;
    }
}

public sealed class MockRecorderBackend : IRecorderBackend
{
    public event Action<RawRecordedEvent>? RawEventCaptured;
    public bool IsCapturing { get; private set; }

    public Task StartCaptureAsync()
    {
        IsCapturing = true;
        return Task.CompletedTask;
    }

    public Task StopCaptureAsync()
    {
        IsCapturing = false;
        return Task.CompletedTask;
    }

    public void SimulateEvent(RawRecordedEvent rawEvent)
    {
        if (IsCapturing)
        {
            RawEventCaptured?.Invoke(rawEvent);
        }
    }
}
