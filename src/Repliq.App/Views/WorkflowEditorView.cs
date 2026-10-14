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

    // Left Column: Step Sequence Timeline
    private readonly Label _lblStepCountBadge = new();
    private readonly ThemedButton _btnAddType = new();
    private readonly ThemedButton _btnAddClick = new();
    private readonly ThemedButton _btnAddKey = new();
    private readonly ThemedButton _btnAddWait = new();
    private readonly ListView _listActions = new();
    private readonly ThemedButton _btnMoveUp = new();
    private readonly ThemedButton _btnMoveDown = new();
    private readonly ThemedButton _btnToggleEnable = new();
    private readonly ThemedButton _btnDeleteAction = new();

    // Right Column: Step Inspector (Detail)
    private readonly Panel _pnlInspectorHost = new();
    private readonly Panel _pnlEmptyInspector = new();
    private readonly Panel _pnlActiveInspector = new();

    // Inspector Header
    private readonly Label _lblInspectorTitle = new();
    private readonly Label _lblActionIdBadge = new();

    // Card 1: Action Definition
    private readonly ComboBox _cmbActionType = new();
    private readonly TextBox _txtStepDescription = new();

    // Contextual parameter controls
    private readonly Panel _pnlContextCard = new();
    private readonly Label _lblContextCardHeader = new();
    private readonly Panel _pnlTypeControls = new();
    private readonly Panel _pnlKeyControls = new();
    private readonly Panel _pnlClickControls = new();
    private readonly Panel _pnlWaitControls = new();
    private readonly TextBox _txtTextParam = new();
    private readonly ComboBox _cmbKeyParam = new();
    private readonly TextBox _txtCustomKey = new();
    private readonly ComboBox _cmbClickButton = new();
    private readonly NumericUpDown _numClickX = new();
    private readonly NumericUpDown _numClickY = new();
    private readonly NumericUpDown _numWaitDuration = new();

    // Card 2: Target Scope (Optional)
    private readonly Panel _pnlTargetCard = new();
    private readonly TextBox _txtTargetProcess = new();
    private readonly TextBox _txtTargetWindow = new();

    // Card 3: Guardrails & Timing
    private readonly NumericUpDown _numDelay = new();
    private readonly NumericUpDown _numTimeout = new();
    private readonly CheckBox _chkRequiresConfirmation = new();
    private readonly CheckBox _chkVerifyTargetExists = new();

    // Card 4: Live Validation Status
    private readonly Panel _pnlValidationCard = new();
    private readonly Label _lblValidationText = new();

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
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 2-Column Split

        // --- 1. Top Action Bar ---
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

        _txtWorkflowName.Width = 300;
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
        _btnValidate.Tag = "subtle";
        _btnValidate.Size = new Size(140, 34);
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

        // --- 2. 2-Column Split: Master Timeline + Detail Inspector ---
        _splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 380,
            Margin = new Padding(0)
        };

        // ==================== LEFT COLUMN: TIMELINE CARD ====================
        var pnlTimelineCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UITheme.Current.CardSurface,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(12),
            Margin = new Padding(0, 0, 8, 0)
        };

        // Header: Title & Step Count
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

        // Quick-Add Action Bar (Direct 1-click step creation)
        var pnlQuickAdd = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 4)
        };

        _btnAddType.Text = "+ ⌨ Type";
        _btnAddType.Tag = "subtle";
        _btnAddType.Size = new Size(74, 28);
        _btnAddType.Margin = new Padding(0, 0, 4, 0);
        _btnAddType.Click += (_, _) => AddNewAction(ActionTypes.TypeText);

        _btnAddClick.Text = "+ 🖱 Click";
        _btnAddClick.Tag = "subtle";
        _btnAddClick.Size = new Size(74, 28);
        _btnAddClick.Margin = new Padding(0, 0, 4, 0);
        _btnAddClick.Click += (_, _) => AddNewAction(ActionTypes.Click);

        _btnAddKey.Text = "+ 🔤 Key";
        _btnAddKey.Tag = "subtle";
        _btnAddKey.Size = new Size(74, 28);
        _btnAddKey.Margin = new Padding(0, 0, 4, 0);
        _btnAddKey.Click += (_, _) => AddNewAction(ActionTypes.KeyPress);

        _btnAddWait.Text = "+ ⏳ Wait";
        _btnAddWait.Tag = "subtle";
        _btnAddWait.Size = new Size(74, 28);
        _btnAddWait.Margin = new Padding(0, 0, 4, 0);
        _btnAddWait.Click += (_, _) => AddNewAction(ActionTypes.Wait);

        pnlQuickAdd.Controls.AddRange(new Control[] { _btnAddType, _btnAddClick, _btnAddKey, _btnAddWait });

        // Timeline Toolbar (Bottom)
        var pnlTimelineToolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };

        _btnMoveUp.Text = "▲ Up";
        _btnMoveUp.Size = new Size(62, 28);
        _btnMoveUp.Margin = new Padding(0, 0, 4, 0);
        _btnMoveUp.Click += (_, _) => MoveAction(-1);

        _btnMoveDown.Text = "▼ Down";
        _btnMoveDown.Size = new Size(68, 28);
        _btnMoveDown.Margin = new Padding(0, 0, 4, 0);
        _btnMoveDown.Click += (_, _) => MoveAction(1);

        _btnToggleEnable.Text = "Disable";
        _btnToggleEnable.Size = new Size(70, 28);
        _btnToggleEnable.Margin = new Padding(0, 0, 4, 0);
        _btnToggleEnable.Click += (_, _) => ToggleActionEnable();

        _btnDeleteAction.Text = "✕ Del";
        _btnDeleteAction.Tag = "danger";
        _btnDeleteAction.Size = new Size(58, 28);
        _btnDeleteAction.Margin = new Padding(0, 0, 4, 0);
        _btnDeleteAction.Click += (_, _) => DeleteSelectedAction();

        pnlTimelineToolbar.Controls.AddRange(new Control[] { _btnMoveUp, _btnMoveDown, _btnToggleEnable, _btnDeleteAction });

        // ListView (Center, Fill)
        _listActions.Dock = DockStyle.Fill;
        _listActions.View = View.Details;
        _listActions.FullRowSelect = true;
        _listActions.MultiSelect = false;
        _listActions.Font = UITheme.FontBody;
        _listActions.Columns.Add("#", 34);
        _listActions.Columns.Add("Type", 100);
        _listActions.Columns.Add("Step Summary / Target", 220);
        _listActions.SelectedIndexChanged += OnActionSelectionChanged;

        // Assembly of Left Panel (Order ensures correct Docking layout in WinForms)
        pnlTimelineCard.Controls.Add(_listActions);
        pnlTimelineCard.Controls.Add(pnlQuickAdd);
        pnlTimelineCard.Controls.Add(pnlTimelineToolbar);
        pnlTimelineCard.Controls.Add(pnlTimelineHeader);
        _listActions.BringToFront();

        _splitMain.Panel1.Controls.Add(pnlTimelineCard);

        // ==================== RIGHT COLUMN: STEP INSPECTOR ====================
        _pnlInspectorHost.Dock = DockStyle.Fill;
        _pnlInspectorHost.BackColor = UITheme.Current.CardSurface;
        _pnlInspectorHost.BorderStyle = BorderStyle.FixedSingle;
        _pnlInspectorHost.Padding = new Padding(16);
        _pnlInspectorHost.AutoScroll = true;

        // Empty State Panel (when no step is selected)
        _pnlEmptyInspector.Dock = DockStyle.Fill;
        var lblEmptyPrompt = new Label
        {
            Text = "Select a step from the timeline or use the quick-add buttons (+ Type, + Click, + Key, + Wait) to start editing.",
            Font = UITheme.FontSubheader,
            ForeColor = UITheme.Current.SecondaryText,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill
        };
        _pnlEmptyInspector.Controls.Add(lblEmptyPrompt);

        // Active Inspector Layout Panel
        _pnlActiveInspector.Dock = DockStyle.Top;
        _pnlActiveInspector.AutoSize = true;

        var inspectorFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = new Padding(0)
        };

        // Header: Step Title & Action Badge
        var pnlInspHeader = new TableLayoutPanel
        {
            Width = 520,
            Height = 36,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 12)
        };
        pnlInspHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pnlInspHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _lblInspectorTitle.Text = "Step Inspector";
        _lblInspectorTitle.Font = UITheme.FontHeader;
        _lblInspectorTitle.ForeColor = UITheme.Current.PrimaryText;
        _lblInspectorTitle.Dock = DockStyle.Fill;
        _lblInspectorTitle.TextAlign = ContentAlignment.MiddleLeft;
        pnlInspHeader.Controls.Add(_lblInspectorTitle, 0, 0);

        _lblActionIdBadge.Text = "ID: -";
        _lblActionIdBadge.Font = UITheme.FontMicro;
        _lblActionIdBadge.BackColor = UITheme.Current.SecondarySurface;
        _lblActionIdBadge.ForeColor = UITheme.Current.SecondaryText;
        _lblActionIdBadge.AutoSize = true;
        _lblActionIdBadge.Padding = new Padding(8, 4, 8, 4);
        _lblActionIdBadge.Dock = DockStyle.Right;
        pnlInspHeader.Controls.Add(_lblActionIdBadge, 1, 0);
        inspectorFlow.Controls.Add(pnlInspHeader);

        // --- Card 1: Action Identity & Description ---
        var pnlActionDefCard = CreateCard("1. Action Definition");
        pnlActionDefCard.Width = 520;
        pnlActionDefCard.AutoSize = true;

        var pnlDefGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            Padding = new Padding(12),
            Margin = new Padding(0)
        };
        pnlDefGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        pnlDefGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        pnlDefGrid.Controls.Add(new Label { Text = "Action Type:", Font = UITheme.FontBodyBold, ForeColor = UITheme.Current.PrimaryText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _cmbActionType.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbActionType.Dock = DockStyle.Fill;
        _cmbActionType.Font = UITheme.FontBody;
        foreach (var t in ActionTypes.All) _cmbActionType.Items.Add(t);
        _cmbActionType.SelectedIndexChanged += OnActionTypeChanged;
        pnlDefGrid.Controls.Add(_cmbActionType, 1, 0);

        pnlDefGrid.Controls.Add(new Label { Text = "Description:", Font = UITheme.FontBodyBold, ForeColor = UITheme.Current.PrimaryText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        _txtStepDescription.Dock = DockStyle.Fill;
        _txtStepDescription.Font = UITheme.FontBody;
        _txtStepDescription.PlaceholderText = "e.g. Enter Customer Email or Click Submit";
        _txtStepDescription.TextChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.Description = _txtStepDescription.Text;
            _hasUnsavedChanges = true;
            UpdateListRow();
        };
        pnlDefGrid.Controls.Add(_txtStepDescription, 1, 1);
        pnlActionDefCard.Controls.Add(pnlDefGrid);
        inspectorFlow.Controls.Add(pnlActionDefCard);

        // --- Card 2: Contextual Parameters ---
        _pnlContextCard.Width = 520;
        _pnlContextCard.AutoSize = true;
        _pnlContextCard.BackColor = UITheme.Current.SecondarySurface;
        _pnlContextCard.BorderStyle = BorderStyle.FixedSingle;
        _pnlContextCard.Margin = new Padding(0, 0, 0, 10);
        _pnlContextCard.Padding = new Padding(12);

        _lblContextCardHeader.Text = "2. Parameters";
        _lblContextCardHeader.Font = UITheme.FontSubheader;
        _lblContextCardHeader.ForeColor = ColorTranslator.FromHtml("#60A5FA");
        _lblContextCardHeader.Dock = DockStyle.Top;
        _lblContextCardHeader.Height = 24;
        _pnlContextCard.Controls.Add(_lblContextCardHeader);

        // Contextual Sub-Panel: TypeText
        _pnlTypeControls.Dock = DockStyle.Top;
        _pnlTypeControls.AutoSize = true;
        _pnlTypeControls.Padding = new Padding(0, 6, 0, 0);
        var lblTypePrompt = new Label { Text = "Text Payload (sent to focused target):", Font = UITheme.FontBodyBold, ForeColor = UITheme.Current.PrimaryText, Dock = DockStyle.Top, Height = 22 };
        _txtTextParam.Dock = DockStyle.Top;
        _txtTextParam.Height = 56;
        _txtTextParam.Multiline = true;
        _txtTextParam.Font = UITheme.FontMonospace;
        _txtTextParam.ScrollBars = ScrollBars.Vertical;
        _txtTextParam.PlaceholderText = "Enter text to type into active control...";
        _txtTextParam.TextChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            if (_selectedAction.Type == ActionTypes.TypeText)
            {
                _selectedAction.Parameters["text"] = _txtTextParam.Text;
                _hasUnsavedChanges = true;
                UpdateListRow();
            }
        };
        _pnlTypeControls.Controls.Add(_txtTextParam);
        _pnlTypeControls.Controls.Add(lblTypePrompt);
        _pnlContextCard.Controls.Add(_pnlTypeControls);

        // Contextual Sub-Panel: KeyPress
        _pnlKeyControls.Dock = DockStyle.Top;
        _pnlKeyControls.AutoSize = true;
        _pnlKeyControls.Padding = new Padding(0, 6, 0, 0);
        var pnlKeyGrid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 2, AutoSize = true };
        pnlKeyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pnlKeyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        pnlKeyGrid.Controls.Add(new Label { Text = "Standard Key Preset:", Font = UITheme.FontBodyBold, ForeColor = UITheme.Current.PrimaryText, Dock = DockStyle.Fill }, 0, 0);
        pnlKeyGrid.Controls.Add(new Label { Text = "Or Custom Key Code:", Font = UITheme.FontBodyBold, ForeColor = UITheme.Current.PrimaryText, Dock = DockStyle.Fill }, 1, 0);

        _cmbKeyParam.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbKeyParam.Dock = DockStyle.Fill;
        _cmbKeyParam.Items.AddRange(new object[] { "Return", "Tab", "Escape", "Back", "Space", "Down", "Up", "Left", "Right", "Delete", "Home", "End", "PageUp", "PageDown", "F1", "F2", "F5", "Custom" });
        _cmbKeyParam.SelectedIndexChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            var sel = _cmbKeyParam.SelectedItem?.ToString();
            if (sel != "Custom" && sel is not null)
            {
                _txtCustomKey.Text = sel;
                _selectedAction.Parameters["key"] = sel;
                _hasUnsavedChanges = true;
                UpdateListRow();
            }
        };
        pnlKeyGrid.Controls.Add(_cmbKeyParam, 0, 1);

        _txtCustomKey.Dock = DockStyle.Fill;
        _txtCustomKey.PlaceholderText = "e.g. Return, Tab, Escape";
        _txtCustomKey.TextChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            if (_selectedAction.Type == ActionTypes.KeyPress)
            {
                _selectedAction.Parameters["key"] = _txtCustomKey.Text.Trim();
                _hasUnsavedChanges = true;
                UpdateListRow();
            }
        };
        pnlKeyGrid.Controls.Add(_txtCustomKey, 1, 1);
        _pnlKeyControls.Controls.Add(pnlKeyGrid);
        _pnlContextCard.Controls.Add(_pnlKeyControls);

        // Contextual Sub-Panel: Click
        _pnlClickControls.Dock = DockStyle.Top;
        _pnlClickControls.AutoSize = true;
        _pnlClickControls.Padding = new Padding(0, 6, 0, 0);
        var pnlClickGrid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, RowCount = 2, AutoSize = true };
        pnlClickGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        pnlClickGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        pnlClickGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        pnlClickGrid.Controls.Add(new Label { Text = "Mouse Button:", Font = UITheme.FontBodyBold, ForeColor = UITheme.Current.PrimaryText, Dock = DockStyle.Fill }, 0, 0);
        pnlClickGrid.Controls.Add(new Label { Text = "X Coord (opt):", Font = UITheme.FontBodyBold, ForeColor = UITheme.Current.PrimaryText, Dock = DockStyle.Fill }, 1, 0);
        pnlClickGrid.Controls.Add(new Label { Text = "Y Coord (opt):", Font = UITheme.FontBodyBold, ForeColor = UITheme.Current.PrimaryText, Dock = DockStyle.Fill }, 2, 0);

        _cmbClickButton.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbClickButton.Dock = DockStyle.Fill;
        _cmbClickButton.Items.AddRange(new object[] { "Left", "Right", "Middle" });
        _cmbClickButton.SelectedIndex = 0;
        _cmbClickButton.SelectedIndexChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            if (_selectedAction.Type == ActionTypes.Click)
            {
                _selectedAction.Parameters["button"] = _cmbClickButton.SelectedItem?.ToString() ?? "Left";
                _hasUnsavedChanges = true;
                UpdateListRow();
            }
        };
        pnlClickGrid.Controls.Add(_cmbClickButton, 0, 1);

        _numClickX.Minimum = 0;
        _numClickX.Maximum = 9999;
        _numClickX.Dock = DockStyle.Fill;
        _numClickX.ValueChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.Parameters["x"] = (int)_numClickX.Value;
            _hasUnsavedChanges = true;
        };
        pnlClickGrid.Controls.Add(_numClickX, 1, 1);

        _numClickY.Minimum = 0;
        _numClickY.Maximum = 9999;
        _numClickY.Dock = DockStyle.Fill;
        _numClickY.ValueChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.Parameters["y"] = (int)_numClickY.Value;
            _hasUnsavedChanges = true;
        };
        pnlClickGrid.Controls.Add(_numClickY, 2, 1);
        _pnlClickControls.Controls.Add(pnlClickGrid);
        _pnlContextCard.Controls.Add(_pnlClickControls);

        // Contextual Sub-Panel: Wait
        _pnlWaitControls.Dock = DockStyle.Top;
        _pnlWaitControls.AutoSize = true;
        _pnlWaitControls.Padding = new Padding(0, 6, 0, 0);
        var pnlWaitGrid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1, AutoSize = true };
        pnlWaitGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        pnlWaitGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _numWaitDuration.Minimum = 0;
        _numWaitDuration.Maximum = 60000;
        _numWaitDuration.Dock = DockStyle.Fill;
        _numWaitDuration.ValueChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.DelayMs = (int)_numWaitDuration.Value;
            _selectedAction.Parameters["durationMs"] = (int)_numWaitDuration.Value;
            _hasUnsavedChanges = true;
            UpdateListRow();
        };
        pnlWaitGrid.Controls.Add(_numWaitDuration, 0, 0);

        var lblWaitNote = new Label
        {
            Text = "Pause execution before advancing to next step.",
            ForeColor = UITheme.Current.SecondaryText,
            Font = UITheme.FontSmall,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        pnlWaitGrid.Controls.Add(lblWaitNote, 1, 0);
        _pnlWaitControls.Controls.Add(pnlWaitGrid);
        _pnlContextCard.Controls.Add(_pnlWaitControls);

        inspectorFlow.Controls.Add(_pnlContextCard);

        // --- Card 3: Target Application (Hidden for Wait action) ---
        _pnlTargetCard.Width = 520;
        _pnlTargetCard.AutoSize = true;
        _pnlTargetCard.BackColor = UITheme.Current.SecondarySurface;
        _pnlTargetCard.BorderStyle = BorderStyle.FixedSingle;
        _pnlTargetCard.Margin = new Padding(0, 0, 0, 10);
        _pnlTargetCard.Padding = new Padding(12);

        var lblTargetHeader = new Label
        {
            Text = "3. Target Application (Optional)",
            Font = UITheme.FontSubheader,
            ForeColor = ColorTranslator.FromHtml("#60A5FA"),
            Dock = DockStyle.Top,
            Height = 24
        };
        _pnlTargetCard.Controls.Add(lblTargetHeader);

        var pnlTargetGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            Padding = new Padding(0, 4, 0, 0)
        };
        pnlTargetGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        pnlTargetGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        pnlTargetGrid.Controls.Add(new Label { Text = "Target Process:", Font = UITheme.FontBodyBold, ForeColor = UITheme.Current.PrimaryText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _txtTargetProcess.Dock = DockStyle.Fill;
        _txtTargetProcess.PlaceholderText = "e.g. notepad.exe or msedge.exe";
        _txtTargetProcess.TextChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.Target ??= new TargetSelector();
            _selectedAction.Target.ProcessName = string.IsNullOrWhiteSpace(_txtTargetProcess.Text) ? null : _txtTargetProcess.Text.Trim();
            _hasUnsavedChanges = true;
        };
        pnlTargetGrid.Controls.Add(_txtTargetProcess, 1, 0);

        pnlTargetGrid.Controls.Add(new Label { Text = "Target Window Title:", Font = UITheme.FontBodyBold, ForeColor = UITheme.Current.PrimaryText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        _txtTargetWindow.Dock = DockStyle.Fill;
        _txtTargetWindow.PlaceholderText = "e.g. Untitled - Notepad";
        _txtTargetWindow.TextChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.Target ??= new TargetSelector();
            _selectedAction.Target.WindowTitle = string.IsNullOrWhiteSpace(_txtTargetWindow.Text) ? null : _txtTargetWindow.Text.Trim();
            _hasUnsavedChanges = true;
        };
        pnlTargetGrid.Controls.Add(_txtTargetWindow, 1, 1);

        var lblTargetHint = new Label
        {
            Text = "💡 Tip: Leave both empty to interact with whichever window currently holds active desktop focus.",
            Font = UITheme.FontSmall,
            ForeColor = UITheme.Current.SecondaryText,
            Dock = DockStyle.Fill,
            Height = 22,
            Margin = new Padding(0, 4, 0, 0)
        };
        pnlTargetGrid.Controls.Add(lblTargetHint, 1, 2);
        _pnlTargetCard.Controls.Add(pnlTargetGrid);
        inspectorFlow.Controls.Add(_pnlTargetCard);

        // --- Card 4: Timing & Safety Guardrails ---
        var pnlGuardCard = CreateCard("4. Timing & Safety Policies");
        pnlGuardCard.Width = 520;
        pnlGuardCard.AutoSize = true;

        var pnlTimingGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            Padding = new Padding(12),
            Margin = new Padding(0)
        };
        pnlTimingGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pnlTimingGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        pnlTimingGrid.Controls.Add(new Label { Text = "Pre-Action Delay (ms):", Font = UITheme.FontBodyBold, ForeColor = UITheme.Current.PrimaryText, Dock = DockStyle.Fill }, 0, 0);
        pnlTimingGrid.Controls.Add(new Label { Text = "Action Timeout (ms):", Font = UITheme.FontBodyBold, ForeColor = UITheme.Current.PrimaryText, Dock = DockStyle.Fill }, 1, 0);

        _numDelay.Minimum = 0;
        _numDelay.Maximum = 60000;
        _numDelay.Dock = DockStyle.Fill;
        _numDelay.ValueChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.DelayMs = (int)_numDelay.Value;
            _hasUnsavedChanges = true;
        };
        pnlTimingGrid.Controls.Add(_numDelay, 0, 1);

        _numTimeout.Minimum = 500;
        _numTimeout.Maximum = 60000;
        _numTimeout.Dock = DockStyle.Fill;
        _numTimeout.ValueChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.TimeoutMs = (int)_numTimeout.Value;
            _hasUnsavedChanges = true;
        };
        pnlTimingGrid.Controls.Add(_numTimeout, 1, 1);
        pnlGuardCard.Controls.Add(pnlTimingGrid);

        var pnlChecks = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Padding = new Padding(12, 0, 12, 8)
        };

        _chkRequiresConfirmation.Text = "Require user confirmation popup before executing this step";
        _chkRequiresConfirmation.AutoSize = true;
        _chkRequiresConfirmation.Font = UITheme.FontBody;
        _chkRequiresConfirmation.ForeColor = UITheme.Current.PrimaryText;
        _chkRequiresConfirmation.Margin = new Padding(0, 0, 0, 6);
        _chkRequiresConfirmation.CheckedChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.RequiresConfirmation = _chkRequiresConfirmation.Checked;
            _hasUnsavedChanges = true;
        };
        pnlChecks.Controls.Add(_chkRequiresConfirmation);

        _chkVerifyTargetExists.Text = "Verify target application and window exist prior to action";
        _chkVerifyTargetExists.AutoSize = true;
        _chkVerifyTargetExists.Font = UITheme.FontBody;
        _chkVerifyTargetExists.ForeColor = UITheme.Current.PrimaryText;
        _chkVerifyTargetExists.Margin = new Padding(0, 0, 0, 4);
        _chkVerifyTargetExists.CheckedChanged += (_, _) =>
        {
            if (_isLoadingAction || _selectedAction is null) return;
            _selectedAction.Verification ??= new ActionVerification();
            _selectedAction.Verification.VerifyTargetExists = _chkVerifyTargetExists.Checked;
            _hasUnsavedChanges = true;
        };
        pnlChecks.Controls.Add(_chkVerifyTargetExists);
        pnlGuardCard.Controls.Add(pnlChecks);
        inspectorFlow.Controls.Add(pnlGuardCard);

        // --- Card 5: Real-Time Validation Feedback ---
        _pnlValidationCard.Width = 520;
        _pnlValidationCard.AutoSize = true;
        _pnlValidationCard.Padding = new Padding(12);
        _pnlValidationCard.BorderStyle = BorderStyle.FixedSingle;
        _pnlValidationCard.Margin = new Padding(0, 0, 0, 16);

        _lblValidationText.Font = UITheme.FontBody;
        _lblValidationText.AutoSize = true;
        _lblValidationText.Dock = DockStyle.Fill;
        _pnlValidationCard.Controls.Add(_lblValidationText);
        inspectorFlow.Controls.Add(_pnlValidationCard);

        _pnlActiveInspector.Controls.Add(inspectorFlow);

        _pnlInspectorHost.Controls.Add(_pnlEmptyInspector);
        _pnlInspectorHost.Controls.Add(_pnlActiveInspector);

        _splitMain.Panel2.Controls.Add(_pnlInspectorHost);
        mainLayout.Controls.Add(_splitMain, 0, 1);

        Controls.Add(mainLayout);

        Resize += (_, _) => AdjustSplitter();
        UITheme.Apply(this);
    }

    private static Panel CreateCard(string title)
    {
        var card = new Panel
        {
            BackColor = UITheme.Current.SecondarySurface,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(0)
        };
        var lbl = new Label
        {
            Text = title,
            Font = UITheme.FontSubheader,
            ForeColor = ColorTranslator.FromHtml("#60A5FA"),
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(12, 6, 0, 0)
        };
        card.Controls.Add(lbl);
        return card;
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
            int available = _listActions.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 140;
            _listActions.Columns[2].Width = Math.Max(120, available);
        }

        // Keep inspector cards responsive to full width
        int inspectorWidth = _pnlInspectorHost.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 32;
        if (inspectorWidth > 320)
        {
            foreach (Control c in _pnlActiveInspector.Controls)
            {
                if (c is FlowLayoutPanel flp)
                {
                    foreach (Control child in flp.Controls)
                    {
                        child.Width = inspectorWidth;
                    }
                }
            }
        }
    }

    private void PopulateActionsList()
    {
        _listActions.Items.Clear();
        for (int i = 0; i < _workflow.Actions.Count; i++)
        {
            var a = _workflow.Actions[i];
            var item = new ListViewItem((i + 1).ToString());
            item.SubItems.Add(GetActionDisplayType(a.Type));
            item.SubItems.Add(GetActionDisplayDetail(a));
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
            ShowEmptyInspector();
        }
    }

    private static string GetActionDisplayType(string type) => type switch
    {
        ActionTypes.TypeText => "⌨ Type Text",
        ActionTypes.KeyPress => "🔤 Key Press",
        ActionTypes.Click => "🖱 Mouse Click",
        ActionTypes.Wait => "⏳ Delay",
        _ => type
    };

    private static string GetActionDisplayDetail(WorkflowAction a)
    {
        if (a.Type == ActionTypes.TypeText && a.Parameters.TryGetValue("text", out var t))
        {
            var text = t?.ToString() ?? "";
            return $"Text: \"{(text.Length > 24 ? text[..24] + "..." : text)}\"";
        }
        if (a.Type == ActionTypes.KeyPress && a.Parameters.TryGetValue("key", out var k))
        {
            return $"Key: [{k}]";
        }
        if (a.Type == ActionTypes.Click && a.Parameters.TryGetValue("button", out var b))
        {
            var target = a.Target?.ProcessName ?? "Active App";
            return $"Button: {b} ({target})";
        }
        if (a.Type == ActionTypes.Wait)
        {
            return $"Wait: {a.DelayMs} ms";
        }
        return a.Description ?? a.Target?.ProcessName ?? a.Id;
    }

    private void OnActionSelectionChanged(object? sender, EventArgs e)
    {
        if (_listActions.SelectedItems.Count > 0 && _listActions.SelectedItems[0].Tag is WorkflowAction action)
        {
            _selectedAction = action;
            _isLoadingAction = true;
            try
            {
                _pnlEmptyInspector.Visible = false;
                _pnlActiveInspector.Visible = true;

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
            ShowEmptyInspector();
        }
    }

    private void ShowEmptyInspector()
    {
        _selectedAction = null;
        _pnlActiveInspector.Visible = false;
        _pnlEmptyInspector.Visible = true;
    }

    private void UpdateContextualParameters(WorkflowAction action)
    {
        _pnlTypeControls.Visible = false;
        _pnlKeyControls.Visible = false;
        _pnlClickControls.Visible = false;
        _pnlWaitControls.Visible = false;

        // Hide target process & window card when action is purely a delay
        _pnlTargetCard.Visible = action.Type != ActionTypes.Wait;

        if (action.Type == ActionTypes.TypeText)
        {
            _lblContextCardHeader.Text = "2. Text Input Payload";
            _pnlTypeControls.Visible = true;
            _txtTextParam.Text = action.Parameters.TryGetValue("text", out var t) ? t?.ToString() ?? string.Empty : string.Empty;
        }
        else if (action.Type == ActionTypes.KeyPress)
        {
            _lblContextCardHeader.Text = "2. Keyboard Key Selection";
            _pnlKeyControls.Visible = true;
            string key = action.Parameters.TryGetValue("key", out var k) ? k?.ToString() ?? "Return" : "Return";
            if (_cmbKeyParam.Items.Contains(key))
            {
                _cmbKeyParam.SelectedItem = key;
            }
            else
            {
                _cmbKeyParam.SelectedItem = "Custom";
            }
            _txtCustomKey.Text = key;
        }
        else if (action.Type == ActionTypes.Click)
        {
            _lblContextCardHeader.Text = "2. Mouse Click Configuration";
            _pnlClickControls.Visible = true;
            string btn = action.Parameters.TryGetValue("button", out var b) ? b?.ToString() ?? "Left" : "Left";
            _cmbClickButton.SelectedItem = btn;
            _numClickX.Value = action.Parameters.TryGetValue("x", out var x) && int.TryParse(x?.ToString(), out int xv) ? Math.Clamp(xv, 0, 9999) : 0;
            _numClickY.Value = action.Parameters.TryGetValue("y", out var y) && int.TryParse(y?.ToString(), out int yv) ? Math.Clamp(yv, 0, 9999) : 0;
        }
        else if (action.Type == ActionTypes.Wait)
        {
            _lblContextCardHeader.Text = "2. Delay Duration";
            _pnlWaitControls.Visible = true;
            _numWaitDuration.Value = Math.Clamp(action.DelayMs, 0, 60000);
        }
        else
        {
            _lblContextCardHeader.Text = $"2. Action Parameters ({action.Type})";
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

    private void AddNewAction(string type)
    {
        var action = new WorkflowAction
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8),
            Type = type,
            Description = type switch
            {
                ActionTypes.TypeText => "Type text into target",
                ActionTypes.Click => "Click target element",
                ActionTypes.KeyPress => "Send keystroke",
                ActionTypes.Wait => "Wait pause",
                _ => "Perform " + type
            },
            DelayMs = type == ActionTypes.Wait ? 500 : 100,
            TimeoutMs = 5000,
            Enabled = true
        };

        if (type == ActionTypes.TypeText)
        {
            action.Parameters["text"] = "Sample Text";
        }
        else if (type == ActionTypes.Click)
        {
            action.Parameters["button"] = "Left";
        }
        else if (type == ActionTypes.KeyPress)
        {
            action.Parameters["key"] = "Return";
        }
        else if (type == ActionTypes.Wait)
        {
            action.Parameters["durationMs"] = 500;
        }

        _workflow.Actions.Add(action);
        _hasUnsavedChanges = true;
        PopulateActionsList();

        int lastIdx = _listActions.Items.Count - 1;
        if (lastIdx >= 0)
        {
            _listActions.Items[lastIdx].Selected = true;
            _listActions.EnsureVisible(lastIdx);
        }
        ValidateWorkflow();
    }

    private void DeleteSelectedAction()
    {
        if (_selectedAction is null || _listActions.SelectedIndices.Count == 0) return;
        int idx = _listActions.SelectedIndices[0];
        _workflow.Actions.RemoveAt(idx);
        _hasUnsavedChanges = true;
        PopulateActionsList();
        if (_listActions.Items.Count > 0)
        {
            int nextIdx = Math.Min(idx, _listActions.Items.Count - 1);
            _listActions.Items[nextIdx].Selected = true;
        }
        else
        {
            ShowEmptyInspector();
        }
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
        _listActions.EnsureVisible(newIdx);
    }

    private void ToggleActionEnable()
    {
        if (_selectedAction is null) return;
        _selectedAction.Enabled = !_selectedAction.Enabled;
        _hasUnsavedChanges = true;
        _btnToggleEnable.Text = _selectedAction.Enabled ? "Disable" : "Enable";
        UpdateListRow();
        ValidateWorkflow();
    }

    private void UpdateListRow()
    {
        if (_listActions.SelectedItems.Count > 0 && _selectedAction is not null)
        {
            var item = _listActions.SelectedItems[0];
            item.SubItems[1].Text = GetActionDisplayType(_selectedAction.Type);
            item.SubItems[2].Text = GetActionDisplayDetail(_selectedAction);
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

            _pnlValidationCard.BackColor = UITheme.Current.SuccessSurface;
            _lblValidationText.Text = "✓ All safety and schema validation checks passed.\r\nWorkflow conforms to strict declarative execution policy.";
            _lblValidationText.ForeColor = UITheme.Current.SuccessText;
        }
        else
        {
            _lblValidationBadge.Text = "⚠️ Validation Issues";
            _lblValidationBadge.BackColor = UITheme.Current.WarningSurface;
            _lblValidationBadge.ForeColor = UITheme.Current.Warning;

            _pnlValidationCard.BackColor = UITheme.Current.ErrorSurface;
            _lblValidationText.Text = "✕ VALIDATION ERRORS:\r\n• " + string.Join("\r\n• ", result.Errors);
            _lblValidationText.ForeColor = UITheme.Current.Error;
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
