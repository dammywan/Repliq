using System.Diagnostics;
using Repliq.App.Views;
using Repliq.Core.Models;
using Repliq.Core.Playback;
using Repliq.Core.Policies;
using Repliq.Core.Recording;
using Repliq.Core.Testing;
using Repliq.Storage.History;
using Repliq.Storage.Secrets;
using Repliq.Storage.Settings;
using Repliq.Storage.Workflows;
using Repliq.Windows.Hotkeys;
using Repliq.Windows.Input;

namespace Repliq.App;

public sealed class MainForm : Form
{
    // Services
    private readonly IWorkflowRepository _workflowRepo;
    private readonly IHistoryRepository _historyRepo;
    private readonly ISecretStore _secretStore;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IPolicyEngine _policyEngine;
    private readonly IAutomationBackend _automationBackend;
    private readonly IRecorderBackend _recorderBackend;
    private readonly IExecutionController _executionController;
    private readonly IRecordingController _recordingController;
    private readonly IHotkeyManager _hotkeyManager;

    // Windows
    private QuickPanelForm? _quickPanel;
    private NotifyIcon? _trayIcon;

    // UI Structure
    private readonly Panel _pnlSidebar = new();
    private readonly Panel _pnlContentHost = new();
    private readonly Panel _pnlHeaderBar = new();
    private readonly Label _lblAppTitle = new();
    private readonly Label _lblGlobalStatus = new();
    private readonly Button _btnHeaderEmergencyStop = new();
    private readonly Button _btnToggleQuickPanel = new();

    // Views
    private HomeView? _homeView;
    private WorkflowsView? _workflowsView;
    private RecorderView? _recorderView;
    private WorkflowEditorView? _editorView;
    private DryRunPermissionsView? _dryRunView;
    private RunMonitorView? _monitorView;
    private RunHistoryView? _historyView;
    private SecurityView? _securityView;
    private SettingsView? _settingsView;

    private readonly Dictionary<string, Button> _navButtons = new();
    private string _currentViewName = "Home";

    public MainForm()
    {
        // 1. Initialize services
        _workflowRepo = new WorkflowRepository();
        _historyRepo = new SqliteHistoryRepository();
        _secretStore = new DpapiSecretStore();
        _settingsRepo = new SettingsRepository();
        _policyEngine = new DefaultPolicyEngine();

        // Use native Win32 input backend for app execution
        _automationBackend = new Win32InputBackend();
        _recorderBackend = new MockRecorderBackend(); // Safe mock capture by default

        _executionController = new ExecutionController(_automationBackend, _policyEngine);
        _recordingController = new RecordingController(_recorderBackend);
        _hotkeyManager = new GlobalHotkeyManager();

        InitializeUI();
        InitializeViews();
        InitializeTray();
        RegisterHotkeys();

        _executionController.StateChanged += OnGlobalStateChanged;
        _recordingController.StateChanged += OnRecordingStateChanged;

        Load += async (_, _) =>
        {
            await _historyRepo.InitializeAsync();
            var settings = await _settingsRepo.LoadAsync();
            UITheme.SetTheme(settings.Theme);
            UITheme.Apply(this);

            if (settings.OpenQuickPanelOnStartup)
            {
                ShowQuickPanel();
            }

            NavigateTo("Home");
        };
    }

    private void InitializeUI()
    {
        Text = "Repliq — Desktop Workflow Automation";
        Size = new Size(1150, 750);
        MinimumSize = new Size(960, 640);
        StartPosition = FormStartPosition.CenterScreen;

        // Header Bar
        _pnlHeaderBar.Dock = DockStyle.Top;
        _pnlHeaderBar.Height = 56;
        _pnlHeaderBar.Tag = "surface";
        _pnlHeaderBar.Padding = new Padding(16, 10, 16, 10);

        var pnlHeaderLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1
        };
        pnlHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pnlHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pnlHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pnlHeaderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _lblAppTitle.Text = "REPLIQ";
        _lblAppTitle.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _lblAppTitle.AutoSize = true;
        _lblAppTitle.Margin = new Padding(0, 4, 20, 0);
        pnlHeaderLayout.Controls.Add(_lblAppTitle, 0, 0);

        _lblGlobalStatus.Text = "[● Ready]";
        _lblGlobalStatus.Font = UITheme.FontBodyBold;
        _lblGlobalStatus.AutoSize = true;
        _lblGlobalStatus.Margin = new Padding(0, 6, 0, 0);
        pnlHeaderLayout.Controls.Add(_lblGlobalStatus, 1, 0);

