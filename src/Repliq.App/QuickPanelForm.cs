using Repliq.Core.Models;
using Repliq.Core.Playback;
using Repliq.Core.Recording;
using Repliq.Storage.Workflows;

namespace Repliq.App;

public sealed class QuickPanelForm : Form
{
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IExecutionController _executionController;
    private readonly IRecordingController _recordingController;
    private readonly Action _openMainWindowAction;

    private readonly ComboBox _cmbWorkflows = new();
    private readonly Button _btnRecord = new();
    private readonly Button _btnPlay = new();
    private readonly Button _btnPause = new();
    private readonly Button _btnStop = new();
    private readonly CheckBox _chkPin = new();
    private readonly Label _lblStatus = new();
    private readonly Button _btnOpenMain = new();

    private List<WorkflowDefinition> _workflows = new();

    public QuickPanelForm(
        IWorkflowRepository workflowRepo,
        IExecutionController executionController,
        IRecordingController recordingController,
        Action openMainWindowAction)
    {
        _workflowRepo = workflowRepo;
        _executionController = executionController;
        _recordingController = recordingController;
        _openMainWindowAction = openMainWindowAction;

        InitializeComponents();

        _executionController.StateChanged += OnExecutionStateChanged;
        _recordingController.StateChanged += OnRecordingStateChanged;

        Load += async (_, _) => await RefreshWorkflowsAsync();
    }

    private void InitializeComponents()
    {
        Text = "Repliq Quick Panel";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(Screen.PrimaryScreen?.WorkingArea.Right - 360 ?? 400, 100);
        Size = new Size(350, 220);
        TopMost = true;
        MaximizeBox = false;
        MinimizeBox = false;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Top bar (Brand + Pin)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Workflow selector
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Action controls (Record, Play, Pause, Stop)
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Status line
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Open Main Window link

        // 1. Top bar
        var pnlTop = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        pnlTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pnlTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var lblBrand = new Label { Text = "⚡ Repliq Quick Panel", Font = UITheme.FontSubheader, AutoSize = true };
        pnlTop.Controls.Add(lblBrand, 0, 0);

        _chkPin.Text = "📌 Pin";
        _chkPin.Checked = true;
        _chkPin.AutoSize = true;
        _chkPin.CheckedChanged += (_, _) => TopMost = _chkPin.Checked;
        pnlTop.Controls.Add(_chkPin, 1, 0);
        layout.Controls.Add(pnlTop, 0, 0);

        // 2. Workflow selector
        var pnlWorkflow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        _cmbWorkflows.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbWorkflows.Width = 310;
        pnlWorkflow.Controls.Add(_cmbWorkflows);
        layout.Controls.Add(pnlWorkflow, 0, 1);

        // 3. Action controls
        var pnlControls = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        _btnRecord.Text = "● Rec";
        _btnRecord.Size = new Size(68, 34);
        _btnRecord.Tag = "primary";
        _btnRecord.Click += async (_, _) => await ToggleRecordingAsync();

        _btnPlay.Text = "▶ Play";
        _btnPlay.Size = new Size(68, 34);
        _btnPlay.Tag = "primary";
        _btnPlay.Click += async (_, _) => await StartPlaybackAsync();

        _btnPause.Text = "⏸ Pause";
        _btnPause.Size = new Size(78, 34);
        _btnPause.Enabled = false;
        _btnPause.Click += async (_, _) => await TogglePauseAsync();

        _btnStop.Text = "■ Stop";
        _btnStop.Size = new Size(68, 34);
        _btnStop.Tag = "danger";
        _btnStop.Enabled = false;
        _btnStop.Click += async (_, _) => await StopActiveRunAsync();

        pnlControls.Controls.AddRange(new Control[] { _btnRecord, _btnPlay, _btnPause, _btnStop });
        layout.Controls.Add(pnlControls, 0, 2);

        // 4. Status line
        _lblStatus.Text = "Status: Ready";
        _lblStatus.Font = UITheme.FontSmall;
        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_lblStatus, 0, 3);

        // 5. Open Main Window link
        var pnlBottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        _btnOpenMain.Text = "Open Main Window ↗";
        _btnOpenMain.Size = new Size(160, 28);
        _btnOpenMain.Click += (_, _) => _openMainWindowAction();
        pnlBottom.Controls.Add(_btnOpenMain);
        layout.Controls.Add(pnlBottom, 0, 4);

        Controls.Add(layout);
        UITheme.Apply(this);
    }

    public async Task RefreshWorkflowsAsync()
    {
        _workflows = (await _workflowRepo.GetAllAsync()).ToList();
        _cmbWorkflows.Items.Clear();

        foreach (var w in _workflows)
        {
            _cmbWorkflows.Items.Add(w.Name);
        }

        if (_cmbWorkflows.Items.Count > 0)
        {
            _cmbWorkflows.SelectedIndex = 0;
            _btnPlay.Enabled = true;
        }
        else
        {
            _cmbWorkflows.Items.Add("(No workflows available)");
            _cmbWorkflows.SelectedIndex = 0;
            _btnPlay.Enabled = false;
        }
    }

    private async Task ToggleRecordingAsync()
    {
        if (_recordingController.State == RecordingState.Recording)
        {
            var wf = await _recordingController.StopAsync();
            await _workflowRepo.SaveAsync(wf);
            await RefreshWorkflowsAsync();
            _lblStatus.Text = $"Recording saved as '{wf.Name}'.";
        }
        else
        {
            await _recordingController.StartAsync();
            _lblStatus.Text = "● Recording active... Stop to save draft.";
        }
    }

    private async Task StartPlaybackAsync()
    {
        int idx = _cmbWorkflows.SelectedIndex;
        if (idx < 0 || idx >= _workflows.Count) return;

        var selectedWf = _workflows[idx];
        _lblStatus.Text = $"Playing: {selectedWf.Name}...";

        await Task.Run(async () =>
        {
            await _executionController.StartAsync(selectedWf);
        });
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

    private async Task StopActiveRunAsync()
    {
        if (_recordingController.State == RecordingState.Recording)
        {
            await _recordingController.CancelAsync();
        }
        else
        {
            await _executionController.EmergencyStopAsync();
        }
    }

    private void OnExecutionStateChanged(ExecutionState state)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnExecutionStateChanged(state)));
            return;
        }

        _lblStatus.Text = $"Status: {state}";
        switch (state)
        {
            case ExecutionState.Running:
                _btnPlay.Enabled = false;
                _btnRecord.Enabled = false;
                _btnPause.Enabled = true;
                _btnPause.Text = "⏸ Pause";
                _btnStop.Enabled = true;
                break;
            case ExecutionState.Paused:
                _btnPause.Enabled = true;
                _btnPause.Text = "▶ Resume";
                break;
            case ExecutionState.Completed:
            case ExecutionState.Failed:
            case ExecutionState.Cancelled:
            case ExecutionState.Idle:
                _btnPlay.Enabled = _workflows.Count > 0;
                _btnRecord.Enabled = true;
                _btnPause.Enabled = false;
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
                _btnRecord.Tag = "danger";
                _btnPlay.Enabled = false;
                _btnStop.Enabled = true;
                _lblStatus.Text = "Status: Recording active...";
                break;
            default:
                _btnRecord.Text = "● Rec";
                _btnRecord.Tag = "primary";
                _btnPlay.Enabled = _workflows.Count > 0;
                _btnStop.Enabled = false;
                _lblStatus.Text = $"Status: {state}";
                break;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Hide on close instead of closing application
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
