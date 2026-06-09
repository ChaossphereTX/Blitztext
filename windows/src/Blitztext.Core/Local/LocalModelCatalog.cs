using Blitztext.Core.Settings;

namespace Blitztext.Core.Local;

/// <summary>A selectable local whisper.cpp (GGML) model.</summary>
public sealed record LocalModelInfo(string Id, string DisplayName, string DownloadUrl, long ApproxBytes)
{
    public string FileName => $"ggml-{Id}.bin";
    public string FilePath => Path.Combine(AppPaths.WhisperModelsDirectory, FileName);
    public bool IsInstalled => LocalModelCatalog.IsUsableModel(FilePath);

    public string InstallStateLabel => IsInstalled ? "Installiert" : "Nicht installiert";
    public string ApproxSizeLabel => $"{ApproxBytes / (1024 * 1024)} MB";
}

/// <summary>
/// Windows equivalent of the macOS WhisperKit model catalog. WhisperKit/CoreML is Apple-only,
/// so on Windows we use whisper.cpp GGML models served by the official Hugging Face repo.
/// </summary>
public static class LocalModelCatalog
{
    private const string Repo = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main";

    /// <summary>Default fast model (small footprint, good quality) — mirrors macOS "recommendedFast".</summary>
    public const string RecommendedFastModelName = "small";

    public static readonly IReadOnlyList<LocalModelInfo> All = new[]
    {
        new LocalModelInfo("tiny", "Whisper Tiny", $"{Repo}/ggml-tiny.bin", 77_700_000),
        new LocalModelInfo("base", "Whisper Base", $"{Repo}/ggml-base.bin", 147_900_000),
        new LocalModelInfo("small", "Whisper Small", $"{Repo}/ggml-small.bin", 487_600_000),
        new LocalModelInfo("large-v3-turbo", "Whisper Large v3 Turbo", $"{Repo}/ggml-large-v3-turbo.bin", 1_624_000_000),
        new LocalModelInfo("large-v3", "Whisper Large v3", $"{Repo}/ggml-large-v3.bin", 3_095_000_000),
    };

    public static string NormalizeModelName(string? modelName)
    {
        string trimmed = (modelName ?? string.Empty).Trim();
        return string.IsNullOrEmpty(trimmed) ? RecommendedFastModelName : trimmed;
    }

    public static LocalModelInfo Get(string? modelName)
    {
        string name = NormalizeModelName(modelName);
        return All.FirstOrDefault(m => m.Id == name) ?? All.First(m => m.Id == RecommendedFastModelName);
    }

    public static string DisplayName(string? modelName) => Get(modelName).DisplayName;

    public static bool IsModelInstalled(string? modelName) => Get(modelName).IsInstalled;

    public static IEnumerable<LocalModelInfo> InstalledModels() => All.Where(m => m.IsInstalled);

    /// <summary>A model file counts as usable once it exists and is at least ~half its expected size.</summary>
    public static bool IsUsableModel(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            // Must be larger than 10 MB to rule out truncated/aborted downloads.
            return new FileInfo(filePath).Length > 10 * 1024 * 1024;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The model used at runtime: the preferred one if installed, otherwise any installed model.</summary>
    public static string ResolvedModelName(string? preferredModelName)
    {
        string normalized = NormalizeModelName(preferredModelName);
        if (IsModelInstalled(normalized))
        {
            return normalized;
        }

        return InstalledModels().FirstOrDefault()?.Id ?? normalized;
    }
}
