namespace Usa.Game.UI.Console.Binding;

/// <summary>A key the confirmation dialog listens to.</summary>
public enum DialogKey
{
    Other,

    /// <summary>Enter or keypad Enter (never Space: Space is push-to-talk).</summary>
    Accept,

    /// <summary>Esc.</summary>
    Cancel,

    /// <summary>Tab.</summary>
    FocusNext,

    /// <summary>Shift+Tab.</summary>
    FocusPrevious,

    /// <summary>The push-to-talk key, whatever it is bound to.</summary>
    PushToTalk,
}

/// <summary>Which of the dialog's buttons has keyboard focus.</summary>
public enum DialogFocus
{
    /// <summary>Nothing in the dialog (or nothing at all).</summary>
    None,
    Cancel,
    Confirm,
}

/// <summary>What the dialog does with a key.</summary>
public enum DialogResponse
{
    /// <summary>Not the dialog's key: let it through.</summary>
    None,
    Confirm,
    Cancel,
    FocusCancel,
    FocusConfirm,

    /// <summary>
    /// Push-to-talk while a dialog button has focus: take focus off the button for as long as the key is held, so the key
    /// reaches the microphone and can never press Confirm (review R3-03).
    /// </summary>
    ReleaseFocusForTalk,

    /// <summary>Enter while Confirm is disabled (frozen): consumed so it cannot press a focused button instead.</summary>
    Swallow,
}

/// <summary>
/// The confirmation dialog's keyboard rules (UI.md 5.2, decided during development 2026-11-15, user): Enter always confirms and
/// Esc always cancels, regardless of which button has focus; Tab and Shift+Tab stay between Cancel and Confirm; push-to-talk
/// never confirms.
/// </summary>
public static class ConfirmDialogKeys
{
    public static DialogResponse Decide(DialogKey key, DialogFocus focus, bool canConfirm) => key switch
    {
        DialogKey.Cancel => DialogResponse.Cancel,
        DialogKey.Accept => canConfirm ? DialogResponse.Confirm : DialogResponse.Swallow,
        DialogKey.FocusNext or DialogKey.FocusPrevious => focus == DialogFocus.Cancel ? DialogResponse.FocusConfirm : DialogResponse.FocusCancel,
        DialogKey.PushToTalk when focus != DialogFocus.None => DialogResponse.ReleaseFocusForTalk,
        _ => DialogResponse.None,
    };
}
