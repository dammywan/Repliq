using System.Drawing;
using System.Drawing.Drawing2D;
using Repliq.Core.Models;
using Repliq.Core.Playback;
using Repliq.Storage.History;

namespace Repliq.App.Views;

public sealed class RunMonitorView : UserControl
{
    private readonly IExecutionController _controller;
    private readonly IHistoryRepository _historyRepo;

    // Header Controls
    private readonly Label _lblWorkflowTitle = new();
    private readonly Label _lblWorkflowSubtitle = new();
    private readonly Label _lblStatusPill = new();

    // Progress Controls
    private readonly Label _lblStepsRatio = new();
    private readonly CustomProgressBar _progressBar = new();

    // Steps Timeline Container
    private readonly Panel _pnlStepsContainer = new();
    private readonly List<StepCardItem> _stepCards = new();

    // Bottom Action Controls
    private readonly Button _btnPause = new();
    private readonly Button _btnStop = new();
    private readonly Button _btnEmergencyStop = new();

    // Logs & Recovery Panel
    private readonly Panel _pnlLogsHost = new();
    private readonly TextBox _txtLogMessages = new();
    private readonly Panel _pnlRecovery = new();
    private readonly Label _lblRecoveryMessage = new();
    private readonly Button _btnToggleLogs = new();

    private WorkflowDefinition? _activeWorkflow;
    private bool _isLiveExecution = true;
    private bool _logsExpanded = false;

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

    private void InitializeComponents()
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Padding = new Padding(28, 20, 28, 20);

