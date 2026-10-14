using System.Drawing;
using Repliq.Core.Models;
using Repliq.Core.Recording;
using Repliq.Storage.Workflows;

namespace Repliq.App.Views;

public sealed class RecorderView : UserControl
{
    private readonly IRecordingController _recordingController;
    private readonly IWorkflowRepository _workflowRepo;
    private readonly Action<string> _navigateAction;

    private readonly Panel _pnlRecordingBanner = new();
    private readonly Label _lblRecordingStatus = new();
    private readonly Label _lblTimer = new();
    private readonly Label _lblTargetWindow = new();
    private readonly ThemedButton _btnStartRecording = new();
    private readonly ThemedButton _btnStopRecording = new();
    private readonly ThemedButton _btnCancelRecording = new();

    private readonly GroupBox _grpReview = new();
    private readonly ListView _listReviewActions = new();
    private readonly TextBox _txtWorkflowName = new();
    private readonly ThemedButton _btnDeleteSelected = new();
    private readonly ThemedButton _btnMoveUp = new();
    private readonly ThemedButton _btnMoveDown = new();
    private readonly ThemedButton _btnSaveWorkflow = new();
    private SplitContainer? _reviewSplit;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private WorkflowDefinition? _draftWorkflow;

    public RecorderView(
        IRecordingController recordingController,
        IWorkflowRepository workflowRepo,
        Action<string> navigateAction)
    {
        _recordingController = recordingController;
        _workflowRepo = workflowRepo;
        _navigateAction = navigateAction;

        InitializeComponents();

        _recordingController.StateChanged += OnRecordingStateChanged;
        _recordingController.ActionCaptured += OnActionCaptured;
        _timer.Tick += (_, _) => UpdateTimerDisplay();
    }

    private void InitializeComponents()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(24, 18, 24, 18);
        AutoScroll = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Banner & controls
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Review timeline
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Review Save bar

        // 1. Recording Banner & Controls
        _pnlRecordingBanner.Dock = DockStyle.Fill;
        _pnlRecordingBanner.AutoSize = true;
        _pnlRecordingBanner.BackColor = UITheme.Current.CardSurface;
        _pnlRecordingBanner.Padding = new Padding(14);
        _pnlRecordingBanner.Margin = new Padding(0, 0, 0, 14);

