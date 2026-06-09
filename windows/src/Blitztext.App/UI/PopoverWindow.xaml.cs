using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Blitztext.Core.Workflows;

namespace Blitztext.App.UI;

public partial class PopoverWindow : Window
{
    private readonly AppController _controller;

    public bool ShowOnboarding { get; set; }
    public event Action? OpenSettingsRequested;

    public PopoverWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();

        _controller.StatusChanged += OnStatusChanged;
        _controller.LevelChanged += OnLevelChanged;
        Deactivated += (_, _) => Hide();
    }

    public void ShowNearTray()
    {
        var work = SystemParameters.WorkArea;
        Show();
        UpdateLayout();
        Left = work.Right - ActualWidth - 12;
        Top = work.Bottom - ActualHeight - 12;
        Activate();
        Topmost = true;
    }

    public void RefreshContent()
    {
        OnboardingPanel.Visibility = ShowOnboarding ? Visibility.Visible : Visibility.Collapsed;
        WorkflowList.Visibility = ShowOnboarding ? Visibility.Collapsed : Visibility.Visible;

        WorkflowList.Children.Clear();
        if (!ShowOnboarding)
        {
            foreach (WorkflowType type in WorkflowTypeInfo.MainMenuCases)
            {
                WorkflowList.Children.Add(BuildWorkflowRow(type));
            }
        }

        UpdateStatusVisuals();
    }

    private UIElement BuildWorkflowRow(WorkflowType type)
    {
        bool available = _controller.IsWorkflowAvailable(type);

        var title = new TextBlock
        {
            Text = _controller.DisplayName(type),
            Foreground = (Brush)FindResource("TextBrush"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
        };
        var subtitle = new TextBlock
        {
            Text = available ? _controller.WorkflowSubtitle(type) : _controller.UnavailableReason(type),
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
        };
        var content = new StackPanel();
        content.Children.Add(title);
        content.Children.Add(subtitle);

        string hotkeyLabel = type.HotkeyLabel();
        if (!string.IsNullOrEmpty(hotkeyLabel))
        {
            content.Children.Add(new TextBlock
            {
                Text = hotkeyLabel,
                Foreground = (Brush)FindResource("TextMutedBrush"),
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        var button = new Button
        {
            Content = content,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = (Brush)FindResource("PanelMutedBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            IsEnabled = available && !_controller.IsBusy,
            Tag = type,
        };
        button.Click += OnWorkflowClick;
        return button;
    }

    private void OnWorkflowClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorkflowType type })
        {
            _controller.PrepareForPopoverPresentation();
            _controller.StartWorkflow(type, WorkflowLaunchSource.Manual);
            UpdateStatusVisuals();
        }
    }

    private async void OnStopClick(object sender, RoutedEventArgs e)
    {
        await _controller.StopActiveAsync();
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        Hide();
        OpenSettingsRequested?.Invoke();
    }

    private void OnStatusChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(UpdateStatusVisuals);
            return;
        }

        UpdateStatusVisuals();
    }

    private void OnLevelChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(UpdateLevelBar);
            return;
        }

        UpdateLevelBar();
    }

    private void UpdateStatusVisuals()
    {
        StatusText.Text = _controller.StatusText;
        bool recording = _controller.StatusKind == AppStatusKind.Recording;
        bool busy = _controller.IsBusy;

        StopButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        LevelTrack.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;

        foreach (var child in WorkflowList.Children)
        {
            if (child is Button b && b.Tag is WorkflowType type)
            {
                b.IsEnabled = _controller.IsWorkflowAvailable(type) && !busy;
            }
        }

        UpdateLevelBar();
    }

    private void UpdateLevelBar()
    {
        if (LevelTrack.Visibility != Visibility.Visible)
        {
            return;
        }

        double trackWidth = LevelTrack.ActualWidth;
        if (trackWidth <= 0)
        {
            trackWidth = ActualWidth - 52;
        }

        LevelBar.Width = Math.Clamp(_controller.AudioLevel, 0, 1) * trackWidth;
    }
}
