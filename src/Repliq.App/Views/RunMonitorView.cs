using Repliq.Core.Models;
using Repliq.Core.Playback;
using Repliq.Storage.History;

namespace Repliq.App.Views;

public sealed class RunMonitorView : UserControl
{
    private readonly IExecutionController _controller;
    private readonly IHistoryRepository _historyRepo;

    private readonly Label _lblWorkflowHeader = new();
    private readonly Label _lblStateStatus = new();
    private readonly Label _lblCurrentTarget = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _lblProgressRatio = new();

    private readonly ListView _listStepProgress = new();
    private readonly TextBox _txtLogMessages = new();

    private readonly Button _btnPauseResume = new();
    private readonly Button _btnStop = new();
    private readonly Button _btnEmergencyStop = new();

    private readonly Panel _pnlRecovery = new();
    private readonly Label _lblRecoveryMessage = new();

    private WorkflowDefinition? _activeWorkflow;
    private bool _isLiveExecution = true;

    public RunMonitorView(IExecutionController controller, IHistoryRepository historyRepo)
    {
        _controller = controller;
        _historyRepo = historyRepo;

        InitializeComponents();

        _controller.StateChanged += OnStateChanged;
        _controller.StepProgress += OnStepProgress;
        _controller.LogMessage += OnLogMessage;
        _controller.ConfirmationRequested += OnConfirmationRequested;
    }

    public async Task StartExecutionAsync(WorkflowDefinition workflow, bool isLive = true)
    {
        _activeWorkflow = workflow;
        _isLiveExecution = isLive;

        _lblWorkflowHeader.Text = $"Workflow: {workflow.Name} {(isLive ? "(Live Execution)" : "(Dry Run)")}";
        _lblCurrentTarget.Text = "Target: Preparing...";
        _progressBar.Minimum = 0;
        _progressBar.Maximum = Math.Max(1, workflow.Actions.Count);
        _progressBar.Value = 0;
        _lblProgressRatio.Text = $"0 / {workflow.Actions.Count}";
        _pnlRecovery.Visible = false;

        PopulateStepsList(workflow);

        _txtLogMessages.Clear();
        _txtLogMessages.AppendText($"[INFO] Starting playback for workflow '{workflow.Name}'...\r\n");

        _btnPauseResume.Enabled = true;
        _btnPauseResume.Text = "⏸ Pause";
        _btnStop.Enabled = true;
        _btnEmergencyStop.Enabled = true;

        var record = await _controller.StartAsync(workflow, dryRun: !isLive);

        // Record run to history
        await _historyRepo.RecordRunAsync(record);

        if (record.FinalState == ExecutionState.Failed)
        {
            ShowRecoveryPanel(record);
        }
    }

