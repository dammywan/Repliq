using System.Text.Json;

namespace Repliq.Storage.Settings;

public sealed class AppSettings
{
    public string Theme { get; set; } = "Dark"; // "Light", "Dark", "System"
    public string EmergencyStopShortcut { get; set; } = "Ctrl+Shift+S";
    public string RecordShortcut { get; set; } = "Ctrl+Shift+R";
    public string PlayShortcut { get; set; } = "Ctrl+Shift+P";
    public string PauseResumeShortcut { get; set; } = "Ctrl+Shift+Space";
    public bool OpenQuickPanelOnStartup { get; set; } = false;
    public int LogRetentionDays { get; set; } = 30;
    public bool ExcludeSensitiveFields { get; set; } = true;
    public string? CustomWorkflowsDirectory { get; set; }
}

public interface ISettingsRepository
{
    Task<AppSettings> LoadAsync();
    Task SaveAsync(AppSettings settings);
}

public sealed class SettingsRepository : ISettingsRepository
{
    private readonly string _settingsFilePath;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public SettingsRepository(string? customPath = null)
    {
        _settingsFilePath = customPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Repliq",
            "settings.json");

        var dir = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public async Task<AppSettings> LoadAsync()
    {
        if (!File.Exists(_settingsFilePath))
            return new AppSettings();

        try
        {
            var json = await File.ReadAllTextAsync(_settingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, _options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, _options);
        var temp = _settingsFilePath + ".tmp";
        await File.WriteAllTextAsync(temp, json);
        File.Move(temp, _settingsFilePath, overwrite: true);
    }
}
