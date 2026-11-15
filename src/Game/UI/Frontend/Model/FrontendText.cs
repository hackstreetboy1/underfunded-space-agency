using Usa.Game.Content;

namespace Usa.Game.UI.Frontend.Model;

/// <summary>
/// Front-end wording: <c>content/strings.json</c> first, then the approved Figma copy in <see cref="FrontendTextDefaults"/>
/// for keys the data does not define yet. Every key served from the fallback is listed in <see cref="MissingKeys"/>, so the
/// gaps can be moved into the data file. Main thread only.
/// </summary>
public sealed class FrontendText : ITextSource
{
    private readonly StringTable? _strings;
    private readonly IReadOnlyDictionary<string, string> _fallback;
    private readonly SortedSet<string> _missing = new(StringComparer.Ordinal);

    /// <param name="strings">Null serves the fallback only (tests, or before content loaded).</param>
    public FrontendText(StringTable? strings, IReadOnlyDictionary<string, string>? fallback = null)
    {
        _strings = strings;
        _fallback = fallback ?? FrontendTextDefaults.All;
    }

    /// <summary>Keys answered from the fallback so far, sorted.</summary>
    public IReadOnlyCollection<string> MissingKeys => _missing;

    /// <summary>The underlying table, for models that take a <see cref="StringTable"/>.</summary>
    public StringTable? Strings => _strings;

    /// <exception cref="KeyNotFoundException">Neither the data nor the fallback defines the key.</exception>
    public string Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_strings is not null && _strings.TryGet(key, out var value)) return value;
        if (_fallback.TryGetValue(key, out var fallback))
        {
            _missing.Add(key);
            return fallback;
        }
        throw new KeyNotFoundException($"Front-end string '{key}' is defined neither in {StringsDefinition.RelativePath} nor in {nameof(FrontendTextDefaults)}.");
    }

    public string Format(string key, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return TextTemplate.Fill(Get(key), values);
    }

    /// <summary><see cref="Get"/> with <c>{name}</c> placeholders filled from name/value pairs.</summary>
    public string Format(string key, params (string Name, string Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in values) map[name] = value;
        return Format(key, map);
    }

    /// <summary>The key's text, or <paramref name="otherwise"/> when it is defined nowhere (never throws).</summary>
    public string GetOr(string key, string otherwise)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_strings is not null && _strings.TryGet(key, out var value)) return value;
        return _fallback.TryGetValue(key, out var fallback) ? fallback : otherwise;
    }
}
