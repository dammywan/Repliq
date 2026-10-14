using System.Text.Json;
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
    private readonly Button _btnRun = new();
    private readonly Button _btnDryRun = new();
    private readonly Button _btnEdit = new();
    private readonly Button _btnDelete = new();
    private readonly Button _btnExport = new();
    private readonly Button _btnImport = new();

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
        Padding = new Padding(24);

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
        var pnlTop = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        var lblTitle = new Label { Text = "Workflows Library", Font = UITheme.FontHeader, AutoSize = true, Margin = new Padding(0, 4, 16, 0) };

        _txtSearch.PlaceholderText = "Search workflows...";
        _txtSearch.Width = 220;
        _txtSearch.Height = 32;
        _txtSearch.TextChanged += (_, _) => ApplyFilter();

        _cmbFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbFilter.Items.AddRange(new object[] { "All Workflows", "Ready to Run", "Needs Review" });
        _cmbFilter.SelectedIndex = 0;
        _cmbFilter.Width = 140;
        _cmbFilter.SelectedIndexChanged += (_, _) => ApplyFilter();

        _btnImport.Text = "Import JSON";
        _btnImport.Width = 110;
        _btnImport.Click += async (_, _) => await ImportWorkflowAsync();

        pnlTop.Controls.AddRange(new Control[] { lblTitle, _txtSearch, _cmbFilter, _btnImport });
        mainLayout.Controls.Add(pnlTop, 0, 0);

        // Center split
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 620
        };

        _listWorkflows.Dock = DockStyle.Fill;
        _listWorkflows.View = View.Details;
        _listWorkflows.FullRowSelect = true;
        _listWorkflows.MultiSelect = false;
        _listWorkflows.Font = UITheme.FontBody;
        _listWorkflows.Columns.Add("Name", 220);
        _listWorkflows.Columns.Add("Target Processes", 180);
        _listWorkflows.Columns.Add("Steps", 70);
        _listWorkflows.Columns.Add("Status", 130);
        _listWorkflows.SelectedIndexChanged += OnSelectionChanged;
        split.Panel1.Controls.Add(_listWorkflows);

        _txtWorkflowDetails.Dock = DockStyle.Fill;
        _txtWorkflowDetails.Multiline = true;
        _txtWorkflowDetails.ReadOnly = true;
        _txtWorkflowDetails.ScrollBars = ScrollBars.Vertical;
        _txtWorkflowDetails.Font = UITheme.FontMonospace;
        split.Panel2.Controls.Add(_txtWorkflowDetails);

        mainLayout.Controls.Add(split, 0, 1);

        // Bottom Actions
        var pnlBottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
        _btnRun.Text = "▶ Run Workflow";
        _btnRun.Tag = "primary";
        _btnRun.Size = new Size(140, 38);
        _btnRun.Enabled = false;
        _btnRun.Click += (_, _) =>
        {
            if (_selectedWorkflow is not null) _openWorkflowAction("Run", _selectedWorkflow);
        };

        _btnDryRun.Text = "🔍 Dry Run / Permissions";
        _btnDryRun.Size = new Size(180, 38);
        _btnDryRun.Enabled = false;
        _btnDryRun.Click += (_, _) =>
        {
            if (_selectedWorkflow is not null) _openWorkflowAction("DryRun", _selectedWorkflow);
        };

        _btnEdit.Text = "✏️ Edit Workflow";
        _btnEdit.Size = new Size(140, 38);
        _btnEdit.Enabled = false;
        _btnEdit.Click += (_, _) =>
        {
            if (_selectedWorkflow is not null) _openWorkflowAction("Editor", _selectedWorkflow);
        };

        _btnExport.Text = "Export JSON";
        _btnExport.Size = new Size(120, 38);
        _btnExport.Enabled = false;
        _btnExport.Click += async (_, _) => await ExportWorkflowAsync();

        _btnDelete.Text = "🗑️ Delete";
        _btnDelete.Tag = "danger";
        _btnDelete.Size = new Size(100, 38);
        _btnDelete.Enabled = false;
        _btnDelete.Click += async (_, _) => await DeleteWorkflowAsync();

        pnlBottom.Controls.AddRange(new Control[] { _btnRun, _btnDryRun, _btnEdit, _btnExport, _btnDelete });
        mainLayout.Controls.Add(pnlBottom, 0, 2);

        Controls.Add(mainLayout);
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
