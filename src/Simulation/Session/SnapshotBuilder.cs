using Usa.Simulation.Attitude;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Crew;
using Usa.Simulation.Crew.Tasks;
using Usa.Simulation.Flight;
using Usa.Simulation.Guidance;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge;
using Usa.Simulation.LifeSupport.Atmosphere;
using Usa.Simulation.Ports;
using Usa.Simulation.Time;

namespace Usa.Simulation.Session;

/// <summary>Builds <see cref="SessionSnapshot"/> and <see cref="CompressionInputs"/> from the composed areas.</summary>
internal static class SnapshotBuilder
{
    /// <summary>A slower descent than this (m/s) gives no chute or splashdown estimate.</summary>
    private const double MinDescentMps = 0.5;

    public static SessionSnapshot Build(SessionParts p)
    {
        var state = p.State;
        var vehicles = p.Vehicles.States.Select(v => new VehicleView(
            v.VehicleId,
            v.ComputeMassProperties().MassKg,
            v.PowerGenerationW,
            v.PowerLoadW,
            v.Layout.Sections.Select((s, i) => new SectionView(s.Id, v.IsSectionAttached(i))).ToArray())).ToArray();

        var attitude = p.Attitude.Vehicles.Select(a => new AttitudeView(
            a.Id, a.RateRollDegPerSec, a.RatePitchDegPerSec, a.RateYawDegPerSec, a.RateTotalDegPerSec,
            AttitudeModeText.ToText(a.Mode), a.ControlSetId, a.PointingDeg, a.FiringDuty, a.DockedWithId, a.Orientation)).ToArray();

        var crew = p.Roster.Members.Select(m => new CrewView(
            m.Id, m.Definition.Name, m.Health, CrewRoster.BandName(m.Band), m.IsAlive,
            m.Location == CrewLocation.Eva ? "eva" : "cabin", m.Activity?.Value ?? "")).ToArray();

        var task = p.Runner.Active is { } active
            ? new TaskSlotView(active.Task.Id, active.Assignee, active.Origin, active.StartedAt, active.EndsAt, active.ProgressAt(p.Clock.Now))
            : null;

        var cache = p.ReadCache;
        var missionObjectives = p.Director.Mission.Objectives;
        var objectives = new ObjectiveView[missionObjectives.Count];
        for (var i = 0; i < objectives.Length; i++)
        {
            var o = missionObjectives[i];
            objectives[i] = new ObjectiveView(
                o.Id, o.Title, cache.ObjectiveKinds[i], Text(state, cache.ObjectiveStatePaths[i]), Text(state, cache.ObjectiveCausePaths[i]));
        }

        return new SessionSnapshot(
            p.Clock.Now,
            p.Clock.IsFrozen,
            p.Clock.FreezeReasons,
            p.Director.Phase,
            p.Director.IsEnded,
            Text(state, SessionReadCache.OutcomePath),
            vehicles,
            attitude,
            Flight(p),
            crew,
            task,
            Resources(p),
            Facts(p),
            p.Knowledge.KnownComponents(),
            [.. p.Alarms.Active],
            objectives,
            p.Forecast.Latest,
            Compression(p),
            p.Guidance.CurrentTip,
            p.Tutorial.ActiveBeatId);
    }

