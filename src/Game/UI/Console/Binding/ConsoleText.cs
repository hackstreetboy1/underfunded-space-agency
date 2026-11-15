using System.Globalization;
using Usa.Game.Presentation.Console.Alarms;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.Console.Tasks;
using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.UI.Console.Binding;

/// <summary>
/// Joins view-model strings into the text one control shows. Words and values come from the view models; this only
/// combines them (shape glyph + word, never colour alone).
/// </summary>
public static class ConsoleText
{
    public const string Separator = " · ";

    /// <summary><c>▲ CAUTION</c>; the glyph is not repeated when the word already starts with it (<c>⇄ CONFLICT</c>).</summary>
    public static string WithGlyph(string? glyph, string? word)
    {
        var text = word ?? string.Empty;
        if (string.IsNullOrEmpty(glyph)) return text;
        if (text.Length == 0) return glyph;
        return text.StartsWith(glyph, StringComparison.Ordinal) ? text : glyph + " " + text;
    }

    public static string Status(StatusVisual visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        return WithGlyph(visual.Glyph, visual.Word);
    }

    /// <summary>Knowledge badge with its shape glyph, e.g. <c>◷ STALE 12m</c>; empty when there is no badge.</summary>
    public static string Knowledge(KnowledgeVisual visual, string? badge)
    {
        ArgumentNullException.ThrowIfNull(visual);
        return string.IsNullOrEmpty(badge) ? string.Empty : WithGlyph(visual.Glyph, badge);
    }

    /// <summary>Header caution count, <c>▲ 1 CAUTION</c>; empty with no active caution.</summary>
    public static string CautionCount(int count, StatusVisual caution)
    {
        ArgumentNullException.ThrowIfNull(caution);
        return count <= 0
            ? string.Empty
            : WithGlyph(caution.Glyph, count.ToString(CultureInfo.InvariantCulture) + " " + caution.Word);
    }

    /// <summary>The status of a systems-board tab's alarms; null when it has none.</summary>
    public static StatusVisual? TabAlert(SystemAlarmCount count, StatusLanguage status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return count.Active > 0 && count.Highest is { } highest ? status.For(highest) : null;
    }

    /// <summary>Tab title with the alarm shape after the label, <c>Prop &amp; attitude ▲</c>.</summary>
    public static string TabTitle(string label, StatusVisual? alert) =>
        alert is null ? label : label + " " + alert.Glyph;

    /// <summary>Strip under a crew feed, <c>CAL BRANDT · COMMAND PILOT</c>.</summary>
    public static string NameTag(string name, string role)
    {
        var upperName = name.ToUpperInvariant();
        return string.IsNullOrWhiteSpace(role) ? upperName : upperName + Separator + role.ToUpperInvariant();
    }

    /// <summary><c>ETA MET 06:58:45 · ≈ 33 s real</c>; empty without a task.</summary>
    public static string Eta(TaskEta? eta) => eta is null ? string.Empty : eta.MetText + Separator + eta.RealText;

    /// <summary>Compact layout, <c>06:58:45 · 33 s</c>; empty without a task.</summary>
    public static string EtaCompact(TaskEta? eta) => eta is null ? string.Empty : eta.ClockText + Separator + eta.RealShortText;

    /// <summary>The value line of a readout: both values of a conflict, <c>7.6 mmHg / 1.8 mmHg</c>, else the value.</summary>
    public static string ReadoutValue(ReadoutText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.SecondaryValue is { } second ? text.Value + " / " + second : text.Value;
    }

    /// <summary>Meta line of a readout, with the conflicting source after the first one.</summary>
    public static string ReadoutMeta(ReadoutText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.SecondaryMeta is not { } second) return text.Meta ?? string.Empty;
        return string.IsNullOrEmpty(text.Meta) ? second : text.Meta + " / " + second;
    }

    /// <summary>Fact chip, <c>OAMS 212 kg · 06:58</c> with its knowledge badge appended when not known and fresh.</summary>
    public static string FactChip(string label, string? badge) =>
        string.IsNullOrEmpty(badge) ? label : label + Separator + badge;
}
