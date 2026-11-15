using Usa.Game.Advisor;
using Usa.Game.Comms;
using Usa.Game.Content;
using Usa.Game.Guidance;
using Usa.Game.Integration.Adapters;
using Usa.Game.Presentation.CrewFeed;
using Usa.Game.Presentation.Cues;
using Usa.Game.Presentation.Map;
using Usa.Game.Presentation.Map.Planning;
using Usa.Game.Presentation.ViewModels;
using Usa.Game.Presentation.Workspace;
using Usa.Simulation.Commands;

namespace Usa.Game.Integration.Runtime;

/// <summary>
/// Everything <see cref="MissionRuntimeComposer"/> needs besides the running session, as narrow inputs (the game fills them
/// from <c>GameServices</c> and the mission's <c>ContentLibrary</c>; tests pass fakes).
/// </summary>
/// <param name="Strings">Player-facing words (<c>content/strings.json</c>).</param>
/// <param name="CommsContent">Comms templates, crew openers and profiles.</param>
/// <param name="Clock">Real time shared with the event marshaller (crew feeds, audio cues, planner throttle).</param>
public sealed record MissionRuntimeInputs(
    ITextSource Strings,
    CommsContent CommsContent,
    AiGuardrails AiGuardrails,
    AdvisorGuardrails AdvisorGuardrails,
    Encyclopedia Encyclopedia,
    ExpressionSet Expressions,
    IAiClient CommsAi,
    ITipClient TipAi,
    IExplainClient ExplainAi,
    IMonotonicClock Clock)
{
    /// <summary>The app-wide UI throttle (pumped by the simulation host); null publishes console values immediately.</summary>
    public UpdateThrottle? UiThrottle { get; init; }

    public IMissionHostControl Host { get; init; } = DetachedHostControl.Instance;

    /// <summary>The app-wide freeze signal (<c>FreezeController.Frozen</c>); comms holds AI requests and submissions while it is set.</summary>
    public IReadOnlyObservable<bool>? Frozen { get; init; }

    /// <summary>Null: no audio cue mapper is composed.</summary>
    public IAudioSinkFactory? AudioSinks { get; init; }

    /// <summary>Null: the orchestrator runs without a client host (no voice, no TTS).</summary>
    public ICommsHostFactory? CommsHosts { get; init; }

    public ICrewVoiceActivity? Voices { get; init; }

    /// <summary>Silenced when the context is disposed; null when the run has no speech output.</summary>
    public ICrewSpeechOutput? Speech { get; init; }

    /// <summary>Wall time for comms confirmations, tip request spacing and advisor timeouts.</summary>
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Test seam for tip and advisor request ids; null uses fresh GUIDs.</summary>
    public Func<string>? NewRequestId { get; init; }

    /// <summary>Null: <see cref="ConsoleLayout.For"/> the mission's crewed vehicle.</summary>
    public ConsoleLayout? Layout { get; init; }

    public TipServiceOptions? TipOptions { get; init; }

    public AdvisorOptions? AdvisorOptions { get; init; }

    public AudioCueSettings? AudioSettings { get; init; }

    public CrewFeedSettings? CrewFeedSettings { get; init; }

    public SpacecraftViewOptions? SpacecraftOptions { get; init; }

    public OrbitMapSettings? MapSettings { get; init; }

    /// <summary>
    /// Display name per recovery zone id (<c>debrief.json</c> <c>zoneNames</c>, text audit L14) for the map's rim labels and
    /// landing arc; null labels zones from their ids.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ZoneNames { get; init; }

    public WorkspaceOptions? WorkspaceOptions { get; init; }

    /// <summary>Where burn proposals are computed (the game: the thread pool); null computes them inline. <see cref="MissionRuntimeContext.Pump"/> applies them.</summary>
    public IProposalScheduler? ProposalScheduler { get; init; }

    /// <summary>
    /// Tutorial rails for the guided first run (<see cref="Usa.Simulation.Session.TutorialRails"/>): the game turns them on; they
    /// engage only on the railed walkthrough steps (Easy, guided). Headless harnesses that script their own off-path play leave them off.
    /// </summary>
    public bool TutorialRails { get; init; }
}
