namespace Usa.Game.Presentation.Console.Systems;

/// <summary>
/// Asks for the high-impact confirmation dialog (UI.md 5.2) for a guarded control. The dialog answers through the
/// control's <c>Confirm</c> or <c>Cancel</c>.
/// </summary>
/// <param name="Title">The action, imperative: <c>Switch OAMS master power OFF</c>.</param>
/// <param name="Effects">Consequences already in words (the snapshot's own effects).</param>
public sealed record ConfirmationRequest(RemoteControlIntent Intent, string Title, IReadOnlyList<string> Effects)
{
    /// <summary>The action's consequences as <c>impact.*</c> string keys; the dialog resolves them after <see cref="Effects"/>.</summary>
    public IReadOnlyList<string> EffectTextKeys { get; init; } = [];

    /// <summary>The change cannot be undone (the dialog's IRREVERSIBLE flag).</summary>
    public bool Irreversible { get; init; }

    /// <summary>Who carries it out, e.g. <c>Ground (remote)</c>; null when not shown.</summary>
    public string? ExecutorName { get; init; }
}
