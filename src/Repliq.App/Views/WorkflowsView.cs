using System.Drawing;
using Repliq.Core.Models;
using Repliq.Core.Validation;
using Repliq.Storage.Workflows;

namespace Repliq.App.Views;

public sealed class WorkflowsView : UserControl
{
    private readonly IWorkflowRepository _repo;
    private readonly Action<string, WorkflowDefinition> _openWorkflowAction;

    private readonly TextBox _txtSearch = new();
    private readonly ComboBox _cmbFilter = new();
    private readonly ListView _listWorkflows = new();
    private readonly TextBox _txtWorkflowDetails = new();
    private readonly ThemedButton _btnRun = new();
    private readonly ThemedButton _btnDryRun = new();
    private readonly ThemedButton _btnEdit = new();
    private readonly ThemedButton _btnDelete = new();
    private readonly ThemedButton _btnExport = new();
    private readonly ThemedButton _btnImport = new();
    private SplitContainer? _split;

    private List<WorkflowDefinition> _allWorkflows = new();
    private WorkflowDefinition? _selectedWorkflow;

    public WorkflowsView(IWorkflowRepository repo, Action<string, WorkflowDefinition> openWorkflowAction)
    {
        _repo = repo;
        _openWorkflowAction = openWorkflowAction;
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(24, 18, 24, 18);
        AutoScroll = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Top bar (Search/Filter/Actions)
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Main Split (List & Details)
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Bottom Action buttons

        // Top bar
        var pnlTop = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 14)
        };
        var lblTitle = new Label
        {
            Text = "Workflows Library",
            Font = UITheme.FontHeader,
            ForeColor = UITheme.Current.PrimaryText,
            AutoSize = true,
            Margin = new Padding(0, 4, 16, 0)
        };

        _txtSearch.PlaceholderText = "Search workflows...";
        _txtSearch.Width = 240;
        _txtSearch.Font = UITheme.FontBody;
        _txtSearch.BackColor = UITheme.Current.CardSurface;
        _txtSearch.ForeColor = UITheme.Current.PrimaryText;
        _txtSearch.TextChanged += (_, _) => ApplyFilter();

        _cmbFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbFilter.Items.AddRange(new object[] { "All Workflows", "Ready to Run", "Needs Review" });
        _cmbFilter.SelectedIndex = 0;
        _cmbFilter.Width = 150;
        _cmbFilter.Font = UITheme.FontBody;
        _cmbFilter.BackColor = UITheme.Current.CardSurface;
        _cmbFilter.ForeColor = UITheme.Current.PrimaryText;
        _cmbFilter.SelectedIndexChanged += (_, _) => ApplyFilter();

        _btnImport.Text = "Import JSON";
        _btnImport.Width = 120;
        _btnImport.Height = 32;
        _btnImport.FlatStyle = FlatStyle.Flat;
        _btnImport.FlatAppearance.BorderSize = 1;
        _btnImport.FlatAppearance.BorderColor = UITheme.Current.CardBorder;
        _btnImport.BackColor = UITheme.Current.CardSurface;
        _btnImport.ForeColor = UITheme.Current.PrimaryText;
        _btnImport.Click += async (_, _) => await ImportWorkflowAsync();

        pnlTop.Controls.AddRange(new Control[] { lblTitle, _txtSearch, _cmbFilter, _btnImport });
        mainLayout.Controls.Add(pnlTop, 0, 0);

        // Center split
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Margin = new Padding(0, 0, 0, 12)
        };

        _listWorkflows.Dock = DockStyle.Fill;
        _listWorkflows.View = View.Details;
        _listWorkflows.FullRowSelect = true;
        _listWorkflows.MultiSelect = false;
        _listWorkflows.Font = UITheme.FontBody;
        _listWorkflows.BackColor = UITheme.Current.CardSurface;
        _listWorkflows.ForeColor = UITheme.Current.PrimaryText;
        _listWorkflows.BorderStyle = BorderStyle.FixedSingle;
        _listWorkflows.Columns.Add("Name", 200);
        _listWorkflows.Columns.Add("Target Processes", 160);
        _listWorkflows.Columns.Add("Steps", 70);
        _listWorkflows.Columns.Add("Status", 120);
        _listWorkflows.SelectedIndexChanged += OnSelectionChanged;
        _listWorkflows.Resize += (_, _) => AdjustListViewColumns();
        _split.Panel1.Controls.Add(_listWorkflows);

        _txtWorkflowDetails.Dock = DockStyle.Fill;
        _txtWorkflowDetails.Multiline = true;
        _txtWorkflowDetails.ReadOnly = true;
        _txtWorkflowDetails.ScrollBars = ScrollBars.Vertical;
        _txtWorkflowDetails.Font = UITheme.FontMonospace;
        _txtWorkflowDetails.BackColor = UITheme.Current.CardSurface;
        _txtWorkflowDetails.ForeColor = UITheme.Current.PrimaryText;
        _txtWorkflowDetails.BorderStyle = BorderStyle.FixedSingle;
        _txtWorkflowDetails.Text = "Select a workflow from the list to inspect permissions, steps, and target policy.";
        _split.Panel2.Controls.Add(_txtWorkflowDetails);

        mainLayout.Controls.Add(_split, 0, 1);

        // Bottom Actions
        var pnlBottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Margin = new Padding(0, 4, 0, 0)
        };

        _btnRun.Text = "▶ Run Workflow";
        _btnRun.Tag = "primary";
        _btnRun.Size = new Size(150, 36);
        _btnRun.FlatStyle = FlatStyle.Flat;
        _btnRun.BackColor = UITheme.Current.AccentLight;
        _btnRun.ForeColor = UITheme.Current.AccentLightText;
        _btnRun.Font = UITheme.FontBodyBold;
        _btnRun.Enabled = false;
        _btnRun.Click += (_, _) =>
        {
            if (_selectedWorkflow is not null) _openWorkflowAction("Run", _selectedWorkflow);
        };

        _btnDryRun.Text = "🔍 Dry Run / Permissions";
        _btnDryRun.Size = new Size(190, 36);
        _btnDryRun.FlatStyle = FlatStyle.Flat;
        _btnDryRun.FlatAppearance.BorderSize = 1;
        _btnDryRun.FlatAppearance.BorderColor = UITheme.Current.CardBorder;
        _btnDryRun.BackColor = UITheme.Current.CardSurface;
        _btnDryRun.ForeColor = UITheme.Current.PrimaryText;
        _btnDryRun.Enabled = false;
        _btnDryRun.Click += (_, _) =>
        {
            if (_selectedWorkflow is not null) _openWorkflowAction("DryRun", _selectedWorkflow);
        };

        _btnEdit.Text = "✏️ Edit Workflow";
        _btnEdit.Size = new Size(140, 36);
        _btnEdit.FlatStyle = FlatStyle.Flat;
        _btnEdit.FlatAppearance.BorderSize = 1;
        _btnEdit.FlatAppearance.BorderColor = UITheme.Current.CardBorder;
        _btnEdit.BackColor = UITheme.Current.CardSurface;
        _btnEdit.ForeColor = UITheme.Current.PrimaryText;
        _btnEdit.Enabled = false;
        _btnEdit.Click += (_, _) =>
        {
            if (_selectedWorkflow is not null) _openWorkflowAction("Editor", _selectedWorkflow);
        };

        _btnExport.Text = "Export JSON";
        _btnExport.Size = new Size(120, 36);
        _btnExport.FlatStyle = FlatStyle.Flat;
        _btnExport.FlatAppearance.BorderSize = 1;
        _btnExport.FlatAppearance.BorderColor = UITheme.Current.CardBorder;
        _btnExport.BackColor = UITheme.Current.CardSurface;
        _btnExport.ForeColor = UITheme.Current.PrimaryText;
        _btnExport.Enabled = false;
        _btnExport.Click += async (_, _) => await ExportWorkflowAsync();

        _btnDelete.Text = "🗑️ Delete";
        _btnDelete.Tag = "danger";
        _btnDelete.Size = new Size(100, 36);
        _btnDelete.FlatStyle = FlatStyle.Flat;
        _btnDelete.BackColor = UITheme.Current.Error;
        _btnDelete.ForeColor = Color.White;
        _btnDelete.Enabled = false;
        _btnDelete.Click += async (_, _) => await DeleteWorkflowAsync();

        pnlBottom.Controls.AddRange(new Control[] { _btnRun, _btnDryRun, _btnEdit, _btnExport, _btnDelete });
        mainLayout.Controls.Add(pnlBottom, 0, 2);

        Controls.Add(mainLayout);
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
        if (_listWorkflows.Columns.Count < 4) return;
        int totalWidth = _listWorkflows.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4;
        if (totalWidth < 300) totalWidth = 300;

        _listWorkflows.Columns[0].Width = (int)(totalWidth * 0.38);
        _listWorkflows.Columns[1].Width = (int)(totalWidth * 0.30);
        _listWorkflows.Columns[2].Width = (int)(totalWidth * 0.14);
        _listWorkflows.Columns[3].Width = (int)(totalWidth * 0.18);
    }

    public async Task RefreshDataAsync()
    {
        _allWorkflows = (await _repo.GetAllAsync()).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _listWorkflows.Items.Clear();
        var query = _txtSearch.Text.Trim();
        var filter = _cmbFilter.SelectedIndex;

        var filtered = _allWorkflows.AsEnumerable();

        if (!string.IsNullOrEmpty(query))
        {
            filtered = filtered.Where(w => w.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                           (w.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        if (filter == 1) // Ready to run
        {
            filtered = filtered.Where(w => !w.Metadata.ContainsKey("RequiresReview"));
        }
        else if (filter == 2) // Needs review
        {
            filtered = filtered.Where(w => w.Metadata.ContainsKey("RequiresReview"));
        }

        foreach (var wf in filtered)
        {
            string targets = wf.TargetPolicy?.AllowedProcesses.Count > 0
                ? string.Join(", ", wf.TargetPolicy.AllowedProcesses)
                : "None specified";
            string status = wf.Metadata.ContainsKey("RequiresReview") ? "[!] Needs Review" : "[✓] Ready";

            var item = new ListViewItem(wf.Name);
            item.SubItems.Add(targets);
            item.SubItems.Add(wf.Actions.Count.ToString());
            item.SubItems.Add(status);
            item.Tag = wf;

            if (wf.Metadata.ContainsKey("RequiresReview"))
                item.ForeColor = UITheme.Current.Warning;
            else
                item.ForeColor = UITheme.Current.PrimaryText;

            _listWorkflows.Items.Add(item);
        }

        OnSelectionChanged(this, EventArgs.Empty);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_listWorkflows.SelectedItems.Count > 0 && _listWorkflows.SelectedItems[0].Tag is WorkflowDefinition wf)
        {
            _selectedWorkflow = wf;
            _btnRun.Enabled = true;
            _btnDryRun.Enabled = true;
            _btnEdit.Enabled = true;
            _btnExport.Enabled = true;
            _btnDelete.Enabled = true;

            var val = WorkflowValidator.Validate(wf);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Workflow: {wf.Name}");
            sb.AppendLine($"ID: {wf.Id}");
            sb.AppendLine($"Created: {wf.CreatedUtc:u}");
            sb.AppendLine($"Modified: {wf.ModifiedUtc:u}");
            sb.AppendLine($"Validation: {(val.IsValid ? "Valid (Schema v1)" : "INVALID: " + string.Join(", ", val.Errors))}");
            sb.AppendLine();
            sb.AppendLine("Target Policy:");
            sb.AppendLine($" - Allowed Processes: {string.Join(", ", wf.TargetPolicy?.AllowedProcesses ?? new())}");
            sb.AppendLine($" - Allowed Domains: {string.Join(", ", wf.TargetPolicy?.AllowedDomains ?? new())}");
            sb.AppendLine();
            sb.AppendLine($"Total Steps: {wf.Actions.Count}");
            for (int i = 0; i < wf.Actions.Count; i++)
            {
                var a = wf.Actions[i];
                sb.AppendLine($"  {i + 1}. [{a.Type}] {a.Description ?? a.Id} (Delay: {a.DelayMs}ms)");
            }

            _txtWorkflowDetails.Text = sb.ToString();
        }
        else
        {
            _selectedWorkflow = null;
            _btnRun.Enabled = false;
            _btnDryRun.Enabled = false;
            _btnEdit.Enabled = false;
            _btnExport.Enabled = false;
            _btnDelete.Enabled = false;
            _txtWorkflowDetails.Text = "Select a workflow from the list to inspect permissions, steps, and target policy.";
        }
    }

    private async Task ImportWorkflowAsync()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            Title = "Import Declarative Workflow JSON"
        };

        if (ofd.ShowDialog() == DialogResult.OK)
        {
            try
            {
                var json = await File.ReadAllTextAsync(ofd.FileName);
                var imported = await _repo.ImportAsync(json);
                MessageBox.Show($"Workflow '{imported.Name}' imported successfully.\nNote: Tagged as untrusted import until reviewed.", "Import Succeeded", MessageBoxButtons.OK, MessageBoxIcon.Information);
                await RefreshDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed: {ex.Message}", "Import Rejected", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private async Task ExportWorkflowAsync()
    {
        if (_selectedWorkflow is null) return;

        using var sfd = new SaveFileDialog
        {
            Filter = "JSON Files (*.json)|*.json",
            FileName = $"{_selectedWorkflow.Name.Replace(" ", "_")}.json",
            Title = "Export Workflow Definition"
        };

        if (sfd.ShowDialog() == DialogResult.OK)
        {
            var json = await _repo.ExportAsync(_selectedWorkflow.Id);
            await File.WriteAllTextAsync(sfd.FileName, json);
            MessageBox.Show("Workflow exported successfully.", "Export Succeeded", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private async Task DeleteWorkflowAsync()
    {
        if (_selectedWorkflow is null) return;

        var confirm = MessageBox.Show(
            $"Are you sure you want to delete workflow '{_selectedWorkflow.Name}'?\nThis action cannot be undone.",
            "Confirm Deletion",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (confirm == DialogResult.Yes)
        {
            await _repo.DeleteAsync(_selectedWorkflow.Id);
            await RefreshDataAsync();
        }
    }
}
