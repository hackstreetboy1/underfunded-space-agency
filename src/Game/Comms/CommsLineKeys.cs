namespace Usa.Game.Comms;

/// <summary>Keys of <see cref="CommsTemplatesFile.Lines"/>; every key is required.</summary>
public static class CommsLineKeys
{
    public const string Pending = "pending";
    public const string PendingCrew = "pending_crew";
    public const string InterpretFailed = "interpret_failed";
    public const string CannotVerify = "cannot_verify";
    public const string UnknownAnswer = "unknown_answer";
    public const string UnknownFactAnswer = "unknown_fact_answer";
    public const string ClarifyCandidates = "clarify_candidates";
    public const string ReadOffer = "read_offer";
    public const string ActionOffer = "action_offer";
    public const string ActionOfferQuestion = "action_offer_question";
    public const string FactItem = "fact_item";
    public const string TaskCompleted = "task_completed";
    public const string TaskFailed = "task_failed";
    public const string ObservationIntro = "observation_intro";
    public const string CrewReportIntro = "crew_report_intro";
    public const string BusyCrew = "busy_crew";
    public const string BusySystem = "busy_system";
    public const string TaskStartedSystem = "task_started_system";
    public const string TaskCompletedSystem = "task_completed_system";
    public const string TaskFailedSystem = "task_failed_system";
    public const string TaskStoppedSystem = "task_stopped_system";
    public const string RemoteAppliedSystem = "remote_applied_system";
    public const string AlarmsAcknowledgedSystem = "alarms_acknowledged_system";
    public const string ConfirmationSystem = "confirmation_system";
    public const string ConfirmationCancelledSystem = "confirmation_cancelled_system";
    public const string ConfirmationExpiredSystem = "confirmation_expired_system";
    public const string RequestCancelledSystem = "request_cancelled_system";
    public const string RequestHeldSystem = "request_held_system";
    public const string ConnectionLostSystem = "connection_lost_system";
    public const string ConnectionRestoredSystem = "connection_restored_system";
    public const string VoiceFailedSystem = "voice_failed_system";
    public const string VoiceEmptySystem = "voice_empty_system";
    public const string NoCrewSystem = "no_crew_system";
    public const string FrozenSystem = "frozen_system";
    public const string ExecutorAnyCrew = "executor_any_crew";
    public const string ExecutorRemote = "executor_remote";

    // The spoken half of a read-out (SpokenLineComposer): {label} is a reading's spoken name, {value} its value in words,
    // {count} a count in words.
    public const string SpokenReading = "spoken_reading";
    public const string SpokenHigh = "spoken_high";
    public const string SpokenLow = "spoken_low";
    public const string SpokenHot = "spoken_hot";
    public const string SpokenCold = "spoken_cold";
    public const string SpokenAllNormal = "spoken_all_normal";
    public const string SpokenRestNormal = "spoken_rest_normal";
    public const string SpokenMoreOff = "spoken_more_off";
    public const string SpokenMore = "spoken_more";

    /// <summary>Default opener of a category, for a crew member without authored phrases: <c>default_&lt;category&gt;</c>.</summary>
    public static string DefaultPhrase(CrewPhraseCategory category) => category switch
    {
        CrewPhraseCategory.Acknowledge => "default_acknowledge",
        CrewPhraseCategory.Standby => "default_standby",
        CrewPhraseCategory.Stop => "default_stop",
        CrewPhraseCategory.Refusal => "default_refusal",
        _ => "default_clarification",
    };

    public static IReadOnlyList<string> All { get; } =
    [
        Pending, PendingCrew, InterpretFailed, CannotVerify, UnknownAnswer, UnknownFactAnswer, ClarifyCandidates, ReadOffer, ActionOffer, ActionOfferQuestion,
        FactItem, TaskCompleted, TaskFailed, ObservationIntro, CrewReportIntro, BusyCrew, BusySystem, TaskStartedSystem, TaskCompletedSystem, TaskFailedSystem,
        TaskStoppedSystem, RemoteAppliedSystem, AlarmsAcknowledgedSystem, ConfirmationSystem, ConfirmationCancelledSystem,
        ConfirmationExpiredSystem, RequestCancelledSystem, RequestHeldSystem, ConnectionLostSystem, ConnectionRestoredSystem,
        VoiceFailedSystem, VoiceEmptySystem, NoCrewSystem, FrozenSystem, ExecutorAnyCrew, ExecutorRemote,
        SpokenReading, SpokenHigh, SpokenLow, SpokenHot, SpokenCold, SpokenAllNormal, SpokenRestNormal, SpokenMoreOff, SpokenMore,
        DefaultPhrase(CrewPhraseCategory.Acknowledge), DefaultPhrase(CrewPhraseCategory.Standby), DefaultPhrase(CrewPhraseCategory.Stop),
        DefaultPhrase(CrewPhraseCategory.Refusal), DefaultPhrase(CrewPhraseCategory.Clarification),
    ];
}
