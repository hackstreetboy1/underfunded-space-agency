namespace Usa.Game.Services.Ai.Contracts;

/// <summary>
/// Response of <c>POST /v1/stt</c> (multipart: <c>audio</c> WAV file + <c>request_id</c> form field).
/// <see cref="Confidence"/> is the mean word probability; force a transcript review when
/// <see cref="MinWordConfidence"/> is below <see cref="ReviewThreshold"/>.
/// </summary>
public sealed record SttResponse(string Transcript, double Confidence, double MinWordConfidence)
{
    public const double ReviewThreshold = 0.6;

    /// <summary>Words below the threshold, in spoken order, for highlighting in the transcript editor.</summary>
    public IReadOnlyList<string> LowConfidenceWords { get; init; } = [];

    // Derived on the client; not part of the wire contract.
    [System.Text.Json.Serialization.JsonIgnore]
    public bool NeedsReview => MinWordConfidence < ReviewThreshold;
}

/// <summary>
/// <c>POST /v1/tts</c>; the response body is streamed <c>audio/mpeg</c>. Text 1-1000 characters.
/// <see cref="Cacheable"/> marks a fixed phrase (acknowledgement, greeting) for the proxy's on-disk phrase cache.
/// </summary>
public sealed record TtsRequest(string RequestId, string VoiceId, string Text) : IAiRequest
{
    public bool Cacheable { get; init; }
}
