using System.Globalization;
using Godot;

namespace Usa.Game.Services.Backend;

/// <summary><see cref="IProcessRunner"/> over Godot's <c>OS</c>. On Windows a stop ends the whole tree (<c>taskkill /T</c>), so the uvicorn child of PowerShell goes too.</summary>
public sealed class GodotProcessRunner : IProcessRunner
{
    public static bool IsWindows => OS.GetName() == "Windows";

    public int Start(string executable, IReadOnlyList<string> arguments) =>
        OS.CreateProcess(executable, [.. arguments], openConsole: false);

    public bool IsRunning(int processId) => OS.IsProcessRunning(processId);

    public void StopTree(int processId)
    {
        if (IsWindows) OS.Execute("taskkill", ["/PID", processId.ToString(CultureInfo.InvariantCulture), "/T", "/F"]);
        else OS.Kill(processId);
    }
}
