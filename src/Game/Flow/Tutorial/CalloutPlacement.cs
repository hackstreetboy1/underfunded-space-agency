namespace Usa.Game.Flow.Tutorial;

/// <summary>An axis-aligned rectangle in UI pixels (Godot-free stand-in for <c>Rect2</c>).</summary>
public readonly record struct LayoutRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    public bool Intersects(LayoutRect other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    public bool Contains(LayoutRect other) =>
        other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;
}

/// <summary>Where the callout sits relative to its anchor; the pointer arrow is on the opposite edge.</summary>
public enum CalloutSide
{
    Below,
    Above,
    Right,
    Left,
}

/// <param name="Docked">Parked in a fixed spot clear of the avoid rectangle: no pointer arrow and no ring.</param>
public readonly record struct CalloutPlacementResult(LayoutRect Bounds, CalloutSide Side, bool FitsViewport, bool Docked = false);

/// <summary>
/// Places a callout next to its anchor without covering it (UI.md 5.1): tries below, above, right, then left, and
/// keeps the first that fits the viewport. When none fits (an anchor that fills most of the screen), it takes the side
/// with the most room and keeps the callout fully on screen, overlapping the anchor's edge only as far as it must.
/// </summary>
public static class CalloutPlacement
{
    public const float GapPx = 12f;

    /// <summary>
    /// Like <see cref="Place(LayoutRect, float, float, LayoutRect)"/>, but first keeps the whole <paramref name="panel"/> the
    /// anchor belongs to clear (e.g. the systems board around an Ask button, so the readout beside it stays readable): the
    /// callout goes beside the panel on the side with the most room, lined up with the anchor. When no side of the panel
    /// has room, only the anchor itself is kept clear.
    /// </summary>
    public static CalloutPlacementResult Place(LayoutRect anchor, LayoutRect? panel, float calloutWidth, float calloutHeight, LayoutRect viewport)
    {
        if (panel is { } keep && keep.Contains(anchor))
        {
            var width = Math.Min(calloutWidth, TutorialCallouts.MaxWidthPx);
            var sides = Enum.GetValues<CalloutSide>().OrderByDescending(side => Room(keep, viewport, side));
            foreach (var side in sides)
            {
                var bounds = Beside(keep, anchor, width, calloutHeight, viewport, side);
                if (viewport.Contains(bounds) && !bounds.Intersects(keep)) return new CalloutPlacementResult(bounds, side, true);
            }
        }
        return Place(anchor, calloutWidth, calloutHeight, viewport);
    }


    /// <summary>
    /// Like <see cref="Place(LayoutRect, LayoutRect?, float, float, LayoutRect)"/>, but the callout never overlaps
    /// <paramref name="avoid"/> (the External 3D view while the player watches it). An anchor outside that rectangle keeps its
    /// pointer, on a side away from it; an anchor inside it (a wait step about the view itself) docks the callout beside the
    /// view instead (<see cref="Dock"/>).
    /// </summary>
    public static CalloutPlacementResult Place(
        LayoutRect anchor, LayoutRect? panel, float calloutWidth, float calloutHeight, LayoutRect viewport, LayoutRect? avoid)
    {
        if (avoid is not { } view) return Place(anchor, panel, calloutWidth, calloutHeight, viewport);
        var width = Math.Min(calloutWidth, TutorialCallouts.MaxWidthPx);
        if (!anchor.Intersects(view))
        {
            if (panel is { } keep && keep.Contains(anchor) && !keep.Intersects(view))
            {
                foreach (var side in Enum.GetValues<CalloutSide>().OrderByDescending(side => Room(keep, viewport, side)))
                {
                    var bounds = Beside(keep, anchor, width, calloutHeight, viewport, side);
                    if (viewport.Contains(bounds) && !bounds.Intersects(keep) && !bounds.Intersects(view))
                        return new CalloutPlacementResult(bounds, side, true);
                }
            }
            foreach (var side in Enum.GetValues<CalloutSide>())
            {
                var bounds = Candidate(anchor, width, calloutHeight, viewport, side);
                if (viewport.Contains(bounds) && !bounds.Intersects(anchor) && !bounds.Intersects(view))
                    return new CalloutPlacementResult(bounds, side, true);
            }
        }
        return Dock(view, width, calloutHeight, viewport);
    }

    /// <summary>
    /// The fixed spot for a callout that must stay off the view: below it (over the event log and task bar), else in the left
    /// column above comms, else the right column, else above it. When no side has room the one with the most is clamped on screen.
    /// </summary>
    public static CalloutPlacementResult Dock(LayoutRect view, float calloutWidth, float calloutHeight, LayoutRect viewport)
    {
        var width = Math.Min(calloutWidth, TutorialCallouts.MaxWidthPx);
        CalloutSide[] order = [CalloutSide.Below, CalloutSide.Left, CalloutSide.Right, CalloutSide.Above];
        foreach (var side in order)
        {
            var bounds = DockCandidate(view, width, calloutHeight, viewport, side);
            if (viewport.Contains(bounds) && !bounds.Intersects(view)) return new CalloutPlacementResult(bounds, side, true, Docked: true);
        }
        var best = order.OrderByDescending(side => Room(view, viewport, side)).First();
        return new CalloutPlacementResult(ClampInto(DockCandidate(view, width, calloutHeight, viewport, best), viewport), best, false, Docked: true);
    }

