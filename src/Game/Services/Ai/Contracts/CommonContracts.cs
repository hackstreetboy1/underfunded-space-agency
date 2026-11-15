using System.Text.Json.Serialization;

namespace Usa.Game.Services.Ai.Contracts;

/// <summary>Every AI request carries a client-chosen id; retries reuse it so the proxy never runs a provider twice.</summary>
public interface IAiRequest
{
    string RequestId { get; }
}

/// <summary>Mirrors <c>app.schemas.common.ServiceStatus</c>.</summary>
public enum ServiceStatus
{
    [JsonStringEnumMemberName("ok")] Ok,
    [JsonStringEnumMemberName("degraded")] Degraded,
    [JsonStringEnumMemberName("down")] Down,
    [JsonStringEnumMemberName("unconfigured")] Unconfigured,
}

/// <summary>Mirrors <c>app.schemas.common.ProviderMode</c>: whether the proxy runs local stand-ins or real providers.</summary>
public enum ProviderMode
{
    [JsonStringEnumMemberName("fake")] Fake,
    [JsonStringEnumMemberName("live")] Live,
}

/// <summary>Body of every non-2xx proxy response (<c>app.schemas.common.ErrorResponse</c>).</summary>
public sealed record ErrorResponse(string ErrorCode, bool Retryable);

/// <summary><c>GET /health</c> (<c>app.schemas.health.HealthResponse</c>).</summary>
/// <param name="Mode">Additive field; an older proxy omits it and reads as <see cref="ProviderMode.Live"/>, the pydantic default.</param>
public sealed record HealthResponse(ServiceStatus Backend, ServiceStatus Gemini, ServiceStatus Elevenlabs, ProviderMode Mode = ProviderMode.Live);
