using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Scenario.Model;

public sealed record MissionRule(
    string Id,
    IExpression When,
    ReturnRequirement? RequireReturn,
    IReadOnlyList<ObjectiveId> ObjectivesUnacceptable,
    CausePolicy Cause);
