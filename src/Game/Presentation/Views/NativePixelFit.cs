using Godot;
using Usa.Game.UI.Console.Binding;

namespace Usa.Game.Presentation.Views;

/// <summary>
/// Makes a stretched <see cref="SubViewportContainer"/> render at the screen's real pixel density. Under the root's
/// <c>canvas_items</c> stretch a container lives in 1920 × 1080 layout units, so its SubViewport would render at that size
/// and be stretched up at 1440p or 4K. Instead the container is sized in screen pixels and scaled back down to fill its
/// parent: the 3D view renders one texel per pixel, and mouse positions reach the SubViewport in those same pixels.
/// Only acts when the parent is a plain <see cref="Control"/> (a <see cref="Container"/> would reset the scale).
/// </summary>
public sealed class NativePixelFit
{
    private Vector2 _logical;
    private Vector2I _pixels;

    /// <summary>Render pixels per layout unit last applied (1 until the first fit).</summary>
    public float Density { get; private set; } = 1f;

    /// <summary>Call every frame (cheap when nothing changed). True when the container was resized.</summary>
    public bool Update(SubViewportContainer container)
    {
        ArgumentNullException.ThrowIfNull(container);
        if (!container.Stretch || container.GetParent() is not Control parent || parent is Container) return false;
        var logical = parent.Size;
        if (logical.X < 1 || logical.Y < 1) return false;

        var perUnit = ScreenPixelsPerUnit(parent);
        var (width, height) = UiScaleRule.RenderPixels(logical.X, logical.Y, perUnit);
        var pixels = new Vector2I(width, height);
        if (pixels == _pixels && logical == _logical) return false;
        _pixels = pixels;
        _logical = logical;
        Density = pixels.X / logical.X;

        // Anchors would size it back to the parent's layout units on the next parent resize.
        if (container.AnchorRight != 0 || container.AnchorBottom != 0) container.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        container.Position = Vector2.Zero;
        container.Size = new Vector2(pixels.X, pixels.Y);
        container.Scale = new Vector2(logical.X / pixels.X, logical.Y / pixels.Y);
        return true;
    }

    // Root stretch and UI scale (the viewport's final transform) times every scale above the parent (the console's fit-to-window).
    private static float ScreenPixelsPerUnit(Control parent)
    {
        var final = parent.GetViewport().GetFinalTransform().Scale;
        var local = parent.GetGlobalTransformWithCanvas().Scale;
        return Math.Abs(final.Y * local.Y);
    }
}
