using Usa.Simulation.Commands;

namespace Usa.Simulation.Session.Scripts;

/// <summary>
/// A scripted headless run (SCRIPTING 15, schema <c>scenario/1</c>): mission, difficulty and seed fix the run; <see cref="Commands"/>
/// are played through <see cref="MissionSession.Submit"/> on a 1 s MET grid; <see cref="Expect"/> is checked at the end.
/// </summary>
public sealed record ScenarioScript
{
    public const string SchemaId = "scenario/1";

    public string Schema { get; init; } = "";

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public string Mission { get; init; } = "";

    public string Difficulty { get; init; } = "";

    public ulong Seed { get; init; }

    /// <summary>What the seed produces (variants, incident time); documentation only, checked through <see cref="Expect"/>.</summary>
    public string SeedNotes { get; init; } = "";

    /// <summary>Absolute MET (s) at which the run stops if the mission has not ended; default mission start + 8 h.</summary>
    public double? MaxMetS { get; init; }

    public IReadOnlyList<ScenarioStep> Commands { get; init; } = [];

    public ScenarioExpectation Expect { get; init; } = new();
}

/// <summary>
/// One scripted input. It becomes due at <see cref="AtMetS"/>, or <see cref="DelayS"/> after the first occurrence of
/// <see cref="AtEvent"/>, or the first whole second <see cref="When"/> holds (after the event, when both are given), rounded up to
/// the next whole MET second. It is either a command (<see cref="Action"/>) or a workspace design (<see cref="Design"/>).
/// </summary>
public sealed record ScenarioStep
{
    /// <summary>Names the step for <c>done:</c>/<c>failed:</c>/<c>refused:</c> event keys.</summary>
    public string? Label { get; init; }

    public double? AtMetS { get; init; }

    /// <summary>An event key (see <see cref="ScenarioEventKeys"/>).</summary>
    public string? AtEvent { get; init; }

    /// <summary>An expression over state paths, polled every MET second.</summary>
    public string? When { get; init; }

    public double DelayS { get; init; }

    public string? Action { get; init; }

    /// <summary>Crew id, "crew" (best available) or null.</summary>
    public string? Assignee { get; init; }

    public Dictionary<string, CommandArg> Args { get; init; } = [];

    /// <summary>Scripts confirm high-impact actions by default; false tests the confirmation prompt.</summary>
    public bool Confirmed { get; init; } = true;

    /// <summary>The reason code the command must be refused with ("*" any); the run continues.</summary>
    public string? ExpectRefused { get; init; }

    /// <summary>A step that may never become due without failing the run.</summary>
    public bool Optional { get; init; }

    /// <summary>Seconds a step keeps retrying while the crew slot is busy before it fails.</summary>
    public double RetryBusyS { get; init; } = 900;

    /// <summary>
    /// Retrofire aiming: when due, the step is re-timed so the retro sequence starts at the proposal for this recovery zone
    /// ("next" = the earliest reachable zone), <see cref="LeadS"/> after submission (default: the task's base duration).
    /// </summary>
    public string? Zone { get; init; }

    public double? LeadS { get; init; }

    public ScenarioDesign? Design { get; init; }
}

/// <summary>A workspace board: <see cref="Chain"/> parts placed in series from the goal's source to its sink.</summary>
public sealed record ScenarioDesign
{
    public string Assembly { get; init; } = "";

    public string Goal { get; init; } = "";

    public IReadOnlyList<string> Chain { get; init; } = [];

    /// <summary>Whether the validator must accept it (and reserve its parts).</summary>
    public bool ExpectReady { get; init; } = true;
}

/// <summary>End-of-run expectations; every null field is not checked.</summary>
public sealed record ScenarioExpectation
{
    /// <summary><c>recovered</c>, <c>all_crew_lost</c> or <c>running</c> (not ended by <see cref="ScenarioScript.MaxMetS"/>).</summary>
    public string? Ended { get; init; }

    /// <summary>Grade label (<c>S+</c>, <c>B-</c>).</summary>
    public string? Grade { get; init; }

    /// <summary>Any of these grade labels.</summary>
    public IReadOnlyList<string>? Grades { get; init; }

    /// <summary><c>A</c>, <c>B</c> or <c>none</c>.</summary>
    public string? SPlusPath { get; init; }

    public int? Survivors { get; init; }

    public double? MinScore { get; init; }

    public double? MaxScore { get; init; }

    /// <summary>Recovery zone id, or <c>none</c>.</summary>
    public string? Zone { get; init; }

    /// <summary>Objective id → final state (<c>completed</c>, <c>impossible</c>, <c>unacceptable_risk</c>, <c>achievable</c>).</summary>
    public Dictionary<string, string>? Objectives { get; init; }

    /// <summary>Event keys that must have occurred.</summary>
    public IReadOnlyList<string> Events { get; init; } = [];

    /// <summary>Event keys that must not have occurred.</summary>
    public IReadOnlyList<string> NotEvents { get; init; } = [];

    /// <summary>Boolean expressions that must hold when the run ends.</summary>
    public IReadOnlyList<string> Checks { get; init; } = [];
}
