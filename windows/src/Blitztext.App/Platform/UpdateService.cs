using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blitztext.App.Platform;

/// <summary>An available update described by the server manifest (latest.json).</summary>
public sealed class UpdateInfo
{
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
    [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
    [JsonPropertyName("notes")] public string? Notes { get; set; }
}

/// <summary>
/// Self-hosted in-app updater. On startup the app fetches a manifest (latest.json) from the
/// company server; if it advertises a newer version, the matching installer is downloaded and
/// run silently, then the app relaunches. No per-machine manual reinstall needed — publishing
/// an update is just "drop the new BlitztextSetup.exe + latest.json on the server".
///
/// latest.json format:
///   { "version": "1.6.0", "url": "https://server/blitztext/BlitztextSetup.exe", "notes": "..." }
/// </summary>
public static class UpdateService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>Resolve the feed URL: env override → settings → empty (disabled).</summary>
    public static string ResolveFeedUrl(string? settingsUrl)
    {
        string env = Environment.GetEnvironmentVariable("BLITZTEXT_UPDATE_URL") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        return (settingsUrl ?? string.Empty).Trim();
    }

    /// <summary>Returns update info if the server advertises a newer version, else null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(string feedUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            return null;
        }

        try
        {
            string json = await Http.GetStringAsync(feedUrl, ct).ConfigureAwait(false);
            var info = JsonSerializer.Deserialize<UpdateInfo>(json);
            if (info == null || string.IsNullOrWhiteSpace(info.Version) || string.IsNullOrWhiteSpace(info.Url))
            {
                return null;
            }

            if (Version.TryParse(NormalizeVersion(info.Version), out Version? remote) && remote > CurrentVersion)
            {
                return info;
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Write($"update: check failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Download the installer to a temp file and return its path.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo info, CancellationToken ct = default)
    {
        string dir = Path.Combine(Path.GetTempPath(), "Blitztext-Update");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"BlitztextSetup-{info.Version}.exe");

        using (var resp = await Http.GetAsync(info.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await src.CopyToAsync(dst, ct).ConfigureAwait(false);
        }

        Log.Write($"update: downloaded {info.Version} -> {path}");
        return path;
    }

    /// <summary>
    /// Launch the downloaded installer silently and exit so it can replace the running files.
    /// The installer relaunches the app afterwards (via the /RELAUNCH switch).
    /// </summary>
    public static void ApplyAndExit(string setupPath)
    {
        Log.Write($"update: applying {setupPath}");
        Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH",
            UseShellExecute = false,
        });

        System.Windows.Application.Current.Shutdown();
    }

    private static string NormalizeVersion(string v)
    {
        // Accept "1.6" or "1.6.0" or "1.6.0.0".
        v = v.Trim();
        int parts = v.Split('.').Length;
        return parts switch
        {
            1 => v + ".0.0.0",
            2 => v + ".0.0",
            3 => v + ".0",
            _ => v,
        };
    }
}
