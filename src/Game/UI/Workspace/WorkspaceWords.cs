using Usa.Game.Content;

namespace Usa.Game.UI.Workspace;

/// <summary>
/// The workspace view's words from <c>content/strings.json</c> (<c>workspace.*</c>), resolved once when the host applies
/// its strings; templates use <c>{name}</c> placeholders. <see cref="Design"/> holds the scene's design words for previews
/// and until the strings are applied.
/// </summary>
public sealed record WorkspaceWords(
    string GoalTitle,
    string GoalTitleNamed,
    string GoalSource,
    string GoalSink,
    string GoalLimits,
    string DifficultyEasy,
    string DifficultyNormal,
    string TestSummaryRun,
    string AssignUnlocks,
    string AssignOpensPicker,
    string SlotBusy,
    string AssignedNote,
    string AssigneeUnavailable,
    string NotAConductor,
    string CardDrag,
    string CardNoneLeft,
    string CardNotCircuit,
    string PickerTitleNamed,
    string PickerAssign,
    string PickerAssignTo,
    string PickerNote,
    string OptionDuration)
{
    public static WorkspaceWords Design { get; } = new(
        "GOAL", "GOAL · {name}", "Source {label} ({connector})", "→ Sink {label} ({connector})",
        "load {load} · wire rating {rating} · route ≥ {length}", "Easy · plain words", "Normal · check names", "{summary} · {run}",
        "Assign unlocks after a passing test.", "Assign opens the crew picker.", "The crew task slot is busy.",
        "Assigned: the crew task is under way.", "That astronaut can't take the task now.", "not a conductor", "Drag onto the board",
        "None left in the spares", "Not part of a circuit", "ASSIGN {goal}", "Assign", "Assign to {name}",
        "Likelihood is shown as a band, never a %. The install passes or fails; a failure uses up the parts it consumes.",
        "≈ {duration} MET");

    public static WorkspaceWords From(ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new(
            text.Get("workspace.goalTitle"),
            text.Get("workspace.goalTitleNamed"),
            text.Get("workspace.goalSource"),
            text.Get("workspace.goalSink"),
            text.Get("workspace.goalLimits"),
            text.Get("workspace.difficultyEasy"),
            text.Get("workspace.difficultyNormal"),
            text.Get("workspace.testSummaryRun"),
            text.Get("workspace.assignUnlocks"),
            text.Get("workspace.assignOpensPicker"),
            text.Get("workspace.slotBusy"),
            text.Get("workspace.assignedNote"),
            text.Get("workspace.assigneeUnavailable"),
            text.Get("workspace.notAConductor"),
            text.Get("workspace.cardDrag"),
            text.Get("workspace.cardNoneLeft"),
            text.Get("workspace.cardNotCircuit"),
            text.Get("workspace.pickerTitleNamed"),
            text.Get("workspace.pickerAssign"),
            text.Get("workspace.pickerAssignTo"),
            text.Get("workspace.pickerNote"),
            text.Get("workspace.optionDuration"));
    }

    /// <summary>Fills <c>{name}</c> placeholders of <paramref name="template"/>.</summary>
    public static string Fill(string template, params (string Name, string Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        var text = template;
        foreach (var (name, value) in values) text = text.Replace("{" + name + "}", value, StringComparison.Ordinal);
        return text;
    }
}
