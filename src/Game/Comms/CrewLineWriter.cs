using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;

namespace Usa.Game.Comms;

/// <summary>
/// Turns a template into a transcript message and a speech request: resolves <c>&lt;FACT:id&gt;</c> tokens from the
/// speaker's own observations, falls back to the safe "can't verify" line when a token can't resolve, and picks
/// crew openers round-robin so repeated acknowledgements vary deterministically. The transcript and caption get the full
/// text; text-to-speech gets only the spoken half (<see cref="SpokenLineComposer"/>), a short radio call.
/// </summary>
internal sealed class CrewLineWriter(ICommsSessionPort session, CommsContent content, FactCatalog facts, Transcript transcript, CommandIdAllocator ids)
{
    private readonly Dictionary<(CrewId, CrewPhraseCategory), int> _nextPhrase = [];
    private readonly SpokenLineComposer _spoken = new(content, facts);

    public event Action<CommsSpeechRequest>? Speak;

    public event Action<string>? Diagnostic;

    public string Phrase(CrewId crew, CrewPhraseCategory category)
    {
        var phrases = content.Phrases(crew, category);
        if (phrases.Count == 0) return content.Line(CommsLineKeys.DefaultPhrase(category));
        var index = _nextPhrase.GetValueOrDefault((crew, category));
        _nextPhrase[(crew, category)] = index + 1;
        return phrases[index % phrases.Count].Text;
    }

    /// <summary>One <c>fact_item</c> line per fact, joined; empty when there are no facts.</summary>
    public string FactLines(IReadOnlyList<FactId> factIds)
    {
        var lines = new List<string>(factIds.Count);
        foreach (var fact in factIds)
        {
            var label = facts.TryResolve(fact, out var definition) ? definition.Label : fact.Value;
            lines.Add(CommsContent.Fill(content.Line(CommsLineKeys.FactItem), ("label", label), ("fact", CommsContent.FactToken(fact))));
        }
        return string.Join(' ', lines);
    }

    /// <param name="cacheable">The text is fixed authored content (no facts), so TTS may come from the phrase cache.</param>
    /// <param name="data">Readings shown after the line, one <c>fact_item</c> each; spoken only as a short summary.</param>
    /// <param name="shortSpoken">Said instead when the spoken line would be over the cap (a refusal's reason without its opener).</param>
    public TranscriptMessage Say(
        CrewId speaker,
        TranscriptMessageKind kind,
        string template,
        CommsSpeechPriority priority = CommsSpeechPriority.Normal,
        bool cacheable = false,
        string? requestId = null,
        Func<TranscriptMessage, TranscriptMessage>? decorate = null,
        IReadOnlyList<FactId>? data = null,
        string? shortSpoken = null)
    {
        data ??= [];
        var full = data.Count == 0 ? template : $"{template.Trim()} {FactLines(data)}".Trim();
        var resolved = session.ResolveFacts(full, speaker);
        if (!resolved.Succeeded)
        {
            Diagnostic?.Invoke($"fact resolution failed for {speaker}: {string.Join("; ", resolved.Errors)}");
            template = full = content.Line(CommsLineKeys.CannotVerify);
            data = [];
            shortSpoken = null;
            resolved = session.ResolveFacts(template, speaker);
            cacheable = true;
        }

        var spoken = _spoken.Compose(template, resolved.Facts, Math.Min(data.Count, resolved.Facts.Count), shortSpoken);
        if (spoken.Trimmed) Diagnostic?.Invoke($"spoken line for {speaker} trimmed to {spoken.Text.Length} characters: {spoken.Text}");

        content.TryGetProfile(speaker, out var profile);
        var draft = TranscriptMessage.Draft(kind, session.Now, resolved.Text) with
        {
            Speaker = speaker,
            SpeakerName = profile?.Name ?? speaker.Value,
            RequestId = requestId,
            Template = full,
            Facts = resolved.Facts,
            SpokenText = spoken.Text,
        };
        var message = transcript.Append(decorate is null ? draft : decorate(draft));
        Speak?.Invoke(new CommsSpeechRequest(
            ids.NextAuxiliary("line"),
            speaker,
            message.SpeakerName ?? speaker.Value,
            message.Text,
            profile?.VoiceId ?? "",
            priority,
            (cacheable && resolved.Facts.Count == 0) || (resolved.Facts.Count > 0 && !spoken.IncludesValues),
            message.Id,
            message.SpokenText ?? spoken.Text));
        return message;
    }

    public TranscriptMessage System(string text, string? requestId = null) =>
        transcript.Append(TranscriptMessage.Draft(TranscriptMessageKind.System, session.Now, text) with { RequestId = requestId });
}
