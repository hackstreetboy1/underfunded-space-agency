namespace Usa.Game.Services.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>One redacted log line. <see cref="Message"/> has already passed through <see cref="LogRedactor"/>.</summary>
public readonly record struct LogEntry(DateTimeOffset TimestampUtc, LogLevel Level, string Category, string Message);

/// <summary>Destination for log entries. Implementations must not throw.</summary>
public interface ILogSink
{
    void Write(in LogEntry entry);
}
