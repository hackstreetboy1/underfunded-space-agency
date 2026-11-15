namespace Usa.Game.Comms;

/// <summary>Comms-side checks on model text, on top of the simulation's <c>AiResponseValidator</c>.</summary>
internal static class AiTextChecks
{
    private const string FactTokenOpen = "<FACT:";

    /// <summary>
    /// True when a digit appears outside a <c>&lt;FACT:id&gt;</c> token. Values reach crew text only through fact
    /// tokens (SCRIPTING 13.2), so any other digit may be invented: the same rule authored templates follow.
    /// </summary>
    public static bool HasDigitOutsideFactTokens(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var i = 0;
        while (i < text.Length)
        {
            if (string.CompareOrdinal(text, i, FactTokenOpen, 0, FactTokenOpen.Length) == 0)
            {
                var close = text.IndexOf('>', i);
                if (close < 0) break;
                i = close + 1;
                continue;
            }
            if (char.IsDigit(text[i])) return true;
            i++;
        }
        for (; i < text.Length; i++)
        {
            if (char.IsDigit(text[i])) return true;
        }
        return false;
    }
}
