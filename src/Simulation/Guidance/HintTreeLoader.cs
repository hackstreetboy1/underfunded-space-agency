using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Guidance;

/// <summary>Loads and validates a mission's hint tree, reporting every error at once.</summary>
public static class HintTreeLoader
{
    public const int MaxTextLength = 240;
    private const int StageCount = 5;

    public static string PathFor(string missionId) => $"missions/{missionId}/hints.json";

    public static HintTree Load(DefinitionLoader loader, string missionId, IExpressionCompiler compiler, GuidanceReferences? references = null)
    {
        ArgumentNullException.ThrowIfNull(loader);
        var path = PathFor(missionId);
        var file = loader.Load<HintsFile>(path, HintsFile.SchemaTag);
        if (file.MissionId != missionId)
            throw new DefinitionException(path, "$.missionId", $"Expected mission '{missionId}' but found '{file.MissionId}'.");
        return Compile(file, path, compiler, references);
    }

    public static HintTree Compile(HintsFile file, string fileLabel, IExpressionCompiler compiler, GuidanceReferences? references = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(compiler);
        references ??= GuidanceReferences.None;
        var errors = new DefinitionErrors();

        if (file.CooldownS < 0) errors.Add(fileLabel, "$.cooldownS", "Must not be negative.");
        CheckTuning(errors, fileLabel, file.Stuck);
        if (file.Problems.Count == 0) errors.Add(fileLabel, "$.problems", "At least one problem is required.");

        var problems = new List<HintProblem>(file.Problems.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < file.Problems.Count; i++)
        {
            var problem = CompileProblem(file.Problems[i], $"$.problems[{i}]", fileLabel, compiler, references, errors);
            if (problem is null) continue;
            if (!ids.Add(problem.Id)) errors.Add(fileLabel, $"$.problems[{i}].id", $"Duplicate problem id '{problem.Id}'.");
            problems.Add(problem);
        }

        errors.ThrowIfAny();
        return new HintTree(file.MissionId, file.CooldownS, file.Stuck, problems);
    }

    private static HintProblem? CompileProblem(
        HintProblemData data, string path, string file, IExpressionCompiler compiler, GuidanceReferences references, DefinitionErrors errors)
    {
        var idOk = GuidanceData.CheckId(errors, file, path + ".id", data.Id, "Problem id");
        var activation = GuidanceData.Compile(compiler, data.Activation, errors, file, path + ".activation", references);
        var resolution = GuidanceData.Compile(compiler, data.Resolution, errors, file, path + ".resolution", references);

        if (data.Stages.Count != StageCount)
        {
            errors.Add(file, path + ".stages", $"Expected {StageCount} stages (nudge to direct) but found {data.Stages.Count}.");
            return null;
        }

        var stages = new HintStageText[StageCount];
        for (var s = 0; s < StageCount; s++)
        {
            var stagePath = $"{path}.stages[{s}]";
            var expected = (HintStage)(s + 1);
            var stage = data.Stages[s];
            if (stage.Stage != expected) errors.Add(file, stagePath + ".stage", $"Expected stage '{expected}' at this position.");
            stages[s] = CompileStage(expected, stage, stagePath, file, compiler, references, errors);
        }

        return idOk && activation is not null && resolution is not null
            ? new HintProblem(data.Id, data.Priority, activation, resolution, stages)
            : null;
    }

    private static HintStageText CompileStage(
        HintStage expected, HintStageData data, string path, string file, IExpressionCompiler compiler, GuidanceReferences references, DefinitionErrors errors)
    {
        GuidanceData.CheckText(errors, file, path + ".text", data.Text, MaxTextLength);
        GuidanceData.CheckFacts(errors, file, path + ".factRefs", data.FactRefs, references);
        GuidanceData.CheckActions(errors, file, path + ".actionRefs", data.ActionRefs, references);
        var content = new HintContent(data.Text, data.FactRefs, data.ActionRefs);

        var alternatives = new List<HintAlternative>();
        var authored = data.Alternatives ?? [];
        for (var a = 0; a < authored.Count; a++)
        {
            var alternative = authored[a];
            var altPath = $"{path}.alternatives[{a}]";
            var when = GuidanceData.Compile(compiler, alternative.When, errors, file, altPath + ".when", references);
            GuidanceData.CheckText(errors, file, altPath + ".text", alternative.Text, MaxTextLength);
            var facts = alternative.FactRefs ?? data.FactRefs;
            var actions = alternative.ActionRefs ?? data.ActionRefs;
            GuidanceData.CheckFacts(errors, file, altPath + ".factRefs", facts, references);
            GuidanceData.CheckActions(errors, file, altPath + ".actionRefs", actions, references);
            if (when is not null) alternatives.Add(new HintAlternative(when, new HintContent(alternative.Text, facts, actions)));
        }

        return new HintStageText(expected, content, alternatives);
    }

    private static void CheckTuning(DefinitionErrors errors, string file, StuckTuning tuning)
    {
        Check(tuning.FailedActionPenaltyS, "failedActionPenaltyS");
        Check(tuning.CriticalAlarmExtraRate, "criticalAlarmExtraRate");
        Check(tuning.NoNewInfoAfterS, "noNewInfoAfterS");
        Check(tuning.NoNewInfoExtraRate, "noNewInfoExtraRate");
        Check(tuning.DiagnosticReliefS, "diagnosticReliefS");
        Check(tuning.RepairReliefS, "repairReliefS");
        Check(tuning.StabilizationReliefS, "stabilizationReliefS");

        void Check(double value, string name)
        {
            if (value < 0 || !double.IsFinite(value)) errors.Add(file, "$.stuck." + name, "Must be a non-negative number.");
        }
    }
}
