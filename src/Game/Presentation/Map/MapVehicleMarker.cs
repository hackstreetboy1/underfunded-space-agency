using Usa.Simulation.Flight;

namespace Usa.Game.Presentation.Map;

/// <summary>A vehicle to show on the map.</summary>
/// <param name="IconKey">e.g. <c>map.vehicle.tandem</c>, <c>map.vehicle.lodestar</c>.</param>
/// <param name="PointingAngleRad">In-plane pointing (world angle); null draws the icon along the velocity.</param>
/// <param name="Label">Display name drawn beside the icon (e.g. "Gemini 8"); null falls back to the id.</param>
public sealed record MapVehicleInput(string VehicleId, StateVector State, string IconKey, double? PointingAngleRad = null, string? Label = null);

/// <summary>A vehicle icon: draw <see cref="IconKey"/> at <see cref="Position"/> rotated by <see cref="ScreenRotationRad"/>.</summary>
public sealed record MapVehicleMarker(string VehicleId, MapPoint Position, double ScreenRotationRad, string IconKey, string? Label = null)
{
    public static MapVehicleMarker For(MapVehicleInput vehicle, MapTransform transform)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(transform);
        var angle = vehicle.PointingAngleRad ?? vehicle.State.VelocityMps.Angle;
        return new MapVehicleMarker(vehicle.VehicleId, transform.WorldToScreen(vehicle.State.PositionM), MapTransform.ScreenAngle(angle), vehicle.IconKey, vehicle.Label);
    }
}
