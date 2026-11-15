using System.Diagnostics.CodeAnalysis;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Knowledge.Catalog;

/// <summary>
/// Every reportable fact: concrete facts (index patterns expanded at load) plus open patterns such as
/// <c>component.&lt;componentId&gt;.condition</c> and <c>crew.&lt;crewId&gt;.condition</c>.
/// </summary>
public sealed class FactCatalog
{
    public const string SchemaTag = "facts/1";
    public const string RelativePath = "catalog/facts.json";

    /// <summary>Prefix of the ground-report paths a <see cref="FactSourceKind.Report"/> fact reads (Session <c>GroundReportPaths</c>).</summary>
    public const string ReportPrefix = "report.";

    private static readonly int MethodCount = Enum.GetValues<ObservationMethod>().Length;

    /// <summary>Ground telemetry dump: precise sensor readings with no observer; never confirms a component.</summary>
    public static MethodCapabilities TelemetryCapabilities { get; } =
        new(ObservationMethod.Telemetry, RequiresObserver: false, CanConfirmComponent: false, ObservationYield.Measurement);

    private readonly Dictionary<string, FactDefinition> _byId;
    private readonly Dictionary<string, FactDefinition>.AlternateLookup<ReadOnlySpan<char>> _byIdSpan;
    private readonly Dictionary<FactId, int> _entryIndex;
    private readonly Dictionary<string, FactId[]> _conflictGroups;
    private readonly FactPattern[] _patterns;
    private readonly MethodCapabilities[] _capabilities;
    private readonly string _fileLabel;

    private FactCatalog(
        string fileLabel,
        FactPhrases phrases,
        MethodCapabilities[] capabilities,
        Dictionary<string, FactDefinition> byId,
        Dictionary<FactId, int> entryIndex,
        FactPattern[] patterns)
    {
        _fileLabel = fileLabel;
        Phrases = phrases;
        _capabilities = capabilities;
        _byId = byId;
        _byIdSpan = byId.GetAlternateLookup<ReadOnlySpan<char>>();
        _entryIndex = entryIndex;
        _patterns = patterns;

        var facts = byId.Values.ToList();
        facts.Sort((a, b) => a.Id.CompareTo(b.Id));
        Facts = facts;
        Patterns = patterns.Select(p => p.Id).Order(StringComparer.Ordinal).ToArray();

        _conflictGroups = new Dictionary<string, FactId[]>(StringComparer.Ordinal);
        foreach (var group in facts.Where(f => f.ConflictGroup is not null).GroupBy(f => f.ConflictGroup!, StringComparer.Ordinal))
            _conflictGroups[group.Key] = group.Select(f => f.Id).ToArray();
    }

    public FactPhrases Phrases { get; }

    /// <summary>Concrete facts sorted by id (index patterns expanded; open patterns excluded).</summary>
    public IReadOnlyList<FactDefinition> Facts { get; }

    /// <summary>Open pattern ids, e.g. <c>component.{component}.condition</c>.</summary>
    public IReadOnlyList<string> Patterns { get; }

    public static FactCatalog Load(IDataSource source) =>
        FromFile(new DefinitionLoader(source).Load<FactCatalogFile>(RelativePath, SchemaTag), RelativePath);

    /// <exception cref="DefinitionException">Every problem in the file at once.</exception>
    public static FactCatalog FromFile(FactCatalogFile file, string fileLabel)
    {
        ArgumentNullException.ThrowIfNull(file);
        var errors = new DefinitionErrors();
        var catalog = Build(file, fileLabel, errors);
        errors.ThrowIfAny();
        return catalog!;
    }

    public MethodCapabilities Capabilities(ObservationMethod method) => _capabilities[(int)method];

    public bool TryResolve(FactId id, [NotNullWhen(true)] out FactDefinition? definition)
    {
        if (_byId.TryGetValue(id.Value, out definition)) return true;
        foreach (var pattern in _patterns)
        {
            if (!pattern.Matches(id.Value)) continue;
            definition = pattern.Resolve(id);
            return true;
        }
        definition = null;
        return false;
    }

    public FactDefinition Get(FactId id) =>
        TryResolve(id, out var definition) ? definition : throw new KeyNotFoundException($"Unknown fact '{id}'.");

