using System.Globalization;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge.Catalog;

/// <summary>An open fact pattern such as <c>component.{component}.condition</c>, resolved per id on demand.</summary>
internal sealed class FactPattern
{
    private readonly FactEntry _entry;
    private readonly string _placeholder;

    public FactPattern(FactEntry entry, int entryIndex)
    {
        _entry = entry;
        EntryIndex = entryIndex;
        Kind = entry.Matcher!.Kind;
        _placeholder = PlaceholderFor(Kind);
        var at = entry.Id.IndexOf(_placeholder, StringComparison.Ordinal);
        Prefix = entry.Id[..at];
        Suffix = entry.Id[(at + _placeholder.Length)..];
    }

    public FactMatcherKind Kind { get; }

    public int EntryIndex { get; }

    public string Id => _entry.Id;

    public string Prefix { get; }

    public string Suffix { get; }

    public static string PlaceholderFor(FactMatcherKind kind) => kind switch
    {
        FactMatcherKind.Component => "{component}",
        FactMatcherKind.Crew => "{crew}",
        _ => "{nn}",
    };

    public bool Matches(ReadOnlySpan<char> factId)
    {
        if (factId.Length <= Prefix.Length + Suffix.Length) return false;
        if (!factId.StartsWith(Prefix, StringComparison.Ordinal) || !factId.EndsWith(Suffix, StringComparison.Ordinal)) return false;
        return IsValidSubject(factId[Prefix.Length..^Suffix.Length]);
    }

    /// <summary>Builds the concrete id for a subject (component or crew id).</summary>
    public string IdFor(string subject) => string.Concat(Prefix, subject, Suffix);

    public FactDefinition Resolve(FactId factId)
    {
        var subject = factId.Value[Prefix.Length..^Suffix.Length];
        return FactDefinitionFactory.Create(_entry, factId.Value, text => text.Replace(_placeholder, subject, StringComparison.Ordinal));
    }

    private bool IsValidSubject(ReadOnlySpan<char> subject)
    {
        var hasSlash = false;
        foreach (var c in subject)
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_') continue;
            if (Kind == FactMatcherKind.Component && c is '/' or '.' or '-')
            {
                hasSlash |= c == '/';
                continue;
            }
            return false;
        }
        return Kind != FactMatcherKind.Component || hasSlash;
    }

    public static string ExpandIndex(string text, int index) => text
        .Replace("{nn}", index.ToString("D2", CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{n}", index.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
