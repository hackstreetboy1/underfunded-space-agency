namespace Usa.Simulation.Knowledge;

/// <summary>Result of submitting an observation: the verdict and, when accepted, the recorded observation.</summary>
public readonly record struct ObservationResult(ObservationVerdict Verdict, Observation? Observation)
{
    public bool Accepted => Verdict.Accepted;
}
