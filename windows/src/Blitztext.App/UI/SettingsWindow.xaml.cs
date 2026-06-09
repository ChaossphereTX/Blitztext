using System.Windows;
using System.Windows.Controls;
using Blitztext.App.Platform;
using Blitztext.Core.Local;
using Blitztext.Core.Models;
using Blitztext.Core.Workflows;

namespace Blitztext.App.UI;

public partial class SettingsWindow : Window
{
    private readonly AppController _controller;
    private bool _loading;

    public SettingsWindow(AppController controller)
    {
        _controller = controller;
        InitializeComponent();
        Icon = IconFactory.WindowIcon;

        _controller.StatusChanged += OnControllerStatusChanged;
        Closed += (_, _) => _controller.StatusChanged -= OnControllerStatusChanged;

        Loaded += (_, _) => LoadValues();
    }

    private void LoadValues()
    {
        _loading = true;

        // Customize tab
        SecureLocalCheck.IsChecked = _controller.App.SecureLocalModeEnabled;
        PopulateModelCombo();
        BuildHotkeyList();
        HotkeyModeCombo.SelectedIndex = _controller.App.HotkeyMode == HotkeyMode.Hold ? 0 : 1;
        ToneCombo.SelectedIndex = (int)_controller.TextImprovement.Tone;
        ImprovePrompt.Text = _controller.TextImprovement.SystemPrompt;
        ImproveContext.Text = _controller.TextImprovement.Context;
        DampfPrompt.Text = _controller.DampfAblassen.SystemPrompt;
        EmojiCombo.SelectedIndex = (int)_controller.Emoji.EmojiDensity;
        LanguageBox.Text = _controller.Transcription.Language;
        BuildTermsList();

        // Access tab
        UpdateApiKeyStatus();
        StartupCheck.IsChecked = StartupService.IsEnabled;

        UpdateDownloadVisuals();
        _loading = false;
    }

