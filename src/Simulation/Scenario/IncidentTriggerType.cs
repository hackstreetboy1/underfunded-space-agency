namespace Usa.Simulation.Scenario;

public enum IncidentTriggerType
{
    /// <summary>At a MET drawn from <c>metS</c> {min, max}.</summary>
    AtTime,

    /// <summary>A delay drawn from <c>delayS</c> {min, max} after a mission event fired.</summary>
    AfterEvent,

    /// <summary>When an expression has held continuously for <c>holdS</c> seconds.</summary>
    Condition,

    /// <summary>Whichever of the nested <c>triggers</c> fires first.</summary>
    Any,
}
