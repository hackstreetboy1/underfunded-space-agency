namespace Usa.Game.Comms;

/// <summary>Push-to-talk states shown on the input bar (UI.md 3.5).</summary>
public enum PttState
{
    /// <summary>"HOLD SPACE TO TALK".</summary>
    Idle,
    Listening,
    Transcribing,

    /// <summary>The transcript was placed in the text field for the player to check (low confidence).</summary>
    Review,

    /// <summary>The microphone is off in settings; text play stays complete.</summary>
    Disabled,
}
