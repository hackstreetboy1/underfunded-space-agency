using Godot;

namespace Usa.Game.Services.Logging;

/// <summary>Writes to the Godot output; warnings and errors also reach the debugger's error list.</summary>
public sealed class ConsoleLogSink : ILogSink
{
    public void Write(in LogEntry entry)
    {
        var line = $"[{LogFormat.LevelTag(entry.Level)}][{entry.Category}] {entry.Message}";
        switch (entry.Level)
        {
            case LogLevel.Error:
                GD.PushError(line);
                break;
            case LogLevel.Warning:
                GD.PushWarning(line);
                break;
            default:
                GD.Print(line);
                break;
        }
    }
}

internal static class LogFormat
{
    public static string LevelTag(LogLevel level) => level switch
    {
        LogLevel.Debug => "DEBUG",
        LogLevel.Info => "INFO",
        LogLevel.Warning => "WARN",
        _ => "ERROR",
    };
}
