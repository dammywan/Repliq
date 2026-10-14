using System.Diagnostics;
using Repliq.Core.Models;
using Repliq.Core.Policies;
using Repliq.Core.Validation;

namespace Repliq.Core.Playback;

public interface IExecutionController
{
    ExecutionState CurrentState { get; }
    WorkflowDefinition? CurrentWorkflow { get; }
    int CurrentStepIndex { get; }
    int TotalSteps { get; }
    string? CurrentTargetInfo { get; }

    event Action<ExecutionState>? StateChanged;
    event Action<int, WorkflowAction, ActionExecutionResult?>? StepProgress;
    event Action<string>? LogMessage;
    event Func<WorkflowAction, Task<bool>>? ConfirmationRequested;

    Task<WorkflowRunRecord> StartAsync(WorkflowDefinition workflow, bool dryRun = false);
    Task PauseAsync();
    Task ResumeAsync();
    Task StopAsync();
    Task EmergencyStopAsync();
}

public sealed class ExecutionController : IExecutionController
{
    private readonly IAutomationBackend _backend;
    private readonly IPolicyEngine _policyEngine;
    private readonly object _stateLock = new();

    private CancellationTokenSource? _workflowCts;
    private TaskCompletionSource<bool>? _pauseTcs;
    private ExecutionState _state = ExecutionState.Idle;
    private WorkflowDefinition? _currentWorkflow;
    private int _currentStepIndex = 0;
    private int _totalSteps = 0;
    private string? _currentTargetInfo;

    public ExecutionState CurrentState
    {
        get { lock (_stateLock) return _state; }
        private set
        {
            lock (_stateLock)
            {
                if (_state != value)
                {
                    _state = value;
                    Task.Run(() => StateChanged?.Invoke(value));
                }
            }
        }
    }

    public WorkflowDefinition? CurrentWorkflow => _currentWorkflow;
    public int CurrentStepIndex => _currentStepIndex;
    public int TotalSteps => _totalSteps;
    public string? CurrentTargetInfo => _currentTargetInfo;

    public event Action<ExecutionState>? StateChanged;
    public event Action<int, WorkflowAction, ActionExecutionResult?>? StepProgress;
    public event Action<string>? LogMessage;
    public event Func<WorkflowAction, Task<bool>>? ConfirmationRequested;

    public ExecutionController(IAutomationBackend backend, IPolicyEngine? policyEngine = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _policyEngine = policyEngine ?? new DefaultPolicyEngine();
    }

