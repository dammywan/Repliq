using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Repliq.Core.Models;
using Repliq.Core.Playback;
using Repliq.Core.Recording;
using Repliq.Storage.History;
using Repliq.Storage.Workflows;

namespace Repliq.App;

public sealed class QuickPanelForm : Form
{
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IExecutionController _executionController;
    private readonly IRecordingController _recordingController;
    private readonly IHistoryRepository? _historyRepo;
    private readonly Action? _openMainWindowAction;
    private readonly bool _isStandalone;

    // Quick Mimic In-Memory State
    private WorkflowDefinition? _quickMimicWorkflow;
    private WorkflowDefinition? _selectedSavedWorkflow;
    private List<WorkflowDefinition> _savedWorkflows = new();
    private int _repeatCount = 1;
    private double _playbackSpeed = 1.0;
    private CancellationTokenSource? _playbackCts;
    private bool _isPinned = true;

    // Controls
    private readonly Label _lblDragGrip = new();
    private readonly Label _lblBrand = new();
    private readonly Label _lblStatusDot = new();
    private readonly Label _lblStatusText = new();
    private readonly QuickToolbarButton _btnRecord = new();
    private readonly QuickToolbarButton _btnPlay = new();
    private readonly QuickToolbarButton _btnPause = new();
    private readonly QuickToolbarButton _btnStop = new();
    private readonly QuickToolbarButton _btnOptions = new();
    private readonly QuickToolbarButton _btnPin = new();
    private readonly QuickToolbarButton _btnClose = new();
    private readonly ToolTip _toolTip = new();
    private readonly ContextMenuStrip _optionsMenu = new();

    public WorkflowDefinition? SelectedWorkflow
    {
        get => _isStandalone ? _quickMimicWorkflow : (_selectedSavedWorkflow ?? _quickMimicWorkflow);
        set
        {
            if (_isStandalone)
            {
                _quickMimicWorkflow = value;
            }
            else
            {
                _selectedSavedWorkflow = value;
            }
        }
    }

    public WorkflowDefinition? QuickMimicWorkflow => _quickMimicWorkflow;

    public QuickPanelForm(
        IWorkflowRepository workflowRepo,
        IExecutionController executionController,
        IRecordingController recordingController,
        Action openMainWindowAction)
        : this(workflowRepo, executionController, recordingController, null, openMainWindowAction, false)
    {
    }

    public QuickPanelForm(
        IWorkflowRepository workflowRepo,
        IExecutionController executionController,
        IRecordingController recordingController,
        IHistoryRepository? historyRepo,
        Action? openMainWindowAction,
        bool isStandalone = false)
    {
        _workflowRepo = workflowRepo;
        _executionController = executionController;
        _recordingController = recordingController;
        _historyRepo = historyRepo;
        _openMainWindowAction = openMainWindowAction;
        _isStandalone = isStandalone;

        InitializeComponents();

        _executionController.StateChanged += OnExecutionStateChanged;
        _recordingController.StateChanged += OnRecordingStateChanged;

        Load += async (_, _) => await RefreshWorkflowsAsync();
    }

    private void InitializeComponents()
    {
        Text = _isStandalone ? "Repliq Quick" : "Repliq Quick Panel";
        if (UITheme.AppIcon != null) Icon = UITheme.AppIcon;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;
        ShowInTaskbar = _isStandalone;
        Size = new Size(515, 46);
        MinimumSize = new Size(460, 46);
        MaximumSize = new Size(620, 46);
        TopMost = true;

        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(Math.Max(50, screen.Right - 535), 45);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 9,
            RowCount = 1,
            Padding = new Padding(6, 4, 6, 4),
            Margin = new Padding(0)
        };

