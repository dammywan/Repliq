using Repliq.Core.Models;
using Repliq.Core.Validation;
using Repliq.Storage.Workflows;

namespace Repliq.App.Views;

public sealed class WorkflowEditorView : UserControl
{
    private readonly IWorkflowRepository _workflowRepo;
    private readonly Action<string> _navigateAction;

    private WorkflowDefinition _workflow = new();
    private WorkflowAction? _selectedAction;
    private bool _hasUnsavedChanges = false;

    public bool HasUnsavedChanges => _hasUnsavedChanges;

    // Region 1: Timeline
    private readonly ListView _listActions = new();
    private readonly Button _btnAddAction = new();
    private readonly Button _btnDeleteAction = new();
    private readonly Button _btnMoveUp = new();
    private readonly Button _btnMoveDown = new();
    private readonly Button _btnToggleEnable = new();

    // Region 2: Step Canvas / Details
    private readonly Label _lblStepSummaryTitle = new();
    private readonly TextBox _txtStepDescription = new();
    private readonly TextBox _txtStepDetails = new();

    // Region 3: Properties Panel
    private readonly ComboBox _cmbActionType = new();
    private readonly TextBox _txtTargetProcess = new();
    private readonly TextBox _txtTargetWindow = new();
    private readonly NumericUpDown _numDelay = new();
    private readonly NumericUpDown _numTimeout = new();
    private readonly TextBox _txtTextParam = new();
    private readonly ComboBox _cmbClickButton = new();
    private readonly TextBox _txtKeyParam = new();
    private readonly CheckBox _chkRequiresConfirmation = new();
    private readonly CheckBox _chkVerifyTargetExists = new();
    private readonly TextBox _txtValidationFeedback = new();

    // Top Bar
    private readonly TextBox _txtWorkflowName = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnValidate = new();

    public WorkflowEditorView(IWorkflowRepository workflowRepo, Action<string> navigateAction)
    {
        _workflowRepo = workflowRepo;
        _navigateAction = navigateAction;

        InitializeComponents();
        LoadWorkflow(new WorkflowDefinition { Name = "New Custom Workflow" });
    }

    public void LoadWorkflow(WorkflowDefinition workflow)
    {
        _workflow = workflow;
        _txtWorkflowName.Text = _workflow.Name;
        _hasUnsavedChanges = false;
        PopulateActionsList();
        ValidateWorkflow();
    }

    private void InitializeComponents()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(16);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Top bar
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 3-pane split

        // Top bar
        var pnlTop = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        var lblName = new Label { Text = "Workflow:", AutoSize = true, Font = UITheme.FontSubheader, Margin = new Padding(0, 6, 8, 0) };
        _txtWorkflowName.Width = 260;
        _txtWorkflowName.TextChanged += (_, _) => { _workflow.Name = _txtWorkflowName.Text; _hasUnsavedChanges = true; };

        _btnSave.Text = "💾 Save Workflow";
        _btnSave.Tag = "primary";
        _btnSave.Size = new Size(130, 32);
        _btnSave.Click += async (_, _) => await SaveWorkflowAsync();

        _btnValidate.Text = "🛡️ Validate Safety";
        _btnValidate.Size = new Size(130, 32);
        _btnValidate.Click += (_, _) => ValidateWorkflow();

        pnlTop.Controls.AddRange(new Control[] { lblName, _txtWorkflowName, _btnSave, _btnValidate });
        mainLayout.Controls.Add(pnlTop, 0, 0);

