using Usa.Game.Services.Connection;

namespace Usa.Game.Flow.Backend;

/// <summary>What the player is told about the local proxy before a mission (main menu, briefing).</summary>
public enum BackendState
{
    /// <summary>No heartbeat has come back yet.</summary>
    Checking,

    /// <summary>The proxy and both real providers answer.</summary>
    Connected,

    /// <summary>The proxy answers with local stand-ins (canned crew replies, test audio).</summary>
    FakeMode,

    /// <summary>The proxy answers, but a provider behind it is down, limited or has no key.</summary>
    ServiceProblem,

    /// <summary>The launcher started the proxy and is waiting for its first healthy heartbeat.</summary>
    Starting,

    /// <summary>Nothing answers on the proxy address.</summary>
    NotRunning,
}

/// <param name="LaunchFailed">The launcher tried to start the proxy and it exited or never answered.</param>
public sealed record BackendStatus(BackendState State, ConnectionReport Report, bool LaunchFailed = false)
{
    public static BackendStatus Initial { get; } = new(BackendState.Checking, ConnectionReport.Initial);

    /// <summary>
    /// GDD 15.6: the backend and both providers are essential, so a mission waits for them (fake providers report
    /// <c>ok</c> and count). There is no captions-only mode.
    /// </summary>
    public bool MissionMayStart => State is BackendState.Connected or BackendState.FakeMode;
}
