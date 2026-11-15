using Godot;

namespace Usa.Game.Services.Audio;

/// <summary>
/// Bus names (SCRIPTING.md 12.5) and idempotent helpers that create a bus or effect only when the layout lacks it,
/// so services work before the audio owner ships <c>default_bus_layout.tres</c> and never duplicate its buses.
/// </summary>
public static class AudioBuses
{
    public const string Master = "Master";
    public const string Speech = "Speech";
    public const string Alarms = "Alarms";
    public const string Sfx = "SFX";
    public const string Ambience = "Ambience";
    public const string Ui = "UI";

    /// <summary>Muted capture bus for the microphone (never audible).</summary>
    public const string Mic = "Mic";

    /// <summary>The player-adjustable buses, in settings order.</summary>
    public static IReadOnlyList<string> Adjustable { get; } = [Master, Speech, Alarms, Sfx, Ambience, Ui];

    /// <summary>Returns the bus index, appending the bus (sending to <paramref name="sendTo"/>) if it doesn't exist.</summary>
    public static int Ensure(string name, string sendTo = Master)
    {
        var index = AudioServer.GetBusIndex(name);
        if (index >= 0) return index;

        AudioServer.AddBus();
        index = AudioServer.BusCount - 1;
        AudioServer.SetBusName(index, name);
        if (name != Master) AudioServer.SetBusSend(index, sendTo);
        return index;
    }

    /// <summary>Returns the first effect of type <typeparamref name="T"/> on the bus and its slot, adding one if absent.</summary>
    public static (T Effect, int Slot) EnsureEffect<T>(int busIndex, Func<T> create)
        where T : AudioEffect
    {
        ArgumentNullException.ThrowIfNull(create);
        var count = AudioServer.GetBusEffectCount(busIndex);
        for (var slot = 0; slot < count; slot++)
        {
            if (AudioServer.GetBusEffect(busIndex, slot) is T existing) return (existing, slot);
        }

        var effect = create();
        AudioServer.AddBusEffect(busIndex, effect);
        return (effect, count);
    }
}
