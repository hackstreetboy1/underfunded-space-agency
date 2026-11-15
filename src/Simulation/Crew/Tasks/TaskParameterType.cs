namespace Usa.Simulation.Crew.Tasks;

public enum TaskParameterType
{
    /// <summary>One of the listed text values.</summary>
    Choice,

    /// <summary>A finite number, optionally bounded by min/max.</summary>
    Number,

    Boolean,

    /// <summary>Free snake_case text such as an assembly id from the engineering workspace.</summary>
    Identifier,
}
