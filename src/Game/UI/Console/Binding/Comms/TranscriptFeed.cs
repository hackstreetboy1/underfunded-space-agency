using Usa.Game.Comms;
using Usa.Game.Presentation.ViewModels;
using Usa.Simulation.Kernel;

namespace Usa.Game.UI.Console.Binding.Comms;

/// <summary>
/// The comms transcript as display rows, kept in step with <see cref="Transcript.Changed"/>, with the <c>Ctrl+F</c> search.
/// Player rows name the crew member the message went to (<see cref="TranscriptMessage.Addressee"/>, fixed when it was sent).
/// Dispose to detach.
/// </summary>
public sealed class TranscriptFeed : IDisposable
{
    private readonly Transcript _transcript;
    private readonly CommsWords _words;
    private readonly Func<CrewId, string?> _crewName;

    private readonly ObservableList<TranscriptRowModel> _rows = new();
    private string _search = string.Empty;
    private bool _attached;

    /// <param name="crewName">The display name of an addressee (the selector's wording); null, or a null result, shows the id.</param>
    public TranscriptFeed(Transcript transcript, CommsWords words, Func<CrewId, string?>? crewName = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(words);
        _transcript = transcript;
        _words = words;
        _crewName = crewName ?? (_ => null);
        transcript.Changed += OnChanged;
        _attached = true;
        Rebuild();
    }

    /// <summary>Rows in transcript order (only the search hits while searching).</summary>
    public ObservableList<TranscriptRowModel> Rows => _rows;

    public string Search => _search;

    /// <summary>True while the panel itself sends: player messages that arrive then are its own, not an unreviewed voice order.</summary>
    public bool Sending { get; set; }

    /// <summary>
    /// A voice order the panel did not send itself arrived (push-to-talk, sent without review): its request id and text, for
    /// the "Undo (2 s)" chip.
    /// </summary>
    public event Action<string, string>? VoiceSent;

    public void SetSearch(string? query)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed == _search) return;
        _search = trimmed;
        Rebuild();
    }

    public void Dispose()
    {
        if (!_attached) return;
        _attached = false;
        _transcript.Changed -= OnChanged;
    }

    private void OnChanged(TranscriptChange change)
    {
        if (change.Kind == TranscriptChangeKind.Removed)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Id != change.Message.Id) continue;
                _rows.RemoveAt(i);
                return;
            }
            return;
        }
        var message = change.Message;
        if (!Sending && message.Kind == TranscriptMessageKind.PlayerVoice && message.RequestId is { } requestId)
            VoiceSent?.Invoke(requestId, message.Text);
        if (_search.Length > 0 && !Matches(message)) return;
        _rows.Add(ToRow(message));
    }

    private void Rebuild()
    {
        var source = _search.Length == 0 ? _transcript.Messages : _transcript.Search(_search);
        var rows = new List<TranscriptRowModel>(source.Count);
        foreach (var message in source) rows.Add(ToRow(message));
        _rows.ResetTo(rows);
    }

    private bool Matches(TranscriptMessage message)
    {
        foreach (var hit in _transcript.Search(_search))
        {
            if (hit.Id == message.Id) return true;
        }
        return false;
    }

    private TranscriptRowModel ToRow(TranscriptMessage message)
    {
        var addressee = message.Addressee is { } crew ? _crewName(crew) ?? crew.Value : null;
        return TranscriptRowModel.From(message, addressee, _words);
    }
}
