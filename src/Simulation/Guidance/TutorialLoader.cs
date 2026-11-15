using Usa.Simulation.Commands;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Guidance;

/// <summary>Loads and validates a mission's guided-first-run script, reporting every error at once.</summary>
public static class TutorialLoader
{
    /// <summary>Callouts are one short sentence or two; anything longer belongs in the encyclopedia.</summary>
    public const int MaxTextLength = 170;

    /// <summary>A suggested order fits on the callout's send button.</summary>
    public const int MaxOrderTextLength = 40;

    /// <summary>A callout title is a short label on one line (UI.md 5.1, text audit L8).</summary>
    public const int MaxTitleLength = 30;

    /// <summary>A railed step's action and wait lines are one short line each.</summary>
    public const int MaxRailTextLength = 90;

    public static string PathFor(string missionId) => $"missions/{missionId}/tutorial.json";

    public static TutorialScript Load(DefinitionLoader loader, string missionId, IExpressionCompiler compiler, GuidanceReferences? references = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var path = PathFor(missionId);
        var file = loader.Load<TutorialFile>(path, TutorialFile.SchemaTag);
        if (file.MissionId != missionId)
            throw new DefinitionException(path, "$.missionId", $"Expected mission '{missionId}' but found '{file.MissionId}'.");
        return Compile(file, path, compiler, references);
    }

    public static TutorialScript Compile(TutorialFile file, string fileLabel, IExpressionCompiler compiler, GuidanceReferences? references = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(compiler);
        references ??= GuidanceReferences.None;
        var errors = new DefinitionErrors();

        if (file.MinDisplayS < 0) errors.Add(fileLabel, "$.minDisplayS", "Must not be negative.");
        var endWhen = file.EndWhen is null ? null : GuidanceData.Compile(compiler, file.EndWhen, errors, fileLabel, "$.endWhen", references);
        if (file.Beats.Count == 0) errors.Add(fileLabel, "$.beats", "At least one beat is required.");

        var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
        var beats = new List<TutorialBeat>(file.Beats.Count);
        for (var i = 0; i < file.Beats.Count; i++)
        {
            var beat = CompileBeat(file.Beats[i], $"$.beats[{i}]", fileLabel, compiler, references, indexById, errors);
            if (!indexById.TryAdd(file.Beats[i].Id, i)) errors.Add(fileLabel, $"$.beats[{i}].id", $"Duplicate beat id '{file.Beats[i].Id}'.");
            if (beat is not null) beats.Add(beat);
        }

        errors.ThrowIfAny();
        return new TutorialScript(file.MissionId, file.MinDisplayS, endWhen, beats);
    }

