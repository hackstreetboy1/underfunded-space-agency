namespace Usa.Game.UI.Entry;

/// <summary>Status-language shapes (UI.md 2.2) drawn by <see cref="HudIcons"/>.</summary>
public enum HudShape
{
    /// <summary>Filled circle: nominal, done well, High likelihood.</summary>
    Nominal,

    /// <summary>Triangle: caution, degraded, Medium likelihood.</summary>
    Caution,

    /// <summary>Octagon: failed, critical, Low likelihood.</summary>
    Failed,

    /// <summary>Square with slash.</summary>
    Isolated,

    /// <summary>Dashed triangle.</summary>
    Suspect,

    /// <summary>Dashed circle: unknown, pending, not checked.</summary>
    Unknown,

    /// <summary>Clock: an older observation.</summary>
    Stale,

    /// <summary>Check mark: a completed step, a passed check.</summary>
    Done,

    /// <summary>Cross: an invalid connection, a landing marker.</summary>
    Invalid,
}
