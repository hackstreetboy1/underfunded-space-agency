using Usa.Game.Services.Ai.Contracts;
using Usa.Simulation.Commands;
using Usa.Simulation.Kernel;

namespace Usa.Game.Comms;

/// <summary>
/// One logical AI call. Its request object is built once and any re-send uses it unchanged, so the id and the
/// payload match and the proxy answers from its cache. <see cref="Attempt"/> tells a stale completion from the current one.
/// </summary>
internal abstract class CommsExchange(string id) : IDisposable
{
    public string Id { get; } = id;

    public int Attempt { get; set; }

    /// <summary>Waiting for the freeze (or the outage) to end.</summary>
    public bool Held { get; set; }

    /// <summary>The current attempt's call has not completed yet.</summary>
    public bool InFlight { get; set; }

    /// <summary>The connection dropped while the call was in flight: send it again after the freeze instead of waiting.</summary>
    public bool ResendOnResume { get; set; }

    /// <summary>A completion that arrived while frozen, applied when the freeze ends instead of asking again.</summary>
    public Action? HeldCompletion { get; set; }

    public bool Cancelled { get; private set; }

    public CancellationTokenSource Cancellation { get; } = new();

    public void Cancel()
    {
        if (Cancelled) return;
        Cancelled = true;
        Cancellation.Cancel();
    }

    public void Dispose() => Cancellation.Dispose();
}

internal sealed class TranscriptionExchange(string id, ReadOnlyMemory<byte> wav, CrewId? addressee) : CommsExchange(id)
{
    public ReadOnlyMemory<byte> Wav { get; } = wav;

    public CrewId? Addressee { get; } = addressee;
}

internal sealed class InterpretExchange(InterpretRequest request, CommandId command, CommandSource source, CrewId? addressee, string pendingMessageId)
    : CommsExchange(request.RequestId)
{
    public InterpretRequest Request { get; } = request;

    public CommandId Command { get; } = command;

    public CommandSource Source { get; } = source;

    public CrewId? Addressee { get; } = addressee;

    public string PendingMessageId { get; } = pendingMessageId;
}

internal sealed class NarrationExchange(
    NarrateRequest request,
    CrewId speaker,
    IReadOnlyList<FactId> approvedFacts,
    string fallbackTemplate,
    TranscriptMessageKind kind,
    CommsSpeechPriority priority) : CommsExchange(request.RequestId)
{
    public NarrateRequest Request { get; } = request;

    public CrewId Speaker { get; } = speaker;

    public IReadOnlyList<FactId> ApprovedFacts { get; } = approvedFacts;

    public string FallbackTemplate { get; } = fallbackTemplate;

    public TranscriptMessageKind Kind { get; } = kind;

    public CommsSpeechPriority Priority { get; } = priority;
}
