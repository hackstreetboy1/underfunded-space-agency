using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;

namespace Usa.Simulation.Commands;

/// <summary>A request whose action, assignee and arguments have been resolved and checked.</summary>
/// <param name="Assignee">The crew member carrying it out; null for remote and crew-less meta actions.</param>
/// <param name="Impact">The action's impact for these arguments.</param>
public sealed record ResolvedCommand(CommandRequest Request, CatalogAction Action, CrewId? Assignee, ParsedArguments Args, ActionImpact Impact);

/// <summary>A validation result plus the resolved command when it is valid or awaiting confirmation.</summary>
public sealed record CommandValidation(ValidationResult Result, ResolvedCommand? Command);
