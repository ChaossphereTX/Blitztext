using Whisper.net;

namespace Blitztext.Core.Local;

public sealed class LocalTranscriptionException : Exception
{
    public LocalTranscriptionException(string message) : base(message) { }
}

/// <summary>
/// On-device transcription using whisper.cpp via Whisper.net. This is the Windows
/// counterpart to the macOS WhisperKit/CoreML LocalTranscriptionService:
/// model download with progress, install detection, lazy pipeline loading and transcription.
/// </summary>
public sealed class LocalTranscriptionService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WhisperFactory? _factory;
    private string? _loadedModelName;

    public async Task<string> DownloadAndInstallAsync(
        string modelName, IProgress<double>? progress, CancellationToken ct = default)
    {
        LocalModelInfo model = LocalModelCatalog.Get(modelName);

        if (model.IsInstalled)
        {
            progress?.Report(1);
            return model.Id;
        }

        Settings.AppPaths.EnsureWhisperModelsDirectoryExists();

        string tempPath = model.FilePath + "." + Guid.NewGuid().ToString("N") + ".part";

        try
        {
            using var response = await _http
                .GetAsync(model.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength ?? model.ApproxBytes;

            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dest = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true))
            {
                var buffer = new byte[1 << 20];
                long readTotal = 0;
                int read;
                int lastReportedPercent = -1;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    readTotal += read;
                    if (total is > 0)
                    {
                        // Report at most once per whole percent to avoid flooding the caller
                        // (a multi-GB model would otherwise emit thousands of callbacks).
                        int percent = (int)(readTotal * 100 / total.Value);
                        if (percent != lastReportedPercent)
                        {
                            lastReportedPercent = percent;
                            progress?.Report(Math.Clamp((double)readTotal / total.Value, 0, 1));
                        }
                    }
                }
            }

            if (!LocalModelCatalog.IsUsableModel(tempPath))
            {
                throw new LocalTranscriptionException($"Das geladene Modell ist unvollständig: {model.DisplayName}");
            }

            if (File.Exists(model.FilePath))
            {
                File.Delete(model.FilePath);
            }

            File.Move(tempPath, model.FilePath);

            // Drop a cached pipeline if it referenced the (now replaced) model.
            if (_loadedModelName == model.Id)
            {
                ResetPipeline();
            }

            progress?.Report(1);
            return model.Id;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    /// <summary>Pre-load the model so the first real transcription is fast.</summary>
    public async Task PrepareAsync(string modelName, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureFactory(modelName);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> TranscribeAsync(
        string audioFilePath, string language, string modelName,
        IReadOnlyList<string>? customTerms = null, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            WhisperFactory factory = EnsureFactory(modelName);

            string resolvedLanguage = (language ?? string.Empty).Trim();
            var builder = factory.CreateBuilder();
            builder = string.IsNullOrEmpty(resolvedLanguage)
                ? builder.WithLanguageDetection()
                : builder.WithLanguage(resolvedLanguage);

            // Bias recognition towards user-defined proper nouns / technical terms via an
            // initial prompt (mirrors the macOS remote "prompt" field, now also offline).
            if (customTerms is { Count: > 0 })
            {
                builder = builder.WithPrompt("Eigennamen und Begriffe: " + string.Join(", ", customTerms));
            }

            using WhisperProcessor processor = builder.Build();

            var parts = new List<string>();
            await using (var stream = File.OpenRead(audioFilePath))
            {
                await foreach (var segment in processor.ProcessAsync(stream, ct).ConfigureAwait(false))
                {
                    parts.Add(segment.Text);
                }
            }

            string text = string.Join(" ", parts).Trim();
            if (string.IsNullOrEmpty(text))
            {
                throw new LocalTranscriptionException("Das lokale Modell hat keinen Text erkannt.");
            }

            return text;
        }
        finally
        {
            _gate.Release();
        }
    }

    private WhisperFactory EnsureFactory(string modelName)
    {
        string resolved = LocalModelCatalog.ResolvedModelName(modelName);
        if (_factory is not null && _loadedModelName == resolved)
        {
            return _factory;
        }

        LocalModelInfo model = LocalModelCatalog.Get(resolved);
        if (!model.IsInstalled)
        {
            throw new LocalTranscriptionException($"Lokales Modell fehlt: {model.FilePath}");
        }

        ResetPipeline();
        _factory = WhisperFactory.FromPath(model.FilePath);
        _loadedModelName = resolved;
        return _factory;
    }

    private void ResetPipeline()
    {
        _factory?.Dispose();
        _factory = null;
        _loadedModelName = null;
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

    public void Dispose()
    {
        ResetPipeline();
        _http.Dispose();
        _gate.Dispose();
    }
}
