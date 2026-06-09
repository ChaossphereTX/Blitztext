using Blitztext.Core.Models;

namespace Blitztext.Core.Workflows;

/// <summary>
/// Builds the OpenAI system prompts. This is a faithful port of the prompt logic in the
/// macOS LLMService so the Windows app produces identical model behaviour.
/// </summary>
public static class PromptBuilder
{
    public static string BuildEmojiSystemPrompt(EmojiDensity density)
    {
        string densityInstruction = density switch
        {
            EmojiDensity.Wenig => "Setze nur vereinzelt Emojis ein, maximal 1-2 pro Absatz.",
            EmojiDensity.Mittel => "Setze regelmaessig passende Emojis ein, etwa alle 1-2 Saetze.",
            EmojiDensity.Viel => "Setze grosszuegig Emojis ein, gerne mehrere pro Satz.",
            _ => "Setze regelmaessig passende Emojis ein, etwa alle 1-2 Saetze.",
        };

        return "Du erhaeltst ein gesprochenes Transkript. Gib den Text moeglichst originalgetreu zurueck, " +
               "aber fuege passende Emojis ein. " + densityInstruction +
               " Korrigiere offensichtliche Sprach- und Grammatikfehler. Behalte den Stil und die " +
               "Bedeutung bei. Gib NUR den Text mit Emojis zurueck, keine Erklaerungen.";
    }

    public static string BuildImprovementSystemPrompt(TextImprovementSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.SystemPrompt))
        {
            string custom = settings.SystemPrompt;
            if (settings.CustomTerms.Count > 0)
            {
                custom += "\n\nWichtig: Diese Eigennamen und Fachbegriffe muessen exakt so geschrieben werden: "
                          + string.Join(", ", settings.CustomTerms);
            }

            return custom;
        }

        string prompt =
            "Du bist ein Lektor und Schreibassistent. Verbessere den folgenden Text:\n" +
            "- Korrigiere Rechtschreibung und Grammatik\n" +
            "- Verbessere die Formulierung und den Lesefluss\n" +
            "- Behalte die urspruengliche Bedeutung bei\n" +
            "- Gib NUR den verbesserten Text zurueck, keine Erklaerungen";

        prompt += settings.Tone switch
        {
            TextTone.Formal => "\n- Verwende einen formellen, professionellen Ton",
            TextTone.Neutral => "\n- Verwende einen neutralen, klaren Ton",
            TextTone.Casual => "\n- Verwende einen lockeren, natuerlichen Ton",
            _ => "\n- Verwende einen neutralen, klaren Ton",
        };

        if (settings.CustomTerms.Count > 0)
        {
            prompt += "\n\nWichtig: Diese Eigennamen und Fachbegriffe muessen exakt so geschrieben werden: "
                      + string.Join(", ", settings.CustomTerms);
        }

        if (!string.IsNullOrEmpty(settings.Context))
        {
            prompt += "\n\nKontext: " + settings.Context;
        }

        return prompt;
    }
}
