using Usa.Game.Services.Ai.Contracts;
using Usa.Simulation.Commands;
using Usa.Simulation.Commands.Catalog;
using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;
using CatalogImpact = Usa.Simulation.Commands.Catalog.ActionImpact;
using WireImpact = Usa.Game.Services.Ai.Contracts.ActionImpact;

namespace Usa.Game.Comms;

/// <summary>
/// Builds the <c>/v1/interpret</c> request (SCRIPTING 14.2): the crew roster, the phase allowlist with parameter
/// schemas and impact, and the ids and labels of facts the addressee knows. Never a value: the model sees no telemetry.
/// </summary>
public sealed class InterpretContextBuilder(ICommsSessionPort session, CommsContent content, FactCatalog facts)
{
    public const int MaxPlayerTextLength = 600;
    public const int MaxRecentLines = 8;
    public const int MaxPendingOffers = 4;
    public const int MaxOfferLabelLength = 160;
    public const int MaxClarificationQuestionLength = 300;
    public const int MaxClarificationOptions = 4;

    /// <param name="addressee">A crew member, <see cref="CommandRequest.AnyCrew"/> or null (whole crew).</param>
    /// <param name="recentTranscript">Oldest first; only the last <see cref="MaxRecentLines"/> are sent.</param>
    /// <param name="pendingOffers">Open crew offers, oldest first; only the last <see cref="MaxPendingOffers"/> are sent.</param>
    /// <param name="clarification">The crew's open clarifying question, if any.</param>
    public InterpretRequest Build(
        string requestId, string playerText, CrewId? addressee, IReadOnlyList<string> recentTranscript,
        IReadOnlyList<PendingOffer>? pendingOffers = null, PendingClarification? clarification = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(requestId);
        ArgumentNullException.ThrowIfNull(playerText);
        ArgumentNullException.ThrowIfNull(recentTranscript);

        var text = playerText.Length > MaxPlayerTextLength ? playerText[..MaxPlayerTextLength] : playerText;
        var recent = recentTranscript.Count > MaxRecentLines
            ? recentTranscript.Skip(recentTranscript.Count - MaxRecentLines).ToArray()
            : recentTranscript.ToArray();

        var offers = (pendingOffers ?? [])
            .TakeLast(MaxPendingOffers)
            .Select(o => o with { Label = Cap(o.Label, MaxOfferLabelLength) })
            .ToArray();
        var open = clarification is null
            ? null
            : clarification with
            {
                Question = Cap(clarification.Question, MaxClarificationQuestionLength),
                Options = clarification.Options.Take(MaxClarificationOptions).ToArray(),
            };

        return new InterpretRequest(requestId, text, addressee?.Value, session.Phase, Crew(), Actions(), KnownFacts(addressee))
        {
            RecentTranscript = recent,
            PendingOffers = offers,
            PendingClarification = open,
            Personas = Personas(),
            ReadableFacts = ReadableFacts(),
        };
    }

    public const int MaxReadableFacts = 96;

    /// <summary>Each living crew member's voice and current stress (narration's persona, so replies sound the same).</summary>
    private List<InterpretPersona> Personas()
    {
        var personas = new List<InterpretPersona>();
        foreach (var member in session.Crew)
        {
            if (!member.IsAlive || !content.TryGetProfile(member.Id, out var profile)) continue;
            var persona = profile.Persona;
            personas.Add(new InterpretPersona(member.Id.Value, persona.Name)
            {
                Role = persona.Role,
                Personality = persona.Personality,
                Cadence = persona.Cadence,
                Warmth = persona.Warmth,
                Humor = persona.Humor,
                StressExpression = persona.StressExpression,
                SampleLines = persona.SampleLines.Take(5).ToArray(),
                Stress = StressLevels.FromHealth(member.HealthPercent),
            });
            if (personas.Count == 4) break;
        }
        return personas;
    }

    /// <summary>Gauges an allowed glance read covers right now, id and label; the model may name any of them.</summary>
    private List<IReadOnlyDictionary<string, string>> ReadableFacts()
    {
        var readable = new List<IReadOnlyDictionary<string, string>>();
        foreach (var id in GlanceReadPlanner.Readable(session.AllowedActions))
        {
            if (!facts.TryResolve(id, out var fact)) continue;
            readable.Add(new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = id.Value, ["label"] = fact.Label });
            if (readable.Count == MaxReadableFacts) break;
        }
        return readable;
    }

    /// <summary>Facts the addressee knows; for the whole crew, the union over living members. Id order.</summary>
    public IReadOnlyList<FactId> KnownFactIds(CrewId? addressee)
    {
        var known = new SortedSet<FactId>();
        foreach (var member in session.Crew)
        {
            if (!member.IsAlive) continue;
            if (IsSpecific(addressee) && member.Id != addressee) continue;
            known.UnionWith(session.FactsKnownBy(member.Id));
        }
        return known.ToArray();
    }

    private List<IReadOnlyDictionary<string, string>> Crew()
    {
        var crew = new List<IReadOnlyDictionary<string, string>>();
        foreach (var member in session.Crew)
        {
            content.TryGetProfile(member.Id, out var profile);
            crew.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["id"] = member.Id.Value,
                ["name"] = profile?.Name ?? member.Id.Value,
                ["role"] = profile?.Role ?? "",
                ["status"] = !member.IsAlive ? "deceased" : member.IsBusy ? "busy" : "available",
            });
        }
        return crew;
    }

    private List<ActionSpec> Actions()
    {
        var actions = new List<ActionSpec>();
        foreach (var action in session.AllowedActions)
        {
            var parameters = new List<ActionParam>(action.Parameters.Count);
            foreach (var parameter in action.Parameters)
            {
                parameters.Add(new ActionParam(parameter.Name, ToWire(parameter.Type)) { EnumValues = parameter.Values ?? [] });
            }
            actions.Add(new ActionSpec(action.Id.Value, action.Definition.Description, ToWire(action.BaseImpact)) { Params = parameters });
        }
        return actions;
    }

    private List<IReadOnlyDictionary<string, string>> KnownFacts(CrewId? addressee)
    {
        var known = new List<IReadOnlyDictionary<string, string>>();
        foreach (var id in KnownFactIds(addressee))
        {
            if (!facts.TryResolve(id, out var fact)) continue;
            known.Add(new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = id.Value, ["label"] = fact.Label });
        }
        return known;
    }

    private static string Cap(string text, int length) => text.Length <= length ? text : text[..length];

    private static bool IsSpecific(CrewId? addressee) => addressee is { } id && id != CommandRequest.AnyCrew;

    private static ActionParamType ToWire(ParameterType type) => type switch
    {
        ParameterType.String => ActionParamType.String,
        ParameterType.Number => ActionParamType.Number,
        ParameterType.Integer => ActionParamType.Integer,
        ParameterType.Boolean => ActionParamType.Boolean,
        _ => ActionParamType.Enum,
    };

    private static WireImpact ToWire(CatalogImpact impact) => impact switch
    {
        CatalogImpact.Low => WireImpact.Low,
        CatalogImpact.High => WireImpact.High,
        _ => WireImpact.Irreversible,
    };
}