    private void PopulateModelCombo()
    {
        ModelCombo.Items.Clear();
        foreach (LocalModelInfo model in LocalModelCatalog.All)
        {
            ModelCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{model.DisplayName} · {model.InstallStateLabel} · {model.ApproxSizeLabel}",
                Tag = model.Id,
            });
        }

        string selected = _controller.SelectedLocalModelName;
        for (int i = 0; i < ModelCombo.Items.Count; i++)
        {
            if (ModelCombo.Items[i] is ComboBoxItem { Tag: string id } && id == selected)
            {
                ModelCombo.SelectedIndex = i;
                break;
            }
        }

        ModelCombo.SelectionChanged -= OnModelChanged;
        ModelCombo.SelectionChanged += OnModelChanged;
    }

    private void OnModelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ModelCombo.SelectedItem is not ComboBoxItem { Tag: string id })
        {
            return;
        }

        _controller.App.SelectedLocalTranscriptionModelName = id;
        _controller.SaveSettings();
        UpdateDownloadVisuals();
    }

    private void BuildHotkeyList()
    {
        HotkeyList.Children.Clear();
        foreach (WorkflowType type in WorkflowTypeInfo.MainMenuCases)
        {
            var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var key = new TextBlock { Text = type.HotkeyLabel(), FontFamily = new System.Windows.Media.FontFamily("Consolas"), Opacity = 0.75, Width = 200 };
            var name = new TextBlock { Text = _controller.DisplayName(type), FontWeight = FontWeights.SemiBold };
            DockPanel.SetDock(key, Dock.Left);
            panel.Children.Add(key);
            panel.Children.Add(name);
            HotkeyList.Children.Add(panel);
        }
    }

    private void BuildTermsList()
    {
        TermsList.Children.Clear();
        foreach (string term in _controller.TextImprovement.CustomTerms.ToList())
        {
            string captured = term;
            var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
            var remove = new Button { Content = "✕", Width = 26, Margin = new Thickness(6, 0, 0, 0) };
            remove.Click += (_, _) =>
            {
                _controller.TextImprovement.CustomTerms.Remove(captured);
                _controller.SaveSettings();
                BuildTermsList();
            };
            DockPanel.SetDock(remove, Dock.Right);
            var label = new TextBlock { Text = captured, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(remove);
            panel.Children.Add(label);
            TermsList.Children.Add(panel);
        }
    }

    private void OnAddTerm(object sender, RoutedEventArgs e)
    {
        string term = NewTerm.Text.Trim();
        if (string.IsNullOrEmpty(term) || _controller.TextImprovement.CustomTerms.Contains(term))
        {
            return;
        }

        _controller.TextImprovement.CustomTerms.Add(term);
        _controller.SaveSettings();
        NewTerm.Text = string.Empty;
        BuildTermsList();
    }

    private async void OnDownloadModel(object sender, RoutedEventArgs e)
    {
        await _controller.InstallSelectedLocalModelAsync();
        PopulateModelCombo();
        UpdateDownloadVisuals();
    }

    private void OnControllerStatusChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(UpdateDownloadVisuals);
            return;
        }

        UpdateDownloadVisuals();
    }

    private void UpdateDownloadVisuals()
    {
        bool downloading = _controller.IsDownloadingLocalModel;
        DownloadProgress.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        if (downloading && _controller.LocalModelDownloadProgress is { } p)
        {
            DownloadProgress.Value = p;
        }

        DownloadStatus.Text = _controller.LocalModelDownloadStatusText ?? string.Empty;
        DownloadError.Text = _controller.LocalModelDownloadErrorText ?? string.Empty;
        DownloadButton.IsEnabled = !downloading && !_controller.SelectedLocalModelIsInstalled;
        DownloadButton.Content = _controller.SelectedLocalModelIsInstalled
            ? $"{LocalModelCatalog.DisplayName(_controller.SelectedLocalModelName)} ist installiert"
            : $"{LocalModelCatalog.DisplayName(_controller.SelectedLocalModelName)} installieren";
    }

    private void UpdateApiKeyStatus()
    {
        if (_controller.ApiKeyConfigured)
        {
            ApiKeyStatus.Text = $"Gespeichert: {_controller.ApiKeyDisplayValue()}";
        }
        else
        {
            ApiKeyStatus.Text = "Noch kein API Key hinterlegt.";
        }
    }

    private void OnSaveApiKey(object sender, RoutedEventArgs e)
    {
        string key = ApiKeyBox.Password.Trim();
        if (string.IsNullOrEmpty(key))
        {
            ApiKeyMessage.Text = "Bitte trage deinen OpenAI API Key ein.";
            return;
        }

        try
        {
            _controller.SaveApiKey(key);
            ApiKeyBox.Clear();
            ApiKeyMessage.Text = "Gespeichert.";
            UpdateApiKeyStatus();
            _controller.SaveSettings();
        }
        catch (Exception ex)
        {
            ApiKeyMessage.Text = ex.Message;
        }
    }

    private void OnToggleStartup(object sender, RoutedEventArgs e)
    {
        StartupService.SetEnabled(StartupCheck.IsChecked == true);
        StartupCheck.IsChecked = StartupService.IsEnabled;
    }

    private void ApplyEditableValues()
    {
        _controller.App.SecureLocalModeEnabled = SecureLocalCheck.IsChecked == true;
        _controller.App.HotkeyMode = HotkeyModeCombo.SelectedIndex == 0 ? HotkeyMode.Hold : HotkeyMode.Toggle;
        _controller.TextImprovement.Tone = (TextTone)Math.Max(0, ToneCombo.SelectedIndex);
        _controller.TextImprovement.SystemPrompt = ImprovePrompt.Text;
        _controller.TextImprovement.Context = ImproveContext.Text;
        _controller.DampfAblassen.SystemPrompt = DampfPrompt.Text;
        _controller.Emoji.EmojiDensity = (EmojiDensity)Math.Max(0, EmojiCombo.SelectedIndex);
        _controller.Transcription.Language = LanguageBox.Text.Trim();
        _controller.SaveSettings();
    }

    private async void OnSaveAndClose(object sender, RoutedEventArgs e)
    {
        ApplyEditableValues();

        // If secure local mode was just enabled but the model is missing, kick off the download.
        if (_controller.App.SecureLocalModeEnabled && !_controller.SelectedLocalModelIsInstalled && !_controller.IsDownloadingLocalModel)
        {
            await _controller.InstallSelectedLocalModelAsync();
        }

        Close();
    }
}
