namespace Usa.Game.Presentation.EntryHud;

/// <summary>
/// What the entry/landing HUD (UI 4.2) and the splashdown card (UI 4.3) read. The session adapter fills the records
/// from Flight, Vehicle, Crew and the Entry Checklist task; the models here never see the simulation. Read it at the
/// UI cadence (10 Hz, UI 6.4), not per frame.
/// </summary>
public interface IEntryHudSource
{
    /// <summary>Deploy altitudes and crew g tolerance; normally fixed for a mission.</summary>
    EntryHudLimits Limits { get; }

    /// <summary>
    /// What the ground knows of the flight: instrument readings from knowledge (<c>knowledge.fact.entry.*</c>) and the
    /// ground prediction, never the true <c>flight.*</c> values.
    /// </summary>
    EntryFlightInputs Flight { get; }

    EntrySequenceInputs Sequence { get; }

    EntryChecklistInputs Checklist { get; }

    /// <summary>Crew in seat order.</summary>
    IReadOnlyList<EntryCrewInputs> Crew { get; }

    SplashdownInputs Splashdown { get; }
}
