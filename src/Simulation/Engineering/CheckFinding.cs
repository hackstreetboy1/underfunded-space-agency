namespace Usa.Simulation.Engineering;

/// <summary>One fact behind a check result, in the order it reads; the workspace turns it into a sentence.</summary>
/// <param name="Args">Figures and names for the note's placeholders (see <see cref="CheckNote"/>).</param>
public sealed record CheckFinding(CheckNote Note, IReadOnlyList<CheckArg> Args)
{
    public CheckFinding(CheckNote note)
        : this(note, [])
    {
    }

    /// <summary>The value of <paramref name="name"/>, or null when the finding has none.</summary>
    public string? this[string name]
    {
        get
        {
            foreach (var arg in Args)
            {
                if (string.Equals(arg.Name, name, StringComparison.Ordinal)) return arg.Value;
            }
            return null;
        }
    }
}

/// <summary>A placeholder value: a part or port name, or a figure with its unit (<c>7.5 A</c>, <c>2.5 m</c>).</summary>
/// <param name="IsPlacedPart">
/// The value is a reference that may name a placed part or a fixed port; a placed part is worded as a part
/// (<c>the spare harness</c>), a port by its bare label.
/// </param>
public readonly record struct CheckArg(string Name, string Value, bool IsPlacedPart = false);