    public static CompressionInputs Compression(SessionParts p)
    {
        var now = p.Clock.Now;
        var kinematics = p.Flight.Kinematics;
        var maxRate = 0.0;
        foreach (var vehicle in p.Attitude.Vehicles)
        {
            if (vehicle.Id == p.Flight.CrewedId) maxRate = Math.Max(maxRate, vehicle.RateTotalDegPerSec);
        }

        double? next = null;
        void Consider(MissionTime? at)
        {
            if (at is not { } time || time <= now) return;
            var seconds = (time.Milliseconds - now.Milliseconds) / 1000.0;
            next = next is { } current ? Math.Min(current, seconds) : seconds;
        }
        Consider(p.Runner.Active?.EndsAt);
        Consider(p.Flight.ScheduledBurn?.StartTime);
        Consider(p.MissionEvents.RecoveryDue);
        if (p.Glances.Pending.Count > 0) Consider(p.Glances.Pending[0].ReadyAt);
        var belowInterface = kinematics.Phase == FlightPhase.Atmospheric && kinematics.AltitudeM < p.Flight.Mission.Corridor.InterfaceAltitudeM;

        return new CompressionInputs
        {
            Now = now,
            CriticalAlarmUnacknowledged = p.Alarms.MasterUnacknowledged,
            MaxAngularRateDps = maxRate,
            BurnActive = p.Burns.AnyBurn || kinematics.Phase == FlightPhase.Powered,
            DockingActive = p.Runner.Active is { } active && p.DockingTasks.Contains(active.Task.Id),
            BelowEntryInterface = belowInterface,
            ParachutePhase = kinematics.Phase == FlightPhase.Atmospheric && (p.Flight.Drogue == ChuteState.Deployed || p.Flight.Main == ChuteState.Deployed),
            CrewTaskRunning = p.Runner.Active is not null,
            AnyWorseningCondition = p.Alarms.UnacknowledgedCount(AlarmLevel.Warning) > 0,
            SecondsToNextEventMet = next,
            CriticalAlertSlowdown = p.Data.Difficulty.GetBool("critical_alert_slowdown", false),
            GLoad = kinematics.GLoad,
            CalmOpening = p.ReadCache.IsCalmOpening(p.Director),
            SecondsToDecisiveEventMet = SecondsToDecisiveEvent(p, now, kinematics),
            SecondsSinceDecisiveEventMet = SecondsSinceDecisiveEvent(p, now),
            SecondsToSplashdownMet = SecondsToSplashdown(p, kinematics),
            SecondsToChuteDeployMet = SecondsToChuteDeploy(p, kinematics),
            TipEscalationPending = TipEscalationPending(p),
            TutorialActionMaxRate = TutorialWaitsForPlayer(p) ? p.Tutorial.ActiveMaxTimeRate : null,
            SecondsToRecoveryMet = p.MissionEvents.RecoveryDue is { } due && due > now ? (due.Milliseconds - now.Milliseconds) / 1000.0 : null,
            CrewTaskSecondsLeftMet = p.Runner.Active is { } running && running.EndsAt > now ? (running.EndsAt.Milliseconds - now.Milliseconds) / 1000.0 : null,
            CrewTaskKind = p.Runner.Active is { } kindOf ? KindName(kindOf.Task.Definition.Kind) : null,
            GuidedFirstRun = GuidedFirstRunActive(p),
        };
    }

    /// <summary>
    /// The guide is on, not finished and its walkthrough has started: the script's first authored beat (<c>walk_dock</c>, Easy only)
    /// has triggered. Normal's lighter callouts come later in the script and never start the walkthrough, so Normal keeps the
    /// normal pacing, as does a run with the guide switched off.
    /// </summary>
    private static bool GuidedFirstRunActive(SessionParts p)
    {
        var tutorial = p.Tutorial;
        if (!tutorial.IsEnabled || tutorial.IsFinished) return false;
        var beats = p.Data.Tutorial.Beats;
        return beats.Count > 0 && tutorial.StatusOf(beats[0].Id) != TutorialBeatStatus.Waiting;
    }

    /// <summary>
    /// The callout's time cap holds only while it waits for the player: not while an order runs (a task, a readout, a burn on its
    /// way), when the player is only waiting too, nor once the player has ordered a crew task since it appeared (they took their
    /// own course, e.g. Path B never reads the thruster temperatures the callout asks for).
    /// </summary>
    private static bool TutorialWaitsForPlayer(SessionParts p)
    {
        if (p.Runner.Active is not null || p.Glances.Pending.Count > 0 || p.Burns.AnyBurn) return false;
        if (p.Tutorial.ActiveSince is not { } since) return true;
        var entries = p.Log.Entries;
        for (var i = entries.Count - 1; i >= 0 && entries[i].MetMs >= since.Milliseconds; i--)
        {
            if (p.Data.Actions.TryGet(entries[i].Request.Action, out var action) && action.Kind == ActionKind.CrewTask) return false;
        }
        return true;
    }

    /// <summary>The kind as the task catalog spells it (<c>tasks.json</c> <c>kind</c>).</summary>
    private static string KindName(TaskKind kind) => kind switch
    {
        TaskKind.Operate => "operate",
        TaskKind.Inspection => "inspection",
        TaskKind.Test => "test",
        TaskKind.Repair => "repair",
        TaskKind.Eva => "eva",
        TaskKind.Procedure => "procedure",
        _ => kind.ToString(),
    };

