namespace Usa.Game.Presentation.Console.Alarms;

/// <summary>
/// The player acknowledged alarms (click, Ctrl+A or voice). The command pipeline turns it into the
/// <c>acknowledge_alarms</c> meta command; acknowledging never fixes anything.
/// </summary>
/// <param name="Count">Alarms that were unacknowledged and are acknowledged by this request.</param>
public sealed record AcknowledgeAlarmsRequest(int Count)
{
    /// <summary>The one alarm to acknowledge; null acknowledges every active alarm (the only form the session supports today).</summary>
    public string? AlarmId { get; init; }
}
