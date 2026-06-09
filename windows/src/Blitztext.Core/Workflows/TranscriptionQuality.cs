using System.Globalization;

namespace Blitztext.Core.Workflows;

/// <summary>
/// Heuristics that reject empty/garbage recordings and Whisper hallucination artifacts.
/// Direct port of the macOS TranscriptionQualityService.
/// </summary>
public static class TranscriptionQuality
{
    public const double MinimumRecordingDuration = 0.3;

    public static bool ShouldRejectRecording(double duration) => duration < MinimumRecordingDuration;

    public static string CleanedTranscript(string text) => text.Trim();

    public static bool IsLikelyArtifact(string text, double recordingDuration)
    {
        string cleaned = CleanedTranscript(text);
        if (cleaned.Length == 0)
        {
            return true;
        }

        int wordCount = cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        int letters = cleaned.Count(c => char.IsLetter(c));

        if (letters == 0)
        {
            return true;
        }

        if (recordingDuration < 0.55 && (wordCount >= 5 || cleaned.Length >= 32))
        {
            return true;
        }

        if (recordingDuration < 0.8 && cleaned.Length >= 56)
        {
            return true;
        }

        return false;
    }
}