    /// <summary>
    /// MET seconds to the next decisive moment flight can predict: a scheduled burn's start; on a coasting trajectory that reaches
    /// the atmosphere, the entry interface; below it, each undeployed chute's nominal altitude and splashdown (current descent
    /// rate, as the entry HUD estimates them). Null when none lies ahead.
    /// </summary>
    private static double? SecondsToDecisiveEvent(SessionParts p, MissionTime now, in FlightKinematics kinematics)
    {
        var flight = p.Flight;
        double? next = null;
        void Consider(double seconds)
        {
            if (seconds > 0 && double.IsFinite(seconds) && !(next <= seconds)) next = seconds;
        }

        if (flight.ScheduledBurn is { } burn && burn.StartTime > now) Consider((burn.StartTime.Milliseconds - now.Milliseconds) / 1000.0);
        if (kinematics.Phase == FlightPhase.Landed) return next;

        var interfaceRadiusM = flight.Mission.Body.RadiusM + flight.Mission.Corridor.InterfaceAltitudeM;
        var conic = flight.Conic;
        if (kinematics.Phase == FlightPhase.Coast && conic.PeriapsisM < interfaceRadiusM && kinematics.State.RadiusM > interfaceRadiusM)
        {
            var anomalyRad = conic.TrueAnomalyAtRadius(interfaceRadiusM);
            if (double.IsFinite(anomalyRad) && conic.NextTimeAtTrueAnomaly(-anomalyRad, now) is { } entry)
                Consider((entry.Milliseconds - now.Milliseconds) / 1000.0);
        }

        if (kinematics.Phase != FlightPhase.Atmospheric) return next;
        var descentMps = -kinematics.SpeedMps * Math.Sin(kinematics.FlightPathAngleRad);
        if (!(descentMps > MinDescentMps)) return next;
        var capsule = flight.Mission.Capsule;
        if (flight.Drogue != ChuteState.Deployed) Consider((kinematics.AltitudeM - capsule.Drogue.NominalDeployAltitudeM) / descentMps);
        if (flight.Main != ChuteState.Deployed) Consider((kinematics.AltitudeM - capsule.Main.NominalDeployAltitudeM) / descentMps);
        Consider(kinematics.AltitudeM / descentMps);
        return next;
    }

    /// <summary>
    /// MET seconds to splashdown under the chutes: the fall at terminal speed in the exponential atmosphere, where the speed
    /// scales as <c>e^(h / 2H)</c>. The current descent rate stands for the open chute's speed here; a stowed main adds its own
    /// stretch from its nominal altitude down. Null when no chute is out or the capsule is not descending.
    /// </summary>
    private static double? SecondsToSplashdown(SessionParts p, in FlightKinematics kinematics)
    {
        var flight = p.Flight;
        if (kinematics.Phase != FlightPhase.Atmospheric || !(flight.Drogue == ChuteState.Deployed || flight.Main == ChuteState.Deployed)) return null;
        var descentMps = -kinematics.SpeedMps * Math.Sin(kinematics.FlightPathAngleRad);
        var altitudeM = kinematics.AltitudeM;
        if (!(descentMps > MinDescentMps) || !(altitudeM > 0)) return null;
        var body = flight.Mission.Body;
        if (body.Atmosphere is not { ScaleHeightM: > 0 } air) return altitudeM / descentMps;

        var twoH = 2 * air.ScaleHeightM;
        var main = flight.Mission.Capsule.Main;
        if (flight.Main == ChuteState.Deployed || flight.Main == ChuteState.Failed || altitudeM <= main.NominalDeployAltitudeM)
            return twoH / descentMps * (Math.Exp(altitudeM / twoH) - 1);

        var toMainS = twoH / descentMps * (Math.Exp((altitudeM - main.NominalDeployAltitudeM) / twoH) - 1);
        var gravityMps2 = body.MuM3PerS2 / (body.RadiusM * body.RadiusM);
        var mainSeaLevelMps = Math.Sqrt(2 * main.BallisticCoefficientKgPerM2 * gravityMps2 / air.SeaLevelDensityKgPerM3);
        var underMainS = twoH / mainSeaLevelMps * (1 - Math.Exp(-main.NominalDeployAltitudeM / twoH));
        var total = toMainS + underMainS;
        return double.IsFinite(total) && total > 0 ? total : null;
    }

    /// <summary>MET seconds to the next stowed chute's nominal deploy altitude at the current descent rate; null when none is ahead.</summary>
    private static double? SecondsToChuteDeploy(SessionParts p, in FlightKinematics kinematics)
    {
        if (kinematics.Phase != FlightPhase.Atmospheric) return null;
        var descentMps = -kinematics.SpeedMps * Math.Sin(kinematics.FlightPathAngleRad);
        if (!(descentMps > MinDescentMps)) return null;
        var flight = p.Flight;
        var capsule = flight.Mission.Capsule;
        var altitudeM = kinematics.AltitudeM;
        if (flight.Drogue == ChuteState.Stowed && altitudeM > capsule.Drogue.NominalDeployAltitudeM)
            return (altitudeM - capsule.Drogue.NominalDeployAltitudeM) / descentMps;
        if (flight.Main == ChuteState.Stowed && altitudeM > capsule.Main.NominalDeployAltitudeM)
            return (altitudeM - capsule.Main.NominalDeployAltitudeM) / descentMps;
        return null;
    }

