using System.Diagnostics.CodeAnalysis;

namespace Usa.Simulation.Commands.Catalog;

/// <summary>Parameter types; names mirror the backend's ActionParam.type literals.</summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Values mirror the AI wire format literals.")]
public enum ParameterType
{
    String,
    Number,
    Integer,
    Boolean,
    Enum,
}
