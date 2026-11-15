using Usa.Game.Services.Ai.Contracts;
using Usa.Simulation.Kernel;

namespace Usa.Game.Comms;

/// <summary>How a crew member is named and voiced in comms (from <c>crew/crew.json</c>).</summary>
/// <param name="VoiceId">TTS voice; empty means caption-only speech.</param>
public sealed record CrewProfile(CrewId Id, string Name, string Role, string VoiceId, CrewPersona Persona)
{
    /// <summary>Surname, as the UI uses it ("Okafor is responding…").</summary>
    public string ShortName { get; } = Name.Contains(' ', StringComparison.Ordinal) ? Name[(Name.LastIndexOf(' ') + 1)..] : Name;
}
