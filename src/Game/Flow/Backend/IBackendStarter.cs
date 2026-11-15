namespace Usa.Game.Flow.Backend;

/// <summary>Starts the local proxy for the player (a dev convenience behind a setting); implemented over the OS process launcher.</summary>
public interface IBackendStarter
{
    /// <summary>The setting allows it and the start script exists.</summary>
    bool CanStart { get; }

    /// <summary>A proxy process this starter launched is still alive.</summary>
    bool IsRunning { get; }

    /// <summary>Launches the proxy unless one it started is still running; false when nothing could be started.</summary>
    bool TryStart();
}
