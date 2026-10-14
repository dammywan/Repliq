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
using Repliq.Windows.Recording;

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
    private readonly Panel _pnlMainWrapper = new();
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
    private AboutView? _aboutView;

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
        _recorderBackend = new Win32RecorderBackend();

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
            RegisterHotkeys(settings);

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
        if (UITheme.AppIcon != null) Icon = UITheme.AppIcon;
        var screen = Screen.PrimaryScreen;
        var workingArea = screen?.WorkingArea ?? new Rectangle(0, 0, 1024, 720);
        int targetWidth = Math.Min(1180, Math.Max(780, (int)(workingArea.Width * 0.94)));
        int targetHeight = Math.Min(740, Math.Max(520, (int)(workingArea.Height * 0.90)));
        Size = new Size(targetWidth, targetHeight);
        MinimumSize = new Size(Math.Min(760, workingArea.Width), Math.Min(480, workingArea.Height));
        StartPosition = FormStartPosition.Manual;
        Location = new Point(
            workingArea.Left + Math.Max(0, (workingArea.Width - targetWidth) / 2),
            workingArea.Top + Math.Max(0, (workingArea.Height - targetHeight) / 2)
        );
        AutoScaleMode = AutoScaleMode.Dpi;

        // Header Bar (top of content area)
        _pnlHeaderBar.Dock = DockStyle.Top;
        _pnlHeaderBar.Height = 52;
        _pnlHeaderBar.Tag = "surface";
        _pnlHeaderBar.Padding = new Padding(16, 8, 16, 8);

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

        _lblAppTitle.Text = "Home";
        _lblAppTitle.Font = UITheme.FontSubheader;
        _lblAppTitle.ForeColor = UITheme.Current.PrimaryText;
        _lblAppTitle.AutoSize = true;
        _lblAppTitle.Margin = new Padding(0, 8, 14, 0);
        pnlHeaderLayout.Controls.Add(_lblAppTitle, 0, 0);

        _lblGlobalStatus.Text = "  ● Ready  ";
        _lblGlobalStatus.Font = UITheme.FontBodyBold;
        _lblGlobalStatus.BackColor = UITheme.Current.SuccessSurface;
        _lblGlobalStatus.ForeColor = UITheme.Current.SuccessText;
        _lblGlobalStatus.AutoSize = true;
        _lblGlobalStatus.Padding = new Padding(8, 4, 8, 4);
        _lblGlobalStatus.Margin = new Padding(0, 5, 0, 0);
        pnlHeaderLayout.Controls.Add(_lblGlobalStatus, 1, 0);

        _btnToggleQuickPanel.Text = "⚡ Quick Panel";
        _btnToggleQuickPanel.Size = new Size(120, 34);
        _btnToggleQuickPanel.FlatStyle = FlatStyle.Flat;
        _btnToggleQuickPanel.FlatAppearance.BorderSize = 1;
        _btnToggleQuickPanel.FlatAppearance.BorderColor = UITheme.Current.CardBorder;
        _btnToggleQuickPanel.BackColor = UITheme.Current.CardSurface;
        _btnToggleQuickPanel.ForeColor = UITheme.Current.PrimaryText;
        _btnToggleQuickPanel.Font = UITheme.FontSmall;
        _btnToggleQuickPanel.Margin = new Padding(0, 0, 10, 0);
        _btnToggleQuickPanel.Click += (_, _) => ToggleQuickPanel();
        pnlHeaderLayout.Controls.Add(_btnToggleQuickPanel, 2, 0);

        _btnHeaderEmergencyStop.Text = "🛑 Emergency Stop (Ctrl+Shift+S)";
        _btnHeaderEmergencyStop.Tag = "danger";
        _btnHeaderEmergencyStop.Size = new Size(230, 34);
        _btnHeaderEmergencyStop.FlatStyle = FlatStyle.Flat;
        _btnHeaderEmergencyStop.BackColor = UITheme.Current.Error;
        _btnHeaderEmergencyStop.ForeColor = Color.White;
        _btnHeaderEmergencyStop.Font = UITheme.FontSmall;
        _btnHeaderEmergencyStop.Click += async (_, _) => await _executionController.EmergencyStopAsync();
        pnlHeaderLayout.Controls.Add(_btnHeaderEmergencyStop, 3, 0);

        _pnlHeaderBar.Controls.Add(pnlHeaderLayout);

        // Sidebar Navigation
        _pnlSidebar.Dock = DockStyle.Left;
        _pnlSidebar.Width = 220;
        _pnlSidebar.Tag = "surface";
        _pnlSidebar.Padding = new Padding(12, 14, 12, 14);

        var pnlNavFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        // Brand at top of sidebar
        var pnlBrand = new TableLayoutPanel
        {
            Width = 196,
            Height = 44,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 14)
        };
        pnlBrand.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pnlBrand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        Control brandControl;
        if (UITheme.AppLogo != null)
        {
            brandControl = new PictureBox
            {
                Image = UITheme.AppLogo,
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(32, 32),
                Margin = new Padding(0, 2, 8, 0)
            };
        }
        else
        {
            brandControl = new Label
            {
                Text = "🔁",
                Font = new Font("Segoe UI", 12F, FontStyle.Regular),
                Size = new Size(32, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 2, 8, 0)
            };
        }
        pnlBrand.Controls.Add(brandControl, 0, 0);

        var pnlBrandTitles = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Margin = new Padding(0)
        };
        var lblBrandHeader = new Label
        {
            Text = "Repliq",
            Font = UITheme.FontSubheader,
            ForeColor = UITheme.Current.PrimaryText,
            AutoSize = true
        };
        var lblBrandSub = new Label
        {
            Text = "Workflow Automation",
            Font = UITheme.FontMicro,
            ForeColor = UITheme.Current.SecondaryText,
            AutoSize = true
        };
        pnlBrandTitles.Controls.AddRange(new Control[] { lblBrandHeader, lblBrandSub });
        pnlBrand.Controls.Add(pnlBrandTitles, 1, 0);
        pnlNavFlow.Controls.Add(pnlBrand);

        // WORKSPACE Section
        var lblWorkspaceCat = new Label
        {
            Text = "WORKSPACE",
            Font = UITheme.FontMicro,
            ForeColor = UITheme.Current.SecondaryText,
            AutoSize = true,
            Margin = new Padding(6, 4, 0, 6)
        };
        pnlNavFlow.Controls.Add(lblWorkspaceCat);

        var workspaceNav = new (string key, string label)[]
        {
            ("Home", "🏠  Home"),
            ("Workflows", "📂  Workflows"),
            ("History", "🕒  Run history"),
            ("RunMonitor", "📊  Run monitor")
        };

        foreach (var (key, label) in workspaceNav)
        {
            var btn = CreateNavButton(key, label);
            _navButtons[key] = btn;
            pnlNavFlow.Controls.Add(btn);
        }

        // SYSTEM Section
        var lblSystemCat = new Label
        {
            Text = "SYSTEM",
            Font = UITheme.FontMicro,
            ForeColor = UITheme.Current.SecondaryText,
            AutoSize = true,
            Margin = new Padding(6, 14, 0, 6)
        };
        pnlNavFlow.Controls.Add(lblSystemCat);

        var systemNav = new (string key, string label)[]
        {
            ("Recorder", "●  Recorder"),
            ("Editor", "✏️  Editor"),
            ("Security", "🛡️  Security"),
            ("Settings", "⚙️  Settings"),
            ("About", "ℹ️  About")
        };

        foreach (var (key, label) in systemNav)
        {
            var btn = CreateNavButton(key, label);
            _navButtons[key] = btn;
            pnlNavFlow.Controls.Add(btn);
        }

        _pnlSidebar.Controls.Add(pnlNavFlow);

        // Content Host and Right-Side Wrapper
        _pnlContentHost.Dock = DockStyle.Fill;
        _pnlMainWrapper.Dock = DockStyle.Fill;
        _pnlMainWrapper.Controls.Add(_pnlContentHost);
        _pnlMainWrapper.Controls.Add(_pnlHeaderBar);

        Controls.Add(_pnlMainWrapper);
        Controls.Add(_pnlSidebar);
    }

    private Button CreateNavButton(string key, string label)
    {
        var btn = new Button
        {
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            Size = new Size(196, 38),
            Margin = new Padding(0, 0, 0, 4),
            Font = UITheme.FontBody,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.Transparent,
            ForeColor = UITheme.Current.SecondaryText,
            Cursor = Cursors.Hand,
            Padding = new Padding(8, 0, 0, 0)
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.Click += (_, _) => NavigateTo(key);
        return btn;
    }

    private void InitializeViews()
    {
        _homeView = new HomeView(_workflowRepo, _historyRepo,
            view => NavigateTo(view),
            (action, wf) =>
            {
                if (action == "Run")
                {
                    NavigateTo("RunMonitor");
                    _ = _monitorView?.StartExecutionAsync(wf, isLive: true);
                }
            });

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
            RegisterHotkeys(settings);
        });
        _aboutView = new AboutView(() => ShowQuickPanel());
    }

    private void InitializeTray()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new DarkToolStripRenderer(),
            BackColor = UITheme.Current.MainSurface,
            ForeColor = UITheme.Current.PrimaryText
        };
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

    private void RegisterHotkeys(AppSettings? settings = null)
    {
        settings ??= _settingsRepo.LoadAsync().GetAwaiter().GetResult();
        _hotkeyManager.UnregisterAll();

        var failed = new List<string>();

        // Emergency Stop
        var stopKey = string.IsNullOrWhiteSpace(settings.EmergencyStopShortcut) ? "Ctrl+Shift+S" : settings.EmergencyStopShortcut;
        var r1 = _hotkeyManager.RegisterShortcut("EmergencyStop", stopKey, async () =>
        {
            await _executionController.EmergencyStopAsync();
        });
        if (!r1.Success) failed.Add($"Emergency Stop ({stopKey})");

        // Record
        var recKey = string.IsNullOrWhiteSpace(settings.RecordShortcut) ? "Ctrl+Shift+R" : settings.RecordShortcut;
        var r2 = _hotkeyManager.RegisterShortcut("Record", recKey, async () =>
        {
            if (_recordingController.State == RecordingState.Recording)
                await _recordingController.StopAsync();
            else
                await _recordingController.StartAsync();
        });
        if (!r2.Success) failed.Add($"Record ({recKey})");

        // Pause/Resume
        var pauseKey = string.IsNullOrWhiteSpace(settings.PauseResumeShortcut) ? "Ctrl+Shift+Space" : settings.PauseResumeShortcut;
        var r3 = _hotkeyManager.RegisterShortcut("PauseResume", pauseKey, async () =>
        {
            if (_executionController.CurrentState == ExecutionState.Running)
                await _executionController.PauseAsync();
            else if (_executionController.CurrentState == ExecutionState.Paused)
                await _executionController.ResumeAsync();
        });
        if (!r3.Success) failed.Add($"Pause/Resume ({pauseKey})");

        // Play Selected
        var playKey = string.IsNullOrWhiteSpace(settings.PlayShortcut) ? "Ctrl+Shift+P" : settings.PlayShortcut;
        var r4 = _hotkeyManager.RegisterShortcut("Play", playKey, () =>
        {
            if (_quickPanel is not null && _quickPanel.Visible && _quickPanel.SelectedWorkflow is not null)
            {
                NavigateTo("RunMonitor");
                _ = _monitorView?.StartExecutionAsync(_quickPanel.SelectedWorkflow, isLive: true);
            }
            else
            {
                ShowQuickPanel();
            }
        });
        if (!r4.Success) failed.Add($"Play ({playKey})");

        if (failed.Count > 0)
        {
            _lblGlobalStatus.Text = $"[Warning: Hotkey conflicts: {string.Join(", ", failed)}]";
        }
    }

    public void NavigateTo(string viewName)
    {
        _currentViewName = viewName;
        _lblAppTitle.Text = viewName.ToLowerInvariant() switch
        {
            "home" => "Home Dashboard",
            "workflows" => "Workflows Library",
            "recorder" => "Workflow Recorder",
            "editor" => "Workflow Editor",
            "dryrun" => "Dry Run / Permissions",
            "runmonitor" => "Run Monitor",
            "history" => "Run History & Diagnostics",
            "security" => "Security & Execution Policies",
            "settings" => "Settings & Preferences",
            _ => "Repliq"
        };

        // Highlight active nav button
        foreach (var kvp in _navButtons)
        {
            if (string.Equals(kvp.Key, viewName, StringComparison.OrdinalIgnoreCase))
            {
                kvp.Value.BackColor = UITheme.Current.SecondarySurface;
                kvp.Value.ForeColor = UITheme.Current.PrimaryText;
                kvp.Value.Font = UITheme.FontBodyBold;
                kvp.Value.FlatAppearance.BorderSize = 1;
                kvp.Value.FlatAppearance.BorderColor = UITheme.Current.CardBorder;
            }
            else
            {
                kvp.Value.BackColor = Color.Transparent;
                kvp.Value.ForeColor = UITheme.Current.SecondaryText;
                kvp.Value.Font = UITheme.FontBody;
                kvp.Value.FlatAppearance.BorderSize = 0;
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
            "about" => _aboutView,
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
        if (_quickPanel is null || _quickPanel.IsDisposed)
            _quickPanel = new QuickPanelForm(_workflowRepo, _executionController, _recordingController, _historyRepo, ShowMainWindow);

        if (_quickPanel.Visible)
            _quickPanel.Hide();
        else
            _quickPanel.Show();
    }

    private void ShowQuickPanel()
    {
        if (_quickPanel is null || _quickPanel.IsDisposed)
            _quickPanel = new QuickPanelForm(_workflowRepo, _executionController, _recordingController, _historyRepo, ShowMainWindow);

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

        switch (state)
        {
            case ExecutionState.Running:
                _lblGlobalStatus.Text = "  ● RUNNING  ";
                _lblGlobalStatus.BackColor = UITheme.Current.SuccessSurface;
                _lblGlobalStatus.ForeColor = UITheme.Current.SuccessText;
                break;
            case ExecutionState.Paused:
                _lblGlobalStatus.Text = "  ⏸ PAUSED  ";
                _lblGlobalStatus.BackColor = UITheme.Current.WarningSurface;
                _lblGlobalStatus.ForeColor = UITheme.Current.Warning;
                break;
            case ExecutionState.Completed:
                _lblGlobalStatus.Text = "  ✓ COMPLETED  ";
                _lblGlobalStatus.BackColor = UITheme.Current.SuccessSurface;
                _lblGlobalStatus.ForeColor = UITheme.Current.SuccessText;
                break;
            case ExecutionState.Failed:
                _lblGlobalStatus.Text = "  ✕ FAILED  ";
                _lblGlobalStatus.BackColor = UITheme.Current.ErrorSurface;
                _lblGlobalStatus.ForeColor = UITheme.Current.Error;
                break;
            case ExecutionState.Stopping:
                _lblGlobalStatus.Text = "  ⏹ STOPPING  ";
                _lblGlobalStatus.BackColor = UITheme.Current.WarningSurface;
                _lblGlobalStatus.ForeColor = UITheme.Current.Warning;
                break;
            default:
                _lblGlobalStatus.Text = "  ● READY  ";
                _lblGlobalStatus.BackColor = UITheme.Current.SuccessSurface;
                _lblGlobalStatus.ForeColor = UITheme.Current.SuccessText;
                break;
        }

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
            _lblGlobalStatus.Text = "  ● RECORDING  ";
            _lblGlobalStatus.BackColor = UITheme.Current.ErrorSurface;
            _lblGlobalStatus.ForeColor = UITheme.Current.Error;
        }
        else
        {
            _lblGlobalStatus.Text = "  ● READY  ";
            _lblGlobalStatus.BackColor = UITheme.Current.SuccessSurface;
            _lblGlobalStatus.ForeColor = UITheme.Current.SuccessText;
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
        if (_recorderBackend is IDisposable d) d.Dispose();
        if (_automationBackend is IDisposable ad) ad.Dispose();
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
