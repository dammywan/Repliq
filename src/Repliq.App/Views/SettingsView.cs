using Repliq.Storage.Settings;

namespace Repliq.App.Views;

public sealed class SettingsView : UserControl
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly Action<AppSettings> _onSettingsSaved;

    private readonly ComboBox _cmbTheme = new();
    private readonly TextBox _txtEmergencyShortcut = new();
    private readonly TextBox _txtRecordShortcut = new();
    private readonly TextBox _txtPlayShortcut = new();
    private readonly TextBox _txtPauseShortcut = new();
    private readonly NumericUpDown _numRetentionDays = new();
    private readonly CheckBox _chkExcludeSensitive = new();
    private readonly CheckBox _chkAutoOpenQuickPanel = new();
    private readonly TextBox _txtStoragePath = new();
    private readonly Button _btnSave = new();

    private AppSettings _settings = new();

    public SettingsView(ISettingsRepository settingsRepo, Action<AppSettings> onSettingsSaved)
    {
        _settingsRepo = settingsRepo;
        _onSettingsSaved = onSettingsSaved;

        InitializeComponents();
        LoadSettings();
    }

    private void InitializeComponents()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(24);
        AutoScroll = true;

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        var lblTitle = new Label { Text = "Settings & Preferences", Font = UITheme.FontHeader, AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
        layout.Controls.Add(lblTitle);

        // Theme Group
        var grpTheme = new GroupBox { Text = "Appearance", Width = 550, AutoSize = true, Font = UITheme.FontSubheader, Margin = new Padding(0, 0, 0, 16) };
        var pnlTheme = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(12) };
        var lblTheme = new Label { Text = "Application Theme:", AutoSize = true, Margin = new Padding(0, 4, 8, 0) };
        _cmbTheme.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTheme.Items.AddRange(new object[] { "Light", "Dark" });
        _cmbTheme.Width = 160;
        pnlTheme.Controls.AddRange(new Control[] { lblTheme, _cmbTheme });
        grpTheme.Controls.Add(pnlTheme);
        layout.Controls.Add(grpTheme);

        // Shortcuts Group
        var grpShortcuts = new GroupBox { Text = "Global Keyboard Shortcuts", Width = 550, AutoSize = true, Font = UITheme.FontSubheader, Margin = new Padding(0, 0, 0, 16) };
        var pnlShortcuts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Padding = new Padding(12) };
        pnlShortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        pnlShortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        pnlShortcuts.Controls.Add(new Label { Text = "🛑 Emergency Stop:", AutoSize = true }, 0, 0);
        _txtEmergencyShortcut.Width = 180;
        pnlShortcuts.Controls.Add(_txtEmergencyShortcut, 1, 0);

        pnlShortcuts.Controls.Add(new Label { Text = "● Start/Stop Recording:", AutoSize = true }, 0, 1);
        _txtRecordShortcut.Width = 180;
        pnlShortcuts.Controls.Add(_txtRecordShortcut, 1, 1);

        pnlShortcuts.Controls.Add(new Label { Text = "▶ Play Selected Workflow:", AutoSize = true }, 0, 2);
        _txtPlayShortcut.Width = 180;
        pnlShortcuts.Controls.Add(_txtPlayShortcut, 1, 2);

        pnlShortcuts.Controls.Add(new Label { Text = "⏸ Pause/Resume Playback:", AutoSize = true }, 0, 3);
        _txtPauseShortcut.Width = 180;
        pnlShortcuts.Controls.Add(_txtPauseShortcut, 1, 3);

        grpShortcuts.Controls.Add(pnlShortcuts);
        layout.Controls.Add(grpShortcuts);

        // Privacy & Storage Group
        var grpStorage = new GroupBox { Text = "Storage & Privacy", Width = 550, AutoSize = true, Font = UITheme.FontSubheader, Margin = new Padding(0, 0, 0, 16) };
        var pnlStorage = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(12) };

        _chkExcludeSensitive.Text = "Exclude sensitive fields and passwords from recording";
        _chkExcludeSensitive.AutoSize = true;
        _chkExcludeSensitive.Margin = new Padding(0, 0, 0, 8);

        _chkAutoOpenQuickPanel.Text = "Open Quick Panel floating window automatically at startup";
        _chkAutoOpenQuickPanel.AutoSize = true;
        _chkAutoOpenQuickPanel.Margin = new Padding(0, 0, 0, 8);

        var pnlRet = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
        pnlRet.Controls.Add(new Label { Text = "Run Log Retention Days:", AutoSize = true, Margin = new Padding(0, 4, 8, 0) });
        _numRetentionDays.Minimum = 1;
        _numRetentionDays.Maximum = 365;
        _numRetentionDays.Width = 80;
        pnlRet.Controls.Add(_numRetentionDays);

        var pnlPath = new FlowLayoutPanel { AutoSize = true };
        pnlPath.Controls.Add(new Label { Text = "AppData Directory:", AutoSize = true, Margin = new Padding(0, 4, 8, 0) });
        _txtStoragePath.Width = 360;
        _txtStoragePath.ReadOnly = true;
        pnlPath.Controls.Add(_txtStoragePath);

        pnlStorage.Controls.AddRange(new Control[] { _chkExcludeSensitive, _chkAutoOpenQuickPanel, pnlRet, pnlPath });
        grpStorage.Controls.Add(pnlStorage);
        layout.Controls.Add(grpStorage);

        // Save Button
        _btnSave.Text = "💾 Save Settings";
        _btnSave.Tag = "primary";
        _btnSave.Size = new Size(160, 38);
        _btnSave.Click += async (_, _) => await SaveSettingsAsync();
        layout.Controls.Add(_btnSave);

        Controls.Add(layout);
    }

    private async void LoadSettings()
    {
        _settings = await _settingsRepo.LoadAsync();
        _cmbTheme.SelectedItem = _settings.Theme;
        _txtEmergencyShortcut.Text = _settings.EmergencyStopShortcut;
        _txtRecordShortcut.Text = _settings.RecordShortcut;
        _txtPlayShortcut.Text = _settings.PlayShortcut;
        _txtPauseShortcut.Text = _settings.PauseResumeShortcut;
        _numRetentionDays.Value = Math.Clamp(_settings.LogRetentionDays, 1, 365);
        _chkExcludeSensitive.Checked = _settings.ExcludeSensitiveFields;
        _chkAutoOpenQuickPanel.Checked = _settings.OpenQuickPanelOnStartup;
        _txtStoragePath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Repliq");
    }

    private async Task SaveSettingsAsync()
    {
        _settings.Theme = _cmbTheme.SelectedItem?.ToString() ?? "Light";
        _settings.EmergencyStopShortcut = _txtEmergencyShortcut.Text.Trim();
        _settings.RecordShortcut = _txtRecordShortcut.Text.Trim();
        _settings.PlayShortcut = _txtPlayShortcut.Text.Trim();
        _settings.PauseResumeShortcut = _txtPauseShortcut.Text.Trim();
        _settings.LogRetentionDays = (int)_numRetentionDays.Value;
        _settings.ExcludeSensitiveFields = _chkExcludeSensitive.Checked;
        _settings.OpenQuickPanelOnStartup = _chkAutoOpenQuickPanel.Checked;

        await _settingsRepo.SaveAsync(_settings);
        _onSettingsSaved(_settings);

        MessageBox.Show("Settings saved successfully.", "Settings Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
