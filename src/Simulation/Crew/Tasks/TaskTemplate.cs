using System.Globalization;
using System.Text;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Crew.Tasks;

/// <summary>
/// <c>{name}</c> placeholders in task templates, e.g. <c>tandem/rcs/ring_{ring}.armed</c> with ring = a
/// gives <c>tandem/rcs/ring_a.armed</c>. Names are lower-case snake_case.
/// </summary>
public static class TaskTemplate
{
    /// <summary>Collects placeholder names in order of appearance.</summary>
    /// <returns>False when a brace is unbalanced or a name is not snake_case.</returns>
    public static bool TryGetPlaceholders(string template, ICollection<string> names)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(names);
        var i = 0;
        while (i < template.Length)
        {
            var c = template[i];
            if (c == '}') return false;
            if (c != '{')
            {
                i++;
                continue;
            }
            var close = template.IndexOf('}', i + 1);
            if (close < 0) return false;
            var name = template[(i + 1)..close];
            if (!IsName(name)) return false;
            names.Add(name);
            i = close + 1;
        }
        return true;
    }

    /// <summary>Replaces every placeholder using <paramref name="values"/>.</summary>
    /// <exception cref="ArgumentException">A placeholder has no value or the template is malformed.</exception>
    public static string Substitute(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        if (!template.Contains('{', StringComparison.Ordinal) && !template.Contains('}', StringComparison.Ordinal)) return template;

        var result = new StringBuilder(template.Length + 16);
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '}') throw new ArgumentException($"Unbalanced '}}' in template \"{template}\".", nameof(template));
            if (c != '{')
            {
                result.Append(c);
                continue;
            }
            var close = template.IndexOf('}', i + 1);
            if (close < 0) throw new ArgumentException($"Unclosed '{{' in template \"{template}\".", nameof(template));
            var name = template[(i + 1)..close];
            if (!values.TryGetValue(name, out var value))
                throw new ArgumentException($"No value for placeholder '{{{name}}}' in \"{template}\".", nameof(values));
            result.Append(value);
            i = close;
        }
        return result.ToString();
    }

    /// <summary>Text form of an argument inside a template: numbers invariant, booleans true/false, text as is.</summary>
    public static string Format(StateValue value) => value.Kind switch
    {
        StateValueKind.Number => value.AsNumber().ToString("R", CultureInfo.InvariantCulture),
        StateValueKind.Bool => value.AsBool() ? "true" : "false",
        StateValueKind.Text => value.AsText(),
        _ => "",
    };

    internal static bool IsName(string name)
    {
        if (name.Length == 0 || name[0] is < 'a' or > 'z') return false;
        foreach (var c in name)
        {
            if (c is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')) return false;
        }
        return true;
    }
}