        _btnToggleQuickPanel.Text = "⚡ Quick Panel";
        _btnToggleQuickPanel.Size = new Size(120, 34);
        _btnToggleQuickPanel.Click += (_, _) => ToggleQuickPanel();
        pnlHeaderLayout.Controls.Add(_btnToggleQuickPanel, 2, 0);

        _btnHeaderEmergencyStop.Text = "🛑 Emergency Stop (Ctrl+Shift+S)";
        _btnHeaderEmergencyStop.Tag = "danger";
        _btnHeaderEmergencyStop.Size = new Size(230, 34);
        _btnHeaderEmergencyStop.Click += async (_, _) => await _executionController.EmergencyStopAsync();
        pnlHeaderLayout.Controls.Add(_btnHeaderEmergencyStop, 3, 0);

        _pnlHeaderBar.Controls.Add(pnlHeaderLayout);
        Controls.Add(_pnlHeaderBar);

        // Sidebar Navigation
        _pnlSidebar.Dock = DockStyle.Left;
        _pnlSidebar.Width = 200;
        _pnlSidebar.Tag = "surface";
        _pnlSidebar.Padding = new Padding(8, 12, 8, 12);

        var navList = new (string key, string label)[]
        {
            ("Home", "🏠 Home"),
            ("Workflows", "📂 Workflows"),
            ("Recorder", "● Recorder"),
            ("Editor", "✏️ Editor"),
            ("RunMonitor", "📊 Run Monitor"),
            ("History", "📜 Run History"),
            ("Security", "🛡️ Security & Policy"),
            ("Settings", "⚙️ Settings")
        };

