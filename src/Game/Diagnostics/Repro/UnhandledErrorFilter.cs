using System.Text.RegularExpressions;

namespace Usa.Game.Diagnostics.Repro;

/// <summary>
/// Decides which lines of Godot's error stream are crashes worth a repro: an exception dump as the engine's C# glue pushes it
/// (<c>System.NullReferenceException: ...</c> at the start of the text), or a caught exception that a handler logged
/// (<c>... failed: InvalidOperationException: ...</c>) when its type is a programming error. Expected runtime failures
/// (network, IO, cancellation, timeouts) never qualify, so a backend outage never floods the repro folder.
/// </summary>
public static partial class UnhandledErrorFilter
{
    public const string GodotErrorSource = "godot-error";

    private const int MaxMessageLength = 500;
    private const int MaxDetailLength = 8000;

    private static readonly HashSet<string> ProgrammingErrors = new(StringComparer.Ordinal)
    {
        "NullReferenceException",
        "InvalidOperationException",
        "ArgumentException",
        "ArgumentNullException",
        "ArgumentOutOfRangeException",
        "IndexOutOfRangeException",
        "KeyNotFoundException",
        "InvalidCastException",
        "NotImplementedException",
        "NotSupportedException",
        "FormatException",
        "OverflowException",
        "DivideByZeroException",
        "ObjectDisposedException",
        "StackOverflowException",
        "AggregateException",
    };

    /// <param name="text">The error's rationale, or its code when there is no rationale.</param>
    public static bool TryMatch(string? text, out ReproFailure failure)
    {
        failure = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var dump = DumpPattern().Match(text);
        if (dump.Success && !IsExpected(dump.Groups["type"].Value))
        {
            failure = Create(dump.Groups["type"].Value, dump.Groups["message"].Value, text);
            return true;
        }

        foreach (Match logged in LoggedPattern().Matches(text))
        {
            if (!ProgrammingErrors.Contains(logged.Groups["type"].Value)) continue;
            failure = Create(logged.Groups["type"].Value, logged.Groups["message"].Value, text);
            return true;
        }
        return false;
    }

    public static ReproFailure FromException(Exception exception, string source)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new ReproFailure(exception.GetType().FullName ?? exception.GetType().Name, Clip(exception.Message, MaxMessageLength), Clip(exception.ToString(), MaxDetailLength), source);
    }

    /// <summary>Same failure, same place: the type plus the first stack frame (or the message when there is no stack).</summary>
    public static string Signature(ReproFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var detail = failure.Detail ?? "";
        foreach (var line in detail.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("at ", StringComparison.Ordinal)) return $"{failure.Type}|{trimmed}";
        }
        return $"{failure.Type}|{failure.Message}";
    }

    /// <summary>Exceptions that report the outside world (network, IO, cancellation), not a bug in the game.</summary>
    public static bool IsExpected(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is OperationCanceledException or IOException or TimeoutException or HttpRequestException
            || IsExpected(exception.GetType().Name);
    }

    private static bool IsExpected(string type) =>
        type is "TaskCanceledException" or "OperationCanceledException" or "TimeoutException" or "HttpRequestException" or "IOException"
            or "SocketException" or "WebException" or "JsonException";

    private static ReproFailure Create(string type, string message, string text) =>
        new(type, Clip(message.Trim(), MaxMessageLength), Clip(text, MaxDetailLength), GodotErrorSource);

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max] + "…";

    [GeneratedRegex(@"^\s*(?:[A-Za-z_]\w*\.)*(?<type>[A-Za-z_]\w*Exception)(?::[ \t]*(?<message>[^\r\n]*))?(?:\r?\n|$)", RegexOptions.CultureInvariant)]
    private static partial Regex DumpPattern();

    [GeneratedRegex(@"(?<![\w.])(?:[A-Za-z_]\w*\.)*(?<type>[A-Za-z_]\w*Exception):[ \t]*(?<message>[^\r\n]*)", RegexOptions.CultureInvariant)]
    private static partial Regex LoggedPattern();
}
