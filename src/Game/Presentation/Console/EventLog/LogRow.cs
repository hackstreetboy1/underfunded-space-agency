using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.EventLog;

/// <summary>One event-log row: MET · category icon · text · source.</summary>
/// <param name="CategoryWord">Category word next to the icon (never colour alone), e.g. <c>ALARM</c>.</param>
/// <param name="IconKey">Category icon key, e.g. <c>alarm</c>.</param>
public sealed record LogRow(
    long Sequence,
    MissionTime At,
    string MetText,
    LogCategory Category,
    string CategoryWord,
    string IconKey,
    string Text,
    string Source,
    string ColourToken)
{
    public static LogRow From(LogEntry entry, ConsoleWords words)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(words);
        var icon = entry.Category switch
        {
            LogCategory.Alarm => "alarm",
            LogCategory.Crew => "crew",
            LogCategory.Command => "command",
            LogCategory.SystemChange => "system",
            LogCategory.Decision => "decision",
            _ => "info",
        };
        return new LogRow(
            entry.Sequence, entry.At, ReadoutFormatter.FormatMet(entry.At), entry.Category, words.CategoryWord(entry.Category), icon,
            entry.Text, entry.Source, ColourFor(entry));
    }

    private static string ColourFor(LogEntry entry)
    {
        if (entry.Category == LogCategory.Info) return ColourTokens.TextLo;
        if (entry.Category != LogCategory.Alarm || entry.Priority is not { } priority) return ColourTokens.TextHi;
        return StatusLanguage.ColourToken(priority);
    }
}
