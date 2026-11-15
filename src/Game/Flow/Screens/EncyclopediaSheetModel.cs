using Usa.Game.Content;

namespace Usa.Game.Flow.Screens;

public enum EngineerQuestionState
{
    Idle,
    Waiting,
    Answered,
    Failed,
}

/// <param name="ModernReferenceTag">"Modern reference" when the entry describes present-day knowledge (GDD 16 era integrity).</param>
/// <param name="SourceLines">"Title, Publisher" (plus author) per source.</param>
public sealed record EncyclopediaEntryView(
    EncyclopediaEntry Entry,
    string CategoryLabel,
    string EraLabel,
    string? ModernReferenceTag,
    IReadOnlyList<string> SourceLines,
    IReadOnlyList<EncyclopediaEntry> Related);

public sealed record EncyclopediaCategoryOption(string? Id, string Label);

/// <summary>
/// The encyclopedia (UI.md 5): a 760-wide side sheet over the running mission (F1) or full screen from the main menu.
/// Search, category filter, entry detail with sources and era tag, and the "Ask the engineers" box. The question is
/// sent by the host on <see cref="QuestionSubmitted"/>; the answer explains and never recommends fixes.
/// </summary>
public sealed class EncyclopediaSheetModel
{
    public const int SheetWidthPx = 760;
    public const string ModernReferenceEra = "modern reference";

    private readonly Encyclopedia _encyclopedia;

    public EncyclopediaSheetModel(Encyclopedia encyclopedia, StringTable strings)
    {
        ArgumentNullException.ThrowIfNull(encyclopedia);
        ArgumentNullException.ThrowIfNull(strings);
        _encyclopedia = encyclopedia;
        Title = strings.Get("encyclopedia.title");
        SearchLabel = strings.Get("encyclopedia.search");
        AskTitle = strings.Get("encyclopedia.askEngineers");
        AskPlaceholder = strings.Get("encyclopedia.askPlaceholder");
        AskDisclaimer = strings.Get("encyclopedia.askDisclaimer");
        SourcesLabel = strings.Get("encyclopedia.sourcesLabel");

        var categories = new List<EncyclopediaCategoryOption> { new(null, strings.Get("encyclopedia.allCategories")) };
        categories.AddRange(encyclopedia.Categories.Select(c => new EncyclopediaCategoryOption(c.Key, c.Value)));
        Categories = categories;
        Results = encyclopedia.Search(null);
    }

    public event Action? Changed;

    /// <summary>The host sends the question to the engineering advisor and answers with <see cref="ReceiveAnswer"/> or <see cref="ReceiveFailure"/>.</summary>
    public event Action<string>? QuestionSubmitted;

    public string Title { get; }

    public string SearchLabel { get; }

    public string AskTitle { get; }

    public string AskPlaceholder { get; }

    public string AskDisclaimer { get; }

    public string SourcesLabel { get; }

    public IReadOnlyList<EncyclopediaCategoryOption> Categories { get; }

    public string Query { get; private set; } = "";

    public string? Category { get; private set; }

    public IReadOnlyList<EncyclopediaEntry> Results { get; private set; }

    public EncyclopediaEntryView? Selected { get; private set; }

    public string Question { get; private set; } = "";

    public EngineerQuestionState QuestionState { get; private set; } = EngineerQuestionState.Idle;

    public string? Answer { get; private set; }

    public IReadOnlyList<string> AnswerSources { get; private set; } = [];

    public void SetQuery(string? query)
    {
        Query = query ?? "";
        Refresh();
    }

    /// <summary>Null shows every category.</summary>
    public void SetCategory(string? category)
    {
        Category = category is not null && _encyclopedia.Categories.ContainsKey(category) ? category : null;
        Refresh();
    }

    public bool Select(string entryId)
    {
        if (!_encyclopedia.TryGet(entryId, out var entry)) return false;
        Selected = new EncyclopediaEntryView(
            entry,
            _encyclopedia.CategoryLabel(entry.Category),
            _encyclopedia.EraLabel(entry.Era),
            entry.Era == ModernReferenceEra ? _encyclopedia.EraLabel(entry.Era) : null,
            entry.Sources.Select(SourceLine).ToArray(),
            _encyclopedia.Related(entry.Id));
        Changed?.Invoke();
        return true;
    }

    public void ClearSelection()
    {
        Selected = null;
        Changed?.Invoke();
    }

    public void SetQuestion(string? text)
    {
        Question = text ?? "";
        Changed?.Invoke();
    }

    /// <summary>False for an empty question or while one is already waiting for an answer.</summary>
    public bool SubmitQuestion()
    {
        var question = Question.Trim();
        if (question.Length == 0 || QuestionState == EngineerQuestionState.Waiting) return false;
        QuestionState = EngineerQuestionState.Waiting;
        Answer = null;
        AnswerSources = [];
        Changed?.Invoke();
        QuestionSubmitted?.Invoke(question);
        return true;
    }

    public void ReceiveAnswer(string answer, IReadOnlyList<string> sources)
    {
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(sources);
        if (QuestionState != EngineerQuestionState.Waiting) return;
        QuestionState = EngineerQuestionState.Answered;
        Answer = answer;
        AnswerSources = sources;
        Changed?.Invoke();
    }

    public void ReceiveFailure()
    {
        if (QuestionState != EngineerQuestionState.Waiting) return;
        QuestionState = EngineerQuestionState.Failed;
        Changed?.Invoke();
    }

    private void Refresh()
    {
        Results = _encyclopedia.Search(Query, Category);
        Changed?.Invoke();
    }

    private static string SourceLine(EncyclopediaSource source) =>
        source.Author is null ? $"{source.Title}, {source.Publisher}" : $"{source.Author}, {source.Title}, {source.Publisher}";
}