        // 3-Pane Responsive Layout using nested SplitContainers
        var splitLeftRight = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 320
        };

        var splitCenterRight = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 340
        };

        // --- Pane 1: Action Sequence / Timeline ---
        var pnlTimeline = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        pnlTimeline.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pnlTimeline.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _listActions.Dock = DockStyle.Fill;
        _listActions.View = View.Details;
        _listActions.FullRowSelect = true;
        _listActions.MultiSelect = false;
        _listActions.Font = UITheme.FontBody;
        _listActions.Columns.Add("#", 35);
        _listActions.Columns.Add("Type", 85);
        _listActions.Columns.Add("Description", 180);
        _listActions.SelectedIndexChanged += OnActionSelectionChanged;
        pnlTimeline.Controls.Add(_listActions, 0, 0);

        var pnlTimelineBtns = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
        _btnAddAction.Text = "+ Add";
        _btnAddAction.Size = new Size(60, 30);
        _btnAddAction.Click += (_, _) => AddNewAction();

        _btnDeleteAction.Text = "✕ Del";
        _btnDeleteAction.Tag = "danger";
        _btnDeleteAction.Size = new Size(55, 30);
        _btnDeleteAction.Click += (_, _) => DeleteSelectedAction();

        _btnMoveUp.Text = "▲";
        _btnMoveUp.Size = new Size(40, 30);
        _btnMoveUp.Click += (_, _) => MoveAction(-1);

        _btnMoveDown.Text = "▼";
        _btnMoveDown.Size = new Size(40, 30);
        _btnMoveDown.Click += (_, _) => MoveAction(1);

        _btnToggleEnable.Text = "Disable";
        _btnToggleEnable.Size = new Size(70, 30);
        _btnToggleEnable.Click += (_, _) => ToggleActionEnable();

        pnlTimelineBtns.Controls.AddRange(new Control[] { _btnAddAction, _btnDeleteAction, _btnMoveUp, _btnMoveDown, _btnToggleEnable });
        pnlTimeline.Controls.Add(pnlTimelineBtns, 0, 1);
        splitLeftRight.Panel1.Controls.Add(pnlTimeline);

        // --- Pane 2: Step Canvas / Details ---
        var pnlCanvas = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        pnlCanvas.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pnlCanvas.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pnlCanvas.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _lblStepSummaryTitle.Text = "Step Details";
        _lblStepSummaryTitle.Font = UITheme.FontSubheader;
        _lblStepSummaryTitle.AutoSize = true;
        pnlCanvas.Controls.Add(_lblStepSummaryTitle, 0, 0);

        _txtStepDescription.PlaceholderText = "Step Description...";
        _txtStepDescription.Dock = DockStyle.Top;
        _txtStepDescription.TextChanged += (_, _) =>
        {
            if (_selectedAction is not null)
            {
                _selectedAction.Description = _txtStepDescription.Text;
                _hasUnsavedChanges = true;
                UpdateListRow();
            }
        };
        pnlCanvas.Controls.Add(_txtStepDescription, 0, 1);

        _txtStepDetails.Dock = DockStyle.Fill;
        _txtStepDetails.Multiline = true;
        _txtStepDetails.ReadOnly = true;
        _txtStepDetails.Font = UITheme.FontMonospace;
        _txtStepDetails.ScrollBars = ScrollBars.Vertical;
        pnlCanvas.Controls.Add(_txtStepDetails, 0, 2);
        splitCenterRight.Panel1.Controls.Add(pnlCanvas);

        // --- Pane 3: Properties Panel ---
        var pnlProps = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(12)
        };

        var lblPropsTitle = new Label { Text = "Properties & Verification", Font = UITheme.FontSubheader, AutoSize = true };

        var lblType = new Label { Text = "Action Type:", AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
        _cmbActionType.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbActionType.Width = 220;
        foreach (var t in ActionTypes.All) _cmbActionType.Items.Add(t);
        _cmbActionType.SelectedIndexChanged += OnActionTypeChanged;

        var lblProc = new Label { Text = "Target Process:", AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
        _txtTargetProcess.Width = 220;
        _txtTargetProcess.PlaceholderText = "e.g. notepad.exe";
        _txtTargetProcess.TextChanged += (_, _) =>
        {
            if (_selectedAction is not null)
            {
                _selectedAction.Target ??= new TargetSelector();
                _selectedAction.Target.ProcessName = _txtTargetProcess.Text;
                _hasUnsavedChanges = true;
            }
        };

        var lblDelay = new Label { Text = "Delay (ms):", AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
        _numDelay.Minimum = 0;
        _numDelay.Maximum = 60000;
        _numDelay.Width = 120;
        _numDelay.ValueChanged += (_, _) =>
        {
            if (_selectedAction is not null)
            {
                _selectedAction.DelayMs = (int)_numDelay.Value;
                _hasUnsavedChanges = true;
            }
        };

        var lblTimeout = new Label { Text = "Timeout (ms):", AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
        _numTimeout.Minimum = 500;
        _numTimeout.Maximum = 60000;
        _numTimeout.Width = 120;
        _numTimeout.ValueChanged += (_, _) =>
        {
            if (_selectedAction is not null)
            {
                _selectedAction.TimeoutMs = (int)_numTimeout.Value;
                _hasUnsavedChanges = true;
            }
        };

        var lblText = new Label { Text = "Text Payload:", AutoSize = true, Margin = new Padding(0, 8, 0, 2) };
        _txtTextParam.Width = 220;
        _txtTextParam.TextChanged += (_, _) =>
        {
            if (_selectedAction is not null && _selectedAction.Type == ActionTypes.TypeText)
            {
                _selectedAction.Parameters["text"] = _txtTextParam.Text;
                _hasUnsavedChanges = true;
            }
        };

        _chkRequiresConfirmation.Text = "Require explicit user confirmation";
        _chkRequiresConfirmation.AutoSize = true;
        _chkRequiresConfirmation.Margin = new Padding(0, 10, 0, 2);
        _chkRequiresConfirmation.CheckedChanged += (_, _) =>
        {
            if (_selectedAction is not null)
            {
                _selectedAction.RequiresConfirmation = _chkRequiresConfirmation.Checked;
                _hasUnsavedChanges = true;
            }
        };

        _chkVerifyTargetExists.Text = "Verify target exists before action";
        _chkVerifyTargetExists.AutoSize = true;
        _chkVerifyTargetExists.Margin = new Padding(0, 6, 0, 2);
        _chkVerifyTargetExists.CheckedChanged += (_, _) =>
        {
            if (_selectedAction is not null)
            {
                _selectedAction.Verification ??= new ActionVerification();
                _selectedAction.Verification.VerifyTargetExists = _chkVerifyTargetExists.Checked;
                _hasUnsavedChanges = true;
            }
        };

        var lblValTitle = new Label { Text = "Safety Feedback:", AutoSize = true, Font = UITheme.FontBodyBold, Margin = new Padding(0, 14, 0, 2) };
        _txtValidationFeedback.Width = 220;
        _txtValidationFeedback.Height = 120;
        _txtValidationFeedback.Multiline = true;
        _txtValidationFeedback.ReadOnly = true;
        _txtValidationFeedback.Font = UITheme.FontSmall;

        pnlProps.Controls.AddRange(new Control[]
        {
            lblPropsTitle, lblType, _cmbActionType, lblProc, _txtTargetProcess,
            lblDelay, _numDelay, lblTimeout, _numTimeout, lblText, _txtTextParam,
            _chkRequiresConfirmation, _chkVerifyTargetExists, lblValTitle, _txtValidationFeedback
        });
        splitCenterRight.Panel2.Controls.Add(pnlProps);

        splitLeftRight.Panel2.Controls.Add(splitCenterRight);
        mainLayout.Controls.Add(splitLeftRight, 0, 1);

        Controls.Add(mainLayout);
    }

    private void PopulateActionsList()
    {
        _listActions.Items.Clear();
        for (int i = 0; i < _workflow.Actions.Count; i++)
        {
            var a = _workflow.Actions[i];
            var item = new ListViewItem((i + 1).ToString());
            item.SubItems.Add(a.Type);
            item.SubItems.Add(a.Description ?? a.Id);
            item.Tag = a;
            if (!a.Enabled) item.ForeColor = Color.Gray;
            _listActions.Items.Add(item);
        }

        if (_listActions.Items.Count > 0)
            _listActions.Items[0].Selected = true;
    }

    private void OnActionSelectionChanged(object? sender, EventArgs e)
    {
        if (_listActions.SelectedItems.Count > 0 && _listActions.SelectedItems[0].Tag is WorkflowAction action)
        {
            _selectedAction = action;
            _lblStepSummaryTitle.Text = $"Step: {action.Type} ({action.Id})";
            _txtStepDescription.Text = action.Description ?? string.Empty;

            _cmbActionType.SelectedItem = action.Type;
            _txtTargetProcess.Text = action.Target?.ProcessName ?? string.Empty;
            _numDelay.Value = Math.Clamp(action.DelayMs, 0, 60000);
            _numTimeout.Value = Math.Clamp(action.TimeoutMs, 500, 60000);
            _chkRequiresConfirmation.Checked = action.RequiresConfirmation;
            _chkVerifyTargetExists.Checked = action.Verification?.VerifyTargetExists ?? true;
            _btnToggleEnable.Text = action.Enabled ? "Disable" : "Enable";

            if (action.Parameters.TryGetValue("text", out var t))
                _txtTextParam.Text = t?.ToString() ?? string.Empty;
            else
                _txtTextParam.Text = string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Action ID: {action.Id}");
            sb.AppendLine($"Type: {action.Type}");
            sb.AppendLine($"Enabled: {action.Enabled}");
            sb.AppendLine($"Delay: {action.DelayMs} ms");
            sb.AppendLine($"Timeout: {action.TimeoutMs} ms");
            sb.AppendLine($"Target Process: {action.Target?.ProcessName ?? "None"}");
            sb.AppendLine($"Target Window: {action.Target?.WindowTitle ?? "None"}");
            sb.AppendLine($"Requires Confirmation: {action.RequiresConfirmation}");
            sb.AppendLine();
            sb.AppendLine("Parameters:");
            foreach (var kvp in action.Parameters)
                sb.AppendLine($" - {kvp.Key}: {kvp.Value}");

            _txtStepDetails.Text = sb.ToString();
        }
    }

    private void OnActionTypeChanged(object? sender, EventArgs e)
    {
        if (_selectedAction is null) return;
        var newType = _cmbActionType.SelectedItem?.ToString();
        if (newType is not null && ActionTypes.IsSupported(newType))
        {
            _selectedAction.Type = newType;
            _hasUnsavedChanges = true;
            UpdateListRow();
            ValidateWorkflow();
        }
    }

    private void AddNewAction()
    {
        var action = new WorkflowAction
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8),
            Type = ActionTypes.Wait,
            Description = "Wait delay",
            DelayMs = 250,
            Parameters = new() { ["durationMs"] = 250 }
        };
        _workflow.Actions.Add(action);
        _hasUnsavedChanges = true;
        PopulateActionsList();
        _listActions.Items[_listActions.Items.Count - 1].Selected = true;
        ValidateWorkflow();
    }

    private void DeleteSelectedAction()
    {
        if (_selectedAction is null || _listActions.SelectedIndices.Count == 0) return;
        int idx = _listActions.SelectedIndices[0];
        _workflow.Actions.RemoveAt(idx);
        _hasUnsavedChanges = true;
        PopulateActionsList();
        ValidateWorkflow();
    }

    private void MoveAction(int direction)
    {
        if (_listActions.SelectedIndices.Count == 0) return;
        int idx = _listActions.SelectedIndices[0];
        int newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= _workflow.Actions.Count) return;

        var temp = _workflow.Actions[idx];
        _workflow.Actions[idx] = _workflow.Actions[newIdx];
        _workflow.Actions[newIdx] = temp;
        _hasUnsavedChanges = true;

        PopulateActionsList();
        _listActions.Items[newIdx].Selected = true;
    }

    private void ToggleActionEnable()
    {
        if (_selectedAction is null) return;
        _selectedAction.Enabled = !_selectedAction.Enabled;
        _hasUnsavedChanges = true;
        _btnToggleEnable.Text = _selectedAction.Enabled ? "Disable" : "Enable";
        UpdateListRow();
    }

    private void UpdateListRow()
    {
        if (_listActions.SelectedItems.Count > 0 && _selectedAction is not null)
        {
            var item = _listActions.SelectedItems[0];
            item.SubItems[1].Text = _selectedAction.Type;
            item.SubItems[2].Text = _selectedAction.Description ?? _selectedAction.Id;
            item.ForeColor = _selectedAction.Enabled ? UITheme.Current.PrimaryText : Color.Gray;
        }
    }

    private void ValidateWorkflow()
    {
        var result = WorkflowValidator.Validate(_workflow);
        if (result.IsValid)
        {
            _txtValidationFeedback.Text = "✓ All safety and schema validation checks passed.\nNo invalid actions detected.";
            _txtValidationFeedback.ForeColor = Color.Green;
        }
        else
        {
            _txtValidationFeedback.Text = "✕ VALIDATION ERRORS:\n" + string.Join("\n", result.Errors);
            _txtValidationFeedback.ForeColor = Color.Red;
        }
    }

    private async Task SaveWorkflowAsync()
    {
        _workflow.Name = _txtWorkflowName.Text.Trim();
        var validation = WorkflowValidator.Validate(_workflow);
        if (!validation.IsValid)
        {
            MessageBox.Show("Cannot save invalid workflow:\n" + string.Join("\n", validation.Errors), "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            await _workflowRepo.SaveAsync(_workflow);
            _hasUnsavedChanges = false;
            MessageBox.Show($"Workflow '{_workflow.Name}' saved successfully.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
