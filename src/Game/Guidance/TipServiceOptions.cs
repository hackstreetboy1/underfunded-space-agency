namespace Usa.Game.Guidance;

/// <param name="RephraseEnabled">Initial value of <see cref="TipService.RephrasingEnabled"/> (off when AI features are off).</param>
/// <param name="RequestCooldown">Minimum real time between rephrase requests; a tip arriving sooner waits for its turn.</param>
public sealed record TipServiceOptions(bool RephraseEnabled, TimeSpan RequestCooldown)
{
    public static TipServiceOptions Default { get; } = new(RephraseEnabled: true, RequestCooldown: TimeSpan.FromSeconds(10));
}
