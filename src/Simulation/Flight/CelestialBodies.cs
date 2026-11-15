using Usa.Simulation.Data;

namespace Usa.Simulation.Flight;

/// <summary>The bodies from <c>config/bodies.json</c>, linked into a parent/child tree.</summary>
public sealed class CelestialBodies
{
    public const string DefaultPath = "config/bodies.json";

    private readonly Dictionary<string, CelestialBody> _byId;

    private CelestialBodies(Dictionary<string, CelestialBody> byId, CelestialBody[] sorted)
    {
        _byId = byId;
        All = sorted;
    }

    /// <summary>Every body, sorted by id.</summary>
    public IReadOnlyList<CelestialBody> All { get; }

    public static CelestialBodies Load(IDataSource source, string relativePath = DefaultPath) =>
        FromDefinition(new DefinitionLoader(source).Load<BodiesDefinition>(relativePath, BodiesDefinition.SchemaTag), relativePath);

    public static CelestialBodies FromDefinition(BodiesDefinition definition, string fileLabel)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new DefinitionErrors();
        var byDefinitionId = new Dictionary<string, BodyDefinition>(StringComparer.Ordinal);
        for (var i = 0; i < definition.Bodies.Count; i++)
        {
            var body = definition.Bodies[i];
            var path = $"$.bodies[{i}]";
            Validate(body, path, fileLabel, errors);
            if (!byDefinitionId.TryAdd(body.Id, body)) errors.Add(fileLabel, path + ".id", $"Duplicate body id '{body.Id}'.");
        }

        for (var i = 0; i < definition.Bodies.Count; i++)
        {
            var body = definition.Bodies[i];
            var path = $"$.bodies[{i}]";
            if (body.Orbit is null)
            {
                if (body.SoiRadiusM is not null) errors.Add(fileLabel, path + ".soiRadiusM", "A root body (no orbit) has no sphere of influence; use null.");
                continue;
            }
            if (body.SoiRadiusM is null) errors.Add(fileLabel, path + ".soiRadiusM", "An orbiting body needs a sphere-of-influence radius.");
            if (!byDefinitionId.TryGetValue(body.Orbit.Parent, out var parent))
                errors.Add(fileLabel, path + ".orbit.parent", $"Unknown parent body '{body.Orbit.Parent}'.");
            else if (body.Orbit.Parent == body.Id || HasCycle(body, byDefinitionId))
                errors.Add(fileLabel, path + ".orbit.parent", "Body orbits form a cycle.");
            else if (body.Orbit.RadiusM <= parent.RadiusM)
                errors.Add(fileLabel, path + ".orbit.radiusM", "Orbit radius must exceed the parent's radius.");
        }
        errors.ThrowIfAny();

        var byId = new Dictionary<string, CelestialBody>(StringComparer.Ordinal);
        foreach (var id in byDefinitionId.Keys.Order(StringComparer.Ordinal)) Create(id, byDefinitionId, byId);

