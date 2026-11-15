using System.Text.Json.Serialization;
using Usa.Game.Services.Ai.Contracts;

namespace Usa.Game.Services.Ai;

/// <summary>
/// Source-generated (trim/AOT safe) JSON for the proxy contract: snake_case names, enums as their wire strings,
/// missing required fields and nulls in non-nullable members rejected, mirroring the pydantic models.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(InterpretRequest))]
[JsonSerializable(typeof(InterpretResponse))]
[JsonSerializable(typeof(NarrateRequest))]
[JsonSerializable(typeof(NarrateResponse))]
[JsonSerializable(typeof(TipRequest))]
[JsonSerializable(typeof(TipResponse))]
[JsonSerializable(typeof(ExplainRequest))]
[JsonSerializable(typeof(ExplainResponse))]
[JsonSerializable(typeof(SttResponse))]
[JsonSerializable(typeof(TtsRequest))]
[JsonSerializable(typeof(ApodResponse))]
public sealed partial class AiJsonContext : JsonSerializerContext;
