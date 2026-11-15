using System.Globalization;
using Godot;
using Usa.Game.Presentation.Rendering;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// Drives the four big indicator lights of the target vehicle's status panel (mission 1: Lodestar's, MODELING 9) from
/// <see cref="ISpacecraftViewSource.GetLodestarPanel"/>: 1 attitude-thruster activity (steady when active, blinking at
/// high activity, the I2 red herring), 2 docked/latched, 3 engine armed, 4 power. Lights use the palette <c>light</c> swatch, whose
/// emission is scaled per mesh through the <c>indicator_level</c> instance uniform, so the knowledge look still
/// applies: while the panel is unknown it is grey and nothing glows. A light whose state the player does not know
/// stays dark, and a panel known to be unpowered shows every light off.
/// </summary>
public sealed partial class LodestarStatusLights : Node
{
    private const float LevelEpsilon = 1e-3f;

    private static readonly StringName IndicatorLevelParam = "indicator_level";

    private readonly MeshInstance3D?[] _lights = new MeshInstance3D?[SpacecraftConventions.StatusLightCount];
    private readonly StatusLightState[] _states = new StatusLightState[SpacecraftConventions.StatusLightCount];
    private readonly float[] _levels = new float[SpacecraftConventions.StatusLightCount];
    private double _acsActivity;
    private bool _acsPrimed;
    private double _blinkSec;

    /// <summary>Smoothed ACS activity at and above which the ACS light blinks.</summary>
    [Export] public double HighActivityThreshold { get; set; } = 0.2;

    /// <summary>Smoothed ACS activity below which the ACS light is off.</summary>
    [Export] public double ActiveThreshold { get; set; } = 0.02;

    /// <summary>Time constant of the ACS activity average (thrusters pulse; the light should not flicker with them).</summary>
    [Export] public double ActivitySmoothingSec { get; set; } = 0.8;

    [Export] public double BlinkHz { get; set; } = 1.25;
    [Export] public float BlinkLowLevel { get; set; } = 0.15f;

    /// <summary>Accessibility setting (UI 2.5): high activity shows steady instead of blinking.</summary>
    [Export] public bool ReducedMotion { get; set; }

    public bool IsBound { get; private set; }

    /// <summary>
    /// Finds <c>Light_1</c>..<c>Light_4</c> under component <paramref name="panelComponentId"/> of <paramref name="targetModel"/>;
    /// false when it has none. Rebinding resets the lights.
    /// </summary>
    public bool Bind(Node targetModel, string panelComponentId)
    {
        ArgumentNullException.ThrowIfNull(targetModel);
        ArgumentNullException.ThrowIfNull(panelComponentId);
        ResetLights();
        Array.Clear(_lights);
        IsBound = false;
        var panel = FindPanel(targetModel, panelComponentId);
        if (panel is null)
        {
            GD.PushWarning($"[LodestarStatusLights] no '{panelComponentId}' component; lights not driven.");
            return false;
        }

        var found = 0;
        for (var i = 0; i < _lights.Length; i++)
        {
            var light = panel.FindChild(SpacecraftConventions.StatusLightPrefix + (i + 1).ToString(CultureInfo.InvariantCulture), recursive: true, owned: false) as MeshInstance3D;
            _lights[i] = light;
            _levels[i] = -1f;
            if (light is not null) found++;
        }
        if (found < _lights.Length) GD.PushWarning($"[LodestarStatusLights] found {found} of {_lights.Length} status lights.");
        IsBound = found > 0;
        return IsBound;
    }

    public StatusLightState GetState(int lightIndex) => _states[lightIndex];

    /// <summary>Every light dark and unknown, the activity average and the blink phase forgotten (new run, detached view).</summary>
    public void ResetLights()
    {
        _acsActivity = 0;
        _acsPrimed = false;
        _blinkSec = 0;
        Array.Fill(_states, StatusLightState.Unknown);
        for (var i = 0; i < _lights.Length; i++)
        {
            _levels[i] = -1f;
            SetLevel(i, 0f);
        }
    }

    /// <summary>Presentation update; call once per frame while Lodestar is shown.</summary>
    public void Update(ISpacecraftViewSource source, double deltaSec)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!IsBound) return;
        var panel = source.GetLodestarPanel();

        if (panel.AcsActivity01 is { } activity)
        {
            var blend = _acsPrimed ? 1.0 - Math.Exp(-deltaSec / Math.Max(ActivitySmoothingSec, 1e-3)) : 1.0;
            _acsActivity += (Math.Clamp(activity, 0.0, 1.0) - _acsActivity) * blend;
            _acsPrimed = true;
        }
        else
        {
            _acsPrimed = false;
            _acsActivity = 0;
        }

        var unpowered = panel.PowerOn == false;
        _states[0] = unpowered ? StatusLightState.Off : AcsState(panel.AcsActivity01 is not null);
        _states[1] = unpowered ? StatusLightState.Off : FromFlag(panel.Latched);
        _states[2] = unpowered ? StatusLightState.Off : FromFlag(panel.EngineArmed);
        _states[3] = FromFlag(panel.PowerOn);

        _blinkSec = (_blinkSec + deltaSec) % (1.0 / Math.Max(BlinkHz, 1e-3));
        var blinkOn = _blinkSec * Math.Max(BlinkHz, 1e-3) < 0.5;
        for (var i = 0; i < _lights.Length; i++) SetLevel(i, LevelFor(_states[i], blinkOn));
    }

    private StatusLightState AcsState(bool known)
    {
        if (!known) return StatusLightState.Unknown;
        if (_acsActivity >= HighActivityThreshold) return StatusLightState.Blinking;
        return _acsActivity >= ActiveThreshold ? StatusLightState.On : StatusLightState.Off;
    }

    private static StatusLightState FromFlag(bool? flag) => flag switch
    {
        true => StatusLightState.On,
        false => StatusLightState.Off,
        null => StatusLightState.Unknown,
    };

    private float LevelFor(StatusLightState state, bool blinkOn) => state switch
    {
        StatusLightState.On => 1f,
        StatusLightState.Blinking when ReducedMotion || blinkOn => 1f,
        StatusLightState.Blinking => BlinkLowLevel,
        _ => 0f,
    };

    private void SetLevel(int index, float level)
    {
        if (_lights[index] is not { } light || Mathf.Abs(level - _levels[index]) < LevelEpsilon) return;
        if (!IsInstanceValid(light)) return;
        _levels[index] = level;
        light.SetInstanceShaderParameter(IndicatorLevelParam, level);
    }

    private static Node? FindPanel(Node node, string panelComponentId)
    {
        if (node.HasMeta(RenderingConventions.ComponentIdMeta) &&
            node.GetMeta(RenderingConventions.ComponentIdMeta).AsString() == panelComponentId)
        {
            return node;
        }
        foreach (var child in node.GetChildren())
        {
            if (FindPanel(child, panelComponentId) is { } panel) return panel;
        }
        return null;
    }
}
