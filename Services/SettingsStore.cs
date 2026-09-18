using System.IO;
using System.Text.Json;
using CodexInputEnhancer.Models;

namespace CodexInputEnhancer.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string DataDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "data");
    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public AppSettings Load()
    {
        Directory.CreateDirectory(DataDirectory);
        if (!File.Exists(SettingsPath))
        {
            var created = new AppSettings();
            Save(created);
            return created;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                           ?? new AppSettings();
            var changed = false;
            if (string.IsNullOrWhiteSpace(settings.OptimizationPrompt))
            {
                settings.OptimizationPrompt = AppSettings.DefaultOptimizationPrompt;
                changed = true;
            }

            if (settings.TimeoutSeconds is < 5 or > 300)
            {
                settings.TimeoutSeconds = 60;
                changed = true;
            }

            if (changed) Save(settings);
            return settings;
        }
        catch
        {
            var fallback = new AppSettings();
            Save(fallback);
            return fallback;
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, SettingsPath, overwrite: true);
    }
}
