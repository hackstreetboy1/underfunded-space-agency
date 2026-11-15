using Usa.Game.Services.Storage;

namespace Usa.Game.Services.Logging;

/// <summary>
/// Application log (SCRIPTING.md 18). One instance is built by <c>GameRoot</c> and injected into services.
/// Messages are redacted before reaching any sink; sinks never throw.
/// </summary>
public sealed class Log(IReadOnlyList<ILogSink> sinks, LogLevel minimumLevel = LogLevel.Info, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public LogLevel MinimumLevel { get; } = minimumLevel;

    /// <summary>A log that discards everything, for tests and tools.</summary>
    public static Log CreateNull() => new([]);

    /// <summary>Console plus <c>user://logs/usa_&lt;date&gt;.log</c>. Debug builds also log <see cref="LogLevel.Debug"/>.</summary>
    public static Log CreateDefault(bool debugBuild) =>
        new([new ConsoleLogSink(), new FileLogSink(UserPaths.Logs)], debugBuild ? LogLevel.Debug : LogLevel.Info);

    public bool IsEnabled(LogLevel level) => level >= MinimumLevel;

    public void Write(LogLevel level, string category, string message)
    {
        if (!IsEnabled(level)) return;
        var entry = new LogEntry(_time.GetUtcNow(), level, category, LogRedactor.Redact(message));
        foreach (var sink in sinks) sink.Write(entry);
    }

    public void Debug(string category, string message) => Write(LogLevel.Debug, category, message);

    public void Info(string category, string message) => Write(LogLevel.Info, category, message);

    public void Warn(string category, string message) => Write(LogLevel.Warning, category, message);

    public void Error(string category, string message) => Write(LogLevel.Error, category, message);
}
