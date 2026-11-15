using Usa.Game.Services.Ai;
using Usa.Game.Services.Ai.Contracts;

namespace Usa.Game.Services.Connection;

/// <summary>State of one essential service as shown on the freeze overlay (UI.md 5: Backend, Voice, Language).</summary>
public enum ServiceHealth
{
	Unknown,
	Ok,
	Degraded,
	Down,
	Unconfigured,

	/// <summary>The proxy itself did not answer, so the provider behind it is not known.</summary>
	Unreachable,
}

/// <summary>
/// Result of one heartbeat. Essential (GDD 15.6) = backend reachable and both providers <c>ok</c>;
/// <see cref="Language"/> is the <c>gemini</c> status, <see cref="Voice"/> the <c>elevenlabs</c> status.
/// </summary>
/// <param name="ErrorCode">Gateway error code when the heartbeat itself failed.</param>
/// <param name="Mode">What the proxy runs (fake stand-ins or real providers); null when it did not answer.</param>
public sealed record ConnectionReport(ServiceHealth Backend, ServiceHealth Language, ServiceHealth Voice, DateTimeOffset CheckedUtc, string? ErrorCode = null, ProviderMode? Mode = null)
{
	public bool EssentialOk => Backend == ServiceHealth.Ok && Language == ServiceHealth.Ok && Voice == ServiceHealth.Ok;

	public static ConnectionReport Initial { get; } = new(ServiceHealth.Unknown, ServiceHealth.Unknown, ServiceHealth.Unknown, DateTimeOffset.MinValue);

	public static ConnectionReport FromHealth(AiResult<HealthResponse> result, DateTimeOffset checkedUtc)
	{
		ArgumentNullException.ThrowIfNull(result);
		if (result.IsSuccess)
		{
			var health = result.Value;
			return new ConnectionReport(Map(health.Backend), Map(health.Gemini), Map(health.Elevenlabs), checkedUtc, Mode: health.Mode);
		}

		var backend = result.Error.Kind == AiFailureKind.Http ? ServiceHealth.Down : ServiceHealth.Unreachable;
		return new ConnectionReport(backend, ServiceHealth.Unknown, ServiceHealth.Unknown, checkedUtc, result.Error.ErrorCode);
	}

	private static ServiceHealth Map(ServiceStatus status) => status switch
	{
		ServiceStatus.Ok => ServiceHealth.Ok,
		ServiceStatus.Degraded => ServiceHealth.Degraded,
		ServiceStatus.Down => ServiceHealth.Down,
		ServiceStatus.Unconfigured => ServiceHealth.Unconfigured,
		_ => ServiceHealth.Unknown,
	};
}
