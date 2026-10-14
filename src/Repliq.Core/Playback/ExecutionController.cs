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
    event Action<WorkflowRunRecord>? ExecutionCompleted;

    Task<WorkflowRunRecord> StartAsync(WorkflowDefinition workflow, bool dryRun = false, IReadOnlyDictionary<string, string>? variables = null);
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
    public event Action<WorkflowRunRecord>? ExecutionCompleted;

    public ExecutionController(IAutomationBackend backend, IPolicyEngine? policyEngine = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _policyEngine = policyEngine ?? new DefaultPolicyEngine();
    }

    public async Task<WorkflowRunRecord> StartAsync(
        WorkflowDefinition workflow,
        bool dryRun = false,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        // If controller was left in Stopping or previous run is unwinding, wait briefly
        if (CurrentState == ExecutionState.Stopping)
        {
            for (int i = 0; i < 15 && CurrentState == ExecutionState.Stopping; i++)
            {
                await Task.Delay(30);
            }
        }

        lock (_stateLock)
        {
            if (_state == ExecutionState.Stopping)
            {
                _state = ExecutionState.Cancelled;
            }

            if (_state is not (ExecutionState.Idle or ExecutionState.Completed or ExecutionState.Failed or ExecutionState.Cancelled))
            {
                throw new InvalidOperationException($"Cannot start workflow: current execution state is '{_state}'.");
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
        string? lastCheckpointId = null;

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

            var effectiveVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (workflow.Variables is not null)
            {
                foreach (var kvp in workflow.Variables)
                    effectiveVariables[kvp.Key] = kvp.Value;
            }
            if (variables is not null)
            {
                foreach (var kvp in variables)
                    effectiveVariables[kvp.Key] = kvp.Value;
            }

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
                runRecord.TargetProcess ??= action.Target?.ProcessName ?? action.Target?.WindowTitle;

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

                // Variable resolution for TypeText actions
                var actionToExecute = action;
                if (action.Type == ActionTypes.TypeText)
                {
                    actionToExecute = new WorkflowAction
                    {
                        Id = action.Id,
                        Type = action.Type,
                        Description = action.Description,
                        DelayMs = action.DelayMs,
                        TimeoutMs = action.TimeoutMs,
                        Enabled = action.Enabled,
                        RequiresConfirmation = action.RequiresConfirmation,
                        IsIdempotent = action.IsIdempotent,
                        Target = action.Target,
                        Verification = action.Verification,
                        Parameters = new Dictionary<string, object?>(action.Parameters)
                    };

                    if (action.Parameters.TryGetValue("variable", out var varNameObj) && varNameObj is not null)
                    {
                        var varName = varNameObj.ToString()!;
                        if (effectiveVariables.TryGetValue(varName, out var resolvedVal))
                        {
                            actionToExecute.Parameters["text"] = resolvedVal;
                        }
                        else
                        {
                            var missingVarMsg = $"Missing required variable '{varName}' for step {_currentStepIndex}.";
                            LogMessage?.Invoke(missingVarMsg);
                            var varResult = ActionExecutionResult.Failure(action.Id, action.Type, missingVarMsg, "MISSING_VARIABLE");
                            runRecord.StepResults.Add(varResult);
                            runRecord.FailedActionId = action.Id;
                            runRecord.SafeErrorMessage = missingVarMsg;
                            runRecord.ErrorCode = "MISSING_VARIABLE";
                            runRecord.FinalState = ExecutionState.Failed;
                            CurrentState = ExecutionState.Failed;
                            await _backend.ReleaseInputStateAsync();
                            return runRecord;
                        }
                    }
                    else if (action.Parameters.TryGetValue("text", out var textObj) && textObj is string tStr)
                    {
                        foreach (var (k, v) in effectiveVariables)
                        {
                            tStr = tStr.Replace("{" + k + "}", v);
                            tStr = tStr.Replace("${Var:" + k + "}", v, StringComparison.OrdinalIgnoreCase);
                        }

                        var envMatches = System.Text.RegularExpressions.Regex.Matches(tStr, @"\$\{Env:([a-zA-Z0-9_]+)\}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        foreach (System.Text.RegularExpressions.Match m in envMatches)
                        {
                            var envKey = m.Groups[1].Value;
                            var envVal = Environment.GetEnvironmentVariable(envKey) ?? string.Empty;
                            tStr = tStr.Replace(m.Value, envVal);
                        }

                        actionToExecute.Parameters["text"] = tStr;
                    }
                }

                // Step 4: Confirmation gate
                if (policyDecision.RequiresConfirmation && !dryRun)
                {
                    CurrentState = ExecutionState.AwaitingConfirmation;
                    LogMessage?.Invoke($"Step {_currentStepIndex} requires explicit confirmation before proceeding.");

                    bool confirmed = false;
                    if (ConfirmationRequested is not null)
                    {
                        confirmed = await ConfirmationRequested.Invoke(actionToExecute);
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
                if (actionToExecute.DelayMs > 0)
                {
                    await Task.Delay(actionToExecute.DelayMs, linkedToken);
                }

                // Dry-run mode: skip physical input execution
                if (dryRun)
                {
                    LogMessage?.Invoke($"[Dry Run] Step {_currentStepIndex}: Would execute {actionToExecute.Type} target={_currentTargetInfo}");
                    var dryResult = ActionExecutionResult.Success(actionToExecute.Id, actionToExecute.Type, $"[Dry Run] Validated step {actionToExecute.Type}", VerificationStatus.Verified);
                    runRecord.StepResults.Add(dryResult);
                    runRecord.ExecutedActions++;
                    runRecord.LastSuccessfulActionId = actionToExecute.Id;
                    StepProgress?.Invoke(_currentStepIndex, actionToExecute, dryResult);
                    continue;
                }

                // Step 6: Verify target if required
                if (actionToExecute.Target is not null && (actionToExecute.Verification?.VerifyTargetExists ?? true))
                {
                    bool targetExists = await _backend.VerifyTargetAsync(actionToExecute.Target, linkedToken);
                    if (!targetExists)
                    {
                        var targetFailMsg = $"Target not found or lost for step {_currentStepIndex} ({actionToExecute.Type}). Stopping for safety.";
                        LogMessage?.Invoke(targetFailMsg);
                        var targetResult = ActionExecutionResult.Failure(actionToExecute.Id, actionToExecute.Type, targetFailMsg, "TARGET_NOT_FOUND", VerificationStatus.Failed);
                        runRecord.StepResults.Add(targetResult);
                        runRecord.FailedActionId = actionToExecute.Id;
                        runRecord.SafeErrorMessage = targetFailMsg;
                        runRecord.ErrorCode = "TARGET_NOT_FOUND";
                        runRecord.FinalState = ExecutionState.Failed;
                        CurrentState = ExecutionState.Failed;
                        await _backend.ReleaseInputStateAsync();
                        return runRecord;
                    }
                }

                // Step 7: Execute with per-action bounded timeout
                int perActionTimeout = Math.Clamp(actionToExecute.TimeoutMs, 500, workflow.Limits?.MaxActionTimeoutMs ?? 10000);
                using var actionTimeoutCts = new CancellationTokenSource(perActionTimeout);
                using var actionLinkedCts = CancellationTokenSource.CreateLinkedTokenSource(linkedToken, actionTimeoutCts.Token);

                var sw = Stopwatch.StartNew();
                ActionExecutionResult result;
                try
                {
                    result = await _backend.ExecuteActionAsync(actionToExecute, actionLinkedCts.Token);
                    sw.Stop();
                    result.DurationMs = sw.ElapsedMilliseconds;
                }
                catch (OperationCanceledException) when (actionTimeoutCts.IsCancellationRequested && !linkedToken.IsCancellationRequested)
                {
                    sw.Stop();
                    result = ActionExecutionResult.Failure(
                        actionToExecute.Id, actionToExecute.Type,
                        $"Step {_currentStepIndex} timed out after {perActionTimeout}ms.",
                        "ACTION_TIMEOUT",
                        actionToExecute.IsIdempotent ? VerificationStatus.Failed : VerificationStatus.UnknownOutcome);
                }

                runRecord.StepResults.Add(result);
                StepProgress?.Invoke(_currentStepIndex, actionToExecute, result);

                if (!result.IsSuccess)
                {
                    LogMessage?.Invoke($"Step {_currentStepIndex} failed: {result.SafeMessage} (Outcome: {result.Status})");
                    runRecord.FailedActionId = actionToExecute.Id;
                    runRecord.SafeErrorMessage = result.SafeMessage;
                    runRecord.ErrorCode = result.ErrorCode ?? "EXECUTION_ERROR";
                    runRecord.FinalState = ExecutionState.Failed;
                    CurrentState = ExecutionState.Failed;
                    await _backend.ReleaseInputStateAsync();
                    return runRecord;
                }

                if (string.Equals(actionToExecute.Type, ActionTypes.Checkpoint, StringComparison.OrdinalIgnoreCase))
                {
                    lastCheckpointId = actionToExecute.Id;
                    LogMessage?.Invoke($"Verified checkpoint reached: Step {_currentStepIndex} ({actionToExecute.Description ?? actionToExecute.Id})");
                }

                runRecord.ExecutedActions++;
                runRecord.LastSuccessfulActionId = actionToExecute.Id;
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
            runRecord.LastCheckpointId = lastCheckpointId;
            runRecord.EndedUtc = DateTime.UtcNow;
            await _backend.ReleaseInputStateAsync();

            lock (_stateLock)
            {
                CurrentState = runRecord.FinalState;
            }

            _ = Task.Run(() => ExecutionCompleted?.Invoke(runRecord));
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
        lock (_stateLock)
        {
            if (_state is ExecutionState.Idle or ExecutionState.Completed or ExecutionState.Failed or ExecutionState.Cancelled)
            {
                return;
            }
            CurrentState = ExecutionState.Stopping;
        }
        LogMessage?.Invoke("Stopping playback...");
        _pauseTcs?.TrySetCanceled();
        _workflowCts?.Cancel();
        await _backend.ReleaseInputStateAsync();
    }

    public async Task EmergencyStopAsync()
    {
        lock (_stateLock)
        {
            if (_state is ExecutionState.Idle or ExecutionState.Completed or ExecutionState.Failed or ExecutionState.Cancelled)
            {
                return;
            }
            CurrentState = ExecutionState.Stopping;
        }
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
