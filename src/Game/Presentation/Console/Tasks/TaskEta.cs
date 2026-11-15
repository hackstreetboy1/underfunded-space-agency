using System.Globalization;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.Presentation.Console.Tasks;

/// <summary>Task ETA in MET and in real time at the current compression (UI.md 3.7).</summary>
/// <param name="MetText"><c>ETA MET 07:12:30</c>.</param>
/// <param name="RemainingText">Remaining MET, <c>in 4 min</c>.</param>
/// <param name="RealText"><c>≈ 24 s real</c>; a dash while frozen.</param>
/// <param name="RealSeconds">Remaining real seconds; null while frozen.</param>
public sealed record TaskEta(string MetText, string RemainingText, string RealText, double? RealSeconds)
{
    /// <summary>The ETA as a bare MET clock, <c>07:12:30</c> (compact layout).</summary>
    public string ClockText { get; init; } = string.Empty;

    /// <summary>Remaining real time without the template, <c>24 s</c>; a dash while frozen (compact layout).</summary>
    public string RealShortText { get; init; } = ReadoutFormatter.Placeholder;

    public static TaskEta For(ActiveTaskSnapshot task, MissionTime now, double compressionRate, ConsoleWords words)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(words);
        var remainingMs = Math.Max(0, task.EstimatedEnd.Milliseconds - now.Milliseconds);
        double? realSeconds = compressionRate > 0 && double.IsFinite(compressionRate) ? remainingMs / 1000.0 / compressionRate : null;
        return new TaskEta(
            words.TaskEtaMet(ReadoutFormatter.FormatMet(task.EstimatedEnd)),
            ReadoutFormatter.FormatRelative(now, now + remainingMs),
            realSeconds is { } seconds ? words.TaskEtaReal(FormatRealDuration(seconds)) : ReadoutFormatter.Placeholder,
            realSeconds)
        {
            ClockText = ReadoutFormatter.FormatMet(task.EstimatedEnd),
            RealShortText = realSeconds is { } real ? FormatRealDuration(real) : ReadoutFormatter.Placeholder,
        };
    }

    /// <summary><c>24 s</c>, <c>4 min</c>, <c>1 h 05 min</c>; seconds round up so a running task never reads 0 s.</summary>
    public static string FormatRealDuration(double seconds)
    {
        var whole = (long)Math.Ceiling(Math.Max(0, seconds));
        if (whole < 60) return string.Create(CultureInfo.InvariantCulture, $"{whole} s");
        var minutes = (long)Math.Round(whole / 60.0, MidpointRounding.AwayFromZero);
        if (minutes < 60) return string.Create(CultureInfo.InvariantCulture, $"{minutes} min");
        return string.Create(CultureInfo.InvariantCulture, $"{minutes / 60} h {minutes % 60:00} min");
    }
}
