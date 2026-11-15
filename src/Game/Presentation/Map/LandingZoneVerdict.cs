namespace Usa.Game.Presentation.Map;

/// <summary>What the previewed landing arc means for its zone (X8: displayed bands are 2σ).</summary>
public enum LandingZoneVerdict
{
    /// <summary>No dispersion to judge (a preview without ground uncertainty), or no landing.</summary>
    None,

    /// <summary>The whole arc lies inside the zone.</summary>
    InZone,

    /// <summary>The landing is in a zone but the arc crosses its edge (caution: aimed at an edge).</summary>
    NearEdge,

    /// <summary>The landing is outside every zone.</summary>
    OutsideZones,
}
