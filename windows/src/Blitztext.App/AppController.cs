using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Blitztext.App.Platform;
using Blitztext.Core.Local;
using Blitztext.Core.Models;
using Blitztext.Core.OpenAi;
using Blitztext.Core.Settings;
using Blitztext.Core.Workflows;

namespace Blitztext.App;

public enum AppStatusKind { Idle, Recording, Processing, Success, Error }

public enum WorkflowLaunchSource { Manual, HotkeyBackground }

/// <summary>
/// Central orchestrator. Windows counterpart to the macOS AppState: owns settings, the
/// recorder, the OpenAI/local transcription services and runs the record → transcribe →
/// (optional) rewrite → auto-paste pipeline.
/// </summary>
public sealed class AppController : INotifyPropertyChanged, IDisposable
{
    private readonly HttpClient _http = new();
    private readonly OpenAiTranscriptionClient _transcription;
    private readonly OpenAiChatClient _chat;
    private readonly LocalTranscriptionService _local = new();
    private readonly AudioRecorder _recorder = new();

    private SettingsContainer _settings;
    private CancellationTokenSource? _processingCts;
    private IntPtr _pasteTarget;
    private IntPtr _lastForegroundBeforePopover;
    private WorkflowLaunchSource _activeSource = WorkflowLaunchSource.Manual;

    public AppController()
    {
        _transcription = new OpenAiTranscriptionClient(_http);
        _chat = new OpenAiChatClient(_http);
        _settings = SettingsStore.Load();

        if (string.IsNullOrEmpty(_settings.App.SelectedLocalTranscriptionModelName))
        {
            _settings.App.SelectedLocalTranscriptionModelName = LocalModelCatalog.RecommendedFastModelName;
        }

        // If the selected local model is not installed but another one is, switch to it so
        // local mode works out of the box with whatever model the user actually downloaded.
        if (!LocalModelCatalog.IsModelInstalled(_settings.App.SelectedLocalTranscriptionModelName)
            && LocalModelCatalog.InstalledModels().FirstOrDefault() is { } installed)
        {
            _settings.App.SelectedLocalTranscriptionModelName = installed.Id;
        }

        _recorder.LevelChanged += () => RaiseLevelChanged();
        PrewarmLocalIfNeeded();
    }

    // ----- Settings -----

    public AppSettings App => _settings.App;
    public TranscriptionSettings Transcription => _settings.Transcription;
    public TextImprovementSettings TextImprovement => _settings.TextImprovement;
    public DampfAblassenSettings DampfAblassen => _settings.DampfAblassen;
    public EmojiTextSettings Emoji => _settings.EmojiText;

    public void SaveSettings()
    {
        SettingsStore.Save(_settings);
        PrewarmLocalIfNeeded();
        OnPropertyChanged(nameof(IsConfigured));
    }

    // ----- Status -----

    public AppStatusKind StatusKind { get; private set; } = AppStatusKind.Idle;
    public WorkflowType? StatusType { get; private set; }
    public string StatusText { get; private set; } = "Bereit.";
    public WorkflowType? ActiveType { get; private set; }
    public bool IsRecording => _recorder.IsRecording;
    public float AudioLevel => _recorder.AudioLevel;
    public bool IsBusy => StatusKind is AppStatusKind.Recording or AppStatusKind.Processing;

    public event Action? StatusChanged;
    public event Action? LevelChanged;
    public event Action? RequestDismissPopover;

    // ----- Configuration helpers -----

    public bool IsConfigured => CredentialStore.IsConfigured || LocalModelCatalog.InstalledModels().Any();

    public bool ShouldShowOnboarding => !IsConfigured && !_settings.App.HasSeenOnboarding;

    public bool ApiKeyConfigured => CredentialStore.IsConfigured;

    public string SelectedLocalModelName => LocalModelCatalog.NormalizeModelName(_settings.App.SelectedLocalTranscriptionModelName);

    public bool SelectedLocalModelIsInstalled => LocalModelCatalog.IsModelInstalled(SelectedLocalModelName);