        var pnlNavFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };

        foreach (var (key, label) in navList)
        {
            var btn = new Button
            {
                Text = label,
                TextAlign = ContentAlignment.MiddleLeft,
                Size = new Size(184, 40),
                Margin = new Padding(0, 0, 0, 6),
                Font = UITheme.FontBody
            };
            btn.Click += (_, _) => NavigateTo(key);
            _navButtons[key] = btn;
            pnlNavFlow.Controls.Add(btn);
        }

        _pnlSidebar.Controls.Add(pnlNavFlow);
        Controls.Add(_pnlSidebar);

        // Content Host
        _pnlContentHost.Dock = DockStyle.Fill;
        Controls.Add(_pnlContentHost);
    }

    private void InitializeViews()
    {
        _homeView = new HomeView(_workflowRepo, _historyRepo, view => NavigateTo(view));
        _workflowsView = new WorkflowsView(_workflowRepo, (action, wf) =>
        {
            if (action == "Run")
            {
                NavigateTo("RunMonitor");
                _ = _monitorView?.StartExecutionAsync(wf, isLive: true);
            }
            else if (action == "DryRun")
            {
                NavigateTo("DryRun");
                _dryRunView?.LoadWorkflow(wf);
            }
            else if (action == "Editor")
            {
                NavigateTo("Editor");
                _editorView?.LoadWorkflow(wf);
            }
        });

        _recorderView = new RecorderView(_recordingController, _workflowRepo, view => NavigateTo(view));
        _editorView = new WorkflowEditorView(_workflowRepo, view => NavigateTo(view));
        _dryRunView = new DryRunPermissionsView(_policyEngine, (target, wf) =>
        {
            if (target == "RunLive")
            {
                NavigateTo("RunMonitor");
                _ = _monitorView?.StartExecutionAsync(wf, isLive: true);
            }
            else if (target == "Editor")
            {
                NavigateTo("Editor");
                _editorView?.LoadWorkflow(wf);
            }
        });

        _monitorView = new RunMonitorView(_executionController, _historyRepo);
        _historyView = new RunHistoryView(_historyRepo);
        _securityView = new SecurityView(_policyEngine, _secretStore);
        _settingsView = new SettingsView(_settingsRepo, settings =>
        {
            UITheme.SetTheme(settings.Theme);
            UITheme.Apply(this);
            if (_quickPanel is not null) UITheme.Apply(_quickPanel);
            RegisterHotkeys();
        });
    }

    private void InitializeTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Repliq Main Window", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Open Quick Panel", null, (_, _) => ShowQuickPanel());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("🛑 Stop Active Run", null, async (_, _) => await _executionController.EmergencyStopAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Repliq", null, (_, _) => ConfirmExit());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Repliq - Desktop Automation",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void RegisterHotkeys()
    {
        _hotkeyManager.UnregisterAll();

        // Emergency Stop
        _hotkeyManager.RegisterShortcut("EmergencyStop", "Ctrl+Shift+S", async () =>
        {
            await _executionController.EmergencyStopAsync();
        });

        // Record
        _hotkeyManager.RegisterShortcut("Record", "Ctrl+Shift+R", async () =>
        {
            if (_recordingController.State == RecordingState.Recording)
                await _recordingController.StopAsync();
            else
                await _recordingController.StartAsync();
        });

        // Pause/Resume
        _hotkeyManager.RegisterShortcut("PauseResume", "Ctrl+Shift+Space", async () =>
        {
            if (_executionController.CurrentState == ExecutionState.Running)
                await _executionController.PauseAsync();
            else if (_executionController.CurrentState == ExecutionState.Paused)
                await _executionController.ResumeAsync();
        });
    }

    public void NavigateTo(string viewName)
    {
        _currentViewName = viewName;

        // Highlight active nav button
        foreach (var kvp in _navButtons)
        {
            if (string.Equals(kvp.Key, viewName, StringComparison.OrdinalIgnoreCase))
            {
                kvp.Value.Tag = "primary";
                kvp.Value.BackColor = UITheme.Current.Accent;
                kvp.Value.ForeColor = UITheme.Current.IsDark ? Color.Black : Color.White;
            }
            else
            {
                kvp.Value.Tag = null;
                kvp.Value.BackColor = UITheme.Current.MainSurface;
                kvp.Value.ForeColor = UITheme.Current.PrimaryText;
            }
        }

        _pnlContentHost.Controls.Clear();

        UserControl? targetView = viewName.ToLowerInvariant() switch
        {
            "home" => _homeView,
            "workflows" => _workflowsView,
            "recorder" => _recorderView,
            "editor" => _editorView,
            "dryrun" => _dryRunView,
            "runmonitor" => _monitorView,
            "history" => _historyView,
            "security" => _securityView,
            "settings" => _settingsView,
            _ => _homeView
        };

        if (targetView is not null)
        {
            targetView.Dock = DockStyle.Fill;
            _pnlContentHost.Controls.Add(targetView);
            UITheme.Apply(targetView);

            if (targetView == _homeView) _ = _homeView.RefreshDataAsync();
            if (targetView == _workflowsView) _ = _workflowsView.RefreshDataAsync();
            if (targetView == _historyView) _ = _historyView.RefreshDataAsync();
        }
    }

    private void ToggleQuickPanel()
    {
        _quickPanel ??= new QuickPanelForm(_workflowRepo, _executionController, _recordingController, ShowMainWindow);
        if (_quickPanel.Visible)
            _quickPanel.Hide();
        else
            _quickPanel.Show();
    }

    private void ShowQuickPanel()
    {
        _quickPanel ??= new QuickPanelForm(_workflowRepo, _executionController, _recordingController, ShowMainWindow);
        _quickPanel.Show();
        _quickPanel.BringToFront();
    }

    private void ShowMainWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        BringToFront();
    }

    private void OnGlobalStateChanged(ExecutionState state)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnGlobalStateChanged(state)));
            return;
        }

        _lblGlobalStatus.Text = $"[● {state.ToString().ToUpperInvariant()}]";
        if (_trayIcon is not null)
            _trayIcon.Text = $"Repliq: {state}";
    }

    private void OnRecordingStateChanged(RecordingState state)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnRecordingStateChanged(state)));
            return;
        }

        if (state == RecordingState.Recording)
        {
            _lblGlobalStatus.Text = "[● RECORDING ACTIVE]";
            _lblGlobalStatus.ForeColor = Color.Red;
        }
        else
        {
            _lblGlobalStatus.Text = "[● IDLE]";
            _lblGlobalStatus.ForeColor = UITheme.Current.PrimaryText;
        }
    }

    private void ConfirmExit()
    {
        if (_executionController.CurrentState is ExecutionState.Running or ExecutionState.Paused ||
            _recordingController.State == RecordingState.Recording)
        {
            var res = MessageBox.Show(
                "A workflow or recording is currently active.\nDo you want to stop active automation and exit Repliq?",
                "Active Automation In Progress",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (res != DialogResult.Yes) return;
        }

        _trayIcon?.Dispose();
        _hotkeyManager.Dispose();
        Application.Exit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            // If Quick Panel is open, we can minimize to tray instead of exiting immediately
            if (_quickPanel is not null && _quickPanel.Visible)
            {
                e.Cancel = true;
                Hide();
                return;
            }
        }
        ConfirmExit();
        base.OnFormClosing(e);
    }
}
