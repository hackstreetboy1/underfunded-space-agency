using Usa.Game.Services.Ai.Contracts;
using Usa.Game.Services.Connection;

namespace Usa.Game.Diagnostics.Overlay;

/// <summary>
/// One-line backend status for the overlay and repros: <c>DOWN</c>, <c>unknown</c>, <c>FAKE</c>, <c>up (live)</c> or
/// <c>degraded</c>. Live or fake comes from the <c>mode</c> field of the proxy's <c>/health</c>
/// (<see cref="ConnectionReport.Mode"/>); a provider without a key is reported as fake whatever the mode says.
/// </summary>
public static class BackendStatusText
{
    /// <param name="launcher">The proxy launcher's state when it matters (e.g. <c>starting</c>), appended as is; null for none.</param>
    public static string Describe(ConnectionReport report, bool lost, string? launcher = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var line = Line(report, lost);
        return launcher is { Length: > 0 } ? $"{line}; proxy launcher {launcher}" : line;
    }

    private static string Line(ConnectionReport report, bool lost)
    {
        var services = $"backend {report.Backend}, language {report.Language}, voice {report.Voice}";
        if (lost || report.Backend is ServiceHealth.Down or ServiceHealth.Unreachable)
            return $"DOWN ({services}{(report.ErrorCode is { Length: > 0 } code ? $", {code}" : "")})";
        if (report.Backend == ServiceHealth.Unknown) return "unknown (no heartbeat yet)";
        if (report.Language == ServiceHealth.Unconfigured || report.Voice == ServiceHealth.Unconfigured)
            return $"FAKE (missing provider key: {services})";
        if (report.Mode == ProviderMode.Fake) return report.EssentialOk ? "FAKE (mode fake)" : $"FAKE (mode fake, degraded: {services})";
        var mode = report.Mode == ProviderMode.Live ? "live" : "mode not reported";
        if (report.EssentialOk) return $"up ({mode})";
        return $"degraded ({mode}: {services})";
    }
}
