namespace Usa.Game.Services.Backend;

/// <summary>The OS process calls the launcher needs; a seam for tests.</summary>
public interface IProcessRunner
{
    /// <summary>Starts a detached process with no console window; returns its id, or -1 when it could not start.</summary>
    int Start(string executable, IReadOnlyList<string> arguments);

    bool IsRunning(int processId);

    /// <summary>Ends the process and every process it started.</summary>
    void StopTree(int processId);
}
