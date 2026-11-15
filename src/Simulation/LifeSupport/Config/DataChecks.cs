using System.Globalization;
using Usa.Simulation.Data;

namespace Usa.Simulation.LifeSupport.Config;

/// <summary>Numeric range checks that report into <see cref="DefinitionErrors"/> instead of throwing.</summary>
internal readonly struct DataChecks(DefinitionErrors errors, string file)
{
    public void Positive(string path, double value)
    {
        if (!(value > 0) || double.IsInfinity(value)) errors.Add(file, path, Format("Must be a positive number", value));
    }

    public void NotNegative(string path, double value)
    {
        if (!(value >= 0) || double.IsInfinity(value)) errors.Add(file, path, Format("Must be zero or positive", value));
    }

    public void InRange(string path, double value, double min, double max)
    {
        if (!(value >= min && value <= max))
        {
            errors.Add(file, path, Format(
                string.Create(CultureInfo.InvariantCulture, $"Must be between {min} and {max}"), value));
        }
    }

    private static string Format(string rule, double value) =>
        string.Create(CultureInfo.InvariantCulture, $"{rule} (found {value}).");
}
