using Godot;
using Usa.Game.Presentation.Console;
using Usa.Game.Presentation.Console.Status;

namespace Usa.Game.UI.Console.Components;

/// <summary>
/// The dial of an arc gauge (custom <c>_Draw</c>, Figma G1-02 "Arc gauge"): a 270° track open at the bottom, the caution
/// and critical bands on its outer edge, and the value arc in the status colour from zero. Nothing fills while the value is
/// unknown. The numeric centre is ordinary labels laid over it.
/// </summary>
public sealed partial class ArcDial : Control
{
    private const float StartDeg = 135f;
    private const float SweepDeg = 270f;
    private const float TrackWidth = 6f;
    private const float BandWidth = 3f;
    private const int Points = 48;

    private GaugeReading? _reading;
    private ConsolePalette _palette = ConsolePalette.Default;
    private string _statusToken = ColourTokens.Unknown;

    public void Show(GaugeReading? reading, string statusColourToken, ConsolePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        _reading = reading;
        _statusToken = statusColourToken;
        _palette = palette;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var radius = Math.Min(Size.X, Size.Y) / 2 - TrackWidth;
        if (radius <= 0) return;
        var centre = Size / 2;
        DrawArc(centre, radius, Rad(0), Rad(1), Points, _palette.Tokens.ColorBg3, TrackWidth, antialiased: true);
        if (_reading is not { } reading) return;

        var bandRadius = radius + TrackWidth / 2 + BandWidth / 2 + 1;
        if (reading.CautionFraction is { } caution)
        {
            var end = reading.CriticalFraction ?? 1;
            if (end > caution) DrawArc(centre, bandRadius, Rad(caution), Rad(end), Points, _palette.Of(ColourTokens.Caution), BandWidth, antialiased: true);
        }
        if (reading.CriticalFraction is { } critical && critical < 1)
            DrawArc(centre, bandRadius, Rad(critical), Rad(1), Points, _palette.Of(ColourTokens.Critical), BandWidth, antialiased: true);
        if (reading.Fraction is { } fraction && fraction > 0)
            DrawArc(centre, radius, Rad(0), Rad(fraction), Points, _palette.Of(_statusToken), TrackWidth, antialiased: true);
    }

    private static float Rad(double fraction) => Mathf.DegToRad(StartDeg + SweepDeg * (float)Math.Clamp(fraction, 0, 1));
}
