using System.Text.Json;
using Usa.Simulation.Vehicle.Definitions;

namespace Usa.Game.Presentation.Diagram;

/// <summary>How to stroke one connection kind: colour token or hex, opacity and pattern (style differs as well as colour).</summary>
public sealed record ConnectionStyle(string Key, string Color, double Opacity, LinePattern Pattern, double WidthPx);

/// <summary>Icon and line style keys for the internal diagram (UI.md 4.5).</summary>
public static class DiagramStyles
{
    public static ConnectionStyle Power { get; } = new("diagram.line.power", "#E8C547", 0.7, LinePattern.Solid, 2);

    public static ConnectionStyle Fluid { get; } = new("diagram.line.fluid", "#5FB3D9", 0.7, LinePattern.DashDot, 2);

    public static ConnectionStyle Propellant { get; } = new("diagram.line.propellant", "#FF8A00", 0.7, LinePattern.Dashed, 2);

    public static ConnectionStyle Data { get; } = new("diagram.line.data", "text.lo", 1, LinePattern.Dotted, 1);

    public static ConnectionStyle For(ConnectionKind kind) => kind switch
    {
        ConnectionKind.Power => Power,
        ConnectionKind.Fluid => Fluid,
        ConnectionKind.Propellant => Propellant,
        ConnectionKind.Data => Data,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static DiagramLayers LayerOf(ConnectionKind kind) => kind switch
    {
        ConnectionKind.Power => DiagramLayers.Power,
        ConnectionKind.Fluid => DiagramLayers.Fluid,
        ConnectionKind.Propellant => DiagramLayers.Propellant,
        ConnectionKind.Data => DiagramLayers.Data,
        _ => DiagramLayers.None,
    };

    /// <summary>Family icon key, e.g. <c>family.thruster_group</c> (matches the data files' family names).</summary>
    public static string IconKey(ComponentFamily family) => "family." + FamilyName(family);

    /// <summary>Snake-case family name as in the vehicle JSON.</summary>
    public static string FamilyName(ComponentFamily family) => JsonNamingPolicy.SnakeCaseLower.ConvertName(family.ToString());

    /// <summary>Human-readable family label for tooltips: <c>Thruster group</c>.</summary>
    public static string FamilyLabel(ComponentFamily family)
    {
        var name = FamilyName(family).Replace('_', ' ');
        return char.ToUpperInvariant(name[0]) + name[1..];
    }
}
