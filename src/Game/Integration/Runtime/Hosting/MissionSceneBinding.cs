using Godot;
using Usa.Game.Presentation.Spacecraft;
using Usa.Game.Services.Settings;
using Usa.Game.UI.Entry;
using Usa.Game.UI.Map;

namespace Usa.Game.Integration.Runtime.Hosting;

/// <summary>
/// The mission scene's views a composed runtime drives. Every part is optional: a scene binds what it has. The console
/// (comms, header, systems, crew cards) binds itself through <c>MissionConsole.BindContext</c>; crew feeds through
/// <see cref="CrewFeedBinding.BindCrewFeed"/>.
/// </summary>
/// <param name="View">The External view's spacecraft (<c>scenes/views/ExternalView.tscn</c>); needs <paramref name="Backdrop"/>.</param>
/// <param name="ShowClosing">Shows the crew-lost closing card (the recovery card closes a recovered mission itself).</param>
public sealed record MissionSceneViews(
    SpacecraftView? View = null,
    EarthBackdrop? Backdrop = null,
    SettingsService? Settings = null,
    OrbitMapView? Map = null,
    EntryHudView? EntryHud = null,
    RecoveryCardView? RecoveryCard = null,
    Action<MissionClosingCard>? ShowClosing = null);

/// <summary>The one call that connects a mission scene's views to a composed runtime.</summary>
public static class MissionSceneBinding
{
    /// <summary>
    /// Binds <paramref name="views"/> to <paramref name="context"/> until the returned handle or the context is disposed: the 3D
    /// view and Earth backdrop (<see cref="SpacecraftViewBinding.Bind"/>), the orbit map and burn planner, the entry HUD and the
    /// recovery card, refreshed after every 10 Hz pump (<see cref="MissionRuntimeContext.Pumped"/>), and the closing beat:
    /// "Proceed to debrief" on the recovery card calls <see cref="MissionRuntimeContext.ProceedToDebrief"/>, and the crew-lost
    /// card is handed to <see cref="MissionSceneViews.ShowClosing"/> (at once when the mission has already ended).
    /// </summary>
    /// <remarks>
    /// The planner's Execute (<see cref="OrbitMapView.ExecuteRequested"/>) is not bound here: the console owns the high-impact
    /// confirmation and the crew order.
    /// </remarks>
    public static IDisposable BindScene(this MissionRuntimeContext context, MissionSceneViews views)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(views);
        ObjectDisposedException.ThrowIf(context.IsDisposed, context);
        if (views.View is { } view)
        {
            var backdrop = views.Backdrop ?? throw new ArgumentException("The spacecraft view needs its Earth backdrop.", nameof(views));
            context.Bind(view, backdrop, views.Settings);
        }
        views.Map?.Bind(context.Map, context.Planner, context.Strings);

        var binding = new SceneBinding(context, views);
        context.Own(binding);
        binding.Refresh();
        if (context.Closing is { } closing) binding.OnClosing(closing);
        return binding;
    }

    private sealed class SceneBinding : IDisposable
    {
        private readonly MissionRuntimeContext _context;
        private readonly MissionSceneViews _views;
        private bool _disposed;

        public SceneBinding(MissionRuntimeContext context, MissionSceneViews views)
        {
            _context = context;
            _views = views;
            context.Pumped += Refresh;
            context.ClosingShown += OnClosing;
            if (views.RecoveryCard is { } card) card.ProceedRequested += OnProceed;
        }

        public void Refresh()
        {
            if (_disposed) return;
            if (Valid(_views.EntryHud) is { } hud)
            {
                // The entry HUD covers most of the external view, so it only appears for the burn and the atmospheric flight.
                hud.Visible = _context.Track.Phase is Usa.Simulation.Flight.FlightPhase.Powered or Usa.Simulation.Flight.FlightPhase.Atmospheric;
                hud.Show(_context.EntryHud);
            }
            if (Valid(_views.RecoveryCard) is { } card) card.Show(_context.Splashdown);
            if (Valid(_views.Map) is { } map)
            {
                var input = _context.MapInput;
                map.Show(input.Build(_context.Planner.BuildPlan(), _context.Planner.Preview));
                var track = _context.Track;
                map.ShowTracking(new MapTrackingOverlay(input.NavAlongTrackUncertaintyM, track.Stale, track.Stale ? $"{(int)(track.FixAgeS / 60)}m" : string.Empty));
                map.SetMassText(input.MassText);
            }
        }

        public void OnClosing(MissionClosingCard closing)
        {
            if (_disposed || closing.UsesRecoveryCard) return;
            _views.ShowClosing?.Invoke(closing);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _context.Pumped -= Refresh;
            _context.ClosingShown -= OnClosing;
            if (Valid(_views.RecoveryCard) is { } card) card.ProceedRequested -= OnProceed;
        }

        private void OnProceed() => _context.ProceedToDebrief();

        private static T? Valid<T>(T? node) where T : GodotObject => node is not null && GodotObject.IsInstanceValid(node) ? node : null;
    }
}