    public bool IsWorkflowAvailable(WorkflowType type) => type switch
    {
        WorkflowType.LocalTranscription => SelectedLocalModelIsInstalled,
        WorkflowType.Transcription => _settings.App.SecureLocalModeEnabled
            ? SelectedLocalModelIsInstalled
            : CredentialStore.IsConfigured,
        _ => !_settings.App.SecureLocalModeEnabled && CredentialStore.IsConfigured,
    };

    public string DisplayName(WorkflowType type)
    {
        string custom = type switch
        {
            WorkflowType.TextImprover => _settings.TextImprovement.CustomName,
            WorkflowType.DampfAblassen => _settings.DampfAblassen.CustomName,
            WorkflowType.EmojiText => _settings.EmojiText.CustomName,
            _ => string.Empty,
        };
        return string.IsNullOrWhiteSpace(custom) ? type.DisplayName() : custom.Trim();
    }

    /// <summary>Reason shown in the popover when a workflow is unavailable.</summary>
    public string UnavailableReason(WorkflowType type) => type switch
    {
        WorkflowType.LocalTranscription => "Lokales Modell fehlt.",
        WorkflowType.Transcription => _settings.App.SecureLocalModeEnabled
            ? "Lokales Modell fehlt."
            : "OpenAI-Key erforderlich.",
        _ => "OpenAI-Key erforderlich.",
    };

    public string WorkflowSubtitle(WorkflowType type)
    {
        switch (type)
        {
            case WorkflowType.Transcription:
                if (_settings.App.SecureLocalModeEnabled)
                {
                    return SelectedLocalModelIsInstalled
                        ? $"Lokal: {LocalModelCatalog.DisplayName(SelectedLocalModelName)}."
                        : "Lokales Modell fehlt.";
                }
                return "Online: Whisper über OpenAI.";
            case WorkflowType.LocalTranscription:
                return "Nur lokal. Kein Server.";
            default:
                return _settings.App.SecureLocalModeEnabled ? "Im lokalen Modus pausiert." : type.Subtitle();
        }
    }

    // ----- Popover focus capture -----

    public void PrepareForPopoverPresentation()
    {
        _lastForegroundBeforePopover = AutoPasteService.CaptureForegroundWindow();
    }

    // ----- API key -----

    public void SaveApiKey(string value) => CredentialStore.SaveApiKey(value);

    public string ApiKeyDisplayValue()
    {
        string? value = CredentialStore.LoadApiKey();
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length > 8 ? value[..4] + " ••••••••" : "••••••••";
    }

    // ----- Workflow lifecycle (mirrors AppDelegate hotkey handling) -----

    public void HandleHotkeyDown(WorkflowType type)
    {
        if (!IsConfigured)
        {
            return;
        }

        if (_settings.App.HotkeyMode == HotkeyMode.Hold)
        {
            StartWorkflow(type, WorkflowLaunchSource.HotkeyBackground);
        }
        else
        {
            if (ActiveType == type && IsBusy)
            {
                _ = StopActiveAsync();
            }
            else
            {
                PrepareForPopoverPresentation();
                StartWorkflow(type, WorkflowLaunchSource.Manual);
            }
        }
    }

    public void HandleHotkeyUp(WorkflowType type)
    {
        if (_settings.App.HotkeyMode != HotkeyMode.Hold)
        {
            return;
        }

        if (ActiveType == type && _recorder.IsRecording)
        {
            _ = StopActiveAsync();
        }
    }

    public void HandleCancel() => CancelActive();

    public void StartWorkflow(WorkflowType type, WorkflowLaunchSource source)
    {
        if (!IsWorkflowAvailable(type))
        {
            return;
        }

        CancelActive();
        _activeSource = source;
        _pasteTarget = source == WorkflowLaunchSource.HotkeyBackground
            ? AutoPasteService.CaptureForegroundWindow()
            : _lastForegroundBeforePopover;

        ActiveType = type;
        _recorder.StartRecording();

        if (_recorder.ErrorMessage != null)
        {
            SetStatus(AppStatusKind.Error, type, _recorder.ErrorMessage);
            return;
        }

        SetStatus(AppStatusKind.Recording, type, "Aufnahme läuft …");
    }

