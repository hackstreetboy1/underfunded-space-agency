using System.Text.Json.Serialization;

namespace Usa.Game.Diagnostics.Repro;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ReproDocument))]
internal sealed partial class ReproJsonContext : JsonSerializerContext;
