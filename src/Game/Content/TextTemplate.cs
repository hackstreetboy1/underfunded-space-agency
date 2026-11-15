using System.Text;

namespace Usa.Game.Content;

/// <summary>Fills <c>{name}</c> placeholders in authored text. Braces around anything that is not a name stay literal.</summary>
public static class TextTemplate
{
    /// <summary>Shown in place of a placeholder that has no value, so a gap never breaks the screen.</summary>
    public const string MissingValue = "—";

    /// <summary>Placeholder names (without braces) in order of first appearance.</summary>
    public static IReadOnlyList<string> Placeholders(string template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var names = new List<string>();
        var index = 0;
        while (TryNext(template, ref index, out var start, out var end))
        {
            var name = template[(start + 1)..end];
            if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
        }
        return names;
    }

    /// <param name="resolve">The value for a placeholder name (without braces), or null when it has none.</param>
    /// <param name="onMissing">Receives every placeholder name that had no value; it is replaced by <see cref="MissingValue"/>.</param>
    public static string Fill(string template, Func<string, string?> resolve, Action<string>? onMissing = null)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(resolve);
        if (!template.Contains('{', StringComparison.Ordinal)) return template;

        var builder = new StringBuilder(template.Length + 16);
        var index = 0;
        var copied = 0;
        while (TryNext(template, ref index, out var start, out var end))
        {
            builder.Append(template, copied, start - copied);
            var name = template[(start + 1)..end];
            var value = resolve(name);
            if (value is null)
            {
                onMissing?.Invoke(name);
                value = MissingValue;
            }
            builder.Append(value);
            copied = end + 1;
        }
        builder.Append(template, copied, template.Length - copied);
        return builder.ToString();
    }

    public static string Fill(string template, IReadOnlyDictionary<string, string> values, Action<string>? onMissing = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Fill(template, name => values.TryGetValue(name, out var value) ? value : null, onMissing);
    }

    private static bool TryNext(string template, ref int index, out int start, out int end)
    {
        while (index < template.Length)
        {
            start = template.IndexOf('{', index);
            if (start < 0) break;
            end = template.IndexOf('}', start + 1);
            if (end < 0) break;
            if (IsName(template.AsSpan(start + 1, end - start - 1)))
            {
                index = end + 1;
                return true;
            }
            index = start + 1;
        }
        start = -1;
        end = -1;
        index = template.Length;
        return false;
    }

    private static bool IsName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty || !char.IsAsciiLetter(name[0])) return false;
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_') return false;
        }
        return true;
    }
}
