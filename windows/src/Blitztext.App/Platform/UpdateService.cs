using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace Blitztext.App.Platform;

/// <summary>An available update.</summary>
public sealed class UpdateInfo
{
    public string Version { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

/// <summary>
/// Automatic updater. By default it pulls the latest signed installer straight from the project's
/// public GitHub Releases — clients configure NOTHING. GitHub is the source, but it is never shown
/// in the UI (no URLs/links surface to the user; only the version number is displayed).
///
/// The GitHub repo that hosts the release assets must be PUBLIC so the download needs no token.
/// An optional override (env BLITZTEXT_UPDATE_URL or settings.updateFeedUrl pointing to a
/// latest.json) is supported for special/offline deployments.
/// </summary>
public static class UpdateService
{
    // Release source (baked in; not user-visible). Repo must be public for token-less download.
    private const string Owner = "ChaossphereTX";
    private const string Repo = "Blitztext";
    private const string AssetName = "BlitztextSetup.exe";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub's API requires a User-Agent.
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Blitztext-Updater");
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return h;
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);

    /// <summary>
    /// Returns update info if a newer version is available, else null.
    /// Uses the optional override feed (latest.json) if set, otherwise GitHub Releases.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(string? overrideFeedUrl = null, CancellationToken ct = default)
    {
        string ov = ResolveOverride(overrideFeedUrl);
        return string.IsNullOrWhiteSpace(ov)
            ? await CheckGitHubAsync(ct).ConfigureAwait(false)
            : await CheckJsonAsync(ov, ct).ConfigureAwait(false);
    }

    private static string ResolveOverride(string? settingsUrl)
    {
        string env = Environment.GetEnvironmentVariable("BLITZTEXT_UPDATE_URL") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        return (settingsUrl ?? string.Empty).Trim();
    }

    private static async Task<UpdateInfo?> CheckGitHubAsync(CancellationToken ct)
    {
        try
        {
            string url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            string json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string version = tag.TrimStart('v', 'V').Trim();
            string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

            string? download = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement a in assets.EnumerateArray())
                {
                    if (a.TryGetProperty("name", out var n) && string.Equals(n.GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        download = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(download))
            {
                return null;
            }

            if (Version.TryParse(Normalize(version), out Version? remote) && remote > CurrentVersion)
            {
                return new UpdateInfo { Version = version, Url = download!, Notes = notes };
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Write($"update: github check failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<UpdateInfo?> CheckJsonAsync(string feedUrl, CancellationToken ct)
    {
        try
        {
            string json = await Http.GetStringAsync(feedUrl, ct).ConfigureAwait(false);
            var info = JsonSerializer.Deserialize<UpdateInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (info == null || string.IsNullOrWhiteSpace(info.Version) || string.IsNullOrWhiteSpace(info.Url))
            {
                return null;
            }

            return Version.TryParse(Normalize(info.Version), out Version? remote) && remote > CurrentVersion ? info : null;
        }
        catch (Exception ex)
        {
            Log.Write($"update: feed check failed: {ex.Message}");
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
    /// Launch the downloaded installer silently and exit so it can replace the running files;
    /// the installer relaunches the app afterwards (/RELAUNCH).
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

    private static string Normalize(string v)
    {
        v = v.Trim();
        return (v.Split('.').Length) switch
        {
            1 => v + ".0.0.0",
            2 => v + ".0.0",
            3 => v + ".0",
            _ => v,
        };
    }
}
