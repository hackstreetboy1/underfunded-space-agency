using Godot;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// ENV-01/02 for the orbital view: the Earth sphere and atmosphere rim on the background render layer, the Sun light,
/// a soft Earth-shine fill, the camera fill, the Sun disc glow and the starfield's orientation.
/// <para>
/// The display frame keeps nadir straight down (<see cref="SpacecraftAxes.DisplayFrame"/>), so Earth's centre is
/// fixed at <see cref="EarthCentreDistanceM"/> below the vehicles and only its size and spin change: the sphere is
/// scaled so it fills the angle it fills from the real altitude (72.8° radius at 300 km), and turned with the
/// display frame so the ground moves under the vehicle and the terminator follows the Sun. Being far away it has
/// almost no parallax, but it is still real geometry: the camera's zoom limit keeps the camera well above it.
/// </para>
/// <para>
/// Lighting (docs/review/gate-3/lookdev/LOOKDEV.md): the Sun is the key; the Earth-shine fill comes from below and
/// weakens over the night side; the camera fill rides on the camera so the side facing the player always shows its
/// forms, and dims with the ambient light in eclipse. None of the fills light the Earth.
/// </para>
/// </summary>
public sealed partial class EarthBackdrop : Node3D
{
    private const double EarthRadiusKm = 6371.0;

    /// <summary>Sidereal rotation rate, degrees per second (360° in 86 164.09 s).</summary>
    private const double SiderealRateDegPerSec = 360.0 / 86164.0905;

    private static readonly StringName SunDirectionParam = "sun_direction";
    private static readonly StringName LongitudeOffsetParam = "longitude_offset_deg";
    private static readonly StringName CloudRotationParam = "cloud_rotation";

    private ShaderMaterial? _atmosphereMaterial;
    private ShaderMaterial? _earthMaterial;
    private Vector3 _skyRotation = new(float.NaN, 0, 0);
    private float _shownAmbient = -1f;

    [Export] public MeshInstance3D? Earth { get; set; }
    [Export] public MeshInstance3D? Atmosphere { get; set; }
    [Export] public DirectionalLight3D? Sun { get; set; }
    [Export] public DirectionalLight3D? EarthShine { get; set; }

    /// <summary>Soft fill attached to the camera (a child of it); lights the vehicles only.</summary>
    [Export] public DirectionalLight3D? CameraFill { get; set; }

    [Export] public WorldEnvironment? WorldEnvironment { get; set; }

    /// <summary>Sun disc billboard; one is created when the scene has none.</summary>
    [Export] public SunGlow? SunGlow { get; set; }

    /// <summary>Display distance from the vehicles to Earth's centre; the sphere's radius follows from the altitude.</summary>
    [Export] public float EarthCentreDistanceM { get; set; } = 4000f;

    /// <summary>Atmosphere shell radius over Earth's (MODELING layouts: 1.025).</summary>
    [Export] public float AtmosphereScale { get; set; } = 1.025f;

    /// <summary>Lower clamp so the camera never ends up inside the atmosphere shell (entry is shown elsewhere).</summary>
    [Export] public double MinAltitudeKm { get; set; } = 200.0;

    [Export] public double DefaultAltitudeKm { get; set; } = 300.0;
    [Export] public float SunEnergy { get; set; } = 1.3f;
    [Export] public float EarthShineEnergy { get; set; } = 0.45f;

    /// <summary>Share of the Earth-shine kept when the Earth below is on its night side.</summary>
    [Export] public float EarthShineNightLevel { get; set; } = 0.15f;

    [Export] public float CameraFillEnergy { get; set; } = 0.65f;

    /// <summary>Environment ambient energy in sunlight.</summary>
    [Export] public float AmbientEnergy { get; set; } = 0.45f;

    /// <summary>Share of the camera fill and the ambient light kept in Earth's shadow, so forms still read at night.</summary>
    [Export] public float EclipseFillLevel { get; set; } = 0.5f;

    /// <summary>Half-width of the Sun's fade at Earth's limb, degrees (soft eclipse entry/exit).</summary>
    [Export] public float EclipseSoftnessDeg { get; set; } = 1.5f;

    /// <summary>Inertial angle of the prime meridian at MET 0, degrees from inertial +X.</summary>
    [Export] public double EarthRotationAngleAtMetZeroDeg { get; set; }

    /// <summary>Cloud drift over the ground, degrees of longitude per hour of MET (eastward).</summary>
    [Export] public double CloudDriftDegPerHour { get; set; } = 2.0;

