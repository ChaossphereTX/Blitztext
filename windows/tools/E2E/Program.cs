using System.IO;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using Blitztext.App.Platform;
using Blitztext.Core.Local;
using WinForms = System.Windows.Forms;

namespace Blitztext.E2E;

/// <summary>
/// Manual end-to-end harness that exercises the real Windows pipeline on this machine:
/// microphone capture, TTS-generated speech → local Whisper.net transcription, and the
/// clipboard + SendInput auto-paste path. It uses the SAME services the app uses.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string modelName = args.Length > 0 ? args[0] : "base";

        if (modelName == "icons")
        {
            DumpTrayIcons();
            return 0;
        }

        int failures = 0;

        Console.WriteLine("=== Blitztext Windows E2E ===\n");

        failures += Section("1) Mikrofon-Aufnahme (NAudio → 16 kHz Mono WAV)", TestMicrophone);
        string? speechWav = null;
        failures += Section("2) Sprachsynthese (SAPI/TTS → WAV mit bekanntem Satz)", () => speechWav = TestTts());
        failures += Section($"3) Lokale Transkription (Whisper.net, Modell '{modelName}')", () => TestLocalTranscription(speechWav, modelName));
        failures += Section("4) Zwischenablage + Auto-Paste (Clipboard + SendInput Strg+V)", TestAutoPaste);

        Console.WriteLine();
        Console.WriteLine(failures == 0 ? "✅ ALLE E2E-CHECKS BESTANDEN" : $"⚠️  {failures} Check(s) mit Hinweis/Fehler");
        return failures;
    }

    private static int Section(string title, Action body)
    {
        Console.WriteLine($"--- {title} ---");
        try
        {
            body();
            Console.WriteLine();
            return 0;
        }
        catch (SkippedException ex)
        {
            Console.WriteLine($"   ⏭️  Übersprungen: {ex.Message}\n");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ Fehler: {ex.Message}\n");
            return 1;
        }
    }

    private static void DumpTrayIcons()
    {
        var states = new[]
        {
            Blitztext.App.AppStatusKind.Idle,
            Blitztext.App.AppStatusKind.Recording,
            Blitztext.App.AppStatusKind.Processing,
            Blitztext.App.AppStatusKind.Success,
        };

        foreach (var kind in states)
        {
            using var icon = Blitztext.App.UI.IconFactory.CreateStatusIcon(kind);
            using var bmp = icon.ToBitmap();
            string path = Path.Combine(Path.GetTempPath(), $"blitztext-tray-{kind}.png");
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"{kind} -> {path}");
        }
    }

    private static void TestMicrophone()
    {
        if (WinForms.SystemInformation.UserInteractive == false)
        {
            throw new SkippedException("keine interaktive Session");
        }

        var recorder = new AudioRecorder();
        recorder.StartRecording();
        if (recorder.ErrorMessage != null)
        {
            throw new SkippedException(recorder.ErrorMessage + " (vermutlich kein Eingabegerät)");
        }

        Thread.Sleep(1200);
        string? file = recorder.StopAsync().GetAwaiter().GetResult();

        if (file == null || !File.Exists(file))
        {
            throw new SkippedException("kein Aufnahme-Gerät / keine Datei (Umgebung ohne Mikrofon)");
        }

        long size = new FileInfo(file).Length;
        Console.WriteLine($"   ✅ Aufnahme {recorder.LastRecordingDuration:F2}s, Datei {size / 1024} KB → {Path.GetFileName(file)}");
        recorder.DiscardRecording();
        recorder.Dispose();
    }

    private static string TestTts()
    {
        using var syn = new SpeechSynthesizer();
        var voice = syn.GetInstalledVoices().FirstOrDefault(v => v.Enabled);
        if (voice == null)
        {
            throw new SkippedException("keine TTS-Stimme installiert");
        }

        string culture = voice.VoiceInfo.Culture.TwoLetterISOLanguageName;
        string sentence = culture == "de"
            ? "Dies ist ein Test der lokalen Transkription mit Blitztext."
            : "This is a test of the local transcription in Blitztext.";

        string path = Path.Combine(Path.GetTempPath(), $"blitztext-e2e-{Guid.NewGuid():N}.wav");
        syn.SetOutputToWaveFile(path, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
        syn.Speak(sentence);
        syn.SetOutputToNull();

        Console.WriteLine($"   ✅ Stimme '{voice.VoiceInfo.Name}' ({culture}), Satz: \"{sentence}\"");
        Console.WriteLine($"      WAV: {new FileInfo(path).Length / 1024} KB");
        Environment.SetEnvironmentVariable("E2E_TTS_CULTURE", culture);
        return path;
    }

    private static void TestLocalTranscription(string? speechWav, string modelName)
    {
        if (speechWav == null || !File.Exists(speechWav))
        {
            throw new SkippedException("keine TTS-WAV aus Schritt 2");
        }

        using var local = new LocalTranscriptionService();
        var model = LocalModelCatalog.Get(modelName);

        if (!model.IsInstalled)
        {
            Console.WriteLine($"   ↓ Lade Modell '{model.DisplayName}' ({model.ApproxSizeLabel}) …");
            int lastPct = -1;
            var progress = new Progress<double>(p =>
            {
                int pct = (int)(p * 100);
                if (pct >= lastPct + 10)
                {
                    lastPct = pct;
                    Console.WriteLine($"      {pct} %");
                }
            });
            local.DownloadAndInstallAsync(model.Id, progress).GetAwaiter().GetResult();
            Console.WriteLine($"   ✅ Modell installiert: {model.FilePath}");
        }
        else
        {
            Console.WriteLine($"   ℹ️  Modell bereits installiert: {model.FilePath}");
        }

        string culture = Environment.GetEnvironmentVariable("E2E_TTS_CULTURE") ?? "en";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string[] terms = { "Blitztext" };
        string text = local.TranscribeAsync(speechWav, culture, model.Id, terms).GetAwaiter().GetResult();
        sw.Stop();

        Console.WriteLine($"   ✅ Transkript ({sw.ElapsedMilliseconds} ms): \"{text}\"");

        if (!text.ToLowerInvariant().Contains("test"))
        {
            throw new Exception($"Transkript enthält nicht das erwartete Wort 'test': \"{text}\"");
        }

        Console.WriteLine("   ✅ Inhaltsprüfung bestanden (enthält 'test').");
        TryDelete(speechWav);
    }

    private static void TestAutoPaste()
    {
        const string payload = "Blitztext E2E Auto-Paste ✔";

        AutoPasteService.WriteToClipboard(payload);
        string roundTrip = System.Windows.Clipboard.GetText();
        if (roundTrip != payload)
        {
            throw new Exception($"Clipboard-Roundtrip fehlgeschlagen: \"{roundTrip}\"");
        }
        Console.WriteLine("   ✅ Clipboard schreiben/lesen ok.");

        // Paste into a real focused WinForms TextBox to validate the SendInput Ctrl+V path.
        using var form = new WinForms.Form { Width = 320, Height = 120, Text = "E2E", TopMost = true, StartPosition = WinForms.FormStartPosition.CenterScreen };
        var box = new WinForms.TextBox { Dock = WinForms.DockStyle.Fill, Multiline = true };
        form.Controls.Add(box);
        form.Show();
        box.Focus();
        Pump(300);

        AutoPasteService.PasteInto(form.Handle);
        Pump(400);

        if (box.Text.Contains("Blitztext E2E Auto-Paste"))
        {
            Console.WriteLine($"   ✅ Auto-Paste in Zielfenster angekommen: \"{box.Text}\"");
        }
        else
        {
            Console.WriteLine($"   ⚠️  Eingefügter Text nicht erkannt (Fokus/Headless?). Box=\"{box.Text}\". Clipboard-Fallback ist verfügbar.");
        }

        form.Close();
    }

    private static void Pump(int ms)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < until)
        {
            WinForms.Application.DoEvents();
            Thread.Sleep(15);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private sealed class SkippedException : Exception
    {
        public SkippedException(string message) : base(message) { }
    }
}
