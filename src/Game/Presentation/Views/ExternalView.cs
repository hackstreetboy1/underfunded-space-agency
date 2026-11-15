using Godot;
using Usa.Game.Presentation.Spacecraft;

namespace Usa.Game.Presentation.Views;

/// <summary>
/// Root of <c>res://scenes/views/ExternalView.tscn</c>: the External tab's 3D viewport (UI 4.4, GDD 4.4), a 1000 × 600
/// SubViewport with its own world holding <see cref="SpacecraftView"/> (vehicles, Earth, sky, Sun, plumes, reveal looks,
/// splashdown sea). It follows its container's size. The chrome (title, Reset / Focus buttons, legend, inspector) is
/// drawn over it by the console; this scene draws no UI.
/// <para>
/// It reads only the spacecraft-view source port (<see cref="ISpacecraftViewSource"/>, knowledge-gated), never the
/// simulation. The runtime binds it with <c>context.Bind(externalView.View, externalView.Backdrop, settings)</c>; picks,
/// reveals, splashdown and entry-HUD changes are the <see cref="View"/>'s signals.
/// </para>
/// </summary>
public sealed partial class ExternalView : SubViewportContainer
{
    private readonly NativePixelFit _fit = new();
    private SpacecraftView _view = null!;

    public SpacecraftView View => _view;

    public EarthBackdrop Backdrop => _view.Backdrop;

    public override void _Ready() => _view = GetNode<SpacecraftView>("%SpacecraftView");

    // Presentation only: renders at the screen's pixel density (1440p, 4K) instead of the 1920 x 1080 layout size.
    public override void _Process(double delta)
    {
        if (_fit.Update(this)) _view.SetInputPixelScale(_fit.Density);
    }

    public void Initialize(ISpacecraftViewSource source) => _view.Initialize(source);

    public void Detach() => _view.Detach();

    public void ResetCamera() => _view.ResetCamera();

    public bool FocusVehicle(string vehicleId) => _view.FocusVehicle(vehicleId);

    public bool FocusComponent(string componentId) => _view.FocusComponent(componentId);

    /// <summary>"Focus last revealed"; false when nothing has been revealed yet.</summary>
    public bool FocusLastRevealed() => _view.FocusLastRevealed();

    /// <summary>
    /// A component's tag anchor (STALE tag, inspector callout) in the parent's coordinates, ready for an overlay laid over
    /// the view; false when unknown or behind the camera.
    /// </summary>
    public bool TryGetTagAnchor(string componentId, out Vector2 localPosition)
    {
        var found = _view.TryGetTagAnchor(componentId, out var viewportPosition);
        // The view renders in screen pixels and is scaled back to layout units (NativePixelFit).
        localPosition = Position + viewportPosition * Math.Max(StretchShrink, 1) * Scale;
        return found;
    }
}