        // Columns:
        // 0: Grip / Brand: AutoSize (~62px)
        // 1: Status Dot & Info: Percent 100 (~115px)
        // 2: Record Button: 60px
        // 3: Play Button: 60px
        // 4: Pause Button: 32px
        // 5: Stop Button: 32px
        // 6: Options Button: 32px
        // 7: Pin Button: 32px
        // 8: Close Button: 32px
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));

        // 0. Drag Grip + Brand Label + Logo
        var pnlBrand = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 5, 4, 0)
        };
        _lblDragGrip.Text = "⠿";
        _lblDragGrip.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _lblDragGrip.ForeColor = UITheme.Current.SecondaryText;
        _lblDragGrip.Tag = "secondary";
        _lblDragGrip.AutoSize = true;
        _lblDragGrip.Cursor = Cursors.SizeAll;
        _lblDragGrip.Margin = new Padding(0, 1, 2, 0);

        if (UITheme.AppLogo != null)
        {
            var pbLogo = new PictureBox
            {
                Image = UITheme.AppLogo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(18, 18),
                Margin = new Padding(0, 1, 4, 0),
                Cursor = Cursors.SizeAll
            };
            pbLogo.MouseDown += OnDragMouseDown;
            pnlBrand.Controls.Add(pbLogo);
        }

        _lblBrand.Text = "Quick";
        _lblBrand.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _lblBrand.ForeColor = UITheme.Current.PrimaryText;
        _lblBrand.AutoSize = true;
        _lblBrand.Cursor = Cursors.SizeAll;
        _lblBrand.Margin = new Padding(0, 1, 4, 0);

        pnlBrand.Controls.AddRange(new Control[] { _lblDragGrip, _lblBrand });
        mainLayout.Controls.Add(pnlBrand, 0, 0);

        // 1. Status Panel (Dot + Text)
        var pnlStatus = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 7, 4, 0)
        };
        _lblStatusDot.Text = "●";
        _lblStatusDot.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
        _lblStatusDot.ForeColor = UITheme.Current.Success;
        _lblStatusDot.AutoSize = true;
        _lblStatusDot.Margin = new Padding(0, 2, 3, 0);

        _lblStatusText.Text = "Mimic: 0 steps";
        _lblStatusText.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular);
        _lblStatusText.ForeColor = UITheme.Current.SecondaryText;
        _lblStatusText.Tag = "secondary";
        _lblStatusText.AutoSize = true;
        _lblStatusText.AutoEllipsis = true;
        _lblStatusText.Margin = new Padding(0, 1, 0, 0);

        pnlStatus.Controls.AddRange(new Control[] { _lblStatusDot, _lblStatusText });
        mainLayout.Controls.Add(pnlStatus, 1, 0);

        // 2. Record Button
        _btnRecord.Text = "● Rec";
        _btnRecord.Click += async (_, _) => await ToggleRecordingAsync();
        _toolTip.SetToolTip(_btnRecord, "Record Quick Mimic in-memory (Ctrl+Shift+R)");
        mainLayout.Controls.Add(_btnRecord, 2, 0);

        // 3. Play Button
        _btnPlay.Text = "▶ Play";
        _btnPlay.IsPlayButton = true;
        _btnPlay.Enabled = false;
        _btnPlay.Click += async (_, _) => await StartPlaybackAsync();
        _toolTip.SetToolTip(_btnPlay, "Play workflow unlimited times (Ctrl+Shift+P)");
        mainLayout.Controls.Add(_btnPlay, 3, 0);

        // 4. Pause Button
        _btnPause.Glyph = ToolbarGlyph.Pause;
        _btnPause.Enabled = false;
        _btnPause.Click += async (_, _) => await TogglePauseAsync();
        _toolTip.SetToolTip(_btnPause, "Pause / Resume");
        mainLayout.Controls.Add(_btnPause, 4, 0);

        // 5. Stop Button
        _btnStop.Glyph = ToolbarGlyph.Stop;
        _btnStop.Enabled = false;
        _btnStop.Click += async (_, _) => await StopActiveRunAsync();
        _toolTip.SetToolTip(_btnStop, "Stop recording or playback (Ctrl+Shift+S)");
        mainLayout.Controls.Add(_btnStop, 5, 0);

        // 6. Options Button
        _btnOptions.Glyph = ToolbarGlyph.Options;
        _btnOptions.Click += (_, _) => ShowOptionsMenu();
        _toolTip.SetToolTip(_btnOptions, _isStandalone ? "Quick Mimic Options (Repeat, Speed)" : "Quick Mimic Options (Repeat, Speed, Workflows)");
        mainLayout.Controls.Add(_btnOptions, 6, 0);

        // 7. Pin Button
        _btnPin.Glyph = ToolbarGlyph.Pin;
        _btnPin.IsPinned = _isPinned;
        _btnPin.Click += (_, _) =>
        {
            _isPinned = !_isPinned;
            TopMost = _isPinned;
            _btnPin.IsPinned = _isPinned;
            _toolTip.SetToolTip(_btnPin, _isPinned ? "Pinned (Always on top)" : "Unpinned");
        };
        _toolTip.SetToolTip(_btnPin, "Pin on top (Always on top)");
        mainLayout.Controls.Add(_btnPin, 7, 0);

        // 8. Close Button
        _btnClose.Glyph = ToolbarGlyph.Close;
        _btnClose.Click += (_, _) => Close();
        _toolTip.SetToolTip(_btnClose, _isStandalone ? "Close Repliq Quick" : "Hide Quick Panel");
        mainLayout.Controls.Add(_btnClose, 8, 0);

        // Dragging handlers on non-button elements
        MouseDown += OnDragMouseDown;
        mainLayout.MouseDown += OnDragMouseDown;
        pnlBrand.MouseDown += OnDragMouseDown;
        _lblDragGrip.MouseDown += OnDragMouseDown;
        _lblBrand.MouseDown += OnDragMouseDown;
        pnlStatus.MouseDown += OnDragMouseDown;
        _lblStatusDot.MouseDown += OnDragMouseDown;
        _lblStatusText.MouseDown += OnDragMouseDown;

        Controls.Add(mainLayout);
        UITheme.Apply(this);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(UITheme.Current.CardBorder, 1);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HT_CAPTION = 0x2;

    private void OnDragMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
        }
    }

    public async Task RefreshWorkflowsAsync()
    {
        if (_isStandalone)
        {
            _savedWorkflows = new();
            UpdateWorkflowSelectionUI();
            return;
        }

        try
        {
            _savedWorkflows = (await _workflowRepo.GetAllAsync()).ToList();
        }
        catch
        {
            _savedWorkflows = new();
        }
        UpdateWorkflowSelectionUI();
    }

    private void UpdateWorkflowSelectionUI()
    {
        if (InvokeRequired)
        {
            Invoke(new Action(UpdateWorkflowSelectionUI));
            return;
        }

        var wf = SelectedWorkflow;
        if (wf is not null && wf.Actions.Count > 0)
        {
            string label = (_isStandalone || wf == _quickMimicWorkflow) ? "Mimic" : wf.Name;
            _lblStatusText.Text = $"{label}: {wf.Actions.Count} steps";
            _btnPlay.Enabled = _executionController.CurrentState is ExecutionState.Idle or ExecutionState.Completed or ExecutionState.Cancelled or ExecutionState.Failed;
        }
        else
        {
            _lblStatusText.Text = "Mimic: 0 steps";
            _btnPlay.Enabled = false;
        }
    }

    public async Task ToggleRecordingAsync()
    {
        if (_recordingController.State == RecordingState.Recording)
        {
            await StopRecordingAsync();
        }
        else
        {
            try
            {
                await _recordingController.StartAsync();
                SetStatus("Recording...", "Capturing actions", isRecording: true);
            }
            catch (Exception ex)
            {
                SetStatus("Error", ex.Message, isError: true);
            }
        }
    }

    public async Task StopRecordingAsync()
    {
        try
        {
            var wf = await _recordingController.StopAsync();
            wf.Name = "Quick Mimic";
            _quickMimicWorkflow = wf;
            _selectedSavedWorkflow = null;

            SetStatus("Mimic Ready", $"{wf.Actions.Count} steps", isSuccess: true);
            _btnPlay.Enabled = wf.Actions.Count > 0;
        }
        catch (Exception ex)
        {
            SetStatus("Error", ex.Message, isError: true);
        }
    }

    public async Task StartPlaybackAsync()
    {
        var wf = SelectedWorkflow;
        if (wf is null || wf.Actions.Count == 0)
        {
            SetStatus("No actions", "Record actions first", isWarning: true);
            return;
        }

        if (_executionController.CurrentState is ExecutionState.Running or ExecutionState.Starting or ExecutionState.Stopping)
        {
            return;
        }

        _playbackCts = new CancellationTokenSource();
        var token = _playbackCts.Token;

        int iterations = _repeatCount;
        string wfName = wf == _quickMimicWorkflow ? "Mimic" : wf.Name;

        try
        {
            await Task.Run(async () =>
            {
                for (int iter = 1; iter <= iterations && !token.IsCancellationRequested; iter++)
                {
                    string iterMsg = iterations == int.MaxValue ? $" (Loop #{iter})" : (iterations > 1 ? $" ({iter}/{iterations})" : "");
                    SetStatus($"Playing{iterMsg}", $"{wfName} ({wf.Actions.Count})", isRunning: true);

                    var scaledWf = ScaleWorkflowSpeed(wf, _playbackSpeed);
                    var record = await _executionController.StartAsync(scaledWf);

                    if (_historyRepo is not null)
                    {
                        try { await _historyRepo.RecordRunAsync(record); } catch { }
                    }

                    if (record.FinalState != ExecutionState.Completed)
                    {
                        SetStatus("Halted", record.SafeErrorMessage ?? "Workflow stopped", isError: true);
                        break;
                    }

                    if (iter < iterations && !token.IsCancellationRequested)
                    {
                        await Task.Delay(200, token);
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
            SetStatus("Stopped", "Playback cancelled");
        }
        catch (Exception ex)
        {
            SetStatus("Failed", ex.Message, isError: true);
        }
    }

    private static WorkflowDefinition ScaleWorkflowSpeed(WorkflowDefinition source, double speed)
    {
        if (Math.Abs(speed - 1.0) < 0.05) return source;

        return new WorkflowDefinition
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            SchemaVersion = source.SchemaVersion,
            TargetPolicy = source.TargetPolicy,
            Limits = source.Limits,
            Metadata = new Dictionary<string, string>(source.Metadata),
            Actions = source.Actions.Select(a => new WorkflowAction
            {
                Id = a.Id,
                Type = a.Type,
                Description = a.Description,
                DelayMs = Math.Max(10, (int)(a.DelayMs / speed)),
                TimeoutMs = a.TimeoutMs,
                Enabled = a.Enabled,
                RequiresConfirmation = a.RequiresConfirmation,
                IsIdempotent = a.IsIdempotent,
                Target = a.Target,
                Verification = a.Verification,
                Parameters = new Dictionary<string, object?>(a.Parameters)
            }).ToList()
        };
    }

    private async Task TogglePauseAsync()
    {
        if (_executionController.CurrentState == ExecutionState.Running)
        {
            await _executionController.PauseAsync();
        }
        else if (_executionController.CurrentState == ExecutionState.Paused)
        {
            await _executionController.ResumeAsync();
        }
    }

    public async Task StopActiveRunAsync()
    {
        _playbackCts?.Cancel();

        if (_recordingController.State == RecordingState.Recording)
        {
            await StopRecordingAsync();
        }
        else
        {
            await _executionController.EmergencyStopAsync();
        }
    }

    private void SetStatus(string title, string details, bool isRunning = false, bool isRecording = false, bool isError = false, bool isSuccess = false, bool isWarning = false)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => SetStatus(title, details, isRunning, isRecording, isError, isSuccess, isWarning)));
            return;
        }

        _lblStatusText.Text = $"{title} ({details})";

        if (isError)
        {
            _lblStatusDot.Text = "✕";
            _lblStatusDot.ForeColor = UITheme.Current.Error;
            _lblStatusText.ForeColor = UITheme.Current.Error;
        }
        else if (isRecording)
        {
            _lblStatusDot.Text = "●";
            _lblStatusDot.ForeColor = UITheme.Current.Error;
            _lblStatusText.ForeColor = UITheme.Current.Error;
        }
        else if (isRunning)
        {
            _lblStatusDot.Text = "▶";
            _lblStatusDot.ForeColor = UITheme.Current.Accent;
            _lblStatusText.ForeColor = UITheme.Current.PrimaryText;
        }
        else if (isSuccess)
        {
            _lblStatusDot.Text = "✓";
            _lblStatusDot.ForeColor = UITheme.Current.Success;
            _lblStatusText.ForeColor = UITheme.Current.PrimaryText;
        }
        else if (isWarning)
        {
            _lblStatusDot.Text = "!";
            _lblStatusDot.ForeColor = UITheme.Current.Warning;
            _lblStatusText.ForeColor = UITheme.Current.Warning;
        }
        else
        {
            _lblStatusDot.Text = "●";
            _lblStatusDot.ForeColor = UITheme.Current.Success;
            _lblStatusText.ForeColor = UITheme.Current.SecondaryText;
        }
    }

    private void OnExecutionStateChanged(ExecutionState state)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnExecutionStateChanged(state)));
            return;
        }

        switch (state)
        {
            case ExecutionState.Running:
                _btnPlay.Enabled = false;
                _btnRecord.Enabled = false;
                _btnPause.Enabled = true;
                _btnPause.Glyph = ToolbarGlyph.Pause;
                _btnPause.ActiveBorder = null;
                _btnStop.Enabled = true;
                break;

            case ExecutionState.Paused:
                _btnPause.Enabled = true;
                _btnPause.Glyph = ToolbarGlyph.Resume;
                _btnPause.ActiveBorder = UITheme.Current.Warning;
                SetStatus("Paused", "Click Pause/Resume", isWarning: true);
                break;

            case ExecutionState.Stopping:
                SetStatus("Stopping", "Halting run...");
                _btnPlay.Enabled = false;
                _btnRecord.Enabled = false;
                _btnPause.Enabled = false;
                _btnPause.Glyph = ToolbarGlyph.Pause;
                _btnPause.ActiveBorder = null;
                _btnStop.Enabled = false;
                break;

            case ExecutionState.Completed:
                SetStatus("Ready", $"{SelectedWorkflow?.Actions.Count ?? 0} steps", isSuccess: true);
                _btnPlay.Enabled = SelectedWorkflow?.Actions.Count > 0;
                _btnRecord.Enabled = true;
                _btnPause.Enabled = false;
                _btnPause.Glyph = ToolbarGlyph.Pause;
                _btnPause.ActiveBorder = null;
                _btnStop.Enabled = false;
                break;

            case ExecutionState.Failed:
                _btnPlay.Enabled = SelectedWorkflow?.Actions.Count > 0;
                _btnRecord.Enabled = true;
                _btnPause.Enabled = false;
                _btnPause.Glyph = ToolbarGlyph.Pause;
                _btnPause.ActiveBorder = null;
                _btnStop.Enabled = false;
                break;

            case ExecutionState.Cancelled:
            case ExecutionState.Idle:
                SetStatus("Ready", $"{SelectedWorkflow?.Actions.Count ?? 0} steps");
                _btnPlay.Enabled = SelectedWorkflow?.Actions.Count > 0;
                _btnRecord.Enabled = true;
                _btnPause.Enabled = false;
                _btnPause.Glyph = ToolbarGlyph.Pause;
                _btnPause.ActiveBorder = null;
                _btnStop.Enabled = false;
                break;
        }
    }

    private void OnRecordingStateChanged(RecordingState state)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnRecordingStateChanged(state)));
            return;
        }

        switch (state)
        {
            case RecordingState.Recording:
                _btnRecord.Text = "■ Stop";
                _btnRecord.IsRecordingActive = true;
                _btnPlay.Enabled = false;
                _btnStop.Enabled = true;
                SetStatus("Recording", "Capturing actions...", isRecording: true);
                break;

            default:
                _btnRecord.Text = "● Rec";
                _btnRecord.IsRecordingActive = false;
                if (_recordingController.CurrentDraftWorkflow is not null && _recordingController.CurrentDraftWorkflow.Actions.Count > 0)
                {
                    _quickMimicWorkflow = _recordingController.CurrentDraftWorkflow;
                    _quickMimicWorkflow.Name = "Quick Mimic";
                    _selectedSavedWorkflow = null;
                    SetStatus("Mimic Ready", $"{_quickMimicWorkflow.Actions.Count} steps", isSuccess: true);
                }
                _btnPlay.Enabled = SelectedWorkflow?.Actions.Count > 0;
                _btnStop.Enabled = false;
                break;
        }
    }

    private void ShowOptionsMenu()
    {
        BuildOptionsMenu();
        _optionsMenu.Show(_btnOptions, new Point(0, _btnOptions.Height + 2));
    }

    public ContextMenuStrip OptionsMenu => _optionsMenu;

    public void BuildOptionsMenu()
    {
        _optionsMenu.Items.Clear();
        _optionsMenu.BackColor = UITheme.Current.MainSurface;
        _optionsMenu.ForeColor = UITheme.Current.PrimaryText;
        _optionsMenu.Renderer = new DarkToolStripRenderer();

        // 1. Repeat / Loop Submenu
        var repeatMenu = CreateSubMenu("Repeat / Loop");
        int[] repeatOptions = { 1, 2, 5, 10, int.MaxValue };
        string[] repeatLabels = { "1x (Play once)", "2x", "5x", "10x", "Continuous Loop (Infinite)" };
        for (int i = 0; i < repeatOptions.Length; i++)
        {
            int count = repeatOptions[i];
            string label = repeatLabels[i];
            var item = CreateMenuItem(label, _repeatCount == count, (_, _) =>
            {
                _repeatCount = count;
                SetStatus("Option set", $"Repeat: {label}");
            });
            repeatMenu.DropDownItems.Add(item);
        }
        _optionsMenu.Items.Add(repeatMenu);

        // 2. Playback Speed Submenu
        var speedMenu = CreateSubMenu("Playback Speed");
        double[] speeds = { 0.5, 1.0, 1.5, 2.0 };
        string[] speedLabels = { "0.5x (Slow)", "1.0x (Normal)", "1.5x (Fast)", "2.0x (Turbo)" };
        for (int i = 0; i < speeds.Length; i++)
        {
            double s = speeds[i];
            string label = speedLabels[i];
            var item = CreateMenuItem(label, Math.Abs(_playbackSpeed - s) < 0.01, (_, _) =>
            {
                _playbackSpeed = s;
                SetStatus("Speed set", $"Speed: {s}x");
            });
            speedMenu.DropDownItems.Add(item);
        }
        _optionsMenu.Items.Add(speedMenu);

        // In standalone mode (Repliq.Quick), hide all library/studio/panel options
        if (!_isStandalone)
        {
            _optionsMenu.Items.Add(CreateSeparator());

            // Workflow Selection Submenu
            var wfMenu = CreateSubMenu("Select Workflow");
            var mimicItem = CreateMenuItem($"[✓ In-Memory] Quick Mimic ({_quickMimicWorkflow?.Actions.Count ?? 0} steps)", _selectedSavedWorkflow == null, (_, _) =>
            {
                _selectedSavedWorkflow = null;
                UpdateWorkflowSelectionUI();
            });
            wfMenu.DropDownItems.Add(mimicItem);

            if (_savedWorkflows.Count > 0)
            {
                wfMenu.DropDownItems.Add(CreateSeparator());
                foreach (var savedWf in _savedWorkflows)
                {
                    var item = CreateMenuItem(savedWf.Name, _selectedSavedWorkflow?.Id == savedWf.Id, (_, _) =>
                    {
                        _selectedSavedWorkflow = savedWf;
                        UpdateWorkflowSelectionUI();
                    });
                    wfMenu.DropDownItems.Add(item);
                }
            }
            _optionsMenu.Items.Add(wfMenu);

            if (_quickMimicWorkflow is not null && _quickMimicWorkflow.Actions.Count > 0)
            {
                var saveItem = CreateMenuItem("💾 Save Mimic to Library...", false, async (_, _) =>
                {
                    await PromptSaveMimicAsync();
                });
                _optionsMenu.Items.Add(saveItem);
            }
        }

        // Clear Mimic (available in both standalone and full mode if mimic exists)
        if (_quickMimicWorkflow is not null && _quickMimicWorkflow.Actions.Count > 0)
        {
            _optionsMenu.Items.Add(CreateSeparator());
            var clearItem = CreateMenuItem("🗑 Clear Mimic", false, (_, _) =>
            {
                _quickMimicWorkflow = null;
                _selectedSavedWorkflow = null;
                UpdateWorkflowSelectionUI();
                SetStatus("Ready", "Mimic cleared");
            });
            _optionsMenu.Items.Add(clearItem);
        }

        _optionsMenu.Items.Add(CreateSeparator());

        if (!_isStandalone && _openMainWindowAction is not null)
        {
            var openMainItem = CreateMenuItem("↗ Open Full Repliq Studio", false, (_, _) =>
            {
                _openMainWindowAction();
            });
            _optionsMenu.Items.Add(openMainItem);
        }

        var aboutItem = CreateMenuItem("ℹ️ About Repliq...", false, (_, _) =>
        {
            MessageBox.Show(
                "Repliq — Desktop Workflow Automation\n" +
                "Version: 0.1.0-alpha\n" +
                "Developed by RetsardCo (2026)\n\n" +
                "Architecture:\n" +
                "• Standalone Lightweight Quick Toolbar (Repliq.Quick)\n" +
                "• Full Studio Workspace & Policy Engine (Repliq.App)\n\n" +
                "MIT License — Copyright © 2026 RetsardCo",
                "About Repliq",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        });
        _optionsMenu.Items.Add(aboutItem);

        var exitItem = CreateMenuItem("✕ Close", false, (_, _) => Close());
        _optionsMenu.Items.Add(exitItem);
    }

    private static ToolStripMenuItem CreateSubMenu(string text)
    {
        var menu = new ToolStripMenuItem(text)
        {
            BackColor = UITheme.Current.MainSurface,
            ForeColor = UITheme.Current.PrimaryText,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular)
        };
        menu.DropDown.Renderer = new DarkToolStripRenderer();
        menu.DropDown.BackColor = UITheme.Current.MainSurface;
        menu.DropDown.ForeColor = UITheme.Current.PrimaryText;
        return menu;
    }

    private static ToolStripMenuItem CreateMenuItem(string text, bool isChecked, EventHandler onClick)
    {
        var item = new ToolStripMenuItem(text)
        {
            BackColor = UITheme.Current.MainSurface,
            ForeColor = UITheme.Current.PrimaryText,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular),
            Checked = isChecked
        };
        item.Click += onClick;
        return item;
    }

    private static ToolStripSeparator CreateSeparator()
    {
        return new ToolStripSeparator
        {
            BackColor = UITheme.Current.MainSurface,
            ForeColor = UITheme.Current.CardBorder
        };
    }

    private async Task PromptSaveMimicAsync()
    {
        if (_quickMimicWorkflow is null || _quickMimicWorkflow.Actions.Count == 0) return;

        using var promptForm = new Form
        {
            Text = "Save Quick Mimic",
            Size = new Size(360, 160),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true,
            BackColor = UITheme.Current.MainSurface,
            ForeColor = UITheme.Current.PrimaryText
        };

        var lbl = new Label
        {
            Text = "Workflow Name:",
            Location = new Point(16, 16),
            AutoSize = true,
            Font = UITheme.FontBodyBold
        };
        var txt = new TextBox
        {
            Text = $"Mimic {DateTime.Now:yyyy-MM-dd HH:mm}",
            Location = new Point(16, 42),
            Size = new Size(310, 26),
            Font = UITheme.FontBody
        };
        var btnSave = new ThemedButton
        {
            Text = "Save",
            Tag = "primary",
            DialogResult = DialogResult.OK,
            Location = new Point(166, 80),
            Size = new Size(80, 30)
        };
        var btnCancel = new ThemedButton
        {
            Text = "Cancel",
            Tag = "subtle",
            DialogResult = DialogResult.Cancel,
            Location = new Point(252, 80),
            Size = new Size(74, 30)
        };

        promptForm.Controls.AddRange(new Control[] { lbl, txt, btnSave, btnCancel });
        promptForm.AcceptButton = btnSave;
        promptForm.CancelButton = btnCancel;

        if (promptForm.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(txt.Text))
        {
            var toSave = new WorkflowDefinition
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = txt.Text.Trim(),
                Description = "Saved from Quick Mimic",
                SchemaVersion = 1,
                TargetPolicy = _quickMimicWorkflow.TargetPolicy,
                Limits = _quickMimicWorkflow.Limits,
                Actions = _quickMimicWorkflow.Actions.Select(a => new WorkflowAction
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = a.Type,
                    Description = a.Description,
                    DelayMs = a.DelayMs,
                    TimeoutMs = a.TimeoutMs,
                    Enabled = a.Enabled,
                    RequiresConfirmation = a.RequiresConfirmation,
                    IsIdempotent = a.IsIdempotent,
                    Target = a.Target,
                    Verification = a.Verification,
                    Parameters = new Dictionary<string, object?>(a.Parameters)
                }).ToList()
            };

            await _workflowRepo.SaveAsync(toSave);
            await RefreshWorkflowsAsync();
            _selectedSavedWorkflow = toSave;
            UpdateWorkflowSelectionUI();
            SetStatus("Saved", $"Saved '{toSave.Name}'", isSuccess: true);
        }
    }

    public void DiscardQuickMimic()
    {
        _playbackCts?.Cancel();
        _quickMimicWorkflow = null;
        _selectedSavedWorkflow = null;
        UpdateWorkflowSelectionUI();
    }

    public new void Close()
    {
        DiscardQuickMimic();
        base.Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        DiscardQuickMimic();

        if (e.CloseReason == CloseReason.UserClosing)
        {
            if (_isStandalone)
            {
                Application.Exit();
            }
            else
            {
                e.Cancel = true;
                Hide();
            }
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _executionController.StateChanged -= OnExecutionStateChanged;
            _recordingController.StateChanged -= OnRecordingStateChanged;
            DiscardQuickMimic();
            _toolTip.Dispose();
            _optionsMenu.Dispose();
        }
        base.Dispose(disposing);
    }
}
