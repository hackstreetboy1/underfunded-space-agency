using Usa.Simulation.Kernel;

namespace Usa.Simulation.Knowledge;

/// <summary>A dialogue template with its <c>&lt;FACT:id&gt;</c> tokens resolved, or the reasons it was rejected.</summary>
/// <param name="Text">Resolved text; empty when the template was rejected.</param>
/// <param name="Facts">One entry per token, in template order (for the UI's fact chips).</param>
public sealed record FactTemplateResult(bool Succeeded, string Text, IReadOnlyList<ResolvedFact> Facts, IReadOnlyList<string> Errors);

/// <summary>A resolved token: the speaker's observation (null when they never observed it) and the text used.</summary>
public sealed record ResolvedFact(FactId Fact, Observation? Observation, bool IsStale, string Text);
