namespace Usa.Game.Presentation.Console.Snapshots;

/// <summary>What one position change of a control does, for its confirmation dialog (UI.md 5.2).</summary>
/// <param name="EffectTextKeys">Consequences as <c>impact.*</c> string keys, in data order.</param>
/// <param name="RequiresConfirmation">The change is high-impact (the action, or one of its parameter rules, is not low), so it
/// goes through the dialog even when the switch has no cover.</param>
/// <param name="Irreversible">The action cannot be undone (the dialog's IRREVERSIBLE flag).</param>
public sealed record ControlImpact(IReadOnlyList<string> EffectTextKeys, bool RequiresConfirmation = false, bool Irreversible = false)
{
    public static ControlImpact None { get; } = new([]);

    public bool Equals(ControlImpact? other) =>
        other is not null
        && RequiresConfirmation == other.RequiresConfirmation
        && Irreversible == other.Irreversible
        && EffectTextKeys.SequenceEqual(other.EffectTextKeys, StringComparer.Ordinal);

    public override int GetHashCode() => HashCode.Combine(RequiresConfirmation, Irreversible, EffectTextKeys.Count);
}