    public async Task StopActiveAsync()
    {
        if (ActiveType is not { } type || !_recorder.IsRecording)
        {
            return;
        }

        SetStatus(AppStatusKind.Processing, type, "Wird transkribiert …");
        string? file = await _recorder.StopAsync();

        if (file == null || !File.Exists(file))
        {
            SetStatus(AppStatusKind.Error, type, "Keine Aufnahme vorhanden.");
            return;
        }

        double duration = _recorder.LastRecordingDuration;
        if (TranscriptionQuality.ShouldRejectRecording(duration))
        {
            _recorder.DiscardRecording();
            SetStatus(AppStatusKind.Error, type, "Keine Aufnahme erkannt.");
            return;
        }

        _processingCts = new CancellationTokenSource();
        try
        {
            await ProcessAsync(type, file, duration, _processingCts.Token);
        }
        catch (OperationCanceledException)
        {
            SetStatus(AppStatusKind.Idle, null, "Abgebrochen.");
        }
        catch (Exception ex)
        {
            Platform.Log.Write($"exception: {ex}");
            SetStatus(AppStatusKind.Error, type, ex.Message);
        }
        finally
        {
            TryDelete(file);
        }
    }

    public void CancelActive()
    {
        _processingCts?.Cancel();
        if (_recorder.IsRecording)
        {
            _ = _recorder.StopAsync();
        }
        _recorder.DiscardRecording();
        if (StatusKind != AppStatusKind.Idle)
        {
            SetStatus(AppStatusKind.Idle, null, "Bereit.");
        }
        ActiveType = null;
    }

    private async Task ProcessAsync(WorkflowType type, string file, double duration, CancellationToken ct)
    {
        string? apiKey = CredentialStore.LoadApiKey();
        bool useLocal = type == WorkflowType.LocalTranscription || _settings.App.SecureLocalModeEnabled;
        var vocab = duration >= 0.9 ? _settings.TextImprovement.CustomTerms : new List<string>();
        Platform.Log.Write($"transcribe: type={type} backend={(useLocal ? "local:" + SelectedLocalModelName : "remote:whisper-1")} dur={duration:F2}s terms={vocab.Count}");

        string raw;
        if (useLocal)
        {
            // Run on a background thread: model loading + inference are CPU-heavy and must
            // not block the UI thread (especially for large models). Custom terms bias the
            // local recognition just like in online mode.
            string lang = _settings.Transcription.Language;
            string model = SelectedLocalModelName;
            var terms = vocab;
            raw = await Task.Run(() => _local.TranscribeAsync(file, lang, model, terms, ct), ct);
        }
        else
        {
            raw = await _transcription.TranscribeAsync(apiKey ?? string.Empty, file, vocab, _settings.Transcription.Language, ct);
        }

        string cleaned = TranscriptionQuality.CleanedTranscript(raw);
        if (TranscriptionQuality.IsLikelyArtifact(cleaned, duration))
        {
            SetStatus(AppStatusKind.Error, type, "Keine Aufnahme erkannt.");
            return;
        }

        ct.ThrowIfCancellationRequested();

        string output = cleaned;
        switch (type)
        {
            case WorkflowType.TextImprover:
                SetStatus(AppStatusKind.Processing, type, "Wird verbessert …");
                output = await _chat.ImproveAsync(apiKey ?? string.Empty, cleaned, _settings.TextImprovement, ct: ct);
                break;
            case WorkflowType.DampfAblassen:
                SetStatus(AppStatusKind.Processing, type, "Wird umformuliert …");
                output = await _chat.DampfAblassenAsync(apiKey ?? string.Empty, cleaned, _settings.DampfAblassen.SystemPrompt, ct: ct);
                break;
            case WorkflowType.EmojiText:
                SetStatus(AppStatusKind.Processing, type, "Emojis werden ergänzt …");
                output = await _chat.AddEmojisAsync(apiKey ?? string.Empty, cleaned, _settings.EmojiText, ct: ct);
                break;
        }

        output = TranscriptionQuality.CleanedTranscript(output);
        SetStatus(AppStatusKind.Success, type, "Fertig – eingefügt.");
        DeliverOutput(output);
    }

