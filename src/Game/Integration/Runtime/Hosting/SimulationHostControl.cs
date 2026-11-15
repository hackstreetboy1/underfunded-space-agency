using Usa.Game.Hosting.Nodes;
using Usa.Game.Integration.Adapters;
using Usa.Game.Presentation.Console.Snapshots;
using Usa.Game.Services.Connection;

namespace Usa.Game.Integration.Runtime.Hosting;

/// <summary><see cref="IMissionHostControl"/> over the flow host's <see cref="SimulationHost"/> and the connection monitor.</summary>
public sealed class SimulationHostControl(SimulationHost host, ConnectionMonitor connection) : IMissionHostControl
{
    private readonly SimulationHost _host = host ?? throw new ArgumentNullException(nameof(host));
    private readonly ConnectionMonitor _connection = connection ?? throw new ArgumentNullException(nameof(connection));

    public CompressionReading Compression =>
        new(_host.CompressionRate, _host.CompressionReason, _host.CanAdvanceToNextEvent, _host.IsAdvancingToEvent);

    public LinkState Link => _connection.IsLost ? LinkState.Reconnecting : LinkState.Ok;

    public bool RequestAdvanceToNextEvent() => _host.RequestAdvanceToNextEvent();
}
