using System.Drawing;
using System.Windows.Forms;
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
    private bool _isLoadingAction = false;

    public bool HasUnsavedChanges => _hasUnsavedChanges;

    // Top Bar
    private readonly TextBox _txtWorkflowName = new();
    private readonly ThemedButton _btnSave = new();
    private readonly ThemedButton _btnValidate = new();
    private readonly Label _lblValidationBadge = new();

    // 2-Column Split
    private SplitContainer? _splitMain;

    // Left Column: Step Timeline
    private readonly ListView _listActions = new();
    private readonly Label _lblStepCountBadge = new();
    private readonly ThemedButton _btnAddAction = new();
    private readonly ThemedButton _btnDeleteAction = new();
    private readonly ThemedButton _btnMoveUp = new();
    private readonly ThemedButton _btnMoveDown = new();
    private readonly ThemedButton _btnToggleEnable = new();

    // Right Column: Step Inspector
    private readonly Label _lblInspectorTitle = new();
    private readonly Label _lblActionIdBadge = new();
    private readonly ComboBox _cmbActionType = new();
    private readonly TextBox _txtStepDescription = new();
    private readonly TextBox _txtTargetProcess = new();
    private readonly TextBox _txtTargetWindow = new();
    private readonly NumericUpDown _numDelay = new();
    private readonly NumericUpDown _numTimeout = new();

    // Contextual Parameters
    private readonly Panel _pnlContextParams = new();
    private readonly Label _lblParamPrompt = new();
    private readonly TextBox _txtTextParam = new();
    private readonly TextBox _txtKeyParam = new();
    private readonly ComboBox _cmbClickButton = new();

    // Guardrails
    private readonly CheckBox _chkRequiresConfirmation = new();
    private readonly CheckBox _chkVerifyTargetExists = new();
    private readonly Panel _pnlValidationFeedback = new();
    private readonly Label _lblValidationFeedback = new();

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
        Padding = new Padding(20, 16, 20, 16);
        AutoScroll = true;
        AutoScaleMode = AutoScaleMode.Dpi;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Top bar
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // Main 2-column editor

        // --- 1. Modern Top Action Bar ---
        var pnlTop = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 12)
        };

        var lblName = new Label
        {
            Text = "Workflow:",
            AutoSize = true,
            Font = UITheme.FontSubheader,
            ForeColor = UITheme.Current.PrimaryText,
            Margin = new Padding(0, 7, 8, 0)
        };

        _txtWorkflowName.Width = 280;
        _txtWorkflowName.Height = 32;
        _txtWorkflowName.Font = UITheme.FontBody;
        _txtWorkflowName.BackColor = UITheme.Current.CardSurface;
        _txtWorkflowName.ForeColor = UITheme.Current.PrimaryText;
        _txtWorkflowName.BorderStyle = BorderStyle.FixedSingle;
        _txtWorkflowName.Margin = new Padding(0, 3, 12, 0);
        _txtWorkflowName.TextChanged += (_, _) =>
        {
            _workflow.Name = _txtWorkflowName.Text;
            _hasUnsavedChanges = true;
        };

        _btnSave.Text = "💾 Save Workflow";
        _btnSave.Tag = "primary";
        _btnSave.Size = new Size(140, 34);
        _btnSave.Margin = new Padding(0, 2, 8, 0);
        _btnSave.Click += async (_, _) => await SaveWorkflowAsync();

        _btnValidate.Text = "🛡️ Validate Safety";
        _btnValidate.Size = new Size(135, 34);
        _btnValidate.Margin = new Padding(0, 2, 12, 0);
        _btnValidate.Click += (_, _) => ValidateWorkflow();

        _lblValidationBadge.Text = "✓ Valid & Secure";
        _lblValidationBadge.Font = UITheme.FontSmallBold;
        _lblValidationBadge.BackColor = UITheme.Current.SuccessSurface;
        _lblValidationBadge.ForeColor = UITheme.Current.SuccessText;
        _lblValidationBadge.AutoSize = true;
        _lblValidationBadge.Padding = new Padding(10, 6, 10, 6);
        _lblValidationBadge.Margin = new Padding(0, 4, 0, 0);

        pnlTop.Controls.AddRange(new Control[] { lblName, _txtWorkflowName, _btnSave, _btnValidate, _lblValidationBadge });
        mainLayout.Controls.Add(pnlTop, 0, 0);

        // --- 2. Clean 2-Column Split: Master Timeline + Detail Inspector ---
        _splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 380,
            Margin = new Padding(0)
        };

        // --- Left Panel: Steps Timeline ---
        var pnlTimelineCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.Current.CardSurface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 8, 0)
        };

        var pnlTimelineHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 32,
            ColumnCount = 2,
            RowCount = 1
        };
        pnlTimelineHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pnlTimelineHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var lblTimelineTitle = new Label
        {
            Text = "Workflow Steps",
            Font = UITheme.FontSubheader,
            ForeColor = UITheme.Current.PrimaryText,
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true
        };
        pnlTimelineHeader.Controls.Add(lblTimelineTitle, 0, 0);

        _lblStepCountBadge.Text = "0 steps";
        _lblStepCountBadge.Font = UITheme.FontMicro;
        _lblStepCountBadge.ForeColor = UITheme.Current.SecondaryText;
        _lblStepCountBadge.Dock = DockStyle.Right;
        _lblStepCountBadge.TextAlign = ContentAlignment.MiddleRight;
        _lblStepCountBadge.AutoSize = true;
        pnlTimelineHeader.Controls.Add(_lblStepCountBadge, 1, 0);

        pnlTimelineCard.Controls.Add(pnlTimelineHeader);

        _listActions.Dock = DockStyle.Fill;
        _listActions.View = View.Details;
        _listActions.FullRowSelect = true;
        _listActions.MultiSelect = false;
        _listActions.Font = UITheme.FontBody;
        _listActions.Columns.Add("#", 38);
        _listActions.Columns.Add("Type", 95);
        _listActions.Columns.Add("Description / Target", 210);
        _listActions.SelectedIndexChanged += OnActionSelectionChanged;
        pnlTimelineCard.Controls.Add(_listActions);

        // Bottom Timeline Toolbar
        var pnlTimelineToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };

        _btnAddAction.Text = "+ Add";
        _btnAddAction.Tag = "primary";
        _btnAddAction.Size = new Size(65, 30);
        _btnAddAction.Margin = new Padding(0, 0, 4, 0);
        _btnAddAction.Click += (_, _) => AddNewAction();

        _btnDeleteAction.Text = "✕ Del";
        _btnDeleteAction.Tag = "danger";
        _btnDeleteAction.Size = new Size(55, 30);
        _btnDeleteAction.Margin = new Padding(0, 0, 4, 0);
        _btnDeleteAction.Click += (_, _) => DeleteSelectedAction();

        _btnMoveUp.Text = "▲";
        _btnMoveUp.Size = new Size(40, 30);
        _btnMoveUp.Margin = new Padding(0, 0, 4, 0);
        _btnMoveUp.Click += (_, _) => MoveAction(-1);

        _btnMoveDown.Text = "▼";
        _btnMoveDown.Size = new Size(40, 30);
        _btnMoveDown.Margin = new Padding(0, 0, 4, 0);
        _btnMoveDown.Click += (_, _) => MoveAction(1);

        _btnToggleEnable.Text = "Disable";
        _btnToggleEnable.Size = new Size(75, 30);
        _btnToggleEnable.Click += (_, _) => ToggleActionEnable();

        pnlTimelineToolbar.Controls.AddRange(new Control[] { _btnAddAction, _btnDeleteAction, _btnMoveUp, _btnMoveDown, _btnToggleEnable });
        pnlTimelineCard.Controls.Add(pnlTimelineToolbar);

        _splitMain.Panel1.Controls.Add(pnlTimelineCard);

        // --- Right Panel: Step Inspector Card ---
        var pnlInspectorCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.Current.CardSurface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(18),
            AutoScroll = true
        };

        var pnlInspectorFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        // Step Inspector Header
        var pnlInspectorHeader = new FlowLayoutPanel
        {
            Width = 560,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 14)
        };

        _lblInspectorTitle.Text = "Step Inspector";
        _lblInspectorTitle.Font = UITheme.FontHeader;
        _lblInspectorTitle.ForeColor = UITheme.Current.PrimaryText;
        _lblInspectorTitle.AutoSize = true;
        _lblInspectorTitle.Margin = new Padding(0, 0, 12, 0);

        _lblActionIdBadge.Text = "No step selected";
        _lblActionIdBadge.Font = UITheme.FontMicro;
        _lblActionIdBadge.BackColor = UITheme.Current.SecondarySurface;
        _lblActionIdBadge.ForeColor = UITheme.Current.SecondaryText;
        _lblActionIdBadge.AutoSize = true;
        _lblActionIdBadge.Padding = new Padding(8, 4, 8, 4);
        _lblActionIdBadge.Margin = new Padding(0, 4, 0, 0);

        pnlInspectorHeader.Controls.AddRange(new Control[] { _lblInspectorTitle, _lblActionIdBadge });
        pnlInspectorFlow.Controls.Add(pnlInspectorHeader);

        // --- Section 1: Action Definition & Target ---
        var pnlSec1 = CreateSectionHeader("1. Action & Target Application");
        pnlInspectorFlow.Controls.Add(pnlSec1);

        var lblType = new Label { Text = "Action Type:", AutoSize = true, Font = UITheme.FontBodyBold, Margin = new Padding(0, 4, 0, 2) };
        _cmbActionType.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbActionType.Width = 320;
        foreach (var t in ActionTypes.All) _cmbActionType.Items.Add(t);
        _cmbActionType.SelectedIndexChanged += OnActionTypeChanged;

        var lblDesc = new Label { Text = "Step Description:", AutoSize = true, Font = UITheme.FontBodyBold, Margin = new Padding(0, 8, 0, 2) };
        _txtStepDescription.Width = 480;
        _txtStepDescription.PlaceholderText = "e.g. Click Submit Button or Enter Customer Name";
        _txtStepDescription.TextChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.Description = _txtStepDescription.Text;
            _hasUnsavedChanges = true;
            UpdateListRow();
        };

        var lblProc = new Label { Text = "Target Process (optional):", AutoSize = true, Font = UITheme.FontBodyBold, Margin = new Padding(0, 8, 0, 2) };
        _txtTargetProcess.Width = 320;
        _txtTargetProcess.PlaceholderText = "e.g. notepad.exe or brave.exe";
        _txtTargetProcess.TextChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.Target ??= new TargetSelector();
            _selectedAction.Target.ProcessName = string.IsNullOrWhiteSpace(_txtTargetProcess.Text) ? null : _txtTargetProcess.Text.Trim();
            _hasUnsavedChanges = true;
        };

        var lblWin = new Label { Text = "Target Window Title (optional):", AutoSize = true, Font = UITheme.FontBodyBold, Margin = new Padding(0, 8, 0, 2) };
        _txtTargetWindow.Width = 480;
        _txtTargetWindow.PlaceholderText = "e.g. Untitled - Notepad";
        _txtTargetWindow.TextChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.Target ??= new TargetSelector();
            _selectedAction.Target.WindowTitle = string.IsNullOrWhiteSpace(_txtTargetWindow.Text) ? null : _txtTargetWindow.Text.Trim();
            _hasUnsavedChanges = true;
        };

        pnlInspectorFlow.Controls.AddRange(new Control[] { lblType, _cmbActionType, lblDesc, _txtStepDescription, lblProc, _txtTargetProcess, lblWin, _txtTargetWindow });

        // --- Section 2: Timing & Parameters ---
        var pnlSec2 = CreateSectionHeader("2. Execution Timing & Payload");
        pnlInspectorFlow.Controls.Add(pnlSec2);

        var pnlTimingGrid = new TableLayoutPanel
        {
            Width = 480,
            Height = 56,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 4, 0, 6)
        };
        pnlTimingGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pnlTimingGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var lblDelay = new Label { Text = "Pre-Action Delay (ms):", AutoSize = true, Font = UITheme.FontBodyBold };
        var lblTimeout = new Label { Text = "Action Timeout (ms):", AutoSize = true, Font = UITheme.FontBodyBold };
        pnlTimingGrid.Controls.Add(lblDelay, 0, 0);
        pnlTimingGrid.Controls.Add(lblTimeout, 1, 0);

        _numDelay.Minimum = 0;
        _numDelay.Maximum = 60000;
        _numDelay.Width = 180;
        _numDelay.ValueChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.DelayMs = (int)_numDelay.Value;
            _hasUnsavedChanges = true;
        };

        _numTimeout.Minimum = 500;
        _numTimeout.Maximum = 60000;
        _numTimeout.Width = 180;
        _numTimeout.ValueChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.TimeoutMs = (int)_numTimeout.Value;
            _hasUnsavedChanges = true;
        };

        pnlTimingGrid.Controls.Add(_numDelay, 0, 1);
        pnlTimingGrid.Controls.Add(_numTimeout, 1, 1);
        pnlInspectorFlow.Controls.Add(pnlTimingGrid);

        // Contextual Parameter Area
        _pnlContextParams.Width = 480;
        _pnlContextParams.AutoSize = true;
        _pnlContextParams.Margin = new Padding(0, 4, 0, 10);

        var pnlParamsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        _lblParamPrompt.Text = "Parameters:";
        _lblParamPrompt.Font = UITheme.FontBodyBold;
        _lblParamPrompt.AutoSize = true;
        _lblParamPrompt.Margin = new Padding(0, 0, 0, 4);

        _txtTextParam.Width = 480;
        _txtTextParam.Height = 28;
        _txtTextParam.PlaceholderText = "Text to type into focused target...";
        _txtTextParam.TextChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            if (_selectedAction.Type == ActionTypes.TypeText)
            {
                _selectedAction.Parameters["text"] = _txtTextParam.Text;
                _hasUnsavedChanges = true;
            }
        };

        _txtKeyParam.Width = 320;
        _txtKeyParam.PlaceholderText = "Key code, e.g. Return, Tab, Escape, Back";
        _txtKeyParam.TextChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            if (_selectedAction.Type == ActionTypes.KeyPress)
            {
                _selectedAction.Parameters["key"] = _txtKeyParam.Text;
                _hasUnsavedChanges = true;
            }
        };

        _cmbClickButton.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbClickButton.Items.AddRange(new object[] { "Left", "Right", "Middle" });
        _cmbClickButton.SelectedIndex = 0;
        _cmbClickButton.Width = 180;
        _cmbClickButton.SelectedIndexChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            if (_selectedAction.Type == ActionTypes.Click)
            {
                _selectedAction.Parameters["button"] = _cmbClickButton.SelectedItem?.ToString() ?? "Left";
                _hasUnsavedChanges = true;
            }
        };

        pnlParamsFlow.Controls.AddRange(new Control[] { _lblParamPrompt, _txtTextParam, _txtKeyParam, _cmbClickButton });
        _pnlContextParams.Controls.Add(pnlParamsFlow);
        pnlInspectorFlow.Controls.Add(_pnlContextParams);

        // --- Section 3: Safety Guardrails & Validation ---
        var pnlSec3 = CreateSectionHeader("3. Safety Policies & Verification");
        pnlInspectorFlow.Controls.Add(pnlSec3);

        _chkRequiresConfirmation.Text = "Require explicit user confirmation dialog before action executes";
        _chkRequiresConfirmation.AutoSize = true;
        _chkRequiresConfirmation.Font = UITheme.FontBody;
        _chkRequiresConfirmation.Margin = new Padding(0, 6, 0, 4);
        _chkRequiresConfirmation.CheckedChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.RequiresConfirmation = _chkRequiresConfirmation.Checked;
            _hasUnsavedChanges = true;
        };

        _chkVerifyTargetExists.Text = "Verify target process and window exist prior to sending input";
        _chkVerifyTargetExists.AutoSize = true;
        _chkVerifyTargetExists.Font = UITheme.FontBody;
        _chkVerifyTargetExists.Margin = new Padding(0, 4, 0, 10);
        _chkVerifyTargetExists.CheckedChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.Verification ??= new ActionVerification();
            _selectedAction.Verification.VerifyTargetExists = _chkVerifyTargetExists.Checked;
            _hasUnsavedChanges = true;
        };

        // Live Validation Feedback Card
        _pnlValidationFeedback.Width = 480;
        _pnlValidationFeedback.AutoSize = true;
        _pnlValidationFeedback.Padding = new Padding(12);
        _pnlValidationFeedback.BackColor = UITheme.Current.SuccessSurface;
        _pnlValidationFeedback.BorderStyle = BorderStyle.FixedSingle;
        _pnlValidationFeedback.Margin = new Padding(0, 4, 0, 14);

        _lblValidationFeedback.Text = "✓ All safety and schema validation checks passed.";
        _lblValidationFeedback.Font = UITheme.FontBody;
        _lblValidationFeedback.ForeColor = UITheme.Current.SuccessText;
        _lblValidationFeedback.AutoSize = true;
        _lblValidationFeedback.Dock = DockStyle.Fill;
        _pnlValidationFeedback.Controls.Add(_lblValidationFeedback);

        pnlInspectorFlow.Controls.AddRange(new Control[] { _chkRequiresConfirmation, _chkVerifyTargetExists, _pnlValidationFeedback });

        pnlInspectorCard.Controls.Add(pnlInspectorFlow);
        _splitMain.Panel2.Controls.Add(pnlInspectorCard);

        mainLayout.Controls.Add(_splitMain, 0, 1);
        Controls.Add(mainLayout);

        Resize += (_, _) => AdjustSplitter();
        UITheme.Apply(this);
    }

    private static Label CreateSectionHeader(string title)
    {
        return new Label
        {
            Text = title,
            Font = UITheme.FontSubheader,
            ForeColor = ColorTranslator.FromHtml("#60A5FA"),
            AutoSize = true,
            Margin = new Padding(0, 14, 0, 6)
        };
    }

    private void AdjustSplitter()
    {
        if (_splitMain is null || _splitMain.Width <= 200) return;
        int targetDist = (int)(_splitMain.Width * 0.38);
        int minDist = 280;
        int maxDist = _splitMain.Width - 340;
        if (maxDist > minDist)
        {
            _splitMain.SplitterDistance = Math.Clamp(targetDist, minDist, maxDist);
        }

        if (_listActions.Columns.Count >= 3)
        {
            int available = _listActions.ClientSize.Width - 140;
            _listActions.Columns[2].Width = Math.Max(120, available);
        }
    }

    private void PopulateActionsList()
    {
        _listActions.Items.Clear();
        for (int i = 0; i < _workflow.Actions.Count; i++)
        {
            var a = _workflow.Actions[i];
            var item = new ListViewItem((i + 1).ToString());
            item.SubItems.Add(a.Type);
            item.SubItems.Add(a.Description ?? a.Target?.ProcessName ?? a.Id);
            item.Tag = a;
            if (!a.Enabled) item.ForeColor = Color.Gray;
            _listActions.Items.Add(item);
        }

        _lblStepCountBadge.Text = $"{_workflow.Actions.Count} step{(_workflow.Actions.Count == 1 ? "" : "s")}";

        if (_listActions.Items.Count > 0)
        {
            _listActions.Items[0].Selected = true;
        }
        else
        {
            ClearInspector();
        }
    }

    private void OnActionSelectionChanged(object? sender, EventArgs e)
    {
        if (_listActions.SelectedItems.Count > 0 && _listActions.SelectedItems[0].Tag is WorkflowAction action)
        {
            _selectedAction = action;
            _isLoadingAction = true;
            try
            {
                int index = _listActions.SelectedIndices[0] + 1;
                _lblInspectorTitle.Text = $"Step {index}: {action.Type}";
                _lblActionIdBadge.Text = $"ID: {action.Id}";

                _cmbActionType.SelectedItem = action.Type;
                _txtStepDescription.Text = action.Description ?? string.Empty;
                _txtTargetProcess.Text = action.Target?.ProcessName ?? string.Empty;
                _txtTargetWindow.Text = action.Target?.WindowTitle ?? string.Empty;
                _numDelay.Value = Math.Clamp(action.DelayMs, 0, 60000);
                _numTimeout.Value = Math.Clamp(action.TimeoutMs, 500, 60000);
                _chkRequiresConfirmation.Checked = action.RequiresConfirmation;
                _chkVerifyTargetExists.Checked = action.Verification?.VerifyTargetExists ?? true;
                _btnToggleEnable.Text = action.Enabled ? "Disable" : "Enable";

                UpdateContextualParameters(action);
            }
            finally
            {
                _isLoadingAction = false;
            }
        }
        else
        {
            ClearInspector();
        }
    }

    private void ClearInspector()
    {
        _selectedAction = null;
        _lblInspectorTitle.Text = "Step Inspector";
        _lblActionIdBadge.Text = "Select a step from the timeline to edit";
        _txtStepDescription.Text = string.Empty;
        _txtTargetProcess.Text = string.Empty;
        _txtTargetWindow.Text = string.Empty;
        _numDelay.Value = 0;
        _numTimeout.Value = 5000;
        _txtTextParam.Visible = false;
        _txtKeyParam.Visible = false;
        _cmbClickButton.Visible = false;
        _lblParamPrompt.Text = "Select a step to view parameters";
    }

    private void UpdateContextualParameters(WorkflowAction action)
    {
        _txtTextParam.Visible = false;
        _txtKeyParam.Visible = false;
        _cmbClickButton.Visible = false;

        if (action.Type == ActionTypes.TypeText)
        {
            _lblParamPrompt.Text = "Text Payload to Type:";
            _txtTextParam.Visible = true;
            _txtTextParam.Text = action.Parameters.TryGetValue("text", out var t) ? t?.ToString() ?? string.Empty : string.Empty;
        }
        else if (action.Type == ActionTypes.KeyPress)
        {
            _lblParamPrompt.Text = "Key Code (e.g. Return, Escape, Tab):";
            _txtKeyParam.Visible = true;
            _txtKeyParam.Text = action.Parameters.TryGetValue("key", out var k) ? k?.ToString() ?? string.Empty : string.Empty;
        }
        else if (action.Type == ActionTypes.Click)
        {
            _lblParamPrompt.Text = "Mouse Button to Click:";
            _cmbClickButton.Visible = true;
            string btn = action.Parameters.TryGetValue("button", out var b) ? b?.ToString() ?? "Left" : "Left";
            _cmbClickButton.SelectedItem = btn;
        }
        else if (action.Type == ActionTypes.Wait)
        {
            _lblParamPrompt.Text = $"Wait duration controlled by Delay setting ({action.DelayMs} ms)";
        }
        else
        {
            _lblParamPrompt.Text = $"Standard action: {action.Type}";
        }
    }

    private void OnActionTypeChanged(object? sender, EventArgs e)
    {
        if (_isLoadingAction || _selectedAction is null) return;
        var newType = _cmbActionType.SelectedItem?.ToString();
        if (newType is not null && ActionTypes.IsSupported(newType))
        {
            _selectedAction.Type = newType;
            _hasUnsavedChanges = true;
            UpdateContextualParameters(_selectedAction);
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
            item.SubItems[2].Text = _selectedAction.Description ?? _selectedAction.Target?.ProcessName ?? _selectedAction.Id;
            item.ForeColor = _selectedAction.Enabled ? UITheme.Current.PrimaryText : Color.Gray;
        }
    }

    private void ValidateWorkflow()
    {
        var result = WorkflowValidator.Validate(_workflow);
        if (result.IsValid)
        {
            _lblValidationBadge.Text = "✓ Valid & Secure";
            _lblValidationBadge.BackColor = UITheme.Current.SuccessSurface;
            _lblValidationBadge.ForeColor = UITheme.Current.SuccessText;

            _pnlValidationFeedback.BackColor = UITheme.Current.SuccessSurface;
            _lblValidationFeedback.Text = "✓ All safety and schema validation checks passed.\r\nWorkflow conforms to strict declarative execution policy.";
            _lblValidationFeedback.ForeColor = UITheme.Current.SuccessText;
        }
        else
        {
            _lblValidationBadge.Text = "⚠️ Validation Issues";
            _lblValidationBadge.BackColor = UITheme.Current.WarningSurface;
            _lblValidationBadge.ForeColor = UITheme.Current.Warning;

            _pnlValidationFeedback.BackColor = UITheme.Current.ErrorSurface;
            _lblValidationFeedback.Text = "✕ VALIDATION ERRORS:\r\n" + string.Join("\r\n", result.Errors);
            _lblValidationFeedback.ForeColor = UITheme.Current.Error;
        }
    }

    private async Task SaveWorkflowAsync()
    {
        _workflow.Name = _txtWorkflowName.Text.Trim();
        var validation = WorkflowValidator.Validate(_workflow);
        if (!validation.IsValid)
        {
            MessageBox.Show("Cannot save invalid workflow:\r\n" + string.Join("\r\n", validation.Errors), "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
