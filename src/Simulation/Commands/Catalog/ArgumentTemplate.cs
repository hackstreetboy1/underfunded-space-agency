using System.Text;

namespace Usa.Simulation.Commands.Catalog;

/// <summary>Text with <c>{param}</c> placeholders, parsed once at load.</summary>
internal sealed class ArgumentTemplate
{
    private readonly string[] _literals;
    private readonly string[] _names;

    private ArgumentTemplate(string source, string[] literals, string[] names)
    {
        Source = source;
        _literals = literals;
        _names = names;
    }

    public string Source { get; }

    public IReadOnlyList<string> Placeholders => _names;

    /// <exception cref="FormatException">Unbalanced braces or an invalid placeholder name.</exception>
    public static ArgumentTemplate Parse(string source)
    {
        var literals = new List<string>();
        var names = new List<string>();
        var start = 0;
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] == '}') throw new FormatException($"Unmatched '}}' at column {i}.");
            if (source[i] != '{') continue;

            var close = source.IndexOf('}', i + 1);
            if (close < 0) throw new FormatException($"Unclosed '{{' at column {i}.");
            var name = source[(i + 1)..close];
            if (!IsParameterName(name)) throw new FormatException($"Invalid placeholder '{{{name}}}' at column {i}.");
            literals.Add(source[start..i]);
            names.Add(name);
            i = close;
            start = close + 1;
        }
        literals.Add(source[start..]);
        return new ArgumentTemplate(source, [.. literals], [.. names]);
    }

    public static bool IsParameterName(string name)
    {
        if (name.Length == 0) return false;
        foreach (var c in name)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')) return false;
        }
        return true;
    }

    public string Render(IReadOnlyDictionary<string, string> values)
    {
        if (_names.Length == 0) return Source;
        var builder = new StringBuilder(_literals[0]);
        for (var i = 0; i < _names.Length; i++) builder.Append(values[_names[i]]).Append(_literals[i + 1]);
        return builder.ToString();
    }

    /// <summary>Every rendering over the cartesian product of the placeholder domains.</summary>
    public IReadOnlyList<string> Expand(IReadOnlyDictionary<string, IReadOnlyList<string>> domains)
    {
        var distinct = _names.Distinct(StringComparer.Ordinal).ToArray();
        var results = new List<string>();
        var assignment = new Dictionary<string, string>(StringComparer.Ordinal);
        Assign(0);
        return results;

        void Assign(int index)
        {
            if (index == distinct.Length)
            {
                results.Add(Render(assignment));
                return;
            }
            foreach (var value in domains[distinct[index]])
            {
                assignment[distinct[index]] = value;
                Assign(index + 1);
            }
        }
    }
}
