namespace Usa.Simulation.Commands.Catalog;

/// <summary>A typed action parameter. <paramref name="Min"/>/<paramref name="Max"/> are inclusive and apply to numbers.</summary>
/// <param name="Values">Allowed values of an <see cref="ParameterType.Enum"/> parameter.</param>
public sealed record ActionParameter(
    string Name,
    ParameterType Type,
    IReadOnlyList<string>? Values = null,
    double? Min = null,
    double? Max = null);

internal static class ActionParameters
{
    public static ActionParameter? Find(IReadOnlyList<ActionParameter>? parameters, string name)
    {
        foreach (var parameter in parameters ?? [])
        {
            if (string.Equals(parameter.Name, name, StringComparison.Ordinal)) return parameter;
        }
        return null;
    }
}