        var sorted = byId.Values.OrderBy(b => b.Id, StringComparer.Ordinal).ToArray();
        foreach (var body in sorted)
            body.SetChildren(sorted.Where(child => ReferenceEquals(child.Parent, body)).ToArray());
        return new CelestialBodies(byId, sorted);
    }

    public CelestialBody Get(string id) =>
        _byId.TryGetValue(id, out var body) ? body : throw new KeyNotFoundException($"Unknown body '{id}'.");

    public bool TryGet(string id, out CelestialBody body) => _byId.TryGetValue(id, out body!);

    private static CelestialBody Create(string id, Dictionary<string, BodyDefinition> definitions, Dictionary<string, CelestialBody> created)
    {
        if (created.TryGetValue(id, out var existing)) return existing;
        var definition = definitions[id];
        var parent = definition.Orbit is { } orbit ? Create(orbit.Parent, definitions, created) : null;
        var body = new CelestialBody(definition, parent);
        created.Add(id, body);
        return body;
    }

    private static bool HasCycle(BodyDefinition start, Dictionary<string, BodyDefinition> definitions)
    {
        var current = start;
        for (var depth = 0; depth <= definitions.Count; depth++)
        {
            if (current.Orbit is null || !definitions.TryGetValue(current.Orbit.Parent, out var parent)) return false;
            if (ReferenceEquals(parent, start)) return true;
            current = parent;
        }
        return true;
    }

    private static void Validate(BodyDefinition body, string path, string file, DefinitionErrors errors)
    {
        if (string.IsNullOrWhiteSpace(body.Id)) errors.Add(file, path + ".id", "Body id is empty.");
        if (body.MuM3PerS2 <= 0) errors.Add(file, path + ".muM3PerS2", "Must be positive.");
        if (body.RadiusM <= 0) errors.Add(file, path + ".radiusM", "Must be positive.");
        if (body.SoiRadiusM is { } soi && soi <= body.RadiusM) errors.Add(file, path + ".soiRadiusM", "Must exceed the body radius.");
        if (body.Atmosphere is { } atmosphere)
        {
            if (atmosphere.ScaleHeightM <= 0) errors.Add(file, path + ".atmosphere.scaleHeightM", "Must be positive.");
            if (atmosphere.SeaLevelDensityKgPerM3 <= 0) errors.Add(file, path + ".atmosphere.seaLevelDensityKgPerM3", "Must be positive.");
            if (atmosphere.InterfaceAltitudeM <= 0) errors.Add(file, path + ".atmosphere.interfaceAltitudeM", "Must be positive.");
            if (atmosphere.UpperAtmosphere is { } upper) ValidateUpper(atmosphere, upper, path + ".atmosphere.upperAtmosphere", file, errors);
        }
    }

    private static void ValidateUpper(AtmosphereModel atmosphere, UpperAtmosphereModel upper, string path, string file, DefinitionErrors errors)
    {
        if (upper.BaseAltitudeM != atmosphere.InterfaceAltitudeM)
            errors.Add(file, path + ".baseAltitudeM", "Must equal the atmosphere's interfaceAltitudeM: the thin air starts where the integrated flight stops.");
        if (atmosphere.JoinStartM(upper) <= 0)
            errors.Add(file, path + ".baseAltitudeM", $"Must lie more than {AtmosphereModel.JoinDepthScaleHeights} sea-level scale heights above the ground.");
        else if (atmosphere.ScaleHeightM > 0 && !(upper.BaseDensityKgPerM3 < atmosphere.SeaLevelDensityKgPerM3 * Math.Exp(-atmosphere.JoinStartM(upper) / atmosphere.ScaleHeightM)))
            errors.Add(file, path + ".baseDensityKgPerM3", "Must be below the sea-level exponential where the join to the upper atmosphere begins.");
        if (!(upper.BaseAltitudeM > 0)) errors.Add(file, path + ".baseAltitudeM", "Must be positive.");
        if (!(upper.BaseDensityKgPerM3 > 0)) errors.Add(file, path + ".baseDensityKgPerM3", "Must be positive.");
        if (!(upper.ScaleHeightM > 0)) errors.Add(file, path + ".scaleHeightM", "Must be positive.");
        if (upper.Layers is not { } layers) return;
        var previousBaseM = upper.BaseAltitudeM;
        for (var i = 0; i < layers.Count; i++)
        {
            var layerPath = $"{path}.layers[{i}]";
            if (!(layers[i].BaseAltitudeM > previousBaseM))
                errors.Add(file, layerPath + ".baseAltitudeM", "Layers must start above the previous layer, in ascending order.");
            if (!(layers[i].ScaleHeightM > 0)) errors.Add(file, layerPath + ".scaleHeightM", "Must be positive.");
            previousBaseM = layers[i].BaseAltitudeM;
        }
    }
}
