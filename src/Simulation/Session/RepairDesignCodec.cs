using System.Globalization;
using System.Text;
using System.Text.Json;
using Usa.Simulation.Engineering;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Session;

/// <summary>
/// The <c>design</c> argument of <c>register_repair_design</c>: an <see cref="AssemblyDesign"/> as compact JSON,
/// <c>{"goalId":"j14_bypass","sink":"tandem/rcs/driver_b","parts":["adapter_round_d",...],"links":[["source","1:0"],["1:1","2:0"],...]}</c>
/// with optional <c>instances</c> and <c>nextInstance</c>. A port is <c>source</c>, <c>sink</c> or <c>&lt;instance&gt;:&lt;end&gt;</c>.
/// </summary>
public static class RepairDesignCodec
{
    private const string SourcePort = "source";
    private const string SinkPort = "sink";

    public static string Serialize(AssemblyDesign design)
    {
        ArgumentNullException.ThrowIfNull(design);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("goalId", design.GoalId);
            writer.WriteString("sink", design.Sink.Value);
            writer.WriteStartArray("parts");
            foreach (var part in design.Parts) writer.WriteStringValue(part.Value);
            writer.WriteEndArray();
            writer.WriteStartArray("links");
            foreach (var link in design.Links)
            {
                writer.WriteStartArray();
                writer.WriteStringValue(Port(link.A));
                writer.WriteStringValue(Port(link.B));
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
            if (design.Instances is { } instances)
            {
                writer.WriteStartArray("instances");
                foreach (var instance in instances) writer.WriteNumberValue(instance);
                writer.WriteEndArray();
            }
            if (design.NextInstance != 0) writer.WriteNumber("nextInstance", design.NextInstance);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <returns>Null when the text is not a well-formed design.</returns>
    public static AssemblyDesign? TryParse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var parts = root.GetProperty("parts").EnumerateArray().Select(p => new PartId(p.GetString()!)).ToArray();
            var links = new List<AssemblyLink>();
            foreach (var link in root.GetProperty("links").EnumerateArray())
            {
                if (link.GetArrayLength() != 2 || Port(link[0].GetString()) is not { } a || Port(link[1].GetString()) is not { } b) return null;
                links.Add(new AssemblyLink(a, b));
            }
            int[]? instances = root.TryGetProperty("instances", out var list) ? list.EnumerateArray().Select(i => i.GetInt32()).ToArray() : null;
            var next = root.TryGetProperty("nextInstance", out var n) ? n.GetInt32() : 0;
            var goal = root.GetProperty("goalId").GetString();
            var sink = root.GetProperty("sink").GetString();
            if (string.IsNullOrEmpty(goal) || string.IsNullOrEmpty(sink)) return null;
            return new AssemblyDesign(goal, new ComponentId(sink), parts, links, instances, next);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    private static string Port(PortRef port) =>
        port == PortRef.Source ? SourcePort
        : port == PortRef.Sink ? SinkPort
        : string.Create(CultureInfo.InvariantCulture, $"{port.Instance.Value}:{port.Port}");

    private static PortRef? Port(string? text)
    {
        if (text == SourcePort) return PortRef.Source;
        if (text == SinkPort) return PortRef.Sink;
        var colon = text?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        if (colon <= 0) return null;
        return int.TryParse(text.AsSpan(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out var instance) && instance > 0 &&
               int.TryParse(text.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var end)
            ? new PortRef(new PartInstanceId(instance), end)
            : null;
    }
}
