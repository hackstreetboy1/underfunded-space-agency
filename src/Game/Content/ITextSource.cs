namespace Usa.Game.Content;

/// <summary>
/// Player-facing text by dotted key (<c>&lt;section&gt;.&lt;key&gt;</c>). <see cref="StringTable"/> serves
/// <c>content/strings.json</c>; presentation modules take this interface so their wording lives in data.
/// </summary>
public interface ITextSource
{
    /// <exception cref="KeyNotFoundException">The key is not defined.</exception>
    string Get(string key);

    /// <summary><see cref="Get"/> with <c>{name}</c> placeholders filled; a missing value shows as <see cref="TextTemplate.MissingValue"/>.</summary>
    string Format(string key, IReadOnlyDictionary<string, string> values);
}
