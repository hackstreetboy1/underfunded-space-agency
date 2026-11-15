namespace Usa.Simulation.Kernel;

/// <summary>Mission elapsed time in whole milliseconds; integer so stepping is exact.</summary>
public readonly record struct MissionTime(long Milliseconds) : IComparable<MissionTime>
{
    public static readonly MissionTime Zero = new(0);

    public double Seconds => Milliseconds / 1000.0;

    public static MissionTime FromSeconds(double seconds) => new((long)Math.Round(seconds * 1000.0));

    public static MissionTime operator +(MissionTime time, long milliseconds) => new(time.Milliseconds + milliseconds);

    public static bool operator <(MissionTime left, MissionTime right) => left.Milliseconds < right.Milliseconds;

    public static bool operator >(MissionTime left, MissionTime right) => left.Milliseconds > right.Milliseconds;

    public static bool operator <=(MissionTime left, MissionTime right) => left.Milliseconds <= right.Milliseconds;

    public static bool operator >=(MissionTime left, MissionTime right) => left.Milliseconds >= right.Milliseconds;

    public int CompareTo(MissionTime other) => Milliseconds.CompareTo(other.Milliseconds);

    /// <summary>Formats as HH:MM:SS; hours keep counting past 24.</summary>
    public override string ToString()
    {
        var totalSeconds = Milliseconds / 1000;
        return $"{totalSeconds / 3600:00}:{totalSeconds / 60 % 60:00}:{totalSeconds % 60:00}";
    }
}
