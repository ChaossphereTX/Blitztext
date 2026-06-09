using System.Text.Json;
using Blitztext.Core.Models;

namespace Blitztext.Core.Settings;

/// <summary>
/// Loads and persists the settings container as JSON. Mirrors the macOS AppState
/// persistence (JSONEncoder/Decoder to settings.json), but lives on Windows under %APPDATA%.
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options =
        SettingsJsonContext.Default.Options;

    public static SettingsContainer Load()
    {
        try
        {
            string path = AppPaths.SettingsFile;
            if (!File.Exists(path))
            {
                return new SettingsContainer();
            }

            string json = File.ReadAllText(path);
            var container = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.SettingsContainer);
            return container ?? new SettingsContainer();
        }
        catch
        {
            // Corrupt or unreadable settings should never crash the app; fall back to defaults.
            return new SettingsContainer();
        }
    }

    public static void Save(SettingsContainer container)
    {
        try
        {
            AppPaths.EnsureAppSupportDirectoryExists();
            string json = JsonSerializer.Serialize(container, SettingsJsonContext.Default.SettingsContainer);
            File.WriteAllText(AppPaths.SettingsFile, json);
        }
        catch
        {
            // Best-effort persistence; ignore transient IO failures.
        }
    }
}
