namespace Usa.Game.Presentation.Map;

/// <summary>Style keys the Godot view maps to UI.md tokens and strokes.</summary>
public static class MapStyleKeys
{
    /// <summary><c>info</c>, solid, 2 px.</summary>
    public const string CurrentPath = "map.path.current";

    /// <summary><c>info-ghost</c>, dashed.</summary>
    public const string GhostPath = "map.path.ghost";

    /// <summary><c>info-ghost</c>, dotted: the predicted descent through the atmosphere.</summary>
    public const string GhostDescent = "map.path.ghost_descent";

    /// <summary>The previewed landing's dispersion arc on the rim (X8).</summary>
    public const string LandingArc = "map.path.landing_arc";

    /// <summary>A tracking station tick on the rim (X8).</summary>
    public const string Station = "map.station";

    /// <summary>Translucent <c>info</c> fill around a path.</summary>
    public const string Uncertainty = "map.band.uncertainty";

    /// <summary>Green fill with amber edges.</summary>
    public const string Corridor = "map.band.corridor";

    public const string CorridorEdge = "map.band.corridor_edge";

    /// <summary><c>#7FB6E8</c> at 40 %.</summary>
    public const string Atmosphere = "map.earth.atmosphere";
}
