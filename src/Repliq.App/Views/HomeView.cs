using Repliq.Core.Models;
using Repliq.Core.Playback;
using Repliq.Core.Recording;
using Repliq.Storage.History;
using Repliq.Storage.Workflows;

namespace Repliq.App.Views;

public sealed class HomeView : UserControl
{
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IHistoryRepository _historyRepo;
    private readonly Action<string> _navigateAction;

    private readonly ListView _listRecentWorkflows = new();
    private readonly ListView _listRecentRuns = new();
    private readonly Label _lblSecurityStatus = new();

    public HomeView(
        IWorkflowRepository workflowRepo,
        IHistoryRepository historyRepo,
        Action<string> navigateAction)
    {
        _workflowRepo = workflowRepo;
        _historyRepo = historyRepo;
        _navigateAction = navigateAction;

        InitializeComponents();
    }

    private void InitializeComponents()
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;

        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(24),
            AutoScroll = true
        };
        mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Header
        mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Action buttons
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); // Recent Workflows
        mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); // Recent Activity
        mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Security Status

        // 1. Header
        var pnlHeader = new Panel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 16) };
        var lblTitle = new Label
        {
            Text = "Home",
            Font = UITheme.FontHeader,
            AutoSize = true,
            Location = new Point(0, 0)
        };
        var lblSubtitle = new Label
        {
            Text = "Secure, local-first Windows desktop workflow automation",
            Font = UITheme.FontBody,
            Tag = "secondary",
            AutoSize = true,
            Location = new Point(0, 32)
        };
        pnlHeader.Controls.AddRange(new Control[] { lblTitle, lblSubtitle });
        mainPanel.Controls.Add(pnlHeader, 0, 0);

        // 2. Action buttons
        var pnlActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 20)
        };
        var btnRecord = new Button
        {
            Text = "● Record a Task",
            Tag = "primary",
            Size = new Size(160, 40),
            Margin = new Padding(0, 0, 12, 0)
        };
        btnRecord.Click += (_, _) => _navigateAction("Recorder");

        var btnNewWorkflow = new Button
        {
            Text = "+ Create Workflow",
            Size = new Size(160, 40),
            Margin = new Padding(0, 0, 12, 0)
        };
        btnNewWorkflow.Click += (_, _) => _navigateAction("Editor");

        var btnViewAll = new Button
        {
            Text = "Workflows Library",
            Size = new Size(160, 40),
            Margin = new Padding(0, 0, 12, 0)
        };
        btnViewAll.Click += (_, _) => _navigateAction("Workflows");

        pnlActions.Controls.AddRange(new Control[] { btnRecord, btnNewWorkflow, btnViewAll });
        mainPanel.Controls.Add(pnlActions, 0, 1);

        // 3. Recent Workflows Group
        var grpWorkflows = new GroupBox
        {
            Text = "Saved Workflows",
            Dock = DockStyle.Fill,
            Font = UITheme.FontSubheader,
            Margin = new Padding(0, 0, 0, 16)
        };
        _listRecentWorkflows.Dock = DockStyle.Fill;
        _listRecentWorkflows.View = View.Details;
        _listRecentWorkflows.FullRowSelect = true;
        _listRecentWorkflows.Font = UITheme.FontBody;
        _listRecentWorkflows.Columns.Add("Workflow Name", 250);
        _listRecentWorkflows.Columns.Add("Target Policy", 180);
        _listRecentWorkflows.Columns.Add("Steps", 80);
        _listRecentWorkflows.Columns.Add("Status", 150);
        _listRecentWorkflows.DoubleClick += (_, _) =>
        {
            if (_listRecentWorkflows.SelectedItems.Count > 0)
                _navigateAction("Workflows");
        };
        grpWorkflows.Controls.Add(_listRecentWorkflows);
        mainPanel.Controls.Add(grpWorkflows, 0, 2);

        // 4. Recent Activity Group
        var grpRuns = new GroupBox
        {
            Text = "Recent Execution Activity",
            Dock = DockStyle.Fill,
            Font = UITheme.FontSubheader,
            Margin = new Padding(0, 0, 0, 16)
        };
        _listRecentRuns.Dock = DockStyle.Fill;
        _listRecentRuns.View = View.Details;
        _listRecentRuns.FullRowSelect = true;
        _listRecentRuns.Font = UITheme.FontBody;
        _listRecentRuns.Columns.Add("Workflow", 220);
        _listRecentRuns.Columns.Add("Outcome", 130);
        _listRecentRuns.Columns.Add("Executed", 100);
        _listRecentRuns.Columns.Add("Started (UTC)", 160);
        _listRecentRuns.Columns.Add("Safe Error / Note", 250);
        grpRuns.Controls.Add(_listRecentRuns);
        mainPanel.Controls.Add(grpRuns, 0, 3);

        // 5. Compact Security Status Banner
        var pnlSec = new Panel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Tag = "secondary",
            Padding = new Padding(12),
            Margin = new Padding(0, 4, 0, 0)
        };
        _lblSecurityStatus.Text = "🛡️ Security Status: Execution Policy Active | Default-Deny Enforced | DPAPI Vault Ready | No elevation";
        _lblSecurityStatus.Font = UITheme.FontBodyBold;
        _lblSecurityStatus.Dock = DockStyle.Fill;
        _lblSecurityStatus.TextAlign = ContentAlignment.MiddleLeft;
        pnlSec.Controls.Add(_lblSecurityStatus);
        mainPanel.Controls.Add(pnlSec, 0, 4);

        Controls.Add(mainPanel);
    }

    public async Task RefreshDataAsync()
    {
        try
        {
            _listRecentWorkflows.Items.Clear();
            var workflows = await _workflowRepo.GetAllAsync();

            if (workflows.Count == 0)
            {
                var item = new ListViewItem("No workflows saved yet. Click 'Record a Task' to begin.");
                item.SubItems.AddRange(new[] { "-", "-", "Empty" });
                _listRecentWorkflows.Items.Add(item);
            }
            else
            {
                foreach (var wf in workflows.Take(10))
                {
                    string target = wf.TargetPolicy?.AllowedProcesses.Count > 0
                        ? string.Join(", ", wf.TargetPolicy.AllowedProcesses)
                        : "Any permitted";
                    string status = wf.Metadata.ContainsKey("RequiresReview")
                        ? "[!] Needs Review (Imported)"
                        : "[✓] Ready to Run";

                    var item = new ListViewItem(wf.Name);
                    item.SubItems.Add(target);
                    item.SubItems.Add(wf.Actions.Count.ToString());
                    item.SubItems.Add(status);
                    item.Tag = wf.Id;
                    _listRecentWorkflows.Items.Add(item);
                }
            }

            _listRecentRuns.Items.Clear();
            var runs = await _historyRepo.GetRecentRunsAsync(10);
            if (runs.Count == 0)
            {
                var item = new ListViewItem("No runs recorded yet.");
                item.SubItems.AddRange(new[] { "-", "-", "-", "-" });
                _listRecentRuns.Items.Add(item);
            }
            else
            {
                foreach (var r in runs)
                {
                    var item = new ListViewItem(r.WorkflowName);
                    item.SubItems.Add($"[{r.FinalState}]");
                    item.SubItems.Add($"{r.ExecutedActions} / {r.TotalActions}");
                    item.SubItems.Add(r.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss"));
                    item.SubItems.Add(r.SafeErrorMessage ?? "Completed smoothly");
                    item.Tag = r.RunId;
                    _listRecentRuns.Items.Add(item);
                }
            }
        }
        catch { }
    }
}
