namespace Blitztext.Core.Settings;

/// <summary>
/// Windows equivalent of the macOS AppSupportPaths. Everything lives under
/// %APPDATA%\Blitztext (e.g. C:\Users\&lt;user&gt;\AppData\Roaming\Blitztext).
/// </summary>
public static class AppPaths
{
    public static string AppSupportDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Blitztext");

    public static string SettingsFile => Path.Combine(AppSupportDirectory, "settings.json");

    public static string LocalModelsDirectory => Path.Combine(AppSupportDirectory, "models");

    /// <summary>Directory holding the downloaded GGML whisper.cpp models.</summary>
    public static string WhisperModelsDirectory => Path.Combine(LocalModelsDirectory, "whisper");

    /// <summary>Temp directory for in-flight recordings.</summary>
    public static string RecordingsDirectory => Path.Combine(Path.GetTempPath(), "Blitztext");

    public static void EnsureAppSupportDirectoryExists() => Directory.CreateDirectory(AppSupportDirectory);

    public static void EnsureWhisperModelsDirectoryExists() => Directory.CreateDirectory(WhisperModelsDirectory);

    public static void EnsureRecordingsDirectoryExists() => Directory.CreateDirectory(RecordingsDirectory);
}
