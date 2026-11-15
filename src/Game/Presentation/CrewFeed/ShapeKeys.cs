namespace Usa.Game.Presentation.CrewFeed;

public static class ShapeKeys
{
    public const int Count = 11;

    /// <summary>Blend-shape names exactly as exported in the crew GLBs, indexed by <see cref="ShapeKey"/>.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "blink", "eyes_wide", "eyes_squint", "brow_up", "brow_down", "brow_worry",
        "mouth_open", "mouth_o", "mouth_flat", "mouth_frown", "mouth_smile_big",
    ];

    public static string Name(ShapeKey key) => Names[(int)key];

    public static bool TryParse(string name, out ShapeKey key)
    {
        for (var i = 0; i < Count; i++)
        {
            if (string.Equals(Names[i], name, StringComparison.Ordinal))
            {
                key = (ShapeKey)i;
                return true;
            }
        }

        key = default;
        return false;
    }
}
