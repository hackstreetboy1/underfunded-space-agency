using System.Globalization;
using Usa.Simulation.Kernel;

namespace Usa.Game.Comms;

/// <summary>
/// Ids for one run. Command ids double as the AI request id, so a re-send dedupes in the proxy and in the command
/// processor. In the game they come from the injected provider, the session's <c>NextCommandId</c>, which console,
/// workspace and comms share so no two sources can hand out the same id. Other AI calls (transcription, narration) and
/// speech lines get <c>&lt;session&gt;-&lt;kind&gt;-&lt;sequence&gt;</c> from their own counters.
/// </summary>
public sealed class CommandIdAllocator
{
    private readonly Dictionary<string, long> _auxiliary = new(StringComparer.Ordinal);
    private readonly Func<CommandId>? _nextCommandId;
    private long _sequence;

    /// <summary>Standalone counter (<c>&lt;session&gt;-&lt;sequence&gt;</c>), for tests and tools without a session.</summary>
    /// <param name="lastSequence">The highest command sequence already used (when resuming a saved session).</param>
    public CommandIdAllocator(string sessionId, long lastSequence = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentOutOfRangeException.ThrowIfNegative(lastSequence);
        SessionId = sessionId;
        _sequence = lastSequence;
    }

    /// <param name="sessionId">Prefix of the auxiliary ids: the session's run-tagged command-id prefix.</param>
    /// <param name="nextCommandId">The run's single command-id source (<c>MissionSession.NextCommandId</c>).</param>
    public CommandIdAllocator(string sessionId, Func<CommandId> nextCommandId)
        : this(sessionId)
    {
        ArgumentNullException.ThrowIfNull(nextCommandId);
        _nextCommandId = nextCommandId;
    }

    public string SessionId { get; }

    /// <summary>The standalone counter's last sequence; 0 when ids come from the injected provider.</summary>
    public long LastSequence => _sequence;

    public CommandId Next() => _nextCommandId?.Invoke() ?? CommandId.Create(SessionId, ++_sequence);

    /// <param name="kind">Short lower-case tag such as <c>stt</c>, <c>narrate</c> or <c>line</c>.</param>
    public string NextAuxiliary(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        var next = _auxiliary.GetValueOrDefault(kind) + 1;
        _auxiliary[kind] = next;
        return string.Create(CultureInfo.InvariantCulture, $"{SessionId}-{kind}-{next:D6}");
    }
}