    private static TutorialBeat? CompileBeat(
        TutorialBeatData data,
        string path,
        string file,
        IExpressionCompiler compiler,
        GuidanceReferences references,
        Dictionary<string, int> earlierBeats,
        DefinitionErrors errors)
    {
        var idOk = GuidanceData.CheckId(errors, file, path + ".id", data.Id, "Beat id");
        if (GuidanceData.CheckId(errors, file, path + ".anchor", data.Anchor, "Anchor")
            && references.IsKnownAnchor is { } isKnownAnchor && !isKnownAnchor(data.Anchor))
            errors.Add(file, path + ".anchor", $"Unknown UI anchor '{data.Anchor}'.");

        GuidanceData.CheckText(errors, file, path + ".text", data.Text, MaxTextLength);
        if (data.Title is not null) CheckTitle(errors, file, path + ".title", data.Title);
        var trigger = GuidanceData.Compile(compiler, data.Trigger, errors, file, path + ".trigger", references);
        var completion = data.Completion is null ? null : GuidanceData.Compile(compiler, data.Completion, errors, file, path + ".completion", references);

        if (data.MaxDisplayS is { } max && max <= 0) errors.Add(file, path + ".maxDisplayS", "Must be positive.");
        if (data.MaxTimeRate is { } rate && !(rate >= 1)) errors.Add(file, path + ".maxTimeRate", "Must be at least 1 (real time).");
        var order = CompileOrder(data.SuggestedOrder, path + ".suggestedOrder", file, references, errors);
        if (data.Rail is { } rail)
        {
            GuidanceData.CheckText(errors, file, path + ".rail.do", rail.Do, MaxRailTextLength);
            if (rail.Wait is not null) GuidanceData.CheckText(errors, file, path + ".rail.wait", rail.Wait, MaxRailTextLength);
            if (rail.Choose is not null) GuidanceData.CheckId(errors, file, path + ".rail.choose", rail.Choose, "Choice");
            if (rail.View is not null && !TutorialRailData.Views.Contains(rail.View))
                errors.Add(file, path + ".rail.view", $"Unknown view '{rail.View}' (expected {string.Join(" or ", TutorialRailData.Views)}).");
            if (rail.ViewOn is not null)
            {
                GuidanceData.CheckId(errors, file, path + ".rail.viewOn", rail.ViewOn, "Moment");
                if (rail.View is null) errors.Add(file, path + ".rail.viewOn", "Needs a view.");
            }
        }

        var afterIndex = -1;
        if (data.After is { } after && !earlierBeats.TryGetValue(after, out afterIndex))
        {
            errors.Add(file, path + ".after", $"'{after}' is not an earlier beat.");
            afterIndex = -1;
        }

        var alternatives = new List<TutorialTextAlternative>();
        var authored = data.Alternatives ?? [];
        for (var a = 0; a < authored.Count; a++)
        {
            var altPath = $"{path}.alternatives[{a}]";
            var when = GuidanceData.Compile(compiler, authored[a].When, errors, file, altPath + ".when", references);
            GuidanceData.CheckText(errors, file, altPath + ".text", authored[a].Text, MaxTextLength);
            var alternativeOrder = CompileOrder(authored[a].SuggestedOrder, altPath + ".suggestedOrder", file, references, errors);
            if (when is not null) alternatives.Add(new TutorialTextAlternative(when, authored[a].Text, alternativeOrder));
        }

        return idOk && trigger is not null
            ? new TutorialBeat(data.Id, data.Anchor, data.Text, trigger, completion, afterIndex, data.MaxDisplayS, alternatives, data.Title, order, data.MaxTimeRate, data.Rail)
            : null;
    }

    private static TutorialOrder? CompileOrder(TutorialOrderData? data, string path, string file, GuidanceReferences references, DefinitionErrors errors)
    {
        if (data is null) return null;
        var ok = GuidanceData.CheckId(errors, file, path + ".action", data.Action, "Action");
        if (ok && references.IsKnownAction is { } isKnownAction && !isKnownAction(data.Action))
        {
            errors.Add(file, path + ".action", $"Unknown action '{data.Action}'.");
            ok = false;
        }
        if (data.Crew is not null) ok &= GuidanceData.CheckId(errors, file, path + ".crew", data.Crew, "Crew");
        if (string.IsNullOrWhiteSpace(data.Text))
        {
            errors.Add(file, path + ".text", "Text is empty.");
            ok = false;
        }
        else if (data.Text.Length > MaxOrderTextLength)
        {
            errors.Add(file, path + ".text", $"Text is {data.Text.Length} characters; the limit is {MaxOrderTextLength}.");
        }
        if (!ok) return null;
        var args = data.Args is null
            ? new Dictionary<string, CommandArg>(StringComparer.Ordinal)
            : new Dictionary<string, CommandArg>(data.Args, StringComparer.Ordinal);
        return new TutorialOrder(new ActionId(data.Action), data.Crew is null ? null : new CrewId(data.Crew), args, data.Text);
    }

    /// <summary>Sentence case, no closing punctuation, short enough for one line of the callout.</summary>
    private static void CheckTitle(DefinitionErrors errors, string file, string path, string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add(file, path, "Title is empty; leave it out instead.");
            return;
        }
        if (title.Length > MaxTitleLength) errors.Add(file, path, $"Title is {title.Length} characters; the limit is {MaxTitleLength}.");
        if (title[^1] is '.' or '!' or '?' or ':' or ';') errors.Add(file, path, "A title has no closing punctuation.");
        if (!char.IsUpper(title[0])) errors.Add(file, path, "A title starts with a capital letter (sentence case).");
        if (GuidanceData.NamesAiProvider(title))
            errors.Add(file, path, "Player-facing text must not name the AI provider.");
    }
}
