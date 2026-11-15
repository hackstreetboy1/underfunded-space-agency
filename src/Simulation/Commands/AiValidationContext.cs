using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands;

/// <summary>The request the response answers.</summary>
/// <param name="RequestId">The AI request id; becomes the command id so retries dedupe.</param>
/// <param name="Addressee">Who the player spoke to, when no crew member is named in the response.</param>
public sealed record AiValidationContext(CommandId RequestId, CommandSource Source, CrewId? Addressee = null);
