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
    private readonly Button _btnStartRecording = new();
    private readonly Button _btnStopRecording = new();
    private readonly Button _btnCancelRecording = new();

    private readonly GroupBox _grpReview = new();
    private readonly ListView _listReviewActions = new();
    private readonly TextBox _txtWorkflowName = new();
    private readonly Button _btnDeleteSelected = new();
    private readonly Button _btnMoveUp = new();
    private readonly Button _btnMoveDown = new();
    private readonly Button _btnSaveWorkflow = new();

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
        Padding = new Padding(24);

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
        _pnlRecordingBanner.Tag = "secondary";
        _pnlRecordingBanner.Padding = new Padding(16);
        _pnlRecordingBanner.Margin = new Padding(0, 0, 0, 16);

        var bannerFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };

        _lblRecordingStatus.Text = "[○ IDLE] Ready to Record";
        _lblRecordingStatus.Font = UITheme.FontHeader;
        _lblRecordingStatus.AutoSize = true;
        _lblRecordingStatus.Margin = new Padding(0, 0, 20, 0);

        _lblTimer.Text = "Elapsed: 00:00";
        _lblTimer.Font = UITheme.FontSubheader;
        _lblTimer.AutoSize = true;
        _lblTimer.Margin = new Padding(0, 4, 20, 0);

        _lblTargetWindow.Text = "Target: None";
        _lblTargetWindow.Font = UITheme.FontBody;
        _lblTargetWindow.Tag = "secondary";
        _lblTargetWindow.AutoSize = true;
        _lblTargetWindow.Margin = new Padding(0, 6, 20, 0);

        _btnStartRecording.Text = "● Start Recording";
        _btnStartRecording.Tag = "primary";
        _btnStartRecording.Size = new Size(150, 36);
        _btnStartRecording.Click += async (_, _) => await StartRecordingAsync();

        _btnStopRecording.Text = "■ Stop & Review";
        _btnStopRecording.Tag = "danger";
        _btnStopRecording.Size = new Size(150, 36);
        _btnStopRecording.Enabled = false;
        _btnStopRecording.Click += async (_, _) => await StopRecordingAsync();

        _btnCancelRecording.Text = "Cancel";
        _btnCancelRecording.Size = new Size(100, 36);
        _btnCancelRecording.Enabled = false;
        _btnCancelRecording.Click += async (_, _) => await CancelRecordingAsync();

        bannerFlow.Controls.AddRange(new Control[]
        {
            _lblRecordingStatus, _lblTimer, _lblTargetWindow,
            _btnStartRecording, _btnStopRecording, _btnCancelRecording
        });
        _pnlRecordingBanner.Controls.Add(bannerFlow);
        layout.Controls.Add(_pnlRecordingBanner, 0, 0);

        // 2. Post-Recording Review Timeline
        _grpReview.Text = "Recorded Actions Review (Draft Timeline)";
        _grpReview.Dock = DockStyle.Fill;
        _grpReview.Font = UITheme.FontSubheader;

        var reviewSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 650
        };

        _listReviewActions.Dock = DockStyle.Fill;
        _listReviewActions.View = View.Details;
        _listReviewActions.FullRowSelect = true;
        _listReviewActions.Font = UITheme.FontBody;
        _listReviewActions.Columns.Add("#", 40);
        _listReviewActions.Columns.Add("Action Type", 110);
        _listReviewActions.Columns.Add("Description", 280);
        _listReviewActions.Columns.Add("Target Info", 140);
        _listReviewActions.Columns.Add("Delay (ms)", 80);
        reviewSplit.Panel1.Controls.Add(_listReviewActions);

        var pnlTimelineControls = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(12)
        };
        _btnMoveUp.Text = "▲ Move Up";
        _btnMoveUp.Size = new Size(130, 34);
        _btnMoveUp.Click += (_, _) => MoveAction(-1);

        _btnMoveDown.Text = "▼ Move Down";
        _btnMoveDown.Size = new Size(130, 34);
        _btnMoveDown.Click += (_, _) => MoveAction(1);

        _btnDeleteSelected.Text = "✕ Remove Action";
        _btnDeleteSelected.Tag = "danger";
        _btnDeleteSelected.Size = new Size(130, 34);
        _btnDeleteSelected.Click += (_, _) => DeleteSelectedAction();

        var lblReminder = new Label
        {
            Text = "Review Notes:\n• Sensitive fields are excluded.\n• Remove any accidental clicks or typos before saving.\n• Workflows can be fine-tuned in Editor.",
            Font = UITheme.FontSmall,
            Tag = "secondary",
            Size = new Size(180, 100),
            Margin = new Padding(0, 16, 0, 0)
        };

        pnlTimelineControls.Controls.AddRange(new Control[] { _btnMoveUp, _btnMoveDown, _btnDeleteSelected, lblReminder });
        reviewSplit.Panel2.Controls.Add(pnlTimelineControls);

        _grpReview.Controls.Add(reviewSplit);
        layout.Controls.Add(_grpReview, 0, 1);

        // 3. Save bar
        var pnlSave = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 12, 0, 0)
        };
        var lblName = new Label { Text = "Workflow Name:", AutoSize = true, Margin = new Padding(0, 8, 8, 0) };
        _txtWorkflowName.Text = "My Recorded Workflow";
        _txtWorkflowName.Width = 240;

        _btnSaveWorkflow.Text = "💾 Save Workflow to Library";
        _btnSaveWorkflow.Tag = "primary";
        _btnSaveWorkflow.Size = new Size(200, 36);
        _btnSaveWorkflow.Enabled = false;
        _btnSaveWorkflow.Click += async (_, _) => await SaveWorkflowAsync();

        pnlSave.Controls.AddRange(new Control[] { lblName, _txtWorkflowName, _btnSaveWorkflow });
        layout.Controls.Add(pnlSave, 0, 2);

        Controls.Add(layout);
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
                _lblRecordingStatus.ForeColor = Color.Red;
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
