using System.Windows;
using Blitztext.Core.Workflows;
using WinForms = System.Windows.Forms;

namespace Blitztext.App.UI;

/// <summary>
/// Owns the system-tray icon and its menu, and coordinates the popover/settings windows.
/// Windows counterpart to the macOS MenuBarStatusController + NSStatusItem/NSPopover.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private readonly AppController _controller;
    private readonly WinForms.NotifyIcon _notifyIcon = new();

    private PopoverWindow? _popover;
    private SettingsWindow? _settings;
    private System.Drawing.Icon? _currentIcon;
    private AppStatusKind? _lastKind;

    /// <summary>Invoked when the user picks "Nach Updates suchen" from the tray menu.</summary>
    public Action? CheckForUpdatesRequested;

    public TrayIconController(AppController controller)
    {
        _controller = controller;
    }

    public void Initialize()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Blitztext öffnen", null, (_, _) => ShowPopover());
        menu.Items.Add("Einstellungen …", null, (_, _) => ShowSettings());
        menu.Items.Add("Protokoll anzeigen", null, (_, _) => OpenLog());
        menu.Items.Add("Nach Updates suchen …", null, (_, _) => CheckForUpdatesRequested?.Invoke());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => System.Windows.Application.Current.Shutdown());

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.Visible = true;
        _notifyIcon.MouseClick += OnTrayMouseClick;

        _controller.StatusChanged += () => Dispatch(UpdateIcon);
        UpdateIcon();
    }

    private void OnTrayMouseClick(object? sender, WinForms.MouseEventArgs e)
    {
        if (e.Button == WinForms.MouseButtons.Left)
        {
            Dispatch(TogglePopover);
        }
    }

    private void UpdateIcon()
    {
        AppStatusKind kind = _controller.StatusKind;

        // Only rebuild the GDI icon when the status kind actually changes. StatusChanged also
        // fires on every download-progress tick; recreating the icon each time caused the tray
        // icon to flicker and flooded the UI thread.
        if (kind != _lastKind)
        {
            System.Drawing.Icon newIcon = IconFactory.CreateStatusIcon(kind);
            _notifyIcon.Icon = newIcon;
            _currentIcon?.Dispose();
            _currentIcon = newIcon;
            _lastKind = kind;
        }

        _notifyIcon.Text = Truncate($"Blitztext – {_controller.StatusText}", 63);
    }

    private void TogglePopover()
    {
        if (_popover is { IsVisible: true })
        {
            _popover.Hide();
        }
        else
        {
            ShowPopover();
        }
    }

    public void ShowPopover(bool forceOnboarding = false)
    {
        _controller.PrepareForPopoverPresentation();
        _popover ??= CreatePopover();
        _popover.ShowOnboarding = forceOnboarding || _controller.ShouldShowOnboarding;
        _popover.RefreshContent();
        _popover.ShowNearTray();
    }

    public void ShowSettings()
    {
        if (_popover is { IsVisible: true })
        {
            _popover.Hide();
        }

        if (_settings is { IsLoaded: true })
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(_controller);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    private void OpenLog()
    {
        try
        {
            string path = Platform.Log.FilePath;
            if (!System.IO.File.Exists(path))
            {
                Platform.Log.Write("(Protokoll geöffnet)");
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Protokoll konnte nicht geöffnet werden: {ex.Message}", "Blitztext");
        }
    }

    public void HandleToggleHotkey(WorkflowType type)
    {
        if (_controller.ActiveType == type && _controller.IsBusy)
        {
            _ = _controller.StopActiveAsync();
            return;
        }

        ShowPopover();
        _controller.StartWorkflow(type, WorkflowLaunchSource.Manual);
    }

    private PopoverWindow CreatePopover()
    {
        var popover = new PopoverWindow(_controller);
        popover.OpenSettingsRequested += ShowSettings;
        _controller.RequestDismissPopover += () => Dispatch(() => _popover?.Hide());
        return popover;
    }

    private static void Dispatch(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app == null)
        {
            return;
        }

        if (app.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            app.Dispatcher.BeginInvoke(action);
        }
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
    }
}
