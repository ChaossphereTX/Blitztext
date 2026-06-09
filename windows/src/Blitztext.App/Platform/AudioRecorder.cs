using System.IO;
using Blitztext.Core.Settings;
using NAudio.Wave;

namespace Blitztext.App.Platform;

/// <summary>
/// Records the microphone to a 16 kHz mono 16-bit PCM WAV file using NAudio (WASAPI via
/// WaveInEvent). Windows counterpart to the macOS AVAudioRecorder. The 16 kHz mono format
/// is what both the OpenAI Whisper API and the local whisper.cpp model expect.
/// </summary>
public sealed class AudioRecorder : IDisposable
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _currentFilePath;
    private DateTime _startedAt;
    private TaskCompletionSource<bool>? _stopTcs;

    public bool IsRecording { get; private set; }
    public string? RecordingFilePath { get; private set; }
    public double LastRecordingDuration { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>Normalized 0..1 input level, updated continuously while recording.</summary>
    public float AudioLevel { get; private set; }

    public event Action? LevelChanged;

    public void StartRecording()
    {
        ErrorMessage = null;
        LastRecordingDuration = 0;
        RecordingFilePath = null;

        try
        {
            AppPaths.EnsureRecordingsDirectoryExists();
            _currentFilePath = Path.Combine(AppPaths.RecordingsDirectory, $"blitztext-{Guid.NewGuid():N}.wav");

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50,
            };
            _writer = new WaveFileWriter(_currentFilePath, _waveIn.WaveFormat);

            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();

            _startedAt = DateTime.UtcNow;
            IsRecording = true;
        }
        catch (Exception ex)
        {
            CleanupDevice();
            ErrorMessage = $"Aufnahme konnte nicht gestartet werden: {ex.Message}";
        }
    }

    /// <summary>
    /// Stops recording and completes once the WAV file has been flushed and closed,
    /// returning the finalized file path (NAudio finalizes on a background thread).
    /// </summary>
    public async Task<string?> StopAsync()
    {
        if (!IsRecording)
        {
            return RecordingFilePath;
        }

        LastRecordingDuration = (DateTime.UtcNow - _startedAt).TotalSeconds;
        IsRecording = false;
        _stopTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            _waveIn?.StopRecording();
        }
        catch
        {
            _stopTcs.TrySetResult(true);
        }

        AudioLevel = 0;
        LevelChanged?.Invoke();

        // Guard against a missed RecordingStopped callback.
        await Task.WhenAny(_stopTcs.Task, Task.Delay(2000)).ConfigureAwait(false);
        return RecordingFilePath;
    }

    public void DiscardRecording()
    {
        TryDelete(RecordingFilePath);
        TryDelete(_currentFilePath);
        RecordingFilePath = null;
        _currentFilePath = null;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);

        // Compute peak level from 16-bit samples for the waveform UI.
        float max = 0;
        for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
        {
            short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            float abs = Math.Abs(sample / 32768f);
            if (abs > max)
            {
                max = abs;
            }
        }

        AudioLevel = Math.Clamp(max * 1.5f, 0, 1);
        LevelChanged?.Invoke();
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        try
        {
            _writer?.Dispose();
            _writer = null;
            RecordingFilePath = _currentFilePath;
            _currentFilePath = null;

            if (e.Exception != null)
            {
                ErrorMessage = "Aufnahme fehlgeschlagen";
            }
        }
        finally
        {
            CleanupDevice();
            _stopTcs?.TrySetResult(true);
        }
    }

    private void CleanupDevice()
    {
        if (_waveIn != null)
        {
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }

        _writer?.Dispose();
        _writer = null;
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

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

    public void Dispose()
    {
        CleanupDevice();
        DiscardRecording();
    }
}
