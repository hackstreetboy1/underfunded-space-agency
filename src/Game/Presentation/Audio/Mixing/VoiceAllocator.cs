namespace Usa.Game.Presentation.Audio.Mixing;

public readonly record struct VoiceSlot(bool Busy, int Priority, long StartSequence);

/// <summary>Picks a voice in a bus pool: a free one, else the lowest-priority (then oldest) one the new sound may steal.</summary>
public static class VoiceAllocator
{
    /// <returns>The slot index, or -1 when every voice outranks the new sound.</returns>
    public static int Choose(ReadOnlySpan<VoiceSlot> slots, int priority)
    {
        var candidate = -1;
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            if (!slot.Busy) return i;
            if (slot.Priority > priority) continue;
            if (candidate < 0
                || slot.Priority < slots[candidate].Priority
                || (slot.Priority == slots[candidate].Priority && slot.StartSequence < slots[candidate].StartSequence))
            {
                candidate = i;
            }
        }

        return candidate;
    }
}