        // Top container (Header & Progress Card)
        var pnlTop = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0)
        };

        // 0. Header (Title, Subtitle, Status Pill)
        var pnlHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 14)
        };
        pnlHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pnlHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var pnlTitles = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Margin = new Padding(0)
        };

        _lblWorkflowTitle.Text = "Workflow Execution";
        _lblWorkflowTitle.Font = UITheme.FontDisplay;
        _lblWorkflowTitle.ForeColor = UITheme.Current.PrimaryText;
        _lblWorkflowTitle.AutoSize = true;

        _lblWorkflowSubtitle.Text = "Ready to start automation";
        _lblWorkflowSubtitle.Font = UITheme.FontBody;
        _lblWorkflowSubtitle.ForeColor = UITheme.Current.SecondaryText;
        _lblWorkflowSubtitle.AutoSize = true;
        _lblWorkflowSubtitle.Margin = new Padding(0, 2, 0, 0);

        pnlTitles.Controls.AddRange(new Control[] { _lblWorkflowTitle, _lblWorkflowSubtitle });
        pnlHeader.Controls.Add(pnlTitles, 0, 0);

        _lblStatusPill.Text = "  Ready  ";
        _lblStatusPill.Font = UITheme.FontBodyBold;
        _lblStatusPill.BackColor = UITheme.Current.SecondarySurface;
        _lblStatusPill.ForeColor = UITheme.Current.SecondaryText;
        _lblStatusPill.AutoSize = true;
        _lblStatusPill.Padding = new Padding(12, 6, 12, 6);
        _lblStatusPill.Margin = new Padding(0, 6, 0, 0);
        pnlHeader.Controls.Add(_lblStatusPill, 1, 0);
        pnlTop.Controls.Add(pnlHeader);

        // 1. Progress Card (Verified Steps)
        var pnlProgressCard = new Panel
        {
            Dock = DockStyle.Top,
            BackColor = UITheme.Current.CardSurface,
            Padding = new Padding(16, 12, 16, 12),
            Margin = new Padding(0, 0, 0, 12),
            Height = 72
        };

        var pnlProgressHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = 24
        };
        pnlProgressHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pnlProgressHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var lblProgressTitle = new Label
        {
            Text = "Verified steps",
            Font = UITheme.FontBody,
            ForeColor = UITheme.Current.SecondaryText,
            AutoSize = true,
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft
        };
        pnlProgressHeader.Controls.Add(lblProgressTitle, 0, 0);

        _lblStepsRatio.Text = "0 / 0";
        _lblStepsRatio.Font = UITheme.FontSubheader;
        _lblStepsRatio.ForeColor = UITheme.Current.SuccessText;
        _lblStepsRatio.AutoSize = true;
        _lblStepsRatio.Dock = DockStyle.Right;
        _lblStepsRatio.TextAlign = ContentAlignment.MiddleRight;
        pnlProgressHeader.Controls.Add(_lblStepsRatio, 1, 0);
        pnlProgressCard.Controls.Add(pnlProgressHeader);

        _progressBar.Dock = DockStyle.Bottom;
        _progressBar.Height = 10;
        pnlProgressCard.Controls.Add(_progressBar);
        pnlTop.Controls.Add(pnlProgressCard);

        // Bottom Container (Pinned Action buttons, Logs, Recovery)
        var pnlBottom = new Panel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Margin = new Padding(0)
        };

        // 3. Action Buttons (Pause & Stop large cards side-by-side)
        var pnlActionCards = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = 50,
            Margin = new Padding(0, 8, 0, 10)
        };
        pnlActionCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pnlActionCards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _btnPause.Dock = DockStyle.Fill;
        _btnPause.Text = "⏸  Pause";
        _btnPause.Font = UITheme.FontBodyBold;
        _btnPause.FlatStyle = FlatStyle.Flat;
        _btnPause.FlatAppearance.BorderSize = 1;
        _btnPause.FlatAppearance.BorderColor = UITheme.Current.CardBorder;
        _btnPause.BackColor = UITheme.Current.CardSurface;
        _btnPause.ForeColor = UITheme.Current.PrimaryText;
        _btnPause.Margin = new Padding(0, 0, 6, 0);
        _btnPause.Enabled = false;
        _btnPause.Click += async (_, _) =>
        {
            if (_controller.CurrentState == ExecutionState.Running)
                await _controller.PauseAsync();
            else if (_controller.CurrentState == ExecutionState.Paused)
                await _controller.ResumeAsync();
        };
        pnlActionCards.Controls.Add(_btnPause, 0, 0);

        _btnStop.Dock = DockStyle.Fill;
        _btnStop.Text = "⏹  Stop";
        _btnStop.Font = UITheme.FontBodyBold;
        _btnStop.FlatStyle = FlatStyle.Flat;
        _btnStop.FlatAppearance.BorderSize = 1;
        _btnStop.FlatAppearance.BorderColor = UITheme.Current.CardBorder;
        _btnStop.BackColor = UITheme.Current.CardSurface;
        _btnStop.ForeColor = UITheme.Current.Error;
        _btnStop.Margin = new Padding(6, 0, 0, 0);
        _btnStop.Enabled = false;
        _btnStop.Click += async (_, _) => await _controller.StopAsync();
        pnlActionCards.Controls.Add(_btnStop, 1, 0);
        pnlBottom.Controls.Add(pnlActionCards);

        // 4. Emergency Stop & Logs Toggle Row
        var pnlAuxRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = 36,
            Margin = new Padding(0, 0, 0, 8)
        };
        pnlAuxRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pnlAuxRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _btnEmergencyStop.Text = "🛑 Emergency Stop (Ctrl+Shift+S)";
        _btnEmergencyStop.Tag = "danger";
        _btnEmergencyStop.Size = new Size(240, 34);
        _btnEmergencyStop.FlatStyle = FlatStyle.Flat;
        _btnEmergencyStop.Font = UITheme.FontSmall;
        _btnEmergencyStop.Enabled = false;
        _btnEmergencyStop.Click += async (_, _) => await _controller.EmergencyStopAsync();
        pnlAuxRow.Controls.Add(_btnEmergencyStop, 0, 0);

        _btnToggleLogs.Text = "📋 View Logs ▼";
        _btnToggleLogs.Size = new Size(130, 34);
        _btnToggleLogs.FlatStyle = FlatStyle.Flat;
        _btnToggleLogs.FlatAppearance.BorderSize = 1;
        _btnToggleLogs.FlatAppearance.BorderColor = UITheme.Current.CardBorder;
        _btnToggleLogs.BackColor = UITheme.Current.CardSurface;
        _btnToggleLogs.ForeColor = UITheme.Current.SecondaryText;
        _btnToggleLogs.Font = UITheme.FontSmall;
        _btnToggleLogs.Click += (_, _) => ToggleLogs();
        pnlAuxRow.Controls.Add(_btnToggleLogs, 1, 0);
        pnlBottom.Controls.Add(pnlAuxRow);

        // 5. Recovery & Log Box
        _pnlLogsHost.Dock = DockStyle.Top;
        _pnlLogsHost.AutoSize = true;
        _pnlLogsHost.Margin = new Padding(0, 0, 0, 8);

        _pnlRecovery.Dock = DockStyle.Top;
        _pnlRecovery.Height = 70;
        _pnlRecovery.BackColor = UITheme.Current.WarningSurface;
        _pnlRecovery.Padding = new Padding(12);
        _pnlRecovery.Visible = false;
        _lblRecoveryMessage.Dock = DockStyle.Fill;
        _lblRecoveryMessage.Font = UITheme.FontBody;
        _lblRecoveryMessage.ForeColor = UITheme.Current.Warning;
        _pnlRecovery.Controls.Add(_lblRecoveryMessage);
        _pnlLogsHost.Controls.Add(_pnlRecovery);

        _txtLogMessages.Dock = DockStyle.Top;
        _txtLogMessages.Height = 140;
        _txtLogMessages.Multiline = true;
        _txtLogMessages.ReadOnly = true;
        _txtLogMessages.ScrollBars = ScrollBars.Vertical;
        _txtLogMessages.Font = UITheme.FontMonospace;
        _txtLogMessages.BackColor = UITheme.Current.SecondarySurface;
        _txtLogMessages.ForeColor = UITheme.Current.PrimaryText;
        _txtLogMessages.Visible = false;
        _pnlLogsHost.Controls.Add(_txtLogMessages);
        pnlBottom.Controls.Add(_pnlLogsHost);

        // 2. Steps Timeline Container (Center Fill)
        _pnlStepsContainer.Dock = DockStyle.Fill;
        _pnlStepsContainer.AutoScroll = true;
        _pnlStepsContainer.Padding = new Padding(0, 4, 0, 4);

        Controls.Add(_pnlStepsContainer);
        Controls.Add(pnlBottom);
        Controls.Add(pnlTop);

        UITheme.Apply(this);
    }

    private void ToggleLogs()
    {
        _logsExpanded = !_logsExpanded;
        _txtLogMessages.Visible = _logsExpanded;
        _btnToggleLogs.Text = _logsExpanded ? "📋 Hide Logs ▲" : "📋 View Logs ▼";
    }

    public async Task StartExecutionAsync(WorkflowDefinition workflow, bool isLive = true)
    {
        _activeWorkflow = workflow;
        _isLiveExecution = isLive;

        _lblWorkflowTitle.Text = workflow.Name;
        _lblWorkflowSubtitle.Text = isLive ? "Workflow execution" : "Dry run simulation (preview)";
        _progressBar.Maximum = Math.Max(1, workflow.Actions.Count);
        _progressBar.Value = 0;
        _lblStepsRatio.Text = $"0 / {workflow.Actions.Count}";
        _pnlRecovery.Visible = false;

        PopulateStepCards(workflow);

        _txtLogMessages.Clear();
        _txtLogMessages.AppendText($"[INFO] Starting {(isLive ? "live execution" : "dry run")} for '{workflow.Name}'...\r\n");

        _btnPause.Enabled = true;
        _btnPause.Text = "⏸  Pause";
        _btnStop.Enabled = true;
        _btnEmergencyStop.Enabled = true;

        try
        {
            var record = await _controller.StartAsync(workflow, dryRun: !isLive);

            await _historyRepo.RecordRunAsync(record);

            if (record.FinalState == ExecutionState.Failed)
            {
                ShowRecoveryPanel(record);
            }
        }
        catch (Exception ex)
        {
            _txtLogMessages.AppendText($"[ERROR] {ex.Message}\r\n");
            UpdateStatusPill(ExecutionState.Failed);
        }
    }

    private void PopulateStepCards(WorkflowDefinition workflow)
    {
        _pnlStepsContainer.Controls.Clear();
        _stepCards.Clear();
        _pnlStepsContainer.SuspendLayout();

        for (int i = workflow.Actions.Count - 1; i >= 0; i--)
        {
            var action = workflow.Actions[i];
            int stepNum = i + 1;
            var item = new StepCardItem(stepNum, action);
            _stepCards.Insert(0, item);
            _pnlStepsContainer.Controls.Add(item.CardPanel);
        }

        _pnlStepsContainer.ResumeLayout();
    }

    private void OnStateChanged(ExecutionState state)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnStateChanged(state)));
            return;
        }

        UpdateStatusPill(state);

        switch (state)
        {
            case ExecutionState.Running:
                _btnPause.Text = "⏸  Pause";
                _btnPause.Enabled = true;
                _btnStop.Enabled = true;
                _btnEmergencyStop.Enabled = true;
                break;

            case ExecutionState.Paused:
                _btnPause.Text = "▶  Resume";
                _btnPause.Enabled = true;
                break;

            case ExecutionState.Completed:
            case ExecutionState.Failed:
            case ExecutionState.Cancelled:
            case ExecutionState.Idle:
                _btnPause.Enabled = false;
                _btnStop.Enabled = false;
                _btnEmergencyStop.Enabled = false;
                break;
        }
    }

    private void UpdateStatusPill(ExecutionState state)
    {
        switch (state)
        {
            case ExecutionState.Running:
                _lblStatusPill.Text = "  Running  ";
                _lblStatusPill.BackColor = UITheme.Current.SuccessSurface;
                _lblStatusPill.ForeColor = UITheme.Current.SuccessText;
                break;
            case ExecutionState.Paused:
                _lblStatusPill.Text = "  Paused  ";
                _lblStatusPill.BackColor = UITheme.Current.WarningSurface;
                _lblStatusPill.ForeColor = UITheme.Current.Warning;
                break;
            case ExecutionState.Completed:
                _lblStatusPill.Text = "  Completed  ";
                _lblStatusPill.BackColor = UITheme.Current.SuccessSurface;
                _lblStatusPill.ForeColor = UITheme.Current.SuccessText;
                break;
            case ExecutionState.Failed:
                _lblStatusPill.Text = "  Failed  ";
                _lblStatusPill.BackColor = UITheme.Current.ErrorSurface;
                _lblStatusPill.ForeColor = UITheme.Current.Error;
                break;
            case ExecutionState.AwaitingConfirmation:
                _lblStatusPill.Text = "  Confirmation  ";
                _lblStatusPill.BackColor = ColorTranslator.FromHtml("#3B0764");
                _lblStatusPill.ForeColor = ColorTranslator.FromHtml("#C084FC");
                break;
            default:
                _lblStatusPill.Text = $"  {state}  ";
                _lblStatusPill.BackColor = UITheme.Current.SecondarySurface;
                _lblStatusPill.ForeColor = UITheme.Current.SecondaryText;
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

        int max = _progressBar.Maximum;
        if (stepIndex <= max)
        {
            _progressBar.Value = stepIndex;
            _lblStepsRatio.Text = $"{stepIndex} / {max}";
        }

        int idx = stepIndex - 1;
        if (idx >= 0 && idx < _stepCards.Count)
        {
            var card = _stepCards[idx];
            _pnlStepsContainer.ScrollControlIntoView(card.CardPanel);
            if (result is null)
            {
                card.SetState(StepVisualState.InProgress, "In progress");
            }
            else if (result.IsSuccess)
            {
                card.SetState(StepVisualState.Verified, "Verified");
            }
            else
            {
                card.SetState(StepVisualState.Failed, $"Failed: {result.SafeMessage}");
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
            MessageBoxDefaultButton.Button2);

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
            $"Status: {checkpoint}.\n" +
            "Safe Next Steps: Target application was not modified beyond the failed step.";
    }

    private enum StepVisualState { Pending, InProgress, Verified, Failed, Confirmation }

    private sealed class StepCardItem
    {
        public TableLayoutPanel CardPanel { get; }
        private readonly Label _lblIcon = new();
        private readonly Label _lblTitle = new();
        private readonly Label _lblSubtitle = new();

        public StepCardItem(int stepNumber, WorkflowAction action)
        {
            CardPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                MinimumSize = new Size(0, 52),
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UITheme.Current.CardSurface,
                Padding = new Padding(12, 8, 12, 8),
                Margin = new Padding(0, 0, 0, 6)
            };
            CardPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            CardPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Icon / Number circle
            _lblIcon.Text = stepNumber.ToString();
            _lblIcon.Font = UITheme.FontBodyBold;
            _lblIcon.ForeColor = UITheme.Current.SecondaryText;
            _lblIcon.BackColor = UITheme.Current.WindowBackground;
            _lblIcon.Size = new Size(28, 28);
            _lblIcon.TextAlign = ContentAlignment.MiddleCenter;
            _lblIcon.Margin = new Padding(0, 4, 12, 0);
            CardPanel.Controls.Add(_lblIcon, 0, 0);

            // Title & Subtitle
            var pnlText = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                Margin = new Padding(0)
            };

            string title = action.Description ?? $"{action.Type} action";
            _lblTitle.Text = title;
            _lblTitle.Font = UITheme.FontBodyBold;
            _lblTitle.ForeColor = UITheme.Current.PrimaryText;
            _lblTitle.AutoSize = true;

            string sub = action.RequiresConfirmation ? "Confirmation required" : "Pending";
            _lblSubtitle.Text = sub;
            _lblSubtitle.Font = UITheme.FontSmall;
            _lblSubtitle.ForeColor = action.RequiresConfirmation ? UITheme.Current.Warning : UITheme.Current.SecondaryText;
            _lblSubtitle.AutoSize = true;
            _lblSubtitle.Margin = new Padding(0, 2, 0, 0);

            pnlText.Controls.AddRange(new Control[] { _lblTitle, _lblSubtitle });
            CardPanel.Controls.Add(pnlText, 1, 0);
        }

        public void SetState(StepVisualState state, string subtitle)
        {
            _lblSubtitle.Text = subtitle;
            switch (state)
            {
                case StepVisualState.Verified:
                    _lblIcon.Text = "✓";
                    _lblIcon.BackColor = UITheme.Current.SuccessSurface;
                    _lblIcon.ForeColor = UITheme.Current.SuccessText;
                    _lblSubtitle.ForeColor = UITheme.Current.SuccessText;
                    break;
                case StepVisualState.InProgress:
                    _lblIcon.Text = "⟳";
                    _lblIcon.BackColor = ColorTranslator.FromHtml("#0C4A6E");
                    _lblIcon.ForeColor = ColorTranslator.FromHtml("#38BDF8");
                    _lblSubtitle.ForeColor = ColorTranslator.FromHtml("#38BDF8");
                    break;
                case StepVisualState.Failed:
                    _lblIcon.Text = "✕";
                    _lblIcon.BackColor = UITheme.Current.ErrorSurface;
                    _lblIcon.ForeColor = UITheme.Current.Error;
                    _lblSubtitle.ForeColor = UITheme.Current.Error;
                    break;
                case StepVisualState.Confirmation:
                    _lblIcon.Text = "!";
                    _lblIcon.BackColor = UITheme.Current.WarningSurface;
                    _lblIcon.ForeColor = UITheme.Current.Warning;
                    _lblSubtitle.ForeColor = UITheme.Current.Warning;
                    break;
            }
        }
    }

    private sealed class CustomProgressBar : Control
    {
        private int _value = 0;
        private int _maximum = 100;

        public int Value
        {
            get => _value;
            set { _value = Math.Clamp(value, 0, _maximum); Invalidate(); }
        }

        public int Maximum
        {
            get => _maximum;
            set { _maximum = Math.Max(1, value); Invalidate(); }
        }

        public CustomProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = ClientRectangle;
            using var bgBrush = new SolidBrush(UITheme.Current.WindowBackground);
            g.FillRectangle(bgBrush, rect);

            float pct = (float)_value / _maximum;
            int fillW = (int)(rect.Width * pct);
            if (fillW > 0)
            {
                var fillRect = new Rectangle(rect.X, rect.Y, fillW, rect.Height);
                using var fillBrush = new SolidBrush(UITheme.Current.Success);
                g.FillRectangle(fillBrush, fillRect);
            }
        }
    }
}