    public async Task<WorkflowRunRecord> StartAsync(WorkflowDefinition workflow, bool dryRun = false)
    {
        lock (_stateLock)
        {
            if (_state is ExecutionState.Running or ExecutionState.Paused or ExecutionState.Starting)
            {
                throw new InvalidOperationException("A workflow is already executing or paused.");
            }
            CurrentState = ExecutionState.Starting;
            _currentWorkflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            _workflowCts = new CancellationTokenSource();
            _pauseTcs = null;
        }

        var runRecord = new WorkflowRunRecord
        {
            WorkflowId = workflow.Id,
            WorkflowName = workflow.Name,
            StartedUtc = DateTime.UtcNow,
            TotalActions = workflow.Actions.Count
        };

        var ct = _workflowCts.Token;

        try
        {
            // Step 1: Pre-execution schema validation
            CurrentState = ExecutionState.Validating;
            LogMessage?.Invoke("Validating workflow schema and safety constraints...");

            var validation = WorkflowValidator.Validate(workflow);
            if (!validation.IsValid)
            {
                var errMsg = "Validation failed: " + string.Join("; ", validation.Errors);
                LogMessage?.Invoke(errMsg);
                runRecord.FinalState = ExecutionState.Failed;
                runRecord.SafeErrorMessage = errMsg;
                runRecord.ErrorCode = "SCHEMA_VALIDATION_ERROR";
                CurrentState = ExecutionState.Failed;
                return runRecord;
            }

            // Step 2: Enforce overall workflow timeout
            int maxDurationSeconds = Math.Clamp(workflow.Limits?.MaxDurationSeconds ?? 300, 1, 3600);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(maxDurationSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            var linkedToken = linkedCts.Token;

            CurrentState = ExecutionState.Running;
            LogMessage?.Invoke(dryRun ? "Starting Dry Run (preview mode)..." : "Starting workflow playback...");

            _totalSteps = workflow.Actions.Count;
            string? lastCheckpointId = null;

            for (int i = 0; i < workflow.Actions.Count; i++)
            {
                _currentStepIndex = i + 1;
                var action = workflow.Actions[i];

                // Check cancellation
                linkedToken.ThrowIfCancellationRequested();

                // Check pause state
                await CheckPauseAsync(linkedToken);

                StepProgress?.Invoke(_currentStepIndex, action, null);

                if (!action.Enabled)
                {
                    LogMessage?.Invoke($"Step {_currentStepIndex}: Skipped (disabled) - {action.Type}");
                    continue;
                }

                // Check target
                _currentTargetInfo = action.Target?.ProcessName ?? action.Target?.WindowTitle ?? "Desktop";

                // Step 3: Central policy check
                var policyDecision = _policyEngine.EvaluateAction(action, workflow);
                if (!policyDecision.IsAllowed)
                {
                    var deniedMsg = $"Step {_currentStepIndex} denied by policy: {policyDecision.Reason}";
                    LogMessage?.Invoke(deniedMsg);
                    var failResult = ActionExecutionResult.Failure(action.Id, action.Type, deniedMsg, policyDecision.ViolationCode ?? "POLICY_DENIAL");
                    runRecord.StepResults.Add(failResult);
                    runRecord.FailedActionId = action.Id;
                    runRecord.SafeErrorMessage = deniedMsg;
                    runRecord.ErrorCode = policyDecision.ViolationCode;
                    runRecord.FinalState = ExecutionState.Failed;
                    CurrentState = ExecutionState.Failed;
                    await _backend.ReleaseInputStateAsync();
                    return runRecord;
                }

                // Step 4: Confirmation gate
                if (policyDecision.RequiresConfirmation && !dryRun)
                {
                    CurrentState = ExecutionState.AwaitingConfirmation;
                    LogMessage?.Invoke($"Step {_currentStepIndex} requires explicit confirmation before proceeding.");

                    bool confirmed = false;
                    if (ConfirmationRequested is not null)
                    {
                        confirmed = await ConfirmationRequested.Invoke(action);
                    }

                    if (!confirmed)
                    {
                        var cancelMsg = $"Step {_currentStepIndex} cancelled by user at confirmation gate.";
                        LogMessage?.Invoke(cancelMsg);
                        runRecord.FinalState = ExecutionState.Cancelled;
                        runRecord.SafeErrorMessage = cancelMsg;
                        CurrentState = ExecutionState.Cancelled;
                        await _backend.ReleaseInputStateAsync();
                        return runRecord;
                    }

                    CurrentState = ExecutionState.Running;
                }

                // Step 5: Inter-step delay
                if (action.DelayMs > 0)
                {
                    await Task.Delay(action.DelayMs, linkedToken);
                }

                // Dry-run mode: skip physical input execution
                if (dryRun)
                {
                    LogMessage?.Invoke($"[Dry Run] Step {_currentStepIndex}: Would execute {action.Type} target={_currentTargetInfo}");
                    var dryResult = ActionExecutionResult.Success(action.Id, action.Type, $"[Dry Run] Validated step {action.Type}", VerificationStatus.Verified);
                    runRecord.StepResults.Add(dryResult);
                    runRecord.ExecutedActions++;
                    StepProgress?.Invoke(_currentStepIndex, action, dryResult);
                    continue;
                }

                // Step 6: Verify target if required
                if (action.Target is not null && (action.Verification?.VerifyTargetExists ?? true))
                {
                    bool targetExists = await _backend.VerifyTargetAsync(action.Target, linkedToken);
                    if (!targetExists)
                    {
                        var targetFailMsg = $"Target not found or lost for step {_currentStepIndex} ({action.Type}). Stopping for safety.";
                        LogMessage?.Invoke(targetFailMsg);
                        var targetResult = ActionExecutionResult.Failure(action.Id, action.Type, targetFailMsg, "TARGET_NOT_FOUND", VerificationStatus.Failed);
                        runRecord.StepResults.Add(targetResult);
                        runRecord.FailedActionId = action.Id;
                        runRecord.SafeErrorMessage = targetFailMsg;
                        runRecord.ErrorCode = "TARGET_NOT_FOUND";
                        runRecord.FinalState = ExecutionState.Failed;
                        CurrentState = ExecutionState.Failed;
                        await _backend.ReleaseInputStateAsync();
                        return runRecord;
                    }
                }

                // Step 7: Execute with per-action bounded timeout
                int perActionTimeout = Math.Clamp(action.TimeoutMs, 500, workflow.Limits?.MaxActionTimeoutMs ?? 10000);
                using var actionTimeoutCts = new CancellationTokenSource(perActionTimeout);
                using var actionLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken, actionTimeoutCts.Token);

                var sw = Stopwatch.StartNew();
                ActionExecutionResult result;
                try
                {
                    result = await _backend.ExecuteActionAsync(action, actionLinkedCts.Token);
                    sw.Stop();
                    result.DurationMs = sw.ElapsedMilliseconds;
                }
                catch (OperationCanceledException) when (actionTimeoutCts.IsCancellationRequested && !linkedToken.IsCancellationRequested)
                {
                    sw.Stop();
                    result = ActionExecutionResult.Failure(
                        action.Id, action.Type,
                        $"Step {_currentStepIndex} timed out after {perActionTimeout}ms.",
                        "ACTION_TIMEOUT",
                        action.IsIdempotent ? VerificationStatus.Failed : VerificationStatus.UnknownOutcome);
                }

                runRecord.StepResults.Add(result);
                StepProgress?.Invoke(_currentStepIndex, action, result);

                if (!result.IsSuccess)
                {
                    LogMessage?.Invoke($"Step {_currentStepIndex} failed: {result.SafeMessage} (Outcome: {result.Status})");
                    runRecord.FailedActionId = action.Id;
                    runRecord.SafeErrorMessage = result.SafeMessage;
                    runRecord.ErrorCode = result.ErrorCode ?? "EXECUTION_ERROR";
                    runRecord.FinalState = ExecutionState.Failed;
                    runRecord.LastCheckpointId = lastCheckpointId;
                    CurrentState = ExecutionState.Failed;
                    await _backend.ReleaseInputStateAsync();
                    return runRecord;
                }

                if (string.Equals(action.Type, ActionTypes.Checkpoint, StringComparison.OrdinalIgnoreCase))
                {
                    lastCheckpointId = action.Id;
                    runRecord.LastCheckpointId = lastCheckpointId;
                    LogMessage?.Invoke($"Verified checkpoint reached: Step {_currentStepIndex} ({action.Description ?? action.Id})");
                }

                runRecord.ExecutedActions++;
            }

            runRecord.FinalState = ExecutionState.Completed;
            CurrentState = ExecutionState.Completed;
            LogMessage?.Invoke("Workflow execution successfully completed.");
        }
        catch (OperationCanceledException)
        {
            LogMessage?.Invoke("Execution cancelled by user or emergency stop.");
            runRecord.FinalState = ExecutionState.Cancelled;
            runRecord.SafeErrorMessage = "Execution was cancelled.";
            CurrentState = ExecutionState.Cancelled;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Unexpected error: {ex.Message}");
            runRecord.FinalState = ExecutionState.Failed;
            runRecord.SafeErrorMessage = "An unexpected error occurred during execution.";
            runRecord.ErrorCode = "UNEXPECTED_SYSTEM_ERROR";
            CurrentState = ExecutionState.Failed;
        }
        finally
        {
            await _backend.ReleaseInputStateAsync();
            runRecord.EndedUtc = DateTime.UtcNow;
        }

        return runRecord;
    }

