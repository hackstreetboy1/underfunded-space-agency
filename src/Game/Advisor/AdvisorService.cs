using Usa.Game.Content;
using Usa.Game.Services.Ai;
using Usa.Game.Services.Ai.Contracts;

namespace Usa.Game.Advisor;

/// <summary>
/// "Ask the engineers" (GDD 9.2, E.17): explains science and equipment from the encyclopedia and never recommends
/// what to do. Sends only the question and the top excerpts (<see cref="ExcerptRetriever"/>), validates the answer
/// (<see cref="AdvisorAnswerValidator"/>), and on any failure, timeout or rejection answers deterministically from the
/// best excerpt. One question in flight at a time; the same question again within the duplicate window is not sent and
/// gets the reply it had (<see cref="AdvisorReplyKind.Duplicate"/>).
/// Continuations run on the caller's synchronization context. Dispose when the mission stops: the question in flight is
/// cancelled and later ones are answered <see cref="AdvisorReplyKind.Cancelled"/>.
/// </summary>
public sealed class AdvisorService : IDisposable
{
    private readonly ExcerptRetriever _retriever;
    private readonly IExplainClient _client;
    private readonly AdvisorAnswerValidator _validator;
    private readonly AdvisorOptions _options;
    private readonly TimeProvider _time;
    private readonly Func<string> _newRequestId;
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _inFlight;
    private bool _disposed;
    private string? _lastQuestionKey;
    private long _lastQuestionTimestamp;
    private AdvisorAnswer? _lastAnswer;

    /// <param name="newRequestId">Test seam; defaults to a fresh GUID per question.</param>
    public AdvisorService(
        ExcerptRetriever retriever,
        IExplainClient client,
        AdvisorAnswerValidator validator,
        AdvisorOptions? options = null,
        TimeProvider? time = null,
        Func<string>? newRequestId = null)
    {
        ArgumentNullException.ThrowIfNull(retriever);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(validator);
        _retriever = retriever;
        _client = client;
        _validator = validator;
        _options = options ?? AdvisorOptions.Default;
        _time = time ?? TimeProvider.System;
        _newRequestId = newRequestId ?? (static () => Guid.NewGuid().ToString("N"));
    }

    public bool IsBusy
    {
        get
        {
            lock (_gate) return _inFlight;
        }
    }

