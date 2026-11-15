using Usa.Simulation.Data;

namespace Usa.Game.Content;

/// <summary>
/// Root of <c>content/strings.json</c> (schema <c>strings/1</c>): UI text grouped by screen. A new section
/// needs a property here and an entry in <see cref="StringTable"/>; a new key inside a section needs nothing.
/// </summary>
/// <param name="Impact">
/// Confirmation-dialog consequence lines (UI.md 5.2) by the action catalogs' <c>effectTextKeys</c> without their
/// <c>impact.</c> prefix, so <c>impact.retrofire.commits_to_entry</c> resolves as is.
/// </param>
/// <param name="Debrief">Debrief screen chrome shared by every mission; mission wording stays in <c>content/&lt;missionId&gt;/debrief.json</c>.</param>
public sealed record StringsDefinition(
    string Schema,
    IReadOnlyDictionary<string, string> Agency,
    IReadOnlyDictionary<string, string> MainMenu,
    IReadOnlyDictionary<string, string> MissionSelect,
    IReadOnlyDictionary<string, string> Briefing,
    IReadOnlyList<string> LoadingLines,
    string LoadingTipPrefix,
    IReadOnlyList<TapeLabel> TapeLabels,
    IReadOnlyDictionary<string, string> ExternalView,
    IReadOnlyDictionary<string, string> EscMenu,
    IReadOnlyDictionary<string, string> FreezeOverlay,
    IReadOnlyDictionary<string, string> Privacy,
    IReadOnlyDictionary<string, string> Encyclopedia,
    IReadOnlyDictionary<string, string> Credits,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyDictionary<string, string> Map,
    IReadOnlyDictionary<string, string> Planner,
    IReadOnlyDictionary<string, string> Workspace,
    IReadOnlyDictionary<string, string> Console,
    IReadOnlyDictionary<string, string> Impact,
    IReadOnlyDictionary<string, string> Debrief) : IVersionedDefinition
{
    public const string SchemaTag = "strings/1";
    public const string RelativePath = "content/strings.json";
}

/// <summary>A humorous tape label stuck on the UI.</summary>
/// <param name="Placement">Where the label goes, for the UI team; not shown.</param>
public sealed record TapeLabel(string Id, string Text, string Placement);
