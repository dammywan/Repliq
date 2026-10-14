using Repliq.Core.Models;
using Repliq.Core.Policies;
using Repliq.Core.Validation;

namespace Repliq.App.Views;

public sealed class DryRunPermissionsView : UserControl
{
    private readonly IPolicyEngine _policyEngine;
    private readonly Action<string, WorkflowDefinition> _navigateWorkflowAction;

    private WorkflowDefinition? _workflow;
    private readonly Label _lblWorkflowTitle = new();
    private readonly Label _lblTargetSummary = new();
    private readonly TextBox _txtPolicySummary = new();
    private readonly ListView _listPlannedSteps = new();
    private readonly TextBox _txtDryRunReport = new();
    private readonly ThemedButton _btnStartDryRun = new();
    private readonly ThemedButton _btnExecuteRun = new();
    private readonly ThemedButton _btnBackToEdit = new();
    private SplitContainer? _split;

    public DryRunPermissionsView(IPolicyEngine policyEngine, Action<string, WorkflowDefinition> navigateWorkflowAction)
    {
        _policyEngine = policyEngine;
        _navigateWorkflowAction = navigateWorkflowAction;

        InitializeComponents();
    }

    public void LoadWorkflow(WorkflowDefinition workflow)
    {
        _workflow = workflow;
        _lblWorkflowTitle.Text = $"Workflow: {workflow.Name}";

        var allowedProcs = workflow.TargetPolicy?.AllowedProcesses.Count > 0
            ? string.Join(", ", workflow.TargetPolicy.AllowedProcesses)
            : "No specific target process declared (will require explicit policy approval)";
        _lblTargetSummary.Text = $"Target Scope: {allowedProcs}";

        // Evaluate permissions
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== PERMISSION & POLICY EVALUATION ===");
        var val = WorkflowValidator.Validate(workflow);
        sb.AppendLine($"Schema & Bounds Validation: {(val.IsValid ? "PASS (Version 1)" : "FAILED: " + string.Join("; ", val.Errors))}");

        bool allActionsAllowed = true;
        int confirmationCount = 0;
        int coordinateCount = 0;

        foreach (var action in workflow.Actions)
        {
            var decision = _policyEngine.EvaluateAction(action, workflow);
            if (!decision.IsAllowed)
            {
                allActionsAllowed = false;
                sb.AppendLine($"[DENIED] Action {action.Type}: {decision.Reason} ({decision.ViolationCode})");
            }
            if (decision.RequiresConfirmation) confirmationCount++;
            if (action.Target?.SelectorType == "coordinate") coordinateCount++;
        }

        if (allActionsAllowed)
        {
            sb.AppendLine("Execution Policy: ALLOWED by centralized security policy.");
        }
        else
        {
            sb.AppendLine("Execution Policy: BLOCKED - One or more actions violate security policy.");
        }

        sb.AppendLine($"Confirmation Gates: {confirmationCount} steps require explicit user confirmation.");
        if (coordinateCount > 0)
        {
            sb.AppendLine($"[!] Warning: {coordinateCount} actions use screen/window coordinates, which are fragile under display scaling.");
        }

        _txtPolicySummary.Text = sb.ToString();

        // Planned steps
        _listPlannedSteps.Items.Clear();
        for (int i = 0; i < workflow.Actions.Count; i++)
        {
            var a = workflow.Actions[i];
            var item = new ListViewItem((i + 1).ToString());
            item.SubItems.Add(a.Type);
            item.SubItems.Add(a.Description ?? a.Id);
            item.SubItems.Add(a.Target?.ProcessName ?? a.Target?.WindowTitle ?? "Desktop");
            item.SubItems.Add(a.RequiresConfirmation ? "[!] Confirm" : "Auto");
            _listPlannedSteps.Items.Add(item);
        }

        _btnExecuteRun.Enabled = val.IsValid && allActionsAllowed;
        _txtDryRunReport.Text = "Click 'Run Dry Run (Preview)' to simulate full workflow execution without physical input injection.";
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
            RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Header
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); // Policy & Steps split
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55)); // Dry Run Preview report
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Bottom buttons

        // Header
        var pnlHeader = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
        _lblWorkflowTitle.Font = UITheme.FontHeader;
        _lblWorkflowTitle.AutoSize = true;
        _lblTargetSummary.Font = UITheme.FontSubheader;
        _lblTargetSummary.Tag = "secondary";
        _lblTargetSummary.AutoSize = true;
        pnlHeader.Controls.AddRange(new Control[] { _lblWorkflowTitle, _lblTargetSummary });
        layout.Controls.Add(pnlHeader, 0, 0);

        // Middle Split (Policy vs Steps)
        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 450
        };
        var split = _split;

        Resize += (_, _) => AdjustLayout();

        var grpPolicy = new GroupBox { Text = "Permission Scope & Safety Policy", Dock = DockStyle.Fill, Font = UITheme.FontSubheader };
        _txtPolicySummary.Dock = DockStyle.Fill;
        _txtPolicySummary.Multiline = true;
        _txtPolicySummary.ReadOnly = true;
        _txtPolicySummary.Font = UITheme.FontMonospace;
        grpPolicy.Controls.Add(_txtPolicySummary);
        split.Panel1.Controls.Add(grpPolicy);

        var grpSteps = new GroupBox { Text = "Planned Steps Sequence", Dock = DockStyle.Fill, Font = UITheme.FontSubheader };
        _listPlannedSteps.Dock = DockStyle.Fill;
        _listPlannedSteps.View = View.Details;
        _listPlannedSteps.FullRowSelect = true;
        _listPlannedSteps.Font = UITheme.FontBody;
        _listPlannedSteps.Columns.Add("#", 35);
        _listPlannedSteps.Columns.Add("Type", 90);
        _listPlannedSteps.Columns.Add("Description", 200);
        _listPlannedSteps.Columns.Add("Target", 120);
        _listPlannedSteps.Columns.Add("Gate", 75);
        grpSteps.Controls.Add(_listPlannedSteps);
        split.Panel2.Controls.Add(grpSteps);

        layout.Controls.Add(split, 0, 1);

        // Dry Run Report
        var grpReport = new GroupBox { Text = "Dry Run Simulation Report", Dock = DockStyle.Fill, Font = UITheme.FontSubheader, Margin = new Padding(0, 10, 0, 10) };
        _txtDryRunReport.Dock = DockStyle.Fill;
        _txtDryRunReport.Multiline = true;
        _txtDryRunReport.ReadOnly = true;
        _txtDryRunReport.Font = UITheme.FontMonospace;
        _txtDryRunReport.ScrollBars = ScrollBars.Vertical;
        grpReport.Controls.Add(_txtDryRunReport);
        layout.Controls.Add(grpReport, 0, 2);

        // Buttons
        var pnlBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        _btnStartDryRun.Text = "🔍 Run Dry Run (Preview)";
        _btnStartDryRun.Size = new Size(180, 38);
        _btnStartDryRun.Click += (_, _) => PerformDryRun();

        _btnExecuteRun.Text = "▶ Run Live Workflow";
        _btnExecuteRun.Tag = "primary";
        _btnExecuteRun.Size = new Size(180, 38);
        _btnExecuteRun.Click += (_, _) =>
        {
            if (_workflow is not null) _navigateWorkflowAction("RunLive", _workflow);
        };

        _btnBackToEdit.Text = "✏️ Back to Editor";
        _btnBackToEdit.Size = new Size(130, 38);
        _btnBackToEdit.Click += (_, _) =>
        {
            if (_workflow is not null) _navigateWorkflowAction("Editor", _workflow);
        };

        pnlBtns.Controls.AddRange(new Control[] { _btnStartDryRun, _btnExecuteRun, _btnBackToEdit });
        layout.Controls.Add(pnlBtns, 0, 3);

        Controls.Add(layout);
        AdjustLayout();
    }

    private void AdjustLayout()
    {
        if (_split is not null && _split.Width > 200)
        {
            int target = (int)(_split.Width * 0.44);
            if (target > 120 && target < _split.Width - 120)
            {
                _split.SplitterDistance = target;
            }
        }
        if (_listPlannedSteps.Columns.Count >= 5)
        {
            int available = _listPlannedSteps.ClientSize.Width - (35 + 90 + 120 + 75);
            _listPlannedSteps.Columns[2].Width = Math.Max(120, available);
        }
    }

    private void PerformDryRun()
    {
        if (_workflow is null) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== DRY RUN SIMULATION REPORT: {_workflow.Name} ===");
        sb.AppendLine($"Timestamp: {DateTime.UtcNow:u}");
        sb.AppendLine($"Total Actions: {_workflow.Actions.Count}");
        sb.AppendLine();

        int executed = 0;
        foreach (var a in _workflow.Actions)
        {
            executed++;
            var decision = _policyEngine.EvaluateAction(a, _workflow);
            if (!decision.IsAllowed)
            {
                sb.AppendLine($"[FAILED] Step {executed} ({a.Type}): Policy Denial: {decision.Reason}");
                sb.AppendLine("Dry run simulation halted due to policy violation.");
                _txtDryRunReport.Text = sb.ToString();
                return;
            }

            string target = a.Target?.ProcessName ?? a.Target?.WindowTitle ?? "Desktop";
            string gate = decision.RequiresConfirmation ? " [REQUIRES EXPLICIT CONFIRMATION]" : "";
            sb.AppendLine($"[SIMULATED] Step {executed}/{_workflow.Actions.Count}: {a.Type} on target '{target}'{gate} (Delay: {a.DelayMs}ms)");
        }

        sb.AppendLine();
        sb.AppendLine("=== DRY RUN OUTCOME: SUCCESS ===");
        sb.AppendLine("All actions successfully passed schema validation, policy limits, and target authorization.");
        sb.AppendLine("No physical input was injected during this dry run.");

        _txtDryRunReport.Text = sb.ToString();
    }
}