    private void DeliverOutput(string text)
    {
        bool clip = AutoPasteService.WriteToClipboard(text);

        if (_activeSource == WorkflowLaunchSource.Manual)
        {
            RequestDismissPopover?.Invoke();
        }

        IntPtr target = _pasteTarget;
        Platform.Log.Write($"deliver: source={_activeSource} target={target} clipboard={clip} chars={text.Length}");

        // Give the popover a moment to dismiss and focus to return before pasting.
        _ = Task.Run(async () =>
        {
            await Task.Delay(_activeSource == WorkflowLaunchSource.Manual ? 220 : 120);
            AutoPasteService.PasteInto(target);
        });

        ActiveType = null;
        ScheduleIdleReset();
    }

    private async void ScheduleIdleReset()
    {
        await Task.Delay(1600);
        if (!_recorder.IsRecording && StatusKind == AppStatusKind.Success)
        {
            SetStatus(AppStatusKind.Idle, null, "Bereit.");
        }
    }

    // ----- Local model download -----

    public double? LocalModelDownloadProgress { get; private set; }
    public string? LocalModelDownloadStatusText { get; private set; }
    public string? LocalModelDownloadErrorText { get; private set; }
    public bool IsDownloadingLocalModel => LocalModelDownloadProgress != null;

    public async Task InstallSelectedLocalModelAsync()
    {
        if (IsDownloadingLocalModel)
        {
            return;
        }

        string modelName = SelectedLocalModelName;
        LocalModelDownloadProgress = 0;
        LocalModelDownloadStatusText = "Download startet …";
        LocalModelDownloadErrorText = null;
        OnPropertyChanged(nameof(IsDownloadingLocalModel));
        StatusChanged?.Invoke();

        int lastPercent = -1;
        var progress = new Progress<double>(p =>
        {
            int percent = (int)(Math.Clamp(p, 0, 1) * 100);
            // Throttle UI updates to once per whole percent; a multi-GB download otherwise
            // fires thousands of times and floods the UI thread.
            if (percent == lastPercent)
            {
                return;
            }

            lastPercent = percent;
            LocalModelDownloadProgress = Math.Clamp(p, 0, 1);
            LocalModelDownloadStatusText = $"Download {percent} %";
            StatusChanged?.Invoke();
        });

        try
        {
            await _local.DownloadAndInstallAsync(modelName, progress);
            _settings.App.SecureLocalModeEnabled = true;
            LocalModelDownloadProgress = null;
            LocalModelDownloadStatusText = $"{LocalModelCatalog.DisplayName(modelName)} wird geladen …";
            StatusChanged?.Invoke();
            SaveSettings();
            // Load the model off the UI thread – a large model can take many seconds and would
            // otherwise freeze the app.
            await Task.Run(() => _local.PrepareAsync(modelName));
            LocalModelDownloadStatusText = $"{LocalModelCatalog.DisplayName(modelName)} ist installiert.";
        }
        catch (Exception ex)
        {
            LocalModelDownloadProgress = null;
            LocalModelDownloadStatusText = null;
            LocalModelDownloadErrorText = ex.Message;
        }
        finally
        {
            OnPropertyChanged(nameof(IsDownloadingLocalModel));
            OnPropertyChanged(nameof(IsConfigured));
            StatusChanged?.Invoke();
        }
    }

    private void PrewarmLocalIfNeeded()
    {
        if (!_settings.App.SecureLocalModeEnabled || !LocalModelCatalog.IsModelInstalled(SelectedLocalModelName))
        {
            return;
        }

        string modelName = SelectedLocalModelName;
        _ = Task.Run(() => _local.PrepareAsync(modelName));
    }

    // ----- Helpers -----

    private void SetStatus(AppStatusKind kind, WorkflowType? type, string text)
    {
        if (kind == AppStatusKind.Error)
        {
            Platform.Log.Write($"error: {type} – {text}");
        }

        StatusKind = kind;
        StatusType = type;
        StatusText = text;
        OnPropertyChanged(nameof(StatusKind));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsBusy));
        StatusChanged?.Invoke();
    }

    private void RaiseLevelChanged()
    {
        OnPropertyChanged(nameof(AudioLevel));
        LevelChanged?.Invoke();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        _recorder.Dispose();
        _local.Dispose();
        _http.Dispose();
        _processingCts?.Dispose();
    }
}