    /// <summary>MET seconds since the latest decisive mission event (<c>session.json</c> <c>pacing.decisiveEvents</c>) fired.</summary>
    private static double? SecondsSinceDecisiveEvent(SessionParts p, MissionTime now)
    {
        long? latestMs = null;
        foreach (var eventId in p.ReadCache.DecisiveEvents)
        {
            if (p.Director.FiredAt(eventId) is { } fired && fired <= now && !(latestMs >= fired.Milliseconds)) latestMs = fired.Milliseconds;
        }
        return latestMs is { } ms ? (now.Milliseconds - ms) / 1000.0 : null;
    }

    /// <summary>
    /// The focused hint problem (open, highest priority) will show its next stage within one escalation window, so compression
    /// must not run past it (tips would skip stages).
    /// </summary>
    private static bool TipEscalationPending(SessionParts p)
    {
        HintProblem? focus = null;
        foreach (var problem in p.Data.Hints.Problems)
        {
            if (p.Guidance.StatusOf(problem.Id) != ProblemStatus.Open) continue;
            if (focus is null || problem.Priority > focus.Priority) focus = problem;
        }
        if (focus is null) return false;
        var settings = p.ReadCache.Guidance;
        var shown = p.Guidance.HighestStageShown(focus.Id);
        if (shown >= HintStage.Direct || shown + 1 > settings.MaxUnsolicitedStage) return false;
        var neededS = (int)(shown + 1) * settings.EscalationS - p.Guidance.StuckScoreS(focus.Id);
        return neededS <= settings.EscalationS;
    }

    private static FlightView Flight(SessionParts p)
    {
        var state = p.State;
        return new FlightView(
            Text(state, "flight.phase"),
            p.Flight.Docked,
            Number(state, "flight.altitude_km"),
            Number(state, "flight.periapsis_km"),
            Number(state, "flight.apoapsis_km"),
            Number(state, "flight.speed_mps"),
            Number(state, "flight.g_load"),
            p.Flight.RangeM,
            p.Flight.Drogue == ChuteState.Deployed,
            p.Flight.Main == ChuteState.Deployed,
            p.Burns.AnyBurn,
            Text(state, "flight.zone"),
            Number(state, "flight.recovery_wait_h"));
    }

    private static ResourcesView Resources(SessionParts p)
    {
        var state = p.State;
        var cabin = CrewedCabin(p);
        var o2 = 0.0;
        foreach (var vehicle in p.Vehicles.States)
        {
            foreach (var tank in vehicle.Layout.Tanks)
            {
                if (vehicle.Layout[tank].Tank!.Resource == "o2" && vehicle.IsAttached(tank)) o2 += vehicle[tank].QuantityKg;
            }
        }
        return new ResourcesView(
            cabin?.PpO2Mmhg ?? 0,
            cabin?.PpCo2Mmhg ?? 0,
            cabin?.PressureKpa ?? 0,
            cabin?.TempC ?? 0,
            cabin?.LiOhPct ?? 0,
            o2,
            Number(state, "provisions.food_kcal"),
            Number(state, "provisions.water_l"));
    }

    /// <summary>The crewed vehicle's cabin (the first cabin when it has none of its own).</summary>
    private static CabinAtmosphere? CrewedCabin(SessionParts p)
    {
        var cabins = p.LifeSupport.Cabins;
        for (var i = 0; i < cabins.Count; i++)
        {
            if (cabins[i].Id == p.Flight.CrewedId) return cabins[i];
        }
        return cabins.Count > 0 ? cabins[0] : null;
    }

    private static FactView[] Facts(SessionParts p)
    {
        var facts = p.ReadCache.Facts;
        var views = new FactView[facts.Length];
        for (var i = 0; i < facts.Length; i++)
        {
            var fact = facts[i];
            var known = p.Knowledge.Describe(fact.Id);
            var latest = known.Latest ?? known.Estimate;
            views[i] = new FactView(fact.Id, fact.Label, fact.Unit, StateText(known.State), latest?.Value, latest?.Qualitative, latest?.Time, known.GroundBandWord);
        }
        return views;
    }

    private static string StateText(KnowledgeState state) => state switch
    {
        KnowledgeState.Known => "known",
        KnowledgeState.Stale => "stale",
        KnowledgeState.Conflicting => "conflicting",
        KnowledgeState.Estimated => "estimated",
        _ => "unknown",
    };

    private static string Text(CompositeStateQuery state, string path) =>
        state.TryGet(path, out var value) && value.Kind == StateValueKind.Text ? value.AsText() : "";

    private static double Number(CompositeStateQuery state, string path) =>
        state.TryGet(path, out var value) && value.Kind is StateValueKind.Number or StateValueKind.Bool ? value.AsNumber() : 0;
}
