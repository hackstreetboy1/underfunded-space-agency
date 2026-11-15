using System.Globalization;

namespace Usa.Game.UI.Frontend.Model;

/// <summary>Position in the briefing image carousel; Previous and Next wrap around.</summary>
public readonly record struct CarouselCursor(int Index, int Count)
{
    public static CarouselCursor Start(int count) => new(0, Math.Max(count, 0));

    public bool IsEmpty => Count == 0;

    public CarouselCursor Next() => IsEmpty ? this : this with { Index = (Index + 1) % Count };

    public CarouselCursor Previous() => IsEmpty ? this : this with { Index = (Index - 1 + Count) % Count };

    /// <summary>One-based position for "2 / 4"; 0 when empty.</summary>
    public int Ordinal => IsEmpty ? 0 : Index + 1;

    public string OrdinalText => Ordinal.ToString(CultureInfo.InvariantCulture);

    public string CountText => Count.ToString(CultureInfo.InvariantCulture);
}