    public async Task<AdvisorReply> AskAsync(string? question, CancellationToken cancellationToken = default)
    {
        var text = CollapseWhitespace(question);
        if (text.Length == 0) return new AdvisorReply(AdvisorReplyKind.Empty);
        if (text.Length > AdvisorOptions.MaxQuestionChars) text = text[..AdvisorOptions.MaxQuestionChars].TrimEnd();

        var admitted = Admit(QuestionKey(text), out var previous);
        if (admitted is { } ignored) return new AdvisorReply(ignored, previous);
        AdvisorReply? reply = null;
        try
        {
            using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            reply = await AnswerAsync(text, call.Token);
            return reply;
        }
        finally
        {
            lock (_gate)
            {
                _inFlight = false;
                // A cancelled or failed ask leaves nothing to repeat, so asking again goes through.
                if (reply is null || reply.Kind == AdvisorReplyKind.Cancelled) _lastQuestionKey = null;
                _lastAnswer = reply?.Answer;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    /// <summary>The deterministic answer from one entry: its definition, body and first source line.</summary>
    public static AdvisorAnswer Fallback(EncyclopediaEntry entry, Encyclopedia encyclopedia)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(encyclopedia);
        var text = $"{entry.FirstUse}\n\n{entry.Body}";
        if (entry.Sources.Count > 0) text += $"\n\n{SourceLine(entry.Sources[0])}";
        return new AdvisorAnswer(text, [ToSource(entry, encyclopedia)], IsFallback: true);
    }

    /// <summary>"Author, Title, Publisher", or "Title, Publisher" without an author (the encyclopedia sheet's source line).</summary>
    public static string SourceLine(EncyclopediaSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Author is null ? $"{source.Title}, {source.Publisher}" : $"{source.Author}, {source.Title}, {source.Publisher}";
    }

    /// <summary>Null when admitted (and now in flight); otherwise why the question is ignored.</summary>
    /// <param name="previous">For a duplicate, the answer that question got (null when nothing covered it).</param>
    private AdvisorReplyKind? Admit(string key, out AdvisorAnswer? previous)
    {
        previous = null;
        lock (_gate)
        {
            if (_disposed) return AdvisorReplyKind.Cancelled;
            if (_inFlight) return AdvisorReplyKind.Busy;
            var now = _time.GetTimestamp();
            if (string.Equals(key, _lastQuestionKey, StringComparison.Ordinal)
                && _time.GetElapsedTime(_lastQuestionTimestamp, now) < _options.DuplicateWindow)
            {
                previous = _lastAnswer;
                return AdvisorReplyKind.Duplicate;
            }
            _inFlight = true;
            _lastQuestionKey = key;
            _lastQuestionTimestamp = now;
            return null;
        }
    }

    private async Task<AdvisorReply> AnswerAsync(string question, CancellationToken cancellationToken)
    {
        var entries = _retriever.Retrieve(question);
        if (entries.Count == 0) return new AdvisorReply(AdvisorReplyKind.NotCovered);

        var excerpts = entries.Select(ExcerptRetriever.ToExcerpt).ToArray();
        var request = new ExplainRequest(_newRequestId(), question) { Excerpts = excerpts };
        var encyclopedia = _retriever.Encyclopedia;

        using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        AiResult<ExplainResponse> result;
        try
        {
            result = await _client.ExplainAsync(request, call.Token).WaitAsync(_options.Timeout, _time, cancellationToken);
        }
        catch (TimeoutException)
        {
            await call.CancelAsync();
            return FallbackReply(entries[0], encyclopedia, AdvisorRejectionCodes.Timeout);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new AdvisorReply(AdvisorReplyKind.Cancelled);
        }

        if (!result.IsSuccess)
        {
            return cancellationToken.IsCancellationRequested
                ? new AdvisorReply(AdvisorReplyKind.Cancelled)
                : FallbackReply(entries[0], encyclopedia, result.Error.ErrorCode);
        }

        var response = result.Value;
        var rejections = _validator.Validate(response, excerpts);
        // A radio habit alone ("CAPCOM, rate damping ...") should not cost a good answer: drop it and judge again.
        if (AdvisorAnswerValidator.OnlyCallSignOpener(rejections) &&
            AdvisorAnswerValidator.WithoutCallSignOpener(response.Answer) is { } stripped)
        {
            response = response with { Answer = stripped };
            rejections = _validator.Validate(response, excerpts);
        }
        if (rejections.Count > 0) return FallbackReply(entries[0], encyclopedia, string.Join("; ", rejections));

        var sources = new List<AdvisorSource>();
        foreach (var id in (response.Sources ?? []).Distinct(StringComparer.Ordinal))
        {
            if (encyclopedia.TryGet(id, out var entry)) sources.Add(ToSource(entry, encyclopedia));
        }
        return new AdvisorReply(AdvisorReplyKind.Answered, new AdvisorAnswer(response.Answer.Trim(), sources, IsFallback: false));
    }

    private static AdvisorReply FallbackReply(EncyclopediaEntry best, Encyclopedia encyclopedia, string detail) =>
        new(AdvisorReplyKind.Answered, Fallback(best, encyclopedia), detail);

    private static AdvisorSource ToSource(EncyclopediaEntry entry, Encyclopedia encyclopedia) =>
        new(entry.Id, entry.Title, encyclopedia.EraLabel(entry.Era));

    private static string CollapseWhitespace(string? text) =>
        string.Join(' ', (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>"What is Delta-v?" and "what is delta-v" are the same question.</summary>
    private static string QuestionKey(string text) => text.TrimEnd('?', '.', '!', ' ').ToUpperInvariant();
}
