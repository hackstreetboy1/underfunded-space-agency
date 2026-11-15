using System.Text.Json.Serialization;

namespace Usa.Game.Services.Saves;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(PlayerProfile))]
[JsonSerializable(typeof(ResumeSave))]
public sealed partial class SaveJsonContext : JsonSerializerContext;
