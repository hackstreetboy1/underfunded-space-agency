namespace Usa.Simulation.Knowledge;

/// <summary>How an observation was made. What each method may establish is data (<c>facts.json</c> "methods").</summary>
public enum ObservationMethod
{
    /// <summary>A cabin gauge or display read in passing; reports what the sensor shows, which may be wrong.</summary>
    Glance,

    /// <summary>A deliberate instrument read (portable indicator, panel meter); still a sensor value.</summary>
    Instrument,

    /// <summary>Physical inspection: the true condition of what is within its scope.</summary>
    Inspection,

    /// <summary>A component-specific diagnostic (ring test, breaker cycling).</summary>
    Test,

    /// <summary>The ground's continuous status bands: coarse words only, never a precise value.</summary>
    Downlink,

    /// <summary>Derived by the forecast or a ground model, not measured.</summary>
    Model,

    /// <summary>
    /// A telemetry dump commanded from the ground: precise downlinked sensor readings, no crew observer. Reports what the
    /// sensors say (which may be wrong or missing) and never confirms a component's condition.
    /// </summary>
    Telemetry,
}

/// <summary>What an observation contributes to a fact's knowledge.</summary>
public enum ObservationYield
{
    /// <summary>A measured or reported value; can make a fact known.</summary>
    Measurement,

    /// <summary>A coarse ground band word; never makes a fact known.</summary>
    Band,

    /// <summary>A model value; makes a fact estimated at most.</summary>
    Estimate,
}
