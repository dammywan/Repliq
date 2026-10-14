using System.Drawing;
using Repliq.Core.Models;
using Repliq.Storage.History;

namespace Repliq.App.Views;

public sealed class RunHistoryView : UserControl
{
    private readonly IHistoryRepository _historyRepo;

    private readonly ListView _listRuns = new();
    private readonly TextBox _txtRunDetails = new();
    private readonly ThemedButton _btnCopySummary = new();
    private readonly ThemedButton _btnClearHistory = new();
    private readonly ThemedButton _btnRefresh = new();
    private SplitContainer? _split;

    private WorkflowRunRecord? _selectedRecord;

    public RunHistoryView(IHistoryRepository historyRepo)
    {
        _historyRepo = historyRepo;
        InitializeComponents();
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
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Header bar
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Main split
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Actions

        // Header bar
        var pnlHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 14)
        };
        pnlHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pnlHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var lblTitle = new Label
        {
            Text = "Run History & Diagnostics",
            Font = UITheme.FontHeader,
            ForeColor = UITheme.Current.PrimaryText,
            AutoSize = true,
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft
        };
        pnlHeader.Controls.Add(lblTitle, 0, 0);

        _btnRefresh.Text = "🔄 Refresh";
        _btnRefresh.Size = new Size(110, 32);
        _btnRefresh.FlatStyle = FlatStyle.Flat;
        _btnRefresh.FlatAppearance.BorderSize = 1;
        _btnRefresh.FlatAppearance.BorderColor = UITheme.Current.CardBorder;
        _btnRefresh.BackColor = UITheme.Current.CardSurface;
        _btnRefresh.ForeColor = UITheme.Current.PrimaryText;
        _btnRefresh.Font = UITheme.FontSmall;
        _btnRefresh.Click += async (_, _) => await RefreshDataAsync();
        pnlHeader.Controls.Add(_btnRefresh, 1, 0);

        layout.Controls.Add(pnlHeader, 0, 0);

        // Split
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Margin = new Padding(0, 0, 0, 12)
        };

        _listRuns.Dock = DockStyle.Fill;
        _listRuns.View = View.Details;
        _listRuns.FullRowSelect = true;
        _listRuns.MultiSelect = false;
        _listRuns.Font = UITheme.FontBody;
        _listRuns.BackColor = UITheme.Current.CardSurface;
        _listRuns.ForeColor = UITheme.Current.PrimaryText;
        _listRuns.BorderStyle = BorderStyle.FixedSingle;
        _listRuns.Columns.Add("Workflow", 160);
        _listRuns.Columns.Add("Outcome", 100);
        _listRuns.Columns.Add("Steps", 80);
        _listRuns.Columns.Add("Started (UTC)", 140);
        _listRuns.Columns.Add("Target", 110);
        _listRuns.SelectedIndexChanged += OnSelectionChanged;
        _listRuns.Resize += (_, _) => AdjustListViewColumns();
        _split.Panel1.Controls.Add(_listRuns);

        _txtRunDetails.Dock = DockStyle.Fill;
        _txtRunDetails.Multiline = true;
        _txtRunDetails.ReadOnly = true;
        _txtRunDetails.ScrollBars = ScrollBars.Vertical;
        _txtRunDetails.Font = UITheme.FontMonospace;
        _txtRunDetails.BackColor = UITheme.Current.CardSurface;
        _txtRunDetails.ForeColor = UITheme.Current.PrimaryText;
        _txtRunDetails.BorderStyle = BorderStyle.FixedSingle;
        _txtRunDetails.Text = "Select a run from the history table to view sanitized diagnostics and recovery details.";
        _split.Panel2.Controls.Add(_txtRunDetails);

        layout.Controls.Add(_split, 0, 1);

        // Actions Bar
        var pnlActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };

        _btnCopySummary.Text = "📋 Copy Diagnostic Summary";
        _btnCopySummary.Size = new Size(210, 36);
        _btnCopySummary.FlatStyle = FlatStyle.Flat;
        _btnCopySummary.FlatAppearance.BorderSize = 1;
        _btnCopySummary.FlatAppearance.BorderColor = UITheme.Current.CardBorder;
        _btnCopySummary.BackColor = UITheme.Current.CardSurface;
        _btnCopySummary.ForeColor = UITheme.Current.PrimaryText;
        _btnCopySummary.Enabled = false;
        _btnCopySummary.Margin = new Padding(0, 0, 10, 0);
        _btnCopySummary.Click += (_, _) => CopySummaryToClipboard();

        _btnClearHistory.Text = "🗑️ Clear History";
        _btnClearHistory.Tag = "danger";
        _btnClearHistory.Size = new Size(130, 36);
        _btnClearHistory.FlatStyle = FlatStyle.Flat;
        _btnClearHistory.BackColor = UITheme.Current.Error;
        _btnClearHistory.ForeColor = Color.White;
        _btnClearHistory.Click += async (_, _) => await ClearHistoryAsync();

        pnlActions.Controls.AddRange(new Control[] { _btnCopySummary, _btnClearHistory });
        layout.Controls.Add(pnlActions, 0, 2);

        Controls.Add(layout);
        UITheme.Apply(this);

        Resize += (_, _) => AdjustSplitter();
    }

    private void AdjustSplitter()
    {
        if (_split is null || _split.Width <= 100) return;
        int targetDist = (int)(_split.Width * 0.55);
        int minDist = 40;
        int maxDist = _split.Width - 40;
        if (maxDist > minDist)
        {
            _split.SplitterDistance = Math.Clamp(targetDist, minDist, maxDist);
        }
        AdjustListViewColumns();
    }

    private void AdjustListViewColumns()
    {
        if (_listRuns.Columns.Count < 5) return;
        int totalWidth = _listRuns.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4;
        if (totalWidth < 300) totalWidth = 300;

        _listRuns.Columns[0].Width = (int)(totalWidth * 0.28);
        _listRuns.Columns[1].Width = (int)(totalWidth * 0.18);
        _listRuns.Columns[2].Width = (int)(totalWidth * 0.14);
        _listRuns.Columns[3].Width = (int)(totalWidth * 0.22);
        _listRuns.Columns[4].Width = (int)(totalWidth * 0.18);
    }

    public async Task RefreshDataAsync()
    {
        _listRuns.Items.Clear();
        var runs = await _historyRepo.GetRecentRunsAsync(100);

        foreach (var r in runs)
        {
            var item = new ListViewItem(r.WorkflowName);
            item.SubItems.Add($"[{r.FinalState}]");
            item.SubItems.Add($"{r.ExecutedActions} / {r.TotalActions}");
            item.SubItems.Add(r.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss"));
            item.SubItems.Add(r.TargetProcess ?? "Desktop");
            item.Tag = r;

            if (r.FinalState == ExecutionState.Completed)
                item.ForeColor = UITheme.Current.SuccessText;
            else if (r.FinalState == ExecutionState.Failed)
                item.ForeColor = UITheme.Current.Error;
            else
                item.ForeColor = UITheme.Current.SecondaryText;

            _listRuns.Items.Add(item);
        }

        OnSelectionChanged(this, EventArgs.Empty);
    }

    private async void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_listRuns.SelectedItems.Count > 0 && _listRuns.SelectedItems[0].Tag is WorkflowRunRecord summary)
        {
            var fullRecord = await _historyRepo.GetRunByIdAsync(summary.RunId) ?? summary;
            _selectedRecord = fullRecord;
            _btnCopySummary.Enabled = true;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== RUN DIAGNOSTIC SUMMARY ===");
            sb.AppendLine($"Run ID: {fullRecord.RunId}");
            sb.AppendLine($"Workflow: {fullRecord.WorkflowName} ({fullRecord.WorkflowId})");
            sb.AppendLine($"Final State: {fullRecord.FinalState}");
            sb.AppendLine($"Started: {fullRecord.StartedUtc:u}");
            sb.AppendLine($"Ended: {fullRecord.EndedUtc?.ToString("u") ?? "Incomplete"}");
            sb.AppendLine($"Executed Actions: {fullRecord.ExecutedActions} / {fullRecord.TotalActions}");
            if (fullRecord.LastCheckpointId is not null)
                sb.AppendLine($"Last Verified Checkpoint: {fullRecord.LastCheckpointId}");
            if (fullRecord.FailedActionId is not null)
                sb.AppendLine($"Failed Action ID: {fullRecord.FailedActionId}");
            if (fullRecord.ErrorCode is not null)
                sb.AppendLine($"Error Code: {fullRecord.ErrorCode}");
            if (fullRecord.SafeErrorMessage is not null)
                sb.AppendLine($"Safe Error Message: {fullRecord.SafeErrorMessage}");

            sb.AppendLine();
            sb.AppendLine("Executed Steps:");
            for (int i = 0; i < fullRecord.StepResults.Count; i++)
            {
                var s = fullRecord.StepResults[i];
                sb.AppendLine($"  {i + 1}. [{s.ActionType}] {s.SafeMessage} -> [{s.Status}] ({s.DurationMs}ms)");
            }

            _txtRunDetails.Text = sb.ToString();
        }
        else
        {
            _selectedRecord = null;
            _btnCopySummary.Enabled = false;
            _txtRunDetails.Text = "Select a run from the history table to view sanitized diagnostics and recovery details.";
        }
    }

    private void CopySummaryToClipboard()
    {
        if (_selectedRecord is null) return;
        var summary = _txtRunDetails.Text;
        Clipboard.SetText(summary);
        MessageBox.Show("Redacted diagnostic summary copied to clipboard.", "Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task ClearHistoryAsync()
    {
        var confirm = MessageBox.Show(
            "Are you sure you want to clear all execution history?",
            "Confirm Clear History",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm == DialogResult.Yes)
        {
            await _historyRepo.ClearHistoryAsync();
            await RefreshDataAsync();
        }
    }
}