        var bannerTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true
        };
        bannerTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bannerTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Top Row: Status, Timer, Target Info
        var pnlStatusInfo = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0)
        };

        _lblRecordingStatus.Text = "[○ IDLE] Ready to Record";
        _lblRecordingStatus.Font = UITheme.FontHeader;
        _lblRecordingStatus.ForeColor = UITheme.Current.PrimaryText;
        _lblRecordingStatus.AutoSize = true;
        _lblRecordingStatus.Margin = new Padding(0, 0, 18, 0);

        _lblTimer.Text = "Elapsed: 00:00";
        _lblTimer.Font = UITheme.FontSubheader;
        _lblTimer.ForeColor = UITheme.Current.Accent;
        _lblTimer.AutoSize = true;
        _lblTimer.Margin = new Padding(0, 3, 18, 0);

        _lblTargetWindow.Text = "Target: None";
        _lblTargetWindow.Font = UITheme.FontBody;
        _lblTargetWindow.ForeColor = UITheme.Current.SecondaryText;
        _lblTargetWindow.AutoSize = true;
        _lblTargetWindow.Margin = new Padding(0, 4, 0, 0);

        pnlStatusInfo.Controls.AddRange(new Control[] { _lblRecordingStatus, _lblTimer, _lblTargetWindow });
        bannerTable.Controls.Add(pnlStatusInfo, 0, 0);

        // Buttons Row
        var pnlButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 0)
        };

        _btnStartRecording.Text = "● Start Recording";
        _btnStartRecording.Tag = "primary";
        _btnStartRecording.Size = new Size(150, 36);
        _btnStartRecording.FlatStyle = FlatStyle.Flat;
        _btnStartRecording.BackColor = UITheme.Current.AccentLight;
        _btnStartRecording.ForeColor = UITheme.Current.AccentLightText;
        _btnStartRecording.Font = UITheme.FontBodyBold;
        _btnStartRecording.Margin = new Padding(0, 0, 10, 0);
        _btnStartRecording.Click += async (_, _) => await StartRecordingAsync();

        _btnStopRecording.Text = "■ Stop & Review";
        _btnStopRecording.Tag = "danger";
        _btnStopRecording.Size = new Size(150, 36);
        _btnStopRecording.FlatStyle = FlatStyle.Flat;
        _btnStopRecording.BackColor = UITheme.Current.Error;
        _btnStopRecording.ForeColor = Color.White;
        _btnStopRecording.Font = UITheme.FontBodyBold;
        _btnStopRecording.Enabled = false;
        _btnStopRecording.Margin = new Padding(0, 0, 10, 0);
        _btnStopRecording.Click += async (_, _) => await StopRecordingAsync();

        _btnCancelRecording.Text = "Cancel";
        _btnCancelRecording.Size = new Size(100, 36);
        _btnCancelRecording.FlatStyle = FlatStyle.Flat;
        _btnCancelRecording.FlatAppearance.BorderSize = 1;
        _btnCancelRecording.FlatAppearance.BorderColor = UITheme.Current.CardBorder;
        _btnCancelRecording.BackColor = UITheme.Current.SecondarySurface;
        _btnCancelRecording.ForeColor = UITheme.Current.SecondaryText;
        _btnCancelRecording.Enabled = false;
        _btnCancelRecording.Click += async (_, _) => await CancelRecordingAsync();

        pnlButtons.Controls.AddRange(new Control[] { _btnStartRecording, _btnStopRecording, _btnCancelRecording });
        bannerTable.Controls.Add(pnlButtons, 0, 1);

        _pnlRecordingBanner.Controls.Add(bannerTable);
        layout.Controls.Add(_pnlRecordingBanner, 0, 0);

        // 2. Post-Recording Review Timeline
        _grpReview.Text = "Recorded Actions Review (Draft Timeline)";
        _grpReview.Dock = DockStyle.Fill;
        _grpReview.Font = UITheme.FontSubheader;
        _grpReview.ForeColor = UITheme.Current.PrimaryText;
        _grpReview.BackColor = UITheme.Current.WindowBackground;

        _reviewSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };

        _listReviewActions.Dock = DockStyle.Fill;
        _listReviewActions.View = View.Details;
        _listReviewActions.FullRowSelect = true;
        _listReviewActions.Font = UITheme.FontBody;
        _listReviewActions.BackColor = UITheme.Current.CardSurface;
        _listReviewActions.ForeColor = UITheme.Current.PrimaryText;
        _listReviewActions.BorderStyle = BorderStyle.FixedSingle;
        _listReviewActions.Columns.Add("#", 40);
        _listReviewActions.Columns.Add("Action Type", 110);
        _listReviewActions.Columns.Add("Description", 240);
        _listReviewActions.Columns.Add("Target Info", 140);
        _listReviewActions.Columns.Add("Delay (ms)", 80);
        _listReviewActions.Resize += (_, _) => AdjustReviewColumns();
        _reviewSplit.Panel1.Controls.Add(_listReviewActions);

        var pnlTimelineControls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(12),
            AutoScroll = true
        };
        _btnMoveUp.Text = "▲ Move Up";
        _btnMoveUp.Size = new Size(140, 34);
        _btnMoveUp.FlatStyle = FlatStyle.Flat;
        _btnMoveUp.FlatAppearance.BorderSize = 1;
        _btnMoveUp.FlatAppearance.BorderColor = UITheme.Current.CardBorder;
        _btnMoveUp.BackColor = UITheme.Current.CardSurface;
        _btnMoveUp.ForeColor = UITheme.Current.PrimaryText;
        _btnMoveUp.Margin = new Padding(0, 0, 0, 6);
        _btnMoveUp.Click += (_, _) => MoveAction(-1);

        _btnMoveDown.Text = "▼ Move Down";
        _btnMoveDown.Size = new Size(140, 34);
        _btnMoveDown.FlatStyle = FlatStyle.Flat;
        _btnMoveDown.FlatAppearance.BorderSize = 1;
        _btnMoveDown.FlatAppearance.BorderColor = UITheme.Current.CardBorder;
        _btnMoveDown.BackColor = UITheme.Current.CardSurface;
        _btnMoveDown.ForeColor = UITheme.Current.PrimaryText;
        _btnMoveDown.Margin = new Padding(0, 0, 0, 6);
        _btnMoveDown.Click += (_, _) => MoveAction(1);

        _btnDeleteSelected.Text = "✕ Remove Action";
        _btnDeleteSelected.Tag = "danger";
        _btnDeleteSelected.Size = new Size(140, 34);
        _btnDeleteSelected.FlatStyle = FlatStyle.Flat;
        _btnDeleteSelected.BackColor = UITheme.Current.Error;
        _btnDeleteSelected.ForeColor = Color.White;
        _btnDeleteSelected.Margin = new Padding(0, 0, 0, 14);
        _btnDeleteSelected.Click += (_, _) => DeleteSelectedAction();

        var lblReminder = new Label
        {
            Text = "Review Notes:\n• Sensitive fields excluded automatically.\n• Remove any accidental clicks or typos before saving.\n• Workflows can be fine-tuned in Editor.",
            Font = UITheme.FontSmall,
            ForeColor = UITheme.Current.SecondaryText,
            Size = new Size(180, 110),
            Margin = new Padding(0)
        };

        pnlTimelineControls.Controls.AddRange(new Control[] { _btnMoveUp, _btnMoveDown, _btnDeleteSelected, lblReminder });
        _reviewSplit.Panel2.Controls.Add(pnlTimelineControls);

        _grpReview.Controls.Add(_reviewSplit);
        layout.Controls.Add(_grpReview, 0, 1);

        // 3. Save bar
        var pnlSave = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 10, 0, 0)
        };
        var lblName = new Label
        {
            Text = "Workflow Name:",
            Font = UITheme.FontBodyBold,
            ForeColor = UITheme.Current.PrimaryText,
            AutoSize = true,
            Margin = new Padding(0, 8, 8, 0)
        };
        _txtWorkflowName.Text = "My Recorded Workflow";
        _txtWorkflowName.Width = 260;
        _txtWorkflowName.Font = UITheme.FontBody;
        _txtWorkflowName.BackColor = UITheme.Current.CardSurface;
        _txtWorkflowName.ForeColor = UITheme.Current.PrimaryText;
        _txtWorkflowName.Margin = new Padding(0, 4, 12, 0);

        _btnSaveWorkflow.Text = "💾 Save Workflow to Library";
        _btnSaveWorkflow.Tag = "primary";
        _btnSaveWorkflow.Size = new Size(220, 36);
        _btnSaveWorkflow.FlatStyle = FlatStyle.Flat;
        _btnSaveWorkflow.BackColor = UITheme.Current.AccentLight;
        _btnSaveWorkflow.ForeColor = UITheme.Current.AccentLightText;
        _btnSaveWorkflow.Font = UITheme.FontBodyBold;
        _btnSaveWorkflow.Enabled = false;
        _btnSaveWorkflow.Click += async (_, _) => await SaveWorkflowAsync();

        pnlSave.Controls.AddRange(new Control[] { lblName, _txtWorkflowName, _btnSaveWorkflow });
        layout.Controls.Add(pnlSave, 0, 2);

        Controls.Add(layout);
        UITheme.Apply(this);

        Resize += (_, _) => AdjustSplitter();
    }

    private void AdjustSplitter()
    {
        if (_reviewSplit is null || _reviewSplit.Width <= 100) return;
        int targetDist = (int)(_reviewSplit.Width * 0.65);
        int minDist = 40;
        int maxDist = _reviewSplit.Width - 40;
        if (maxDist > minDist)
        {
            _reviewSplit.SplitterDistance = Math.Clamp(targetDist, minDist, maxDist);
        }
        AdjustReviewColumns();
    }

    private void AdjustReviewColumns()
    {
        if (_listReviewActions.Columns.Count < 5) return;
        int totalWidth = _listReviewActions.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4;
        if (totalWidth < 300) totalWidth = 300;

        _listReviewActions.Columns[0].Width = 40;
        _listReviewActions.Columns[1].Width = (int)((totalWidth - 40) * 0.22);
        _listReviewActions.Columns[2].Width = (int)((totalWidth - 40) * 0.40);
        _listReviewActions.Columns[3].Width = (int)((totalWidth - 40) * 0.24);
        _listReviewActions.Columns[4].Width = (int)((totalWidth - 40) * 0.14);
    }

    private async Task StartRecordingAsync()
    {
        _listReviewActions.Items.Clear();
        _draftWorkflow = null;
        _btnSaveWorkflow.Enabled = false;
        await _recordingController.StartAsync();
    }

    private async Task StopRecordingAsync()
    {
        _draftWorkflow = await _recordingController.StopAsync(_txtWorkflowName.Text.Trim());
        PopulateReviewList();
        _btnSaveWorkflow.Enabled = _draftWorkflow.Actions.Count > 0;
    }

    private async Task CancelRecordingAsync()
    {
        await _recordingController.CancelAsync();
        _listReviewActions.Items.Clear();
        _draftWorkflow = null;
        _btnSaveWorkflow.Enabled = false;
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
                _lblRecordingStatus.Text = "● RECORDING ACTIVE";
                _lblRecordingStatus.ForeColor = UITheme.Current.Error;
                _btnStartRecording.Enabled = false;
                _btnStopRecording.Enabled = true;
                _btnCancelRecording.Enabled = true;
                _timer.Start();
                break;

            case RecordingState.Stopping:
            case RecordingState.Review:
                _lblRecordingStatus.Text = "○ REVIEW DRAFT";
                _lblRecordingStatus.ForeColor = UITheme.Current.PrimaryText;
                _btnStartRecording.Enabled = true;
                _btnStopRecording.Enabled = false;
                _btnCancelRecording.Enabled = false;
                _timer.Stop();
                if (_draftWorkflow is null && _recordingController.CurrentDraftWorkflow is not null)
                {
                    _draftWorkflow = _recordingController.CurrentDraftWorkflow;
                    PopulateReviewList();
                    _btnSaveWorkflow.Enabled = _draftWorkflow.Actions.Count > 0;
                }
                break;

            case RecordingState.Idle:
                _lblRecordingStatus.Text = "○ IDLE";
                _lblRecordingStatus.ForeColor = UITheme.Current.PrimaryText;
                _btnStartRecording.Enabled = true;
                _btnStopRecording.Enabled = false;
                _btnCancelRecording.Enabled = false;
                _timer.Stop();
                _lblTimer.Text = "Elapsed: 00:00";
                break;
        }
    }

    private void OnActionCaptured(WorkflowAction action)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnActionCaptured(action)));
            return;
        }

        _lblTargetWindow.Text = $"Target: {_recordingController.CurrentTargetWindow ?? "None"}";
    }

    private void UpdateTimerDisplay()
    {
        var elapsed = _recordingController.ElapsedTime;
        _lblTimer.Text = $"Elapsed: {elapsed:mm\\:ss}";
    }

    private void PopulateReviewList()
    {
        _listReviewActions.Items.Clear();
        if (_draftWorkflow is null) return;

        for (int i = 0; i < _draftWorkflow.Actions.Count; i++)
        {
            var a = _draftWorkflow.Actions[i];
            var item = new ListViewItem((i + 1).ToString());
            item.SubItems.Add(a.Type);
            item.SubItems.Add(a.Description ?? a.Id);
            item.SubItems.Add(a.Target?.WindowTitle ?? a.Target?.ProcessName ?? "Window");
            item.SubItems.Add(a.DelayMs.ToString());
            item.Tag = a;
            _listReviewActions.Items.Add(item);
        }
    }

    private void MoveAction(int direction)
    {
        if (_draftWorkflow is null || _listReviewActions.SelectedIndices.Count == 0) return;
        int idx = _listReviewActions.SelectedIndices[0];
        int newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= _draftWorkflow.Actions.Count) return;

        var temp = _draftWorkflow.Actions[idx];
        _draftWorkflow.Actions[idx] = _draftWorkflow.Actions[newIdx];
        _draftWorkflow.Actions[newIdx] = temp;

        PopulateReviewList();
        _listReviewActions.Items[newIdx].Selected = true;
    }

    private void DeleteSelectedAction()
    {
        if (_draftWorkflow is null || _listReviewActions.SelectedIndices.Count == 0) return;
        int idx = _listReviewActions.SelectedIndices[0];
        _draftWorkflow.Actions.RemoveAt(idx);
        PopulateReviewList();
        _btnSaveWorkflow.Enabled = _draftWorkflow.Actions.Count > 0;
    }

    private async Task SaveWorkflowAsync()
    {
        if (_draftWorkflow is null || _draftWorkflow.Actions.Count == 0)
        {
            MessageBox.Show("No actions to save.", "Empty Workflow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _draftWorkflow.Name = _txtWorkflowName.Text.Trim();
        try
        {
            await _workflowRepo.SaveAsync(_draftWorkflow);
            MessageBox.Show($"Workflow '{_draftWorkflow.Name}' saved successfully to your library.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _navigateAction("Workflows");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save workflow: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
