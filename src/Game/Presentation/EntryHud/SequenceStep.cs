namespace Usa.Game.Presentation.EntryHud;

/// <summary>One step of the sequence strip: <c>Retrofire ✓</c>, <c>Entry interface</c> (active), <c>Drogue ◌</c>.</summary>
/// <param name="MetS">When the event happened as observed or reported, or its predicted MET; null when not known.</param>
/// <param name="TimeEstimated"><paramref name="MetS"/> is a prediction.</param>
/// <param name="TimeText">Under the label (Figma G4-03): <c>07:43:40</c> observed, <c>≈ 08:25</c> predicted; empty without a time.</param>
public sealed record SequenceStep(
    EntryStep Step, string Label, SequenceStepState State, HudTone Tone, string Glyph, string Text, double? MetS = null,
    bool TimeEstimated = false, string TimeText = "")
{
    public const string Separator = " · ";

    // Cached: Enum.GetValues allocates a new array on every call, and the strip is rebuilt at 10 Hz.
    private static readonly EntryStep[] Steps = Enum.GetValues<EntryStep>();

    /// <summary>
    /// States from the adapter's progress: Done, Failed and InProgress (active) as reported. A step not started is
    /// failed when a later step has already happened (it was missed), active when it is the next one due (nothing in
    /// progress and every earlier step settled), otherwise pending.
    /// </summary>
    /// <param name="words">For the predicted-time template; null shows observed times only.</param>
    public static IReadOnlyList<SequenceStep> Build(EntrySequenceInputs inputs, EntryHudWords? words = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var steps = Steps;
        var lastStarted = -1;
        var anyInProgress = false;
        for (var i = 0; i < steps.Length; i++)
        {
            var progress = inputs[steps[i]];
            if (progress != EntryEventProgress.NotStarted) lastStarted = i;
            anyInProgress |= progress == EntryEventProgress.InProgress;
        }

        var result = new SequenceStep[steps.Length];
        for (var i = 0; i < steps.Length; i++)
        {
            var state = inputs[steps[i]] switch
            {
                EntryEventProgress.Done => SequenceStepState.Done,
                EntryEventProgress.Failed => SequenceStepState.Failed,
                EntryEventProgress.InProgress => SequenceStepState.Active,
                _ when i < lastStarted => SequenceStepState.Failed,
                _ when i == lastStarted + 1 && !anyInProgress => SequenceStepState.Active,
                _ => SequenceStepState.Pending,
            };
            result[i] = WithTime(Create(steps[i], state), inputs.Times, words ?? EntryHudWords.None);
        }
        return result;
    }

    /// <summary>The strip as one line: <c>Equipment sep ✓ · Retrofire ✓ · Retro sep ✓ · Entry interface · Drogue ◌ · …</c>.</summary>
    public static string Join(IReadOnlyList<SequenceStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var texts = new string[steps.Count];
        for (var i = 0; i < texts.Length; i++) texts[i] = steps[i].Text;
        return string.Join(Separator, texts);
    }

    public static string LabelOf(EntryStep step) => step switch
    {
        EntryStep.EquipmentSeparation => "Equipment sep",
        EntryStep.Retrofire => "Retrofire",
        EntryStep.RetroSeparation => "Retro sep",
        EntryStep.EntryInterface => "Entry interface",
        EntryStep.Drogue => "Drogue",
        EntryStep.Main => "Main",
        EntryStep.Splashdown => "Splashdown",
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
    };

    private static SequenceStep WithTime(SequenceStep step, IReadOnlyDictionary<EntryStep, EntryStepTime>? times, EntryHudWords words)
    {
        if (times is null || !times.TryGetValue(step.Step, out var time) || !double.IsFinite(time.MetS)) return step;
        var text = time.Estimated
            ? EntryHudWords.Fill(words.StepEstimated, ("time", EntryHudFormat.ClockShort(time.MetS)))
            : EntryHudFormat.Clock(time.MetS);
        return step with { MetS = time.MetS, TimeEstimated = time.Estimated, TimeText = text };
    }

    private static SequenceStep Create(EntryStep step, SequenceStepState state)
    {
        var (glyph, tone) = state switch
        {
            SequenceStepState.Done => ("✓", HudTone.Ok),
            SequenceStepState.Failed => ("⯃", HudTone.Critical),
            SequenceStepState.Active => ("", HudTone.Info),
            _ => ("◌", HudTone.Neutral),
        };
        var label = LabelOf(step);
        return new SequenceStep(step, label, state, tone, glyph, glyph.Length == 0 ? label : label + " " + glyph);
    }
}