    public Task PauseAsync()
    {
        lock (_stateLock)
        {
            if (_state == ExecutionState.Running)
            {
                _pauseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                CurrentState = ExecutionState.Paused;
                LogMessage?.Invoke("Playback paused.");
            }
        }
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        lock (_stateLock)
        {
            if (_state == ExecutionState.Paused && _pauseTcs is not null)
            {
                _pauseTcs.TrySetResult(true);
                _pauseTcs = null;
                CurrentState = ExecutionState.Running;
                LogMessage?.Invoke("Playback resumed.");
            }
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CurrentState = ExecutionState.Stopping;
        LogMessage?.Invoke("Stopping playback...");
        _pauseTcs?.TrySetCanceled();
        _workflowCts?.Cancel();
        await _backend.ReleaseInputStateAsync();
    }

    public async Task EmergencyStopAsync()
    {
        CurrentState = ExecutionState.Stopping;
        LogMessage?.Invoke("EMERGENCY STOP ACTIVATED: Immediate halt requested.");
        _pauseTcs?.TrySetCanceled();
        _workflowCts?.Cancel();
        await _backend.ReleaseInputStateAsync();
    }

    private async Task CheckPauseAsync(CancellationToken ct)
    {
        Task? pauseTask = null;
        lock (_stateLock)
        {
            if (_state == ExecutionState.Paused && _pauseTcs is not null)
            {
                pauseTask = _pauseTcs.Task;
            }
        }

        if (pauseTask is not null)
        {
            var cancelTcs = new TaskCompletionSource<bool>();
            using (ct.Register(() => cancelTcs.TrySetCanceled(ct)))
            {
                await Task.WhenAny(pauseTask, cancelTcs.Task);
                ct.ThrowIfCancellationRequested();
            }
        }
    }
}