    /// <summary>True when <paramref name="factId"/> names a concrete fact or matches a pattern. Allocation-free.</summary>
    public bool IsKnownFact(ReadOnlySpan<char> factId)
    {
        if (_byIdSpan.ContainsKey(factId)) return true;
        foreach (var pattern in _patterns)
        {
            if (pattern.Matches(factId)) return true;
        }
        return false;
    }

    /// <summary>Every fact in the same conflict group (including <paramref name="id"/>), or empty.</summary>
    public IReadOnlyList<FactId> ConflictGroupOf(FactId id) =>
        TryResolve(id, out var definition) && definition.ConflictGroup is { } group && _conflictGroups.TryGetValue(group, out var members)
            ? members
            : [];

    /// <summary>The fact that confirms <paramref name="component"/>'s condition, e.g. <c>component.tandem/oams/thruster_08.condition</c>.</summary>
    public FactId ComponentConditionFact(ComponentId component) => new(PatternOf(FactMatcherKind.Component).IdFor(component.Value));

    /// <summary>A crew member's self-reported condition, e.g. <c>crew.june_okafor.condition</c>.</summary>
    public FactId CrewConditionFact(CrewId crew) => new(PatternOf(FactMatcherKind.Crew).IdFor(crew.Value));

    /// <summary>Checks references to other areas' ids and state; adds every problem to <paramref name="errors"/>.</summary>
    public void ValidateReferences(FactReferenceContext context, DefinitionErrors errors)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(errors);
        foreach (var fact in Facts)
        {
            var path = $"$.facts[{_entryIndex[fact.Id]}]";
            if (context.Components is { } components && fact.SourceComponent is { } component && !components.Contains(component))
                errors.Add(_fileLabel, path + ".source", $"Fact '{fact.Id}' reads unknown component '{component}'.");
            if (context.Crew is { } crew && fact.SourceCrew is { } member && !crew.Contains(member))
                errors.Add(_fileLabel, path + ".source", $"Fact '{fact.Id}' names unknown crew member '{member}'.");
            if (context.Tasks is { } tasks && fact.Task is { } task && !tasks.Contains(task))
                errors.Add(_fileLabel, path + ".task", $"Fact '{fact.Id}' names unknown task '{task}'.");
            if (context.Telemetry is { } telemetry && fact.Downlink is { } downlink && !telemetry.IsKnownPath(downlink.TelemetryPath))
                errors.Add(_fileLabel, path + ".downlink.telemetryPath", $"Fact '{fact.Id}' samples unknown state path '{downlink.TelemetryPath}'.");
            if (context.Expressions is { } compiler && fact.ValuePath is { } valuePath)
                ValidateValuePath(fact, valuePath, compiler, context.Telemetry, path + ".valuePath", errors);
        }
    }

    private void ValidateValuePath(FactDefinition fact, string valuePath, IExpressionCompiler compiler, IStateQuery? state, string path, DefinitionErrors errors)
    {
        IExpression expression;
        try
        {
            expression = compiler.Compile(valuePath);
        }
        catch (ExpressionException ex)
        {
            errors.Add(_fileLabel, path, $"Fact '{fact.Id}': {ex.Message}");
            return;
        }
        if (state is null) return;
        var unknownCount = 0;
        foreach (var unknown in expression.UnknownPaths(state))
        {
            errors.Add(_fileLabel, path, $"Fact '{fact.Id}' reads unknown state path '{unknown}'.");
            unknownCount++;
        }
        if (unknownCount > 0) return;

        // Evaluating once turns a kind mismatch into a load error instead of a mid-tick exception.
        var expected = fact.Kind == FactValueKind.Number ? StateValueKind.Number : StateValueKind.Text;
        try
        {
            var kind = expression.Evaluate(state).Kind;
            if (kind != expected) errors.Add(_fileLabel, path, $"Fact '{fact.Id}' is a {expected} fact but its value path gives {kind}.");
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(_fileLabel, path, $"Fact '{fact.Id}': {ex.Message}");
        }
    }

    private FactPattern PatternOf(FactMatcherKind kind) =>
        _patterns.FirstOrDefault(p => p.Kind == kind)
        ?? throw new InvalidOperationException($"The fact catalog has no {kind.ToString().ToLowerInvariant()} pattern.");

    private static FactCatalog? Build(FactCatalogFile file, string label, DefinitionErrors errors)
    {
        var capabilities = BuildCapabilities(file.Methods, label, errors);
        if (string.IsNullOrWhiteSpace(file.Phrases.Unobserved))
            errors.Add(label, "$.phrases.unobserved", "Phrase is empty.");
        if (!file.Phrases.StaleSuffix.Contains("{met}", StringComparison.Ordinal))
            errors.Add(label, "$.phrases.staleSuffix", "Phrase must contain {met}.");

        var byId = new Dictionary<string, FactDefinition>(StringComparer.Ordinal);
        var entryIndex = new Dictionary<FactId, int>();
        var patterns = new List<FactPattern>();

        for (var i = 0; i < file.Facts.Count; i++)
        {
            var entry = file.Facts[i];
            var path = $"$.facts[{i}]";
            ValidateEntry(entry, path, label, capabilities, errors);

            switch (entry.Matcher?.Kind)
            {
                case null:
                    AddConcrete(entry, entry.Id, static text => text);
                    break;
                case FactMatcherKind.Index when entry.Matcher.From >= 0 && entry.Matcher.To >= entry.Matcher.From:
                    for (var index = entry.Matcher.From; index <= entry.Matcher.To; index++)
                    {
                        var n = index;
                        AddConcrete(entry, FactPattern.ExpandIndex(entry.Id, n), text => FactPattern.ExpandIndex(text, n));
                    }
                    break;
                case FactMatcherKind.Component or FactMatcherKind.Crew:
                    var placeholder = FactPattern.PlaceholderFor(entry.Matcher.Kind);
                    if (CountOf(entry.Id, placeholder) != 1) break;
                    var pattern = new FactPattern(entry, i);
                    if (patterns.Any(p => p.Kind == pattern.Kind))
                    {
                        errors.Add(label, path + ".matcher", $"Only one {placeholder} pattern is allowed.");
                        break;
                    }
                    var sample = pattern.Kind == FactMatcherKind.Component ? "vehicle/sample" : "sample";
                    if (TryCreate(entry, pattern.IdFor(sample), text => text.Replace(placeholder, sample, StringComparison.Ordinal), path, out _))
                        patterns.Add(pattern);
                    break;
            }

            void AddConcrete(FactEntry fact, string id, Func<string, string> substitute)
            {
                if (!TryCreate(fact, id, substitute, path, out var definition)) return;
                if (!byId.TryAdd(id, definition))
                {
                    errors.Add(label, path + ".id", $"Duplicate fact id '{id}'.");
                    return;
                }
                entryIndex[definition.Id] = i;
            }
        }

        ValidateConflictGroups(byId.Values, entryIndex, label, errors);
        return errors.HasErrors ? null : new FactCatalog(label, file.Phrases, capabilities, byId, entryIndex, patterns.ToArray());

        bool TryCreate(FactEntry entry, string id, Func<string, string> substitute, string path, [NotNullWhen(true)] out FactDefinition? definition)
        {
            try
            {
                definition = FactDefinitionFactory.Create(entry, id, substitute);
                return true;
            }
            catch (ArgumentException ex)
            {
                errors.Add(label, path, ex.Message);
                definition = null;
                return false;
            }
        }
    }

    private static MethodCapabilities[] BuildCapabilities(IReadOnlyList<MethodCapabilities> methods, string label, DefinitionErrors errors)
    {
        var capabilities = new MethodCapabilities[MethodCount];
        for (var i = 0; i < methods.Count; i++)
        {
            var method = methods[i];
            var path = $"$.methods[{i}]";
            if (capabilities[(int)method.Method] is not null)
                errors.Add(label, path + ".method", $"Method '{method.Method}' is listed twice.");
            capabilities[(int)method.Method] = method;
            if (method.CanConfirmComponent && (method.Yield != ObservationYield.Measurement || !method.RequiresObserver))
                errors.Add(label, path, "Only crew measurements can confirm a component.");
            if (method.Method == ObservationMethod.Downlink && (method.Yield != ObservationYield.Band || method.RequiresObserver))
                errors.Add(label, path, "Downlink must yield bands and have no observer.");
            if (method.Method == ObservationMethod.Telemetry && method != TelemetryCapabilities)
                errors.Add(label, path, "Telemetry yields measurements, has no observer and cannot confirm a component.");
        }
        // Files written before the telemetry method existed get its fixed capabilities.
        capabilities[(int)ObservationMethod.Telemetry] ??= TelemetryCapabilities;
        foreach (var method in Enum.GetValues<ObservationMethod>())
        {
            if (capabilities[(int)method] is null)
            {
                errors.Add(label, "$.methods", $"Method '{method}' has no capabilities.");
                capabilities[(int)method] = new MethodCapabilities(method, false, false, ObservationYield.Estimate);
            }
        }
        return capabilities;
    }

    private static void ValidateEntry(FactEntry entry, string path, string label, MethodCapabilities[] capabilities, DefinitionErrors errors)
    {
        void Error(string member, string message) => errors.Add(label, member.Length == 0 ? path : $"{path}.{member}", message);

        if (string.IsNullOrWhiteSpace(entry.Label)) Error("label", "Label is empty.");
        if (!(entry.StaleAfterS > 0) || double.IsInfinity(entry.StaleAfterS)) Error("staleAfterS", "Must be a positive number of seconds.");
        if (!(entry.ConflictTolerance >= 0) || double.IsInfinity(entry.ConflictTolerance)) Error("conflictTolerance", "Must be zero or positive.");
        if (entry.Decimals is < 0 or > 6) Error("decimals", "Must be between 0 and 6.");
        if ((entry.Kind == FactValueKind.Text) != (entry.Format == FactFormat.Text)) Error("format", "Text facts use format 'text'; number facts use a numeric format.");
        if (entry.NominalValues is not null && entry.Kind != FactValueKind.Text) Error("nominalValues", "Only text facts have nominal values.");
        if (entry.ValuePath is not null && string.IsNullOrWhiteSpace(entry.ValuePath)) Error("valuePath", "Value path is empty; leave it out to derive the value from the source.");

        if (entry.Methods.Count == 0) Error("methods", "At least one method is required.");
        for (var m = 0; m < entry.Methods.Count; m++)
        {
            var method = entry.Methods[m];
            if (method == ObservationMethod.Downlink) Error($"methods[{m}]", "Downlink comes from the 'downlink' block, not 'methods'.");
            for (var k = 0; k < m; k++)
            {
                if (entry.Methods[k] == method) Error($"methods[{m}]", $"Method '{method}' is listed twice.");
            }
            if (entry.ConfirmsComponent && !capabilities[(int)method].CanConfirmComponent)
                Error($"methods[{m}]", $"Method '{method}' cannot confirm a component.");
            if (method == ObservationMethod.Telemetry && entry.SourceKind is not (FactSourceKind.Sensor or FactSourceKind.Report))
                Error($"methods[{m}]", "Telemetry only downlinks sensor readings and ground reports; it cannot establish direct checks, component conditions or crew self-reports.");
            if (method == ObservationMethod.Telemetry && entry.SourceKind == FactSourceKind.Sensor && entry.ValuePath is not null)
                Error($"methods[{m}]", "Telemetry reads the sensor itself; a fact with a valuePath (a truth expression) cannot allow it.");
        }

        if (entry.SourceKind == FactSourceKind.Report)
        {
            // A ground report is what the ground was told, never a reading of hidden truth: telemetry only, from report.* paths.
            if (entry.Methods.Count != 1 || entry.Methods[0] != ObservationMethod.Telemetry) Error("methods", "A report fact allows the telemetry method only.");
            if (!IsReportPath(entry.ValuePath)) Error("valuePath", $"A report fact reads its value from one '@{ReportPrefix}<field>' path and nothing else.");
            if (entry.Downlink is not null) Error("downlink", "A report fact has no downlink band.");
        }

        switch (entry.ReadMode)
        {
            case FactReadMode.Glance when !entry.Methods.Contains(ObservationMethod.Glance):
                Error("methods", "A glance fact must allow the glance method.");
                break;
            case FactReadMode.Inspection when !entry.Methods.Contains(ObservationMethod.Inspection):
                Error("methods", "An inspection fact must allow the inspection method.");
                break;
            case FactReadMode.Task when entry.Task is null:
                Error("task", "A task fact names the task that produces it.");
                break;
            case FactReadMode.Downlink when entry.Downlink is null:
                Error("downlink", "A downlink fact needs a downlink block.");
                break;
            case FactReadMode.Telemetry when !entry.Methods.Contains(ObservationMethod.Telemetry):
                Error("methods", "A telemetry fact must allow the telemetry method.");
                break;
        }
        if (entry.Task is not null && entry.ReadMode != FactReadMode.Task) Error("task", "Only task facts name a task.");

        if (entry.Downlink is { } downlink) ValidateDownlink(entry.Kind, downlink, Error);

        if (entry.ConfirmsComponent)
        {
            if (entry.Matcher?.Kind != FactMatcherKind.Component) Error("confirmsComponent", "Only the component pattern confirms components.");
            if (entry.Kind != FactValueKind.Text) Error("kind", "Component conditions are text.");
        }

        ValidateMatcher(entry, Error);
    }

    private static void ValidateDownlink(FactValueKind kind, DownlinkEntry downlink, Action<string, string> error)
    {
        if (string.IsNullOrWhiteSpace(downlink.TelemetryPath)) error("downlink.telemetryPath", "Telemetry path is empty.");
        if (kind == FactValueKind.Number)
        {
            if (downlink.Words is not null) error("downlink.words", "Number facts use bands.");
            if (downlink.Bands is not { Count: > 0 } bands)
            {
                error("downlink.bands", "At least one band is required.");
                return;
            }
            for (var b = 0; b < bands.Count; b++)
            {
                var member = $"downlink.bands[{b}]";
                if (string.IsNullOrWhiteSpace(bands[b].Word)) error(member + ".word", "Band word is empty.");
                if (b == 0 && bands[b].From is not null) error(member + ".from", "The first band has no lower bound.");
                if (b > 0 && bands[b].From is null) error(member + ".from", "Every band after the first needs 'from'.");
                else if (b > 1 && bands[b].From <= bands[b - 1].From) error(member + ".from", "Bands must be in ascending order.");
            }
        }
        else
        {
            if (downlink.Bands is not null) error("downlink.bands", "Text facts use words.");
            if (downlink.Words is not { Count: > 0 } words)
            {
                error("downlink.words", "At least one word is required.");
                return;
            }
            for (var w = 0; w < words.Count; w++)
            {
                if (string.IsNullOrEmpty(words[w].Value) || string.IsNullOrWhiteSpace(words[w].Word))
                    error($"downlink.words[{w}]", "Value and word are required.");
            }
        }
    }

    private static void ValidateMatcher(FactEntry entry, Action<string, string> error)
    {
        switch (entry.Matcher)
        {
            case null:
                if (entry.Id.Contains('{', StringComparison.Ordinal)) error("id", "Placeholders need a matcher.");
                break;
            case { Kind: FactMatcherKind.Index } index:
                if (!entry.Id.Contains("{nn}", StringComparison.Ordinal) && !entry.Id.Contains("{n}", StringComparison.Ordinal))
                    error("id", "An index pattern contains {nn} or {n}.");
                if (index.From < 0 || index.To < index.From || index.To > 99)
                    error("matcher", "Index range must satisfy 0 <= from <= to <= 99.");
                break;
            case { Kind: var kind }:
                var placeholder = FactPattern.PlaceholderFor(kind);
                if (CountOf(entry.Id, placeholder) != 1) error("id", $"The pattern contains {placeholder} exactly once.");
                if (entry.Source != placeholder) error("source", $"The source of this pattern is {placeholder}.");
                var expected = kind == FactMatcherKind.Crew ? FactSourceKind.Crew : FactSourceKind.Component;
                if (entry.SourceKind != expected) error("sourceKind", $"This pattern's source kind is '{expected}'.");
                if (entry.ConflictGroup is not null) error("conflictGroup", "Open patterns cannot join a conflict group.");
                break;
        }
    }

    private static void ValidateConflictGroups(IEnumerable<FactDefinition> facts, Dictionary<FactId, int> entryIndex, string label, DefinitionErrors errors)
    {
        foreach (var group in facts.Where(f => f.ConflictGroup is not null).GroupBy(f => f.ConflictGroup!, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var members = group.OrderBy(f => f.Id).ToArray();
            var path = $"$.facts[{entryIndex[members[0].Id]}].conflictGroup";
            if (members.Length < 2) errors.Add(label, path, $"Conflict group '{group.Key}' needs at least two facts.");
            if (members.Any(f => f.Kind != members[0].Kind)) errors.Add(label, path, $"Conflict group '{group.Key}' mixes number and text facts.");
        }
    }

    private static bool IsReportPath(string? valuePath)
    {
        var text = valuePath?.Trim();
        if (text is null || !text.StartsWith("@" + ReportPrefix, StringComparison.Ordinal) || text.Length == ReportPrefix.Length + 1) return false;
        foreach (var c in text.AsSpan(1))
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '/' or '-')) return false;
        }
        return true;
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
