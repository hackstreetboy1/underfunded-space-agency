using Usa.Game.Presentation.Console.Snapshots;

namespace Usa.Game.Presentation.Console.Systems;

/// <summary>Fixed presentation of the systems-board tabs (UI.md 3.9); the tab words are in <see cref="ConsoleWords"/>.</summary>
public static class SystemsTabInfo
{
    public static string IconKey(SystemsTab tab) => tab switch
    {
        SystemsTab.Power => "power",
        SystemsTab.LifeSupport => "life_support",
        SystemsTab.PropAttitude => "prop_attitude",
        SystemsTab.Thermal => "thermal",
        SystemsTab.Navigation => "navigation",
        SystemsTab.StructureLanding => "structure_landing",
        _ => "inventory",
    };

    /// <summary>The Thermal panel uses the older mismatched style with a tape label (the underfunded touch).</summary>
    public static bool UsesLegacyPanelStyle(SystemsTab tab) => tab == SystemsTab.Thermal;
}
