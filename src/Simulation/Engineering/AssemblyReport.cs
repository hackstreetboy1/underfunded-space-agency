namespace Usa.Simulation.Engineering;

/// <summary>The digital test result: one <see cref="CheckResult"/> per <see cref="CheckKind"/>, in enum order.</summary>
/// <param name="Task">The crew task for a passing design; null when any check failed or was skipped.</param>
public sealed record AssemblyReport(IReadOnlyList<CheckResult> Checks, RepairTaskSpec? Task)
{
    public bool Passed => Task is not null;

    public CheckResult this[CheckKind check] => Checks[(int)check];

    public IEnumerable<CheckResult> Failures => Checks.Where(c => c.Failed);
}
