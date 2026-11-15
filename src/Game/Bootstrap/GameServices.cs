using Usa.Game.Flow;
using Usa.Game.Flow.Backend;
using Usa.Game.Flow.Profile;
using Usa.Game.Hosting;
using Usa.Game.Integration;
using Usa.Game.Presentation.Audio;
using Usa.Game.Presentation.ViewModels;
using Usa.Game.Services.Ai;
using Usa.Game.Services.Connection;
using Usa.Game.Services.Logging;
using Usa.Game.Services.Saves;
using Usa.Game.Services.Settings;
using Usa.Game.Services.Voice;
using Usa.Simulation.Data;

namespace Usa.Game.Bootstrap;

/// <summary>
/// Application services built once by <see cref="GameRoot"/> (SCRIPTING 12.1). Scenes receive the members they need
/// through <c>Initialize(...)</c>; nothing looks services up by name.
/// </summary>
/// <param name="Data">Read-only game data (<c>res://data</c>).</param>
/// <param name="DefaultBindings">Default bindings of every rebindable action, captured once at start-up (settings screen).</param>
/// <param name="LaunchCount">This game launch's number (1 on the first launch); the main menu rotates its backdrop by it.</param>
/// <param name="Flow">Runs missions for <see cref="Router"/>; mission scenes bind to its <c>Context</c> (<c>ContextChanged</c>).</param>
/// <param name="Freeze">
/// The app's one freeze signal (Menu, ServiceOutage, Loading): the router sets reasons, the simulation host attaches the
/// running session, and everything else that must hold while frozen (comms, confirmation timers) follows its <c>Frozen</c>.
/// </param>
/// <param name="Clock">Real time shared by every <see cref="EventMarshaller"/>, <see cref="UiThrottle"/> and simulation host.</param>
/// <param name="UiThrottle">Pumped by the simulation host after each event flush.</param>
public sealed record GameServices(
    Log Log,
    IDataSource Data,
    SettingsService Settings,
    IReadOnlyDictionary<string, IReadOnlyList<BindingSpec>> DefaultBindings,
    AiGateway Gateway,
    ConnectionMonitor Connection,
    MicCapture Mic,
    SpeechPlayer Speech,
    SaveService Saves,
    ProfileRecorder Profile,
    int LaunchCount,
    ContentCatalog Content,
    MissionLoader Missions,
    MissionFlowHost Flow,
    AudioDirector Audio,
    FreezeController Freeze,
    IMonotonicClock Clock,
    UpdateThrottle UiThrottle)
{
    public ScreenRouter Router => Flow.Router;

    /// <summary>
    /// The ground-link status before a mission (main menu, briefing): <c>Current</c>, <c>Changed</c> and <c>Retry()</c>.
    /// Screens wrap it in a <c>BackendStatusModel</c>. Null only where a test builds services without it.
    /// </summary>
    public BackendStatusTracker? Backend { get; init; }

    /// <summary>The Astronomy Picture of the Day for the loading screens, fetched once in the background at boot.</summary>
    public ApodService? Apod { get; init; }
}
