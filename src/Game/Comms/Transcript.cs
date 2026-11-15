using System.Globalization;

namespace Usa.Game.Comms;

/// <summary>The comms transcript: append-only except for pending placeholders, searchable, with stable ids.</summary>
public sealed class Transcript
{
	private readonly List<TranscriptMessage> _messages = [];
	private long _sequence;

	public event Action<TranscriptChange>? Changed;

	/// <summary>In the order they were added.</summary>
	public IReadOnlyList<TranscriptMessage> Messages => _messages;

	/// <summary>Adds <paramref name="draft"/> with a new id (<c>msg-000001</c>, …) and returns the stored message.</summary>
	public TranscriptMessage Append(TranscriptMessage draft)
	{
		ArgumentNullException.ThrowIfNull(draft);
		var message = draft with { Id = string.Create(CultureInfo.InvariantCulture, $"msg-{++_sequence:D6}") };
		_messages.Add(message);
		Changed?.Invoke(new TranscriptChange(TranscriptChangeKind.Added, message));
		return message;
	}

	public bool Remove(string id)
	{
		var index = _messages.FindIndex(m => string.Equals(m.Id, id, StringComparison.Ordinal));
		if (index < 0) return false;
		var message = _messages[index];
		_messages.RemoveAt(index);
		Changed?.Invoke(new TranscriptChange(TranscriptChangeKind.Removed, message));
		return true;
	}

	public TranscriptMessage? Find(string id) => _messages.Find(m => string.Equals(m.Id, id, StringComparison.Ordinal));

	/// <summary>
	/// Messages whose text, speaker or refusal reason contains <paramref name="query"/> (case-insensitive), oldest
	/// first. Pending placeholders are not searchable.
	/// </summary>
	public IReadOnlyList<TranscriptMessage> Search(string query)
	{
		ArgumentNullException.ThrowIfNull(query);
		var needle = query.Trim();
		if (needle.Length == 0) return [];
		var hits = new List<TranscriptMessage>();
		foreach (var message in _messages)
		{
			if (message.Kind == TranscriptMessageKind.Pending) continue;
			if (Contains(message.Text, needle) || Contains(message.SpeakerName, needle) || Contains(message.ReasonText, needle))
				hits.Add(message);
		}
		return hits;
	}

	private static bool Contains(string? text, string needle) =>
		text is not null && text.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
