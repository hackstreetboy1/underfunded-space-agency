using Godot;
using Usa.Game.Presentation.Spacecraft;
using Usa.Game.Services.Settings;

namespace Usa.Game.Integration.Runtime.Hosting;

/// <summary>Binds the mission's 3D view to a composed runtime.</summary>
public static class SpacecraftViewBinding
{
    /// <summary>
    /// Turns <paramref name="backdrop"/>'s Earth from the mission's MET-zero angle and attaches <paramref name="view"/> to the
    /// runtime's view source. The view then reads <c>MetSec</c> every frame itself (the backdrop's <c>SetMissionTime</c>).
    /// Until the context is disposed the view follows the app freeze (<see cref="MissionRuntimeContext.Frozen"/>) and, with
    /// <paramref name="settings"/>, the player's reduced-motion setting (UI 2.5); teardown detaches it (plumes out, lights dark).
    /// </summary>
    public static void Bind(this MissionRuntimeContext context, SpacecraftView view, EarthBackdrop backdrop, SettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(backdrop);
        backdrop.EarthRotationAngleAtMetZeroDeg = context.EarthRotationAngleAtMetZeroDeg;
        view.Initialize(context.Spacecraft);
        context.Own(new DetachView(view));
        if (context.Frozen is { } frozen) context.Own(frozen.Subscribe(f => { if (GodotObject.IsInstanceValid(view)) view.SetFrozen(f); }));
        if (settings is not null) context.Own(new ReducedMotionFollower(view, settings));
    }

    private sealed class DetachView(SpacecraftView view) : IDisposable
    {
        public void Dispose()
        {
            if (!GodotObject.IsInstanceValid(view)) return;
            view.SetFrozen(false);
            view.Detach();
        }
    }

    private sealed class ReducedMotionFollower : IDisposable
    {
        private readonly SpacecraftView _view;
        private readonly SettingsService _settings;

        public ReducedMotionFollower(SpacecraftView view, SettingsService settings)
        {
            _view = view;
            _settings = settings;
            _view.ReducedMotion = settings.Current.Display.ReducedMotion;
            _settings.Changed += OnChanged;
        }

        public void Dispose() => _settings.Changed -= OnChanged;

        private void OnChanged(SettingsChange change)
        {
            if (GodotObject.IsInstanceValid(_view)) _view.ReducedMotion = change.Current.Display.ReducedMotion;
        }
    }
}
