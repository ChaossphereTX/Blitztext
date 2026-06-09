using System.Windows;
using System.Windows.Threading;
using Blitztext.App.Platform;
using Blitztext.App.UI;

namespace Blitztext.App;

public partial class App : System.Windows.Application
{
    private AppController _controller = null!;
    private GlobalHotkeyService _hotkeys = null!;
    private TrayIconController _tray = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Platform.Log.Write($"FATAL (domain): {args.ExceptionObject}");

        string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
        Platform.Log.Write($"=== Blitztext gestartet (v{version}) ===");

        _controller = new AppController();
        _tray = new TrayIconController(_controller);

        _hotkeys = new GlobalHotkeyService();
        _hotkeys.HotkeyFired += OnHotkeyFired;
        _hotkeys.Start();

        _tray.CheckForUpdatesRequested = () => _ = CheckForUpdatesAsync(silentIfNone: false);
        _tray.Initialize();

        // Background update check (does nothing if no feed URL is configured).
        _ = CheckForUpdatesAsync(silentIfNone: true);

        // First-run onboarding, mirroring the macOS showOnboardingIfNeeded().
        if (_controller.ShouldShowOnboarding)
        {
            Dispatcher.BeginInvoke(() => _tray.ShowPopover(forceOnboarding: true), DispatcherPriority.ApplicationIdle);
        }
    }

    private void OnHotkeyFired(HotkeyEvent evt)
    {
        // The hook runs on the UI thread. Queue the work (BeginInvoke) instead of running it
        // inline so the low-level hook callback returns immediately and never trips the
        // Windows LowLevelHooksTimeout (~300 ms) when starting the audio device.
        Dispatcher.BeginInvoke(() =>
        {
            switch (evt.Kind)
            {
                case HotkeyEventKind.Down:
                    if (_controller.App.HotkeyMode == Core.Models.HotkeyMode.Toggle)
                    {
                        _tray.HandleToggleHotkey(evt.Type);
                    }
                    else
                    {
                        _controller.HandleHotkeyDown(evt.Type);
                    }
                    break;
                case HotkeyEventKind.Up:
                    _controller.HandleHotkeyUp(evt.Type);
                    break;
                case HotkeyEventKind.Cancel:
                    _controller.HandleCancel();
                    break;
            }
        });
    }

    private async Task CheckForUpdatesAsync(bool silentIfNone)
    {
        // Default source is the project's public GitHub Releases (no client config); an optional
        // override feed (settings/env) is honoured by UpdateService if present.
        Platform.UpdateInfo? info = await Platform.UpdateService.CheckAsync(_controller.App.UpdateFeedUrl);
        if (info == null)
        {
            if (!silentIfNone)
            {
                MessageBox.Show(
                    $"Blitztext ist aktuell (Version {Platform.UpdateService.CurrentVersion}).",
                    "Blitztext – Update", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        // Only the version is shown — no source URLs / release-note links surface in the UI.
        var result = MessageBox.Show(
            $"Ein Update auf Version {info.Version} ist verfügbar.\n\n" +
            "Jetzt installieren? Blitztext wird dabei kurz neu gestartet.",
            "Blitztext – Update verfügbar", MessageBoxButton.YesNo, MessageBoxImage.Information);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            string setup = await Platform.UpdateService.DownloadAsync(info);
            Platform.UpdateService.ApplyAndExit(setup);
        }
        catch (Exception ex)
        {
            Platform.Log.Write($"update: apply failed: {ex}");
            MessageBox.Show(
                $"Update fehlgeschlagen:\n\n{ex.Message}",
                "Blitztext – Update", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Platform.Log.Write($"UNHANDLED: {e.Exception}");
        MessageBox.Show(
            $"Unerwarteter Fehler:\n\n{e.Exception.Message}",
            "Blitztext",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _controller?.Dispose();
        base.OnExit(e);
    }
}