    private void InitializeComponents()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(24);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Header & State Banner
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Progress bar
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60)); // Step progress list
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); // Log messages
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Control buttons & Emergency Stop

        // 1. Header Banner
        var pnlBanner = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        pnlBanner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        pnlBanner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        var pnlLeftBanner = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true };
        _lblWorkflowHeader.Text = "Workflow: Not Started";
        _lblWorkflowHeader.Font = UITheme.FontHeader;
        _lblWorkflowHeader.AutoSize = true;

        _lblCurrentTarget.Text = "Target: Idle";
        _lblCurrentTarget.Font = UITheme.FontBody;
        _lblCurrentTarget.Tag = "secondary";
        _lblCurrentTarget.AutoSize = true;
        pnlLeftBanner.Controls.AddRange(new Control[] { _lblWorkflowHeader, _lblCurrentTarget });
        pnlBanner.Controls.Add(pnlLeftBanner, 0, 0);

        _lblStateStatus.Text = "[● IDLE]";
        _lblStateStatus.Font = UITheme.FontHeader;
        _lblStateStatus.TextAlign = ContentAlignment.MiddleRight;
        _lblStateStatus.Dock = DockStyle.Fill;
        pnlBanner.Controls.Add(_lblStateStatus, 1, 0);

        layout.Controls.Add(pnlBanner, 0, 0);

        // 2. Progress Bar
        var pnlProgress = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        _progressBar.Width = 600;
        _progressBar.Height = 22;
        _lblProgressRatio.Font = UITheme.FontBodyBold;
        _lblProgressRatio.AutoSize = true;
        _lblProgressRatio.Margin = new Padding(12, 2, 0, 0);
        pnlProgress.Controls.AddRange(new Control[] { _progressBar, _lblProgressRatio });
        layout.Controls.Add(pnlProgress, 0, 1);

        // 3. Step Progress List
        _listStepProgress.Dock = DockStyle.Fill;
        _listStepProgress.View = View.Details;
        _listStepProgress.FullRowSelect = true;
        _listStepProgress.Font = UITheme.FontBody;
        _listStepProgress.Columns.Add("Step", 50);
        _listStepProgress.Columns.Add("Type", 100);
        _listStepProgress.Columns.Add("Description", 300);
        _listStepProgress.Columns.Add("Target", 150);
        _listStepProgress.Columns.Add("Status", 150);
        layout.Controls.Add(_listStepProgress, 0, 2);

        // 4. Log Box & Recovery Panel
        var pnlLogsAndRecovery = new Panel { Dock = DockStyle.Fill };
        _txtLogMessages.Dock = DockStyle.Fill;
        _txtLogMessages.Multiline = true;
        _txtLogMessages.ReadOnly = true;
        _txtLogMessages.Font = UITheme.FontMonospace;
        _txtLogMessages.ScrollBars = ScrollBars.Vertical;

        _pnlRecovery.Dock = DockStyle.Bottom;
        _pnlRecovery.Height = 85;
        _pnlRecovery.BackColor = ColorTranslator.FromHtml("#FFF3CD"); // Safe warning banner
        _pnlRecovery.Padding = new Padding(12);
        _pnlRecovery.Visible = false;

        _lblRecoveryMessage.Dock = DockStyle.Fill;
        _lblRecoveryMessage.Font = UITheme.FontBody;
        _lblRecoveryMessage.ForeColor = ColorTranslator.FromHtml("#856404");
        _lblRecoveryMessage.Text = "Safe Recovery Information";
        _pnlRecovery.Controls.Add(_lblRecoveryMessage);

        pnlLogsAndRecovery.Controls.Add(_txtLogMessages);
        pnlLogsAndRecovery.Controls.Add(_pnlRecovery);
        layout.Controls.Add(pnlLogsAndRecovery, 0, 3);

        // 5. Buttons
        var pnlButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };

        _btnPauseResume.Text = "⏸ Pause";
        _btnPauseResume.Size = new Size(130, 38);
        _btnPauseResume.Enabled = false;
        _btnPauseResume.Click += async (_, _) =>
        {
            if (_controller.CurrentState == ExecutionState.Running)
                await _controller.PauseAsync();
            else if (_controller.CurrentState == ExecutionState.Paused)
                await _controller.ResumeAsync();
        };

        _btnStop.Text = "■ Stop Playback";
        _btnStop.Size = new Size(140, 38);
        _btnStop.Enabled = false;
        _btnStop.Click += async (_, _) => await _controller.StopAsync();

        _btnEmergencyStop.Text = "🛑 EMERGENCY STOP (Ctrl+Shift+S)";
        _btnEmergencyStop.Tag = "danger";
        _btnEmergencyStop.Size = new Size(260, 38);
        _btnEmergencyStop.Enabled = false;
        _btnEmergencyStop.Click += async (_, _) => await _controller.EmergencyStopAsync();

        pnlButtons.Controls.AddRange(new Control[] { _btnPauseResume, _btnStop, _btnEmergencyStop });
        layout.Controls.Add(pnlButtons, 0, 4);

        Controls.Add(layout);
    }

    private void PopulateStepsList(WorkflowDefinition workflow)
    {
        _listStepProgress.Items.Clear();
        for (int i = 0; i < workflow.Actions.Count; i++)
        {
            var a = workflow.Actions[i];
            var item = new ListViewItem((i + 1).ToString());
            item.SubItems.Add(a.Type);
            item.SubItems.Add(a.Description ?? a.Id);
            item.SubItems.Add(a.Target?.ProcessName ?? a.Target?.WindowTitle ?? "Desktop");
            item.SubItems.Add("[⋯ Pending]");
            item.Tag = a;
            _listStepProgress.Items.Add(item);
        }
    }

    private void OnStateChanged(ExecutionState state)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnStateChanged(state)));
            return;
        }

        _lblStateStatus.Text = $"[{state.ToString().ToUpperInvariant()}]";

        switch (state)
        {
            case ExecutionState.Running:
                _lblStateStatus.ForeColor = Color.Green;
                _btnPauseResume.Text = "⏸ Pause";
                _btnPauseResume.Enabled = true;
                _btnStop.Enabled = true;
                _btnEmergencyStop.Enabled = true;
                break;

            case ExecutionState.Paused:
                _lblStateStatus.ForeColor = Color.Orange;
                _btnPauseResume.Text = "▶ Resume";
                _btnPauseResume.Enabled = true;
                break;

            case ExecutionState.AwaitingConfirmation:
                _lblStateStatus.ForeColor = Color.Purple;
                break;

            case ExecutionState.Completed:
                _lblStateStatus.ForeColor = Color.Green;
                _btnPauseResume.Enabled = false;
                _btnStop.Enabled = false;
                _btnEmergencyStop.Enabled = false;
                break;

            case ExecutionState.Failed:
                _lblStateStatus.ForeColor = Color.Red;
                _btnPauseResume.Enabled = false;
                _btnStop.Enabled = false;
                _btnEmergencyStop.Enabled = false;
                break;

            case ExecutionState.Cancelled:
                _lblStateStatus.ForeColor = Color.Gray;
                _btnPauseResume.Enabled = false;
                _btnStop.Enabled = false;
                _btnEmergencyStop.Enabled = false;
                break;
        }
    }

    private void OnStepProgress(int stepIndex, WorkflowAction action, ActionExecutionResult? result)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnStepProgress(stepIndex, action, result)));
            return;
        }

        if (stepIndex <= _progressBar.Maximum)
        {
            _progressBar.Value = stepIndex;
            _lblProgressRatio.Text = $"{stepIndex} / {_progressBar.Maximum}";
        }

        _lblCurrentTarget.Text = $"Target: {_controller.CurrentTargetInfo ?? "Desktop"}";

        int idx = stepIndex - 1;
        if (idx >= 0 && idx < _listStepProgress.Items.Count)
        {
            var item = _listStepProgress.Items[idx];
            if (result is null)
            {
                item.SubItems[4].Text = "[● Running]";
                item.Selected = true;
                _listStepProgress.EnsureVisible(idx);
            }
            else if (result.IsSuccess)
            {
                item.SubItems[4].Text = $"[✓ {result.Status}]";
                item.ForeColor = Color.DarkGreen;
            }
            else
            {
                item.SubItems[4].Text = $"[✕ {result.Status}]";
                item.ForeColor = Color.Red;
            }
        }
    }

    private void OnLogMessage(string message)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnLogMessage(message)));
            return;
        }

        _txtLogMessages.AppendText($"[{DateTime.UtcNow:HH:mm:ss}] {message}\r\n");
    }

    private Task<bool> OnConfirmationRequested(WorkflowAction action)
    {
        if (InvokeRequired)
        {
            return (Task<bool>)Invoke(new Func<WorkflowAction, Task<bool>>(OnConfirmationRequested), action);
        }

        var result = MessageBox.Show(
            $"Repliq reached a configured confirmation gate:\n\nStep: {action.Type}\nDescription: {action.Description}\nTarget: {action.Target?.ProcessName ?? "Desktop"}\n\nDo you want to proceed with this operation?",
            "High-Impact Action Confirmation Required",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2); // Default to safest option (No)

        return Task.FromResult(result == DialogResult.Yes);
    }

    private void ShowRecoveryPanel(WorkflowRunRecord record)
    {
        _pnlRecovery.Visible = true;
        var checkpoint = record.LastCheckpointId is not null
            ? $"Last verified checkpoint: {record.LastCheckpointId}"
            : "No checkpoint reached before failure.";

        _lblRecoveryMessage.Text =
            $"⚠️ WORKFLOW STOPPED: {record.SafeErrorMessage}\n" +
            $"Status: {checkpoint}. Target state may be partially altered.\n" +
            "Safe Next Steps: Inspect the target application manually. Do NOT blindly retry non-idempotent actions.";
    }
}
