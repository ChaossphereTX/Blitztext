using System.IO;
using Blitztext.Core.Settings;

namespace Blitztext.App.Platform;

/// <summary>
/// Always-on append log at %APPDATA%\Blitztext\blitztext.log with simple size-based rotation.
/// Intended for internal/admin diagnostics: it captures startup, workflow errors and unhandled
/// exceptions so issues can be investigated after the fact without a debugger.
/// </summary>
public static class Log
{
    private const long MaxBytes = 1_000_000; // ~1 MB, then roll to .old
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.AppSupportDirectory, "blitztext.log");

    public static void Write(string message)
    {
        try
        {
            AppPaths.EnsureAppSupportDirectoryExists();
            string path = FilePath;
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}";

            lock (Gate)
            {
                RotateIfNeeded(path);
                File.AppendAllText(path, line);
            }
        }
        catch
        {
            // never let logging break the app
        }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Exists && info.Length > MaxBytes)
            {
                string old = path + ".old";
                if (File.Exists(old))
                {
                    File.Delete(old);
                }

                File.Move(path, old);
            }
        }
        catch
        {
            // rotation is best-effort
        }
    }
}