    private static LayoutRect DockCandidate(LayoutRect view, float width, float height, LayoutRect viewport, CalloutSide side)
    {
        switch (side)
        {
            case CalloutSide.Below:
            case CalloutSide.Above:
            {
                var x = Clamp(view.X, viewport.X, viewport.Right - width);
                var y = side == CalloutSide.Below ? view.Bottom + GapPx : view.Y - GapPx - height;
                return new LayoutRect(x, y, width, height);
            }
            default:
            {
                var y = Clamp(view.Y, viewport.Y, viewport.Bottom - height);
                var x = side == CalloutSide.Right ? view.Right + GapPx : view.X - GapPx - width;
                return new LayoutRect(x, y, width, height);
            }
        }
    }

    // Next to the panel, but aligned with the anchor along the panel's edge so the arrow still points at it.
    private static LayoutRect Beside(LayoutRect panel, LayoutRect anchor, float width, float height, LayoutRect viewport, CalloutSide side)
    {
        switch (side)
        {
            case CalloutSide.Below:
            case CalloutSide.Above:
            {
                var x = Clamp(anchor.X + (anchor.Width - width) / 2f, viewport.X, viewport.Right - width);
                var y = side == CalloutSide.Below ? panel.Bottom + GapPx : panel.Y - GapPx - height;
                return new LayoutRect(x, y, width, height);
            }
            default:
            {
                var y = Clamp(anchor.Y + (anchor.Height - height) / 2f, viewport.Y, viewport.Bottom - height);
                var x = side == CalloutSide.Right ? panel.Right + GapPx : panel.X - GapPx - width;
                return new LayoutRect(x, y, width, height);
            }
        }
    }

    public static CalloutPlacementResult Place(LayoutRect anchor, float calloutWidth, float calloutHeight, LayoutRect viewport)
    {
        var width = Math.Min(calloutWidth, TutorialCallouts.MaxWidthPx);
        CalloutPlacementResult? best = null;
        var bestRoom = float.NegativeInfinity;
        foreach (var side in Enum.GetValues<CalloutSide>())
        {
            var bounds = Candidate(anchor, width, calloutHeight, viewport, side);
            if (viewport.Contains(bounds) && !bounds.Intersects(anchor)) return new CalloutPlacementResult(bounds, side, true);

            var room = Room(anchor, viewport, side);
            if (room > bestRoom)
            {
                bestRoom = room;
                best = new CalloutPlacementResult(ClampInto(bounds, viewport), side, false);
            }
        }
        return best!.Value;
    }

    private static LayoutRect Candidate(LayoutRect anchor, float width, float height, LayoutRect viewport, CalloutSide side)
    {
        switch (side)
        {
            case CalloutSide.Below:
            case CalloutSide.Above:
            {
                var x = Clamp(anchor.X + (anchor.Width - width) / 2f, viewport.X, viewport.Right - width);
                var y = side == CalloutSide.Below ? anchor.Bottom + GapPx : anchor.Y - GapPx - height;
                return new LayoutRect(x, y, width, height);
            }
            default:
            {
                var y = Clamp(anchor.Y + (anchor.Height - height) / 2f, viewport.Y, viewport.Bottom - height);
                var x = side == CalloutSide.Right ? anchor.Right + GapPx : anchor.X - GapPx - width;
                return new LayoutRect(x, y, width, height);
            }
        }
    }

    private static float Room(LayoutRect anchor, LayoutRect viewport, CalloutSide side) => side switch
    {
        CalloutSide.Below => viewport.Bottom - anchor.Bottom,
        CalloutSide.Above => anchor.Y - viewport.Y,
        CalloutSide.Right => viewport.Right - anchor.Right,
        _ => anchor.X - viewport.X,
    };

    /// <summary>
    /// Where the pointer arrow meets the anchor-facing edge of <paramref name="bounds"/>: lined up with the anchor's centre
    /// and kept <paramref name="insetPx"/> from the callout's corners.
    /// </summary>
    public static (float X, float Y) ArrowBase(LayoutRect bounds, CalloutSide side, LayoutRect anchor, float insetPx)
    {
        var centreX = Clamp(anchor.X + anchor.Width / 2f, bounds.X + insetPx, bounds.Right - insetPx);
        var centreY = Clamp(anchor.Y + anchor.Height / 2f, bounds.Y + insetPx, bounds.Bottom - insetPx);
        return side switch
        {
            CalloutSide.Below => (centreX, bounds.Y),
            CalloutSide.Above => (centreX, bounds.Bottom),
            CalloutSide.Right => (bounds.X, centreY),
            _ => (bounds.Right, centreY),
        };
    }

    private static LayoutRect ClampInto(LayoutRect bounds, LayoutRect viewport) => bounds with
    {
        X = Clamp(bounds.X, viewport.X, viewport.Right - bounds.Width),
        Y = Clamp(bounds.Y, viewport.Y, viewport.Bottom - bounds.Height),
    };

    /// <summary>Clamps into [min, max]; a callout wider than the viewport aligns to <paramref name="min"/>.</summary>
    private static float Clamp(float value, float min, float max) => max < min ? min : Math.Clamp(value, min, max);
}
