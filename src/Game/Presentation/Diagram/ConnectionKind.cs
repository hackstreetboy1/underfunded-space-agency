namespace Usa.Game.Presentation.Diagram;

/// <summary>What a diagram line carries (UI.md 4.5).</summary>
public enum ConnectionKind
{
    Power,

    /// <summary>Fluids and gases: reactants, oxygen, water, coolant.</summary>
    Fluid,

    Propellant,

    /// <summary>Data and control: sensor readings, thruster drivers, group arming.</summary>
    Data,
}

/// <summary>Diagram layer toggles.</summary>
[Flags]
public enum DiagramLayers
{
    None = 0,
    Power = 1,
    Fluid = 2,
    Propellant = 4,
    Data = 8,
    All = Power | Fluid | Propellant | Data,
}

public enum LinePattern
{
    Solid,
    Dashed,
    Dotted,

    /// <summary>Dash-dot, so fluid lines differ from power by pattern as well as colour (UI.md 7, GDD 17).</summary>
    DashDot,
}
