namespace Usa.Simulation.Scenario;

/// <summary>Effect targets may start with <c>{variant.&lt;id&gt;}</c>, replaced by that variant's pick.</summary>
internal static class EffectTargets
{
    private const string Open = "{variant.";

    public static bool TryGetVariantId(string target, out string variantId)
    {
        variantId = "";
        if (!target.StartsWith(Open, StringComparison.Ordinal)) return false;
        var close = target.IndexOf('}', StringComparison.Ordinal);
        if (close <= Open.Length) return false;
        variantId = target[Open.Length..close];
        return true;
    }

    public static bool HasPlaceholder(string target) => target.Contains('{', StringComparison.Ordinal);

    public static string Resolve(string target, VariantPicker variants)
    {
        if (!TryGetVariantId(target, out var variantId)) return target;
        var close = target.IndexOf('}', StringComparison.Ordinal);
        return variants.Get(variantId) + target[(close + 1)..];
    }
}
