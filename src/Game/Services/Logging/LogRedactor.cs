using System.Text.RegularExpressions;

namespace Usa.Game.Services.Logging;

/// <summary>
/// Last line of defence for the "no secrets, audio or prompt bodies in logs" rule (SCRIPTING.md 18).
/// Callers must still never pass bodies; this scrubs key-shaped tokens and encoded blobs that slip through.
/// </summary>
public static partial class LogRedactor
{
    public const int MaxMessageLength = 600;

    public static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;

        var result = message.ReplaceLineEndings(" ");
        result = GoogleApiKey().Replace(result, "[redacted]");
        result = GoogleAccessKey().Replace(result, "[redacted]");
        result = ElevenLabsKey().Replace(result, "[redacted]");
        result = SecretAssignment().Replace(result, "$1=[redacted]");
        result = EncodedBlob().Replace(result, "[blob]");
        return result.Length <= MaxMessageLength ? result : string.Concat(result.AsSpan(0, MaxMessageLength), "…");
    }

    [GeneratedRegex(@"AIza[0-9A-Za-z_\-]{20,}")]
    private static partial Regex GoogleApiKey();

    // AQ.-format keys may contain '-' and '_', which split them into runs too short for EncodedBlob.
    [GeneratedRegex(@"\bAQ\.[0-9A-Za-z_\-]{20,}")]
    private static partial Regex GoogleAccessKey();

    [GeneratedRegex(@"\bsk_[0-9A-Za-z]{20,}")]
    private static partial Regex ElevenLabsKey();

    // '_' is a word character, so \b would miss env-style names (GEMINI_API_KEY=, client_secret=, "refresh_token":):
    // the name instead starts at the beginning of a word and may carry a prefix (NASA_API_KEY, ACCESS_TOKEN) or a
    // _key suffix (SECRET_KEY), and must not run on into a longer word (token_count, tokens). The optional quote after
    // the name covers JSON ("api_key": "..."); the optional auth scheme keeps "Authorization: Bearer <token>" from
    // treating the scheme word as the secret and leaving the token behind.
    [GeneratedRegex(@"(?i)(?<![A-Za-z0-9_-])((?:[A-Za-z0-9]+[_-])*(?:api[_-]?key|xi-api-key|authorization|bearer|token|secret|password)(?:[_-]key)?)(?![A-Za-z0-9_-])[""']?\s*[:=]?\s*[""']?(?:(?:bearer|basic)\s+)?[^\s""',;]{6,}")]
    private static partial Regex SecretAssignment();

    // Unbroken base64/hex runs this long are audio, keys or payloads, never useful log text. '/' is left out so
    // file paths survive; base64 split at '/' still leaves long runs on average.
    [GeneratedRegex(@"[A-Za-z0-9+]{40,}={0,2}")]
    private static partial Regex EncodedBlob();
}
