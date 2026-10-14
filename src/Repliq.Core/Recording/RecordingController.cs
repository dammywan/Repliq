using Repliq.Core.Models;

namespace Repliq.Core.Recording;

public sealed class RawRecordedEvent
{
    public string EventType { get; set; } = string.Empty; // "click", "key", "scroll", "window"
    public int X { get; set; }
    public int Y { get; set; }
    public string? Button { get; set; } // "left", "right", "middle"
    public string? Key { get; set; }
    public string? Modifiers { get; set; }
    public int DeltaY { get; set; }
    public string? ProcessName { get; set; }
    public string? WindowTitle { get; set; }
    public bool IsSensitiveField { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public interface IRecorderBackend
{
    event Action<RawRecordedEvent>? RawEventCaptured;
    Task StartCaptureAsync();
    Task StopCaptureAsync();
}

public enum RecordingState
{
    Idle,
    Starting,
    Recording,
    Stopping,
    Review,
    Saved
}

public sealed class RecordingConfig
{
    public int MaxEvents { get; set; } = 1000;
    public int MaxDurationSeconds { get; set; } = 600; // 10 minutes max
    public int MinEventIntervalMs { get; set; } = 50; // Filter micro-jitter
    public HashSet<string> ExcludedProcesses { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "repliq.app.exe", "repliq.exe", "repliq.quick.exe", "repliq.quick"
    };
    public bool ExcludeSensitiveFields { get; set; } = true;
}

public interface IRecordingController
{
    RecordingState State { get; }
    TimeSpan ElapsedTime { get; }
    int CapturedEventCount { get; }
    int RedactedEventCount { get; }
    string? CurrentTargetWindow { get; }
    WorkflowDefinition? CurrentDraftWorkflow { get; }

    event Action<RecordingState>? StateChanged;
    event Action<WorkflowAction>? ActionCaptured;
    event Action<string>? LogMessage;

    Task StartAsync();
    Task<WorkflowDefinition> StopAsync(string workflowName = "Recorded Workflow");
    Task CancelAsync();
}

public sealed class RecordingController : IRecordingController
{
    private readonly IRecorderBackend _backend;
    private readonly RecordingConfig _config;
    private readonly object _lock = new();

    private RecordingState _state = RecordingState.Idle;
    private DateTime? _startedUtc;
    private readonly List<WorkflowAction> _recordedActions = new();
    private DateTime? _lastEventTime;
    private string? _currentTargetWindow;
    private string? _currentTargetProcess;
    private int _redactedEventCount = 0;
    private WorkflowDefinition? _currentDraftWorkflow;

    public RecordingState State
    {
        get { lock (_lock) return _state; }
        private set
        {
            lock (_lock)
            {
                if (_state != value)
                {
                    _state = value;
                    Task.Run(() => StateChanged?.Invoke(value));
                }
            }
        }
    }

    public TimeSpan ElapsedTime => _startedUtc.HasValue ? DateTime.UtcNow - _startedUtc.Value : TimeSpan.Zero;
    public int CapturedEventCount { get { lock (_lock) return _recordedActions.Count; } }
    public int RedactedEventCount => _redactedEventCount;
    public string? CurrentTargetWindow => _currentTargetWindow;
    public WorkflowDefinition? CurrentDraftWorkflow { get { lock (_lock) return _currentDraftWorkflow; } }

    public event Action<RecordingState>? StateChanged;
    public event Action<WorkflowAction>? ActionCaptured;
    public event Action<string>? LogMessage;

    public RecordingController(IRecorderBackend backend, RecordingConfig? config = null)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _config = config ?? new RecordingConfig();
        _backend.RawEventCaptured += OnRawEventCaptured;
    }

    public async Task StartAsync()
    {
        lock (_lock)
        {
            if (_state != RecordingState.Idle && _state != RecordingState.Review && _state != RecordingState.Saved)
                throw new InvalidOperationException($"Cannot start recording from state {_state}.");

            _recordedActions.Clear();
            _redactedEventCount = 0;
            _startedUtc = DateTime.UtcNow;
            _lastEventTime = _startedUtc;
            _currentTargetWindow = null;
            _currentTargetProcess = null;
            _currentDraftWorkflow = null;
            State = RecordingState.Starting;
        }

        LogMessage?.Invoke("Initializing recording capture...");
        await _backend.StartCaptureAsync();
        State = RecordingState.Recording;
        LogMessage?.Invoke("Recording active. Input capture in progress.");
    }

    public async Task<WorkflowDefinition> StopAsync(string workflowName = "Recorded Workflow")
    {
        State = RecordingState.Stopping;
        LogMessage?.Invoke("Stopping input capture...");
        await _backend.StopCaptureAsync();

        WorkflowDefinition workflow;
        lock (_lock)
        {
            var allowedProcs = _recordedActions
                .Select(a => a.Target?.ProcessName)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => Path.GetFileName(p!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!string.IsNullOrWhiteSpace(_currentTargetProcess))
            {
                var fn = Path.GetFileName(_currentTargetProcess);
                if (!allowedProcs.Contains(fn, StringComparer.OrdinalIgnoreCase))
                    allowedProcs.Add(fn);
            }

            workflow = new WorkflowDefinition
            {
                Name = workflowName,
                CreatedUtc = DateTime.UtcNow,
                ModifiedUtc = DateTime.UtcNow,
                TargetPolicy = new TargetPolicy
                {
                    AllowedProcesses = allowedProcs
                },
                Actions = new List<WorkflowAction>(_recordedActions)
            };
            _currentDraftWorkflow = workflow;
            State = RecordingState.Review;
        }

        LogMessage?.Invoke($"Recording completed: {_recordedActions.Count} actions captured, {_redactedEventCount} sensitive events redacted.");
        return workflow;
    }

    public async Task CancelAsync()
    {
        State = RecordingState.Stopping;
        await _backend.StopCaptureAsync();
        lock (_lock)
        {
            _recordedActions.Clear();
            _currentDraftWorkflow = null;
            State = RecordingState.Idle;
        }
        LogMessage?.Invoke("Recording cancelled.");
    }

    private void OnRawEventCaptured(RawRecordedEvent raw)
    {
        lock (_lock)
        {
            if (_state != RecordingState.Recording)
                return;

            // Check event count bounds
            if (_recordedActions.Count >= _config.MaxEvents)
            {
                State = RecordingState.Stopping;
                LogMessage?.Invoke($"Maximum recording events ({_config.MaxEvents}) reached. Halting capture.");
                Task.Run(async () => await StopAsync());
                return;
            }

            // Check duration bounds
            if (ElapsedTime.TotalSeconds >= _config.MaxDurationSeconds)
            {
                State = RecordingState.Stopping;
                LogMessage?.Invoke($"Maximum recording duration ({_config.MaxDurationSeconds}s) reached. Halting capture.");
                Task.Run(async () => await StopAsync());
                return;
            }

            // Filter Repliq's own windows
            if (!string.IsNullOrWhiteSpace(raw.ProcessName))
            {
                var proc = raw.ProcessName.Trim();
                var fileName = Path.GetFileName(proc);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(proc);
                var nameWithExt = nameWithoutExt + ".exe";

                if (_config.ExcludedProcesses.Contains(proc) ||
                    _config.ExcludedProcesses.Contains(fileName) ||
                    _config.ExcludedProcesses.Contains(nameWithExt) ||
                    _config.ExcludedProcesses.Contains(nameWithoutExt))
                {
                    return;
                }
            }

            // Filter sensitive fields
            if (raw.IsSensitiveField && _config.ExcludeSensitiveFields)
            {
                _redactedEventCount++;
                return;
            }

            // Calculate delay
            var now = DateTime.UtcNow;
            int delayMs = _lastEventTime.HasValue
                ? Math.Clamp((int)(now - _lastEventTime.Value).TotalMilliseconds, 50, 5000)
                : 250;
            _lastEventTime = now;

            if (!string.IsNullOrWhiteSpace(raw.WindowTitle))
                _currentTargetWindow = raw.WindowTitle;
            if (!string.IsNullOrWhiteSpace(raw.ProcessName))
                _currentTargetProcess = raw.ProcessName;

            WorkflowAction? action = null;

            switch (raw.EventType.ToLowerInvariant())
            {
                case "click":
                    action = new WorkflowAction
                    {
                        Type = ActionTypes.Click,
                        Description = $"Click {raw.Button ?? "left"} button at ({raw.X}, {raw.Y})",
                        DelayMs = delayMs,
                        Target = new TargetSelector
                        {
                            SelectorType = "coordinate",
                            ProcessName = raw.ProcessName,
                            WindowTitle = raw.WindowTitle,
                            X = raw.X,
                            Y = raw.Y,
                            RelativeTo = "window"
                        },
                        Parameters = new Dictionary<string, object?>
                        {
                            ["button"] = raw.Button ?? "left",
                            ["x"] = raw.X,
                            ["y"] = raw.Y
                        }
                    };
                    break;

                case "key":
                    if (string.IsNullOrWhiteSpace(raw.Key) || string.Equals(raw.Key, "None", StringComparison.OrdinalIgnoreCase))
                        break;

                    action = new WorkflowAction
                    {
                        Type = ActionTypes.KeyPress,
                        Description = $"Press key '{raw.Key}'",
                        DelayMs = delayMs,
                        Target = new TargetSelector
                        {
                            SelectorType = "window",
                            ProcessName = raw.ProcessName,
                            WindowTitle = raw.WindowTitle
                        },
                        Parameters = new Dictionary<string, object?>
                        {
                            ["key"] = raw.Key ?? "Unknown",
                            ["modifiers"] = raw.Modifiers
                        }
                    };
                    break;

                case "scroll":
                    action = new WorkflowAction
                    {
                        Type = ActionTypes.Scroll,
                        Description = $"Scroll delta {raw.DeltaY}",
                        DelayMs = delayMs,
                        Target = new TargetSelector
                        {
                            SelectorType = "window",
                            ProcessName = raw.ProcessName,
                            WindowTitle = raw.WindowTitle
                        },
                        Parameters = new Dictionary<string, object?>
                        {
                            ["deltaY"] = raw.DeltaY
                        }
                    };
                    break;

                case "window":
                    action = new WorkflowAction
                    {
                        Type = ActionTypes.FocusWindow,
                        Description = $"Focus window '{raw.WindowTitle}'",
                        DelayMs = delayMs,
                        Target = new TargetSelector
                        {
                            SelectorType = "window",
                            ProcessName = raw.ProcessName,
                            WindowTitle = raw.WindowTitle
                        }
                    };
                    break;
            }

            if (action is not null)
            {
                _recordedActions.Add(action);
                ActionCaptured?.Invoke(action);
            }
        }
    }
}
