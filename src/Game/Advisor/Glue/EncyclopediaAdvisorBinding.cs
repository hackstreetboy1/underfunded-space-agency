using Usa.Game.Flow.Screens;

namespace Usa.Game.Advisor.Glue;

/// <summary>
/// Connects the encyclopedia sheet's "Ask the engineers" box to <see cref="AdvisorService"/>: a submitted question is
/// answered with the engineers' text and "Title (era)" source labels; an uncovered question, a question the service
/// cannot take now (another sheet's question is in flight) or an unexpected error shows the sheet's failure state; a
/// duplicate re-shows the answer that question already got. The sheet never stays waiting.
/// </summary>
public sealed class EncyclopediaAdvisorBinding : IDisposable
{
    private readonly EncyclopediaSheetModel _sheet;
    private readonly AdvisorService _advisor;
    private readonly CancellationTokenSource _lifetime = new();

    public EncyclopediaAdvisorBinding(EncyclopediaSheetModel sheet, AdvisorService advisor)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(advisor);
        _sheet = sheet;
        _advisor = advisor;
        _sheet.QuestionSubmitted += OnQuestionSubmitted;
    }

    public void Dispose()
    {
        _sheet.QuestionSubmitted -= OnQuestionSubmitted;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    public static string SourceLabel(AdvisorSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return $"{source.Title} ({source.EraLabel})";
    }

    private void OnQuestionSubmitted(string question) => _ = AnswerAsync(question, _lifetime.Token);

    private async Task AnswerAsync(string question, CancellationToken cancellationToken)
    {
        AdvisorReply reply;
        try
        {
            reply = await _advisor.AskAsync(question, cancellationToken);
        }
        catch (Exception)
        {
            // Fire-and-forget: an escaping exception would be unobserved and leave the sheet waiting.
            if (!cancellationToken.IsCancellationRequested) _sheet.ReceiveFailure();
            return;
        }

        switch (reply.Kind)
        {
            case AdvisorReplyKind.Answered or AdvisorReplyKind.Duplicate when reply.Answer is { } answer:
                Show(answer);
                break;
            case AdvisorReplyKind.Cancelled:
                // Only a disposed binding or service cancels; the sheet is closing.
                break;
            default:
                // NotCovered, Busy (the sheet's own Waiting state prevents a second ask, so the question in flight
                // belongs to another sheet), Empty, or a duplicate of an uncovered question.
                _sheet.ReceiveFailure();
                break;
        }
    }

    private void Show(AdvisorAnswer answer) => _sheet.ReceiveAnswer(answer.Text, answer.Sources.Select(SourceLabel).ToArray());
}
