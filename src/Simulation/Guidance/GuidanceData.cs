using System.Text.RegularExpressions;
using Usa.Simulation.Data;
using Usa.Simulation.Kernel;
using Usa.Simulation.Ports;

namespace Usa.Simulation.Guidance;

/// <summary>Validation shared by the hint and tutorial loaders.</summary>
internal static class GuidanceData
{
    private static readonly string[] Placeholders = ["<", ">", "{", "}", "...", "TODO", "TBD", "XXX"];

    // "Gemini" alone is the spacecraft (Gemini 8); only the AI vendor's phrasing names the provider.
    private static readonly Regex AiProviderName = new(
        @"\bgoogle\s+gemini\b|\bgemini[\s\-]+(?:api|ai|flash|pro|ultra|nano)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IExpression? Compile(
        IExpressionCompiler compiler, string? source, DefinitionErrors errors, string file, string jsonPath, GuidanceReferences references)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            errors.Add(file, jsonPath, "Expression is empty.");
            return null;
        }

        IExpression expression;
        try
        {
            expression = compiler.Compile(source);
        }
        catch (ExpressionException ex)
        {
            errors.Add(file, jsonPath, ex.Message);
            return null;
        }

        if (references.State is { } state)
        {
            foreach (var path in expression.Paths)
            {
                if (!state.IsKnownPath(path)) errors.Add(file, jsonPath, $"Unknown state path '@{path}'.");
            }
        }
        return expression;
    }

    /// <summary>Player-facing text: written in full, no placeholders, never the AI provider's name (GDD D15).</summary>
    public static void CheckText(DefinitionErrors errors, string file, string jsonPath, string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            errors.Add(file, jsonPath, "Text is empty.");
            return;
        }

        foreach (var placeholder in Placeholders)
        {
            if (text.Contains(placeholder, StringComparison.Ordinal))
                errors.Add(file, jsonPath, $"Text contains the placeholder '{placeholder}'.");
        }

        if (NamesAiProvider(text))
            errors.Add(file, jsonPath, "Player-facing text must not name the AI provider.");
        if (text.Length > maxLength) errors.Add(file, jsonPath, $"Text is {text.Length} characters; the limit is {maxLength}.");
        if (text[^1] is not ('.' or '?' or '!')) errors.Add(file, jsonPath, "Text must end as a sentence.");
    }

    /// <summary>True when the text names the AI provider ("Gemini API", "Google Gemini"); the spacecraft name "Gemini 8" is fine.</summary>
    public static bool NamesAiProvider(string text) => AiProviderName.IsMatch(text);

    public static bool CheckId(DefinitionErrors errors, string file, string jsonPath, string? id, string kind)
    {
        try
        {
            IdRules.Validate(id ?? "", kind);
            return true;
        }
        catch (ArgumentException ex)
        {
            errors.Add(file, jsonPath, ex.Message);
            return false;
        }
    }

    public static void CheckFacts(DefinitionErrors errors, string file, string jsonPath, IReadOnlyList<FactId> facts, GuidanceReferences references)
    {
        if (references.IsKnownFact is not { } isKnown) return;
        for (var i = 0; i < facts.Count; i++)
        {
            if (!isKnown(facts[i].Value)) errors.Add(file, $"{jsonPath}[{i}]", $"Unknown fact '{facts[i]}'.");
        }
    }

    public static void CheckActions(DefinitionErrors errors, string file, string jsonPath, IReadOnlyList<ActionId> actions, GuidanceReferences references)
    {
        if (references.IsKnownAction is not { } isKnown) return;
        for (var i = 0; i < actions.Count; i++)
        {
            if (!isKnown(actions[i].Value)) errors.Add(file, $"{jsonPath}[{i}]", $"Unknown action '{actions[i]}'.");
        }
    }
}
