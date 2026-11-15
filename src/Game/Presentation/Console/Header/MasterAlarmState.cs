namespace Usa.Game.Presentation.Console.Header;

public enum MasterAlarmState
{
    Off,

    /// <summary>An unacknowledged warning.</summary>
    Lit,

    /// <summary>An unacknowledged master/critical alarm.</summary>
    Flashing,
}
