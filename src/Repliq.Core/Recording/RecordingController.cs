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
        "repliq.app.exe", "repliq.exe"
    };
    public bool ExcludeSensitiveFields { get; set; } = true;
}

public interface IRecordingController
{
    RecordingState State { get; }
    TimeSpan ElapsedTime { get; }
    int CapturedEventCount { get; }
    string? CurrentTargetWindow { get; }

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
            workflow = new WorkflowDefinition
            {
                Name = workflowName,
                CreatedUtc = DateTime.UtcNow,
                ModifiedUtc = DateTime.UtcNow,
                TargetPolicy = new TargetPolicy
                {
                    AllowedProcesses = _currentTargetProcess is not null ? new List<string> { _currentTargetProcess } : new List<string>()
                },
                Actions = new List<WorkflowAction>(_recordedActions)
            };
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
                LogMessage?.Invoke($"Maximum recording events ({_config.MaxEvents}) reached. Halting capture.");
                Task.Run(() => StopAsync());
                return;
            }

            // Check duration bounds
            if (ElapsedTime.TotalSeconds >= _config.MaxDurationSeconds)
            {
                LogMessage?.Invoke($"Maximum recording duration ({_config.MaxDurationSeconds}s) reached. Halting capture.");
                Task.Run(() => StopAsync());
                return;
            }

            // Filter Repliq's own windows
            if (!string.IsNullOrWhiteSpace(raw.ProcessName) && _config.ExcludedProcesses.Contains(raw.ProcessName))
            {
                return;
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
