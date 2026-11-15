using Usa.Simulation.Commands;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;

namespace Usa.Game.Comms;

/// <summary>
/// Root of <c>content/comms_templates.json</c> (schema <c>comms_templates/1</c>): deterministic lines, refusal reasons
/// by reason code, and the quick-command row. Text holds no digits; values enter only through <c>{fact}</c>.
/// </summary>
/// <param name="Lines">Line templates by key (<see cref="CommsLineKeys"/>).</param>
/// <param name="Reasons">Refusal reason in the crew's words by validator reason code, plus <c>default</c>.</param>
public sealed record CommsTemplatesFile(
    string Schema,
    IReadOnlyDictionary<string, string> Lines,
    IReadOnlyDictionary<string, string> Reasons,
    IReadOnlyList<QuickCommandDefinition> QuickCommands,
    string? Notes = null) : IVersionedDefinition
{
    public const string SchemaTag = "comms_templates/1";
    public const string RelativePath = "content/comms_templates.json";
}

/// <summary>A structured button that submits a command directly, without the AI (GDD 5.6).</summary>
public sealed record QuickCommandDefinition(string Id, string Label, ActionId Action, IReadOnlyDictionary<string, CommandArg>? Args = null);
