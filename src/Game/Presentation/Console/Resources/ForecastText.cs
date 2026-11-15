using Usa.Game.Content;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Presentation.Console.Status;
using Usa.Game.Presentation.ViewModels;

namespace Usa.Game.Presentation.Console.Resources;

/// <summary>
/// Forecast wording (GDD 6.4, <c>console.forecast*</c> strings): always a range, a limiting factor and a
/// confidence, never a single countdown.
/// </summary>
public sealed class ForecastText
{
    private const string Separator = ReadoutFormatter.MetaSeparator;

    private readonly ITextSource _text;

    public ForecastText(ITextSource text, StatusLanguage status)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(status);
        _text = text;
        Status = status;
        PendingText = text.Get("console.forecastPending");
        Title = text.Get("console.forecastTitle");
        PendingDetail = text.Get("console.forecastPendingDetail");
        EstimateBadge = text.Get("console.factEstimated");
        text.Get("console.forecastValue");
    }

    public StatusLanguage Status { get; }

    /// <summary><c>Safe autonomy — · Forecast pending</c>.</summary>
    public string PendingText { get; }

    /// <summary>The block's first line, <c>Safe autonomy</c>.</summary>
    public string Title { get; }

    /// <summary>Third line before the first forecast.</summary>
    public string PendingDetail { get; }

    /// <summary>Badge of a forecast value, <c>EST</c>.</summary>
    public string EstimateBadge { get; }

    public ForecastPill Pill(ForecastSnapshot? forecast) =>
        forecast is null
            ? new ForecastPill(PendingText, Status.For(ForecastCoverage.NoPlanEnd), Available: false)
            {
                Title = Title,
                Value = ReadoutFormatter.Placeholder,
                Detail = PendingDetail,
            }
            : new ForecastPill(PillText(forecast), Status.For(forecast.Coverage), Available: true)
            {
                Title = Title,
                Value = ValueText(forecast),
                Detail = DetailText(forecast),
                Badge = EstimateBadge,
            };

    /// <summary>The block's second line, <c>≈ 7.5–9 h</c>, or <c>beyond horizon</c>.</summary>
    public string ValueText(ForecastSnapshot forecast)
    {
        ArgumentNullException.ThrowIfNull(forecast);
        return double.IsPositiveInfinity(forecast.SafeAutonomyLowH)
            ? _text.Get("console.forecastBeyondHorizon")
            : _text.Format("console.forecastValue", ("range", RangeHours(forecast.SafeAutonomyLowH, forecast.SafeAutonomyHighH)));
    }

    /// <summary>The block's third line, <c>Limit: re-entry battery · Confidence: medium</c>.</summary>
    public string DetailText(ForecastSnapshot forecast)
    {
        ArgumentNullException.ThrowIfNull(forecast);
        return _text.Format("console.forecastLimit", ("factor", LimitText(forecast.LimitingFactor))) + Separator + Confidence(forecast.Confidence);
    }

    /// <summary><c>Safe autonomy ≈ 7.5–9 h · Limit: re-entry battery · Confidence: medium</c>.</summary>
    public string PillText(ForecastSnapshot forecast)
    {
        ArgumentNullException.ThrowIfNull(forecast);
        var autonomy = double.IsPositiveInfinity(forecast.SafeAutonomyLowH)
            ? _text.Get("console.forecastAutonomyBeyondHorizon")
            : _text.Format("console.forecastAutonomy", ("range", RangeHours(forecast.SafeAutonomyLowH, forecast.SafeAutonomyHighH)));
        return autonomy + Separator + _text.Format("console.forecastLimit", ("factor", LimitText(forecast.LimitingFactor)))
            + Separator + Confidence(forecast.Confidence);
    }

    /// <summary><c>7.5–9 h</c>; an open upper end reads <c>40+ h</c>; equal ends collapse to <c>8 h</c>.</summary>
    public string RangeHours(double lowH, double highH)
    {
        if (double.IsNaN(lowH) || double.IsNaN(highH)) return ReadoutFormatter.Placeholder;
        if (double.IsPositiveInfinity(lowH)) return _text.Get("console.forecastBeyondHorizon");
        if (lowH > highH) (lowH, highH) = (highH, lowH);
        lowH = Math.Max(0, lowH);
        if (double.IsPositiveInfinity(highH)) return Trimmed(lowH) + "+ h";

        var low = Trimmed(lowH);
        var high = Trimmed(Math.Max(0, highH));
        return low == high ? low + " h" : low + ReadoutFormatter.RangeDash + high + " h";
    }

    public string ConfidenceWord(ForecastConfidenceLevel confidence) => _text.Get(confidence switch
    {
        ForecastConfidenceLevel.High => "console.forecastConfidenceHigh",
        ForecastConfidenceLevel.Medium => "console.forecastConfidenceMedium",
        _ => "console.forecastConfidenceLow",
    });

    public string CoverageSentence(ForecastCoverage coverage) => _text.Get(coverage switch
    {
        ForecastCoverage.Covered => "console.forecastCoverageCovered",
        ForecastCoverage.AtRisk => "console.forecastCoverageAtRisk",
        ForecastCoverage.NotCovered => "console.forecastCoverageNotCovered",
        _ => "console.forecastCoverageNoPlan",
    });

    public ForecastBreakdown Breakdown(ForecastSnapshot forecast)
    {
        ArgumentNullException.ThrowIfNull(forecast);
        var confidence = Confidence(forecast.Confidence);
        if (!string.IsNullOrWhiteSpace(forecast.ConfidenceReason)) confidence += " (" + forecast.ConfidenceReason + ")";

        var rows = new ForecastBreakdownRow[forecast.Resources.Count];
        for (var i = 0; i < rows.Length; i++)
        {
            var line = forecast.Resources[i];
            rows[i] = new ForecastBreakdownRow(
                line.Label,
                RangeHours(line.LowH, line.HighH),
                line.Knowledge switch
                {
                    ForecastInputKnowledge.Stale => _text.Get("console.forecastInputStale"),
                    ForecastInputKnowledge.Unknown => _text.Get("console.forecastInputUnknown"),
                    _ => null,
                },
                Limiting: forecast.LimitingFactor.Length > 0 && string.Equals(line.Label, forecast.LimitingFactor, StringComparison.OrdinalIgnoreCase));
        }

        return new ForecastBreakdown(
            PillText(forecast),
            _text.Format("console.forecastComputedAt", ("met", ReadoutFormatter.FormatMetShort(forecast.ComputedAt))),
            confidence,
            CoverageSentence(forecast.Coverage),
            Status.For(forecast.Coverage),
            rows,
            [.. forecast.Assumptions]);
    }

    private string Confidence(ForecastConfidenceLevel confidence) =>
        _text.Format("console.forecastConfidence", ("level", ConfidenceWord(confidence)));

    private string LimitText(string limitingFactor) =>
        string.IsNullOrWhiteSpace(limitingFactor) ? _text.Get("console.forecastLimitNone") : limitingFactor;

    // UI.md 6.2 numbers, with a whole value's ".0" dropped as in range end points ("7.5–9 h").
    private static string Trimmed(double value)
    {
        var text = ReadoutFormatter.FormatNumber(value);
        return text.EndsWith(".0", StringComparison.Ordinal) ? text[..^2] : text;
    }
}