    public override void _Ready()
    {
        // Own copies: the Sun direction and the Earth's rotation are per view.
        if (Atmosphere?.MaterialOverride is ShaderMaterial atmosphere)
        {
            _atmosphereMaterial = (ShaderMaterial)atmosphere.Duplicate();
            Atmosphere.MaterialOverride = _atmosphereMaterial;
        }
        if (Earth?.MaterialOverride is ShaderMaterial surface)
        {
            _earthMaterial = (ShaderMaterial)surface.Duplicate();
            Earth.MaterialOverride = _earthMaterial;
        }
        foreach (var layer in new GeometryInstance3D?[] { Earth, Atmosphere })
        {
            if (layer is null) continue;
            layer.Layers = SpacecraftConventions.BackdropLayers;
            layer.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        // The fills come from the Earth and the camera; they must not light the Earth.
        foreach (var fill in new[] { EarthShine, CameraFill })
        {
            if (fill is null) continue;
            fill.LightCullMask = ~SpacecraftConventions.BackdropLayers;
            fill.ShadowEnabled = false;
        }
        if (EarthShine is not null) EarthShine.Basis = Basis.LookingAt(Vector3.Up, Vector3.Right);
        if (SunGlow is null)
        {
            SunGlow = new SunGlow { Name = "SunGlow" };
            AddChild(SunGlow);
        }
        SetMissionTime(0.0);
    }

    /// <summary>Turns the Earth to its angle at <paramref name="metSec"/> and drifts the clouds; call once per frame.</summary>
    public void SetMissionTime(double metSec)
    {
        if (_earthMaterial is null) return;
        var longitudeOffsetDeg = WrapDegrees(EarthRotationAngleAtMetZeroDeg + SiderealRateDegPerSec * metSec);
        _earthMaterial.SetShaderParameter(LongitudeOffsetParam, (float)longitudeOffsetDeg);
        var cloudTurns = WrapDegrees(CloudDriftDegPerHour * metSec / 3600.0) / 360.0;
        _earthMaterial.SetShaderParameter(CloudRotationParam, (float)cloudTurns);
    }

    /// <summary>Presentation update from the display frame and the backdrop view; call once per frame.</summary>
    public void Apply(Basis displayFrame, in OrbitalBackdropView view)
    {
        var altitudeKm = view.AltitudeKm > 0 ? Math.Max(view.AltitudeKm, MinAltitudeKm) : DefaultAltitudeKm;
        var radiusRatio = EarthRadiusKm / (EarthRadiusKm + altitudeKm);
        var earthRadius = EarthCentreDistanceM * (float)radiusRatio;
        var centre = new Vector3(0f, -EarthCentreDistanceM, 0f);

        if (Earth is not null) Earth.Transform = new Transform3D(displayFrame.Scaled(Vector3.One * earthRadius), centre);
        if (Atmosphere is not null)
        {
            Atmosphere.Transform = new Transform3D(displayFrame.Scaled(Vector3.One * earthRadius * AtmosphereScale), centre);
        }

        var sun = SpacecraftAxes.ToDisplayDirection(displayFrame, view.SunInertial);
        if (sun == Vector3.Zero) sun = new Vector3(0.6f, 0.5f, 0.6f).Normalized();
        _atmosphereMaterial?.SetShaderParameter(SunDirectionParam, sun);

        var sunVisibility = SunVisibility(sun, radiusRatio);
        if (Sun is not null)
        {
            Sun.Basis = Basis.LookingAt(-sun, Mathf.Abs(sun.Y) > 0.99f ? Vector3.Right : Vector3.Up);
            Sun.LightEnergy = SunEnergy * sunVisibility;
        }
        SunGlow?.Apply(sun, sunVisibility);
        if (EarthShine is not null)
        {
            // Earth below is mostly day side when the Sun is overhead.
            var litShare = (1f + sun.Dot(Vector3.Up)) * 0.5f;
            EarthShine.LightEnergy = EarthShineEnergy * Mathf.Lerp(EarthShineNightLevel, 1f, litShare);
        }

        var fillLevel = Mathf.Lerp(EclipseFillLevel, 1f, sunVisibility);
        if (CameraFill is not null) CameraFill.LightEnergy = CameraFillEnergy * fillLevel;
        UpdateEnvironment(displayFrame, AmbientEnergy * fillLevel);
    }

    // 0 while the Sun is behind the Earth disc seen from the vehicle, 1 in full sunlight.
    private float SunVisibility(Vector3 sun, double radiusRatio)
    {
        var earthAngularRadius = Math.Asin(Math.Clamp(radiusRatio, 0.0, 1.0));
        var sunFromNadir = Math.Acos(Math.Clamp(sun.Dot(Vector3.Down), -1f, 1f));
        var softness = Mathf.DegToRad(Math.Max(EclipseSoftnessDeg, 0.01f));
        return (float)Math.Clamp((sunFromNadir - earthAngularRadius + softness) / (2 * softness), 0.0, 1.0);
    }

    // Stars are fixed in inertial space, which turns in the display frame. Unchanged values are skipped so the sky's
    // radiance and the environment are not refreshed every frame.
    private void UpdateEnvironment(Basis displayFrame, float ambientEnergy)
    {
        if (WorldEnvironment?.Environment is not { } environment) return;
        if (Mathf.Abs(ambientEnergy - _shownAmbient) > 1e-3f)
        {
            _shownAmbient = ambientEnergy;
            environment.AmbientLightEnergy = ambientEnergy;
        }
        var rotation = displayFrame.GetEuler();
        if (!float.IsNaN(_skyRotation.X) && rotation.DistanceSquaredTo(_skyRotation) < 1e-8f) return;
        _skyRotation = rotation;
        environment.SkyRotation = rotation;
    }

    private static double WrapDegrees(double degrees)
    {
        var wrapped = degrees % 360.0;
        return wrapped < 0 ? wrapped + 360.0 : wrapped;
    }
}
