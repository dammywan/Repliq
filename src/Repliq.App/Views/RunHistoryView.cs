using Repliq.Core.Diagnostics;
using Repliq.Core.Models;
using Repliq.Storage.History;

namespace Repliq.App.Views;

public sealed class RunHistoryView : UserControl
{
    private readonly IHistoryRepository _historyRepo;

    private readonly ListView _listRuns = new();
    private readonly TextBox _txtRunDetails = new();
    private readonly Button _btnCopySummary = new();
    private readonly Button _btnClearHistory = new();
    private readonly Button _btnRefresh = new();

    private WorkflowRunRecord? _selectedRecord;

    public RunHistoryView(IHistoryRepository historyRepo)
    {
        _historyRepo = historyRepo;
        InitializeComponents();
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
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Header bar
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Main split
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Actions

        // Header bar
        var pnlHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        var lblTitle = new Label { Text = "Run History & Diagnostics", Font = UITheme.FontHeader, AutoSize = true, Margin = new Padding(0, 4, 16, 0) };

        _btnRefresh.Text = "🔄 Refresh";
        _btnRefresh.Size = new Size(100, 32);
        _btnRefresh.Click += async (_, _) => await RefreshDataAsync();

        pnlHeader.Controls.AddRange(new Control[] { lblTitle, _btnRefresh });
        layout.Controls.Add(pnlHeader, 0, 0);

        // Split
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 620
        };

        _listRuns.Dock = DockStyle.Fill;
        _listRuns.View = View.Details;
        _listRuns.FullRowSelect = true;
        _listRuns.MultiSelect = false;
        _listRuns.Font = UITheme.FontBody;
        _listRuns.Columns.Add("Workflow", 200);
        _listRuns.Columns.Add("Outcome", 120);
        _listRuns.Columns.Add("Steps", 90);
        _listRuns.Columns.Add("Started (UTC)", 150);
        _listRuns.Columns.Add("Target", 120);
        _listRuns.SelectedIndexChanged += OnSelectionChanged;
        split.Panel1.Controls.Add(_listRuns);

        _txtRunDetails.Dock = DockStyle.Fill;
        _txtRunDetails.Multiline = true;
        _txtRunDetails.ReadOnly = true;
        _txtRunDetails.ScrollBars = ScrollBars.Vertical;
        _txtRunDetails.Font = UITheme.FontMonospace;
        split.Panel2.Controls.Add(_txtRunDetails);

        layout.Controls.Add(split, 0, 1);

        // Actions
        var pnlActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        _btnCopySummary.Text = "📋 Copy Diagnostic Summary";
        _btnCopySummary.Size = new Size(200, 36);
        _btnCopySummary.Enabled = false;
        _btnCopySummary.Click += (_, _) => CopySummaryToClipboard();

        _btnClearHistory.Text = "🗑️ Clear History";
        _btnClearHistory.Tag = "danger";
        _btnClearHistory.Size = new Size(130, 36);
        _btnClearHistory.Click += async (_, _) => await ClearHistoryAsync();

        pnlActions.Controls.AddRange(new Control[] { _btnCopySummary, _btnClearHistory });
        layout.Controls.Add(pnlActions, 0, 2);

        Controls.Add(layout);
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
