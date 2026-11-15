using Godot;
using Usa.Game.Advisor;
using Usa.Game.Advisor.Glue;
using Usa.Game.Bootstrap;
using Usa.Game.Comms;
using Usa.Game.Comms.Hosting;
using Usa.Game.Guidance.Glue;
using Usa.Game.Integration.Runtime.Hosting;
using Usa.Game.Presentation.CrewFeed;
using Usa.Game.Presentation.Map.Planning;
using Usa.Simulation.Commands;
using Usa.Simulation.Data;

namespace Usa.Game.Integration.Runtime;

public static partial class MissionRuntimeComposer
{
    /// <summary>
    /// Composes a started mission from the app services: content from the mission's <c>ContentLibrary</c>, guardrails and
    /// crew expressions from the data, AI clients over the gateway, comms ids from <see cref="LoadedMission.CreateCommsIds"/>,
    /// crew voice activity from the speech player and push-to-talk, a <c>CommsController</c> and a per-frame ticker under
    /// <paramref name="owner"/>, audio cues to the <c>AudioDirector</c>, comms held while <c>services.Freeze.Frozen</c>, crew
    /// speech silenced at teardown, and a resume's history restored without being replayed to the player.
    /// <c>MissionFlowHost</c> calls it when a run starts (before <c>MissionStarted</c>) and disposes the result when it stops.
    /// If anything fails, what was already wired is released before the exception propagates.
    /// </summary>
    /// <exception cref="DefinitionException">A content, guardrail or expression file failed to load.</exception>
    public static MissionRuntimeContext Compose(MissionRuntime runtime, GameServices services, Node owner)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(owner);
        var session = runtime.Mission.Session;
        var library = services.Content.Get(session.MissionId);
        var loader = new DefinitionLoader(services.Data);
        var gateway = services.Gateway;

        var inputs = new MissionRuntimeInputs(
            library.Strings,
            CommsContent.Load(session.Source, new CrewPhraseBookSource(library.CrewPhrases)),
            AiGuardrails.Load(loader).ForVehicles(session.Data.Vehicles.Select(v => v.Id)),
            AdvisorGuardrails.Load(loader),
            library.Encyclopedia,
            ExpressionSet.Load(services.Data),
            new AiGatewayClient(gateway),
            new AiGatewayTipClient(gateway),
            new AiGatewayExplainClient(gateway),
            services.Clock)
        {
            UiThrottle = services.UiThrottle,
            Host = new SimulationHostControl(services.Flow.Host, services.Connection),
            Frozen = services.Freeze.Frozen,
            AudioSinks = new AudioDirectorSinkFactory(services.Audio),
            CommsHosts = new CommsControllerHostFactory(owner, gateway, services.Connection, services.Mic, services.Speech, services.Log,
                services.Audio, services.Clock),
            Voices = new SpeechVoiceActivity(services.Speech, services.Mic),
            Speech = new SpeechPlayerOutput(services.Speech),
            ProposalScheduler = new ThreadPoolProposalScheduler(),
            ZoneNames = library.Debrief.ZoneNames,
            TutorialRails = true,
        };

        var context = Compose(session, runtime.Events, inputs, nextCommandId: null, runtime.Mission.CreateCommsIds(), runtime.Mission.History);
        try
        {
            var ticker = new MissionRuntimeTicker();
            ticker.Initialize(context);
            owner.AddChild(ticker);
            context.Own(new DetachTicker(ticker));
        }
        catch
        {
            context.Dispose();
            throw;
        }
        return context;
    }

    private sealed class DetachTicker(MissionRuntimeTicker ticker) : IDisposable
    {
        public void Dispose()
        {
            if (GodotObject.IsInstanceValid(ticker)) ticker.Detach();
        }
    }
}
