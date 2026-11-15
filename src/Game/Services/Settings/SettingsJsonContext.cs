using System.Text.Json.Serialization;

namespace Usa.Game.Services.Settings;

/// <summary>camelCase, indented, enums by name; unknown properties are ignored so older builds can read newer files.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(GameSettings))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
