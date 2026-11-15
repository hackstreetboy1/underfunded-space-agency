using Usa.Simulation.Data;

namespace Usa.Game.Presentation.CrewFeed;

/// <summary>Validated expression presets: every preset present, only known shape keys, weights within 0..1, neutral all 0.</summary>
public sealed class ExpressionSet
{
    private readonly float[] _weights;

    private ExpressionSet(float[] weights) => _weights = weights;

    public float Weight(ExpressionPreset preset, ShapeKey key) => _weights[Index(preset, key)];

    /// <exception cref="DefinitionException">Missing file, wrong schema or invalid presets.</exception>
    public static ExpressionSet Load(IDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return From(new DefinitionLoader(source).Load<ExpressionsFile>(ExpressionsFile.RelativePath, ExpressionsFile.SchemaTag));
    }

    public static ExpressionSet Parse(string json, string fileLabel = ExpressionsFile.RelativePath)
    {
        var file = DefinitionLoader.Deserialize<ExpressionsFile>(fileLabel, json);
        if (file.Schema != ExpressionsFile.SchemaTag)
            throw new DefinitionException(fileLabel, "$.schema", $"Expected schema '{ExpressionsFile.SchemaTag}' but found '{file.Schema}'.");
        return From(file, fileLabel);
    }

    public static ExpressionSet From(ExpressionsFile file, string fileLabel = ExpressionsFile.RelativePath)
    {
        ArgumentNullException.ThrowIfNull(file);
        var weights = new float[ExpressionPresets.Count * ShapeKeys.Count];
        foreach (var name in file.Presets.Keys)
        {
            if (!ExpressionPresets.Names.Contains(name, StringComparer.Ordinal))
                throw new DefinitionException(fileLabel, $"$.presets.{name}", "Unknown expression preset.");
        }

        for (var p = 0; p < ExpressionPresets.Count; p++)
        {
            var presetName = ExpressionPresets.Names[p];
            if (!file.Presets.TryGetValue(presetName, out var keys))
                throw new DefinitionException(fileLabel, "$.presets", $"Preset '{presetName}' is missing.");
            foreach (var (keyName, value) in keys)
            {
                var path = $"$.presets.{presetName}.{keyName}";
                if (!ShapeKeys.TryParse(keyName, out var key)) throw new DefinitionException(fileLabel, path, "Unknown shape key.");
                if (!(value is >= 0.0 and <= 1.0)) throw new DefinitionException(fileLabel, path, "Weight must be within 0..1.");
                if (p == (int)ExpressionPreset.Neutral && value != 0.0)
                    throw new DefinitionException(fileLabel, path, "Neutral must leave every key at 0 (the approved face).");
                weights[Index((ExpressionPreset)p, key)] = (float)value;
            }
        }

        return new ExpressionSet(weights);
    }

    private static int Index(ExpressionPreset preset, ShapeKey key) => ((int)preset * ShapeKeys.Count) + (int)key;
}
