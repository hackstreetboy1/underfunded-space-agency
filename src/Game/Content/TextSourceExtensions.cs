namespace Usa.Game.Content;

public static class TextSourceExtensions
{
    /// <summary><see cref="ITextSource.Get"/> with <c>{name}</c> placeholders filled from name/value pairs.</summary>
    public static string Format(this ITextSource text, string key, params (string Name, string Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(values);
        return TextTemplate.Fill(text.Get(key), name =>
        {
            foreach (var (candidate, value) in values)
            {
                if (string.Equals(candidate, name, StringComparison.Ordinal)) return value;
            }
            return null;
        });
    }
}
