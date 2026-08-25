using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceMeeterOutputAutoSwitcher.Application.Abstractions;
using VoiceMeeterOutputAutoSwitcher.Application.Settings;
using VoiceMeeterOutputAutoSwitcher.Core.Routing;

namespace VoiceMeeterOutputAutoSwitcher.Infrastructure.Settings;

public sealed class JsonAppSettingsRepository : IAppSettingsRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _filePath;
    private readonly object _gate = new();

    public JsonAppSettingsRepository(string? filePath = null)
    {
        _filePath = filePath
                    ?? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "VoicemeeterOutputAutoSwitcher",
                        "settings.json");
    }

    public string FilePath => _filePath;

    public AppSettings Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                var created = new AppSettings();
                SaveUnlocked(created);
                return Clone(created);
            }

            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            settings.ManagedDevices ??= [];
            if (settings.DebounceMilliseconds <= 0)
            {
                settings.DebounceMilliseconds = 500;
            }

            return Clone(settings);
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            SaveUnlocked(settings);
        }
    }

    private void SaveUnlocked(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = Clone(settings);
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        File.WriteAllText(_filePath, json);
    }

    private static AppSettings Clone(AppSettings settings) =>
        new()
        {
            DebounceMilliseconds = settings.DebounceMilliseconds,
            StartWithWindows = settings.StartWithWindows,
            ManagedDevices = settings.ManagedDevices
                .Select(d => new ManagedDevice(d.DeviceId, d.Name, d.Enabled, d.Priority))
                .ToList(),
        };
}
