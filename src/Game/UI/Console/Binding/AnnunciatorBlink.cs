using Usa.Game.Presentation.Console.Header;

namespace Usa.Game.UI.Console.Binding;

/// <summary>Whether the master alarm annunciator is lit at a moment (UI.md 3.1, 3.10).</summary>
public static class AnnunciatorBlink
{
    /// <summary>Half of the flash cycle: lit for this long, then dark for as long.</summary>
    public static readonly TimeSpan HalfPeriod = TimeSpan.FromMilliseconds(500);

    /// <param name="sinceChange">Real time since the indicator entered its state, so every flash starts lit.</param>
    /// <param name="reducedMotion">Holds a flashing annunciator steadily lit instead.</param>
    public static bool IsLit(MasterAlarmState state, TimeSpan sinceChange, bool reducedMotion) => state switch
    {
        MasterAlarmState.Lit => true,
        MasterAlarmState.Flashing => reducedMotion || (long)(Math.Max(0, sinceChange.Ticks) / HalfPeriod.Ticks) % 2 == 0,
        _ => false,
    };
}
