namespace Usa.Simulation.Knowledge.Catalog;

/// <summary>How a fact is normally learned (GDD 4.3).</summary>
public enum FactReadMode
{
    Glance,
    Task,
    Inspection,
    Downlink,

    /// <summary>A ground telemetry dump (method <c>telemetry</c>).</summary>
    Telemetry,
}

public enum FactValueKind
{
    Number,
    Text,
}

/// <summary>Display rule for a fact's value (UI 6.2).</summary>
public enum FactFormat
{
    /// <summary>0 decimals at 100 or more, 1 decimal below.</summary>
    Mass,

    /// <summary>Whole numbers.</summary>
    Percent,

    /// <summary>Always signed, 1 decimal.</summary>
    Rate,

    /// <summary>The fact's <c>decimals</c>.</summary>
    Fixed,

    Text,
}

/// <summary>Where the value comes from, which tells producers whether it can be wrong.</summary>
public enum FactSourceKind
{
    /// <summary>A sensor or gauge; may be biased or failed.</summary>
    Sensor,

    /// <summary>A direct physical check that reads the true value.</summary>
    Direct,

    /// <summary>The condition of the component itself.</summary>
    Component,

    /// <summary>A crew member's self-report.</summary>
    Crew,

    /// <summary>
    /// A report the ground receives about what it commanded or what someone on scene saw (attitude mode and holding
    /// system, the recovery forces' splashdown report). Telemetry only, read from its <c>valuePath</c> under
    /// <c>report.</c>, never from hidden truth.
    /// </summary>
    Report,
}

public enum FactMatcherKind
{
    /// <summary>Expands <c>{nn}</c> / <c>{n}</c> over a numeric range at load.</summary>
    Index,

    /// <summary>Matches any component id in place of <c>{component}</c>.</summary>
    Component,

    /// <summary>Matches any crew id in place of <c>{crew}</c>.</summary>
    Crew,
}
