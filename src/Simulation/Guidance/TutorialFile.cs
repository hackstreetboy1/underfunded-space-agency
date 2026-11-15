using Usa.Simulation.Commands;
using Usa.Simulation.Data;

namespace Usa.Simulation.Guidance;

/// <summary>Contents of <c>missions/&lt;id&gt;/tutorial.json</c> (schema <c>tutorial/1</c>): the guided first run (GDD 9.4).</summary>
/// <param name="MinDisplayS">A callout stays at least this long before its completion condition may close it.</param>
/// <param name="EndWhen">Once true, remaining callouts are dropped and dynamic tips take over; null runs every beat to its end.</param>
/// <param name="Beats">Callouts in authored order; that order breaks ties when several trigger together.</param>
public sealed record TutorialFile(
    string Schema,
    string MissionId,
    double MinDisplayS,
    IReadOnlyList<TutorialBeatData> Beats,
    string? EndWhen = null) : IVersionedDefinition
{
    public const string SchemaTag = "tutorial/1";
}

/// <param name="Anchor">UI element the callout points at, e.g. <c>resource_strip</c>.</param>
/// <param name="Trigger">Expression that queues the callout.</param>
/// <param name="Completion">Expression that closes it (the step is done, or moot); null means only the player closes it.</param>
/// <param name="After">Id of an earlier beat that must be complete first.</param>
/// <param name="Text">The body: one or two short sentences (UI.md 5.1).</param>
/// <param name="MaxDisplayS">The callout closes itself after this long; mission data leaves it out (callouts wait for the player).</param>
/// <param name="Title">Optional heading above the body: sentence case, no closing punctuation, at most <see cref="TutorialLoader.MaxTitleLength"/>.</param>
/// <param name="SuggestedOrder">An order the callout offers to send as written, so the step never needs the AI to parse it.</param>
/// <param name="MaxTimeRate">While the callout waits for the player, time compression stays at or below this rate (1 holds real time).</param>
public sealed record TutorialBeatData(
    string Id,
    string Anchor,
    string Text,
    string Trigger,
    string? Completion = null,
    string? After = null,
    double? MaxDisplayS = null,
    IReadOnlyList<TutorialTextAlternativeData>? Alternatives = null,
    string? Title = null,
    TutorialOrderData? SuggestedOrder = null,
    double? MaxTimeRate = null,
    TutorialRailData? Rail = null);

/// <summary>
/// Makes a beat a numbered step of the railed walkthrough (the guided first run on Easy): steps are numbered in authored order,
/// and while one is pending only its own action is possible (<c>Session.TutorialRails</c>).
/// </summary>
/// <param name="Do">The exact action, shown under the callout text: "Click Send", "Press ACK", "Choose Retro sequence".</param>
/// <param name="Wait">Shown once the step is done while the game works on it ("Wait — Cal is docking"); null when the next step follows at once.</param>
/// <param name="Choose">The one item a dropdown step allows: an engine id, or a recovery zone id.</param>
/// <param name="Skip">The callout keeps its own button, which closes the step without doing it.</param>
/// <param name="View">
/// The main view the console switches to for this step (<c>orbit</c> or <c>external</c>), so the player sees what the step is
/// about (the docking approach, the chutes, the planner); null leaves the view alone.
/// </param>
/// <param name="ViewOn">
/// When the switch happens: null as the step shows, <c>wait</c> once the step is done and the game works on it, or a mission
/// event id (<c>drogue</c>) when that event fires during the step.
/// </param>
public sealed record TutorialRailData(string Do, string? Wait = null, string? Choose = null, bool Skip = false, string? View = null, string? ViewOn = null)
{
    /// <summary><see cref="ViewOn"/> value: switch once the step is done.</summary>
    public const string ViewOnWait = "wait";

    /// <summary>The <see cref="View"/> values the console knows.</summary>
    public static readonly IReadOnlyList<string> Views = ["orbit", "external"];
}

/// <summary>Variant wording, first matching <c>when</c> wins (e.g. per navigation-assist level); its order replaces the beat's when given.</summary>
public sealed record TutorialTextAlternativeData(string When, string Text, TutorialOrderData? SuggestedOrder = null);

/// <summary>A structured order a callout can send: catalog action, crew member (null lets the session pick), arguments and the words shown.</summary>
public sealed record TutorialOrderData(string Action, string Text, string? Crew = null, IReadOnlyDictionary<string, CommandArg>? Args = null);
