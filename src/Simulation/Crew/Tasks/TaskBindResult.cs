using System.Diagnostics.CodeAnalysis;

namespace Usa.Simulation.Crew.Tasks;

/// <summary>Either a <see cref="BoundTask"/> or a player-facing reason code naming the bad parameter.</summary>
public sealed record TaskBindResult
{
    public const string MissingParameter = "missing_parameter";
    public const string UnknownParameter = "unknown_parameter";
    public const string InvalidParameter = "invalid_parameter";

    private TaskBindResult(BoundTask? task, string? errorReason, string? parameter)
    {
        Task = task;
        ErrorReason = errorReason;
        Parameter = parameter;
    }

    public BoundTask? Task { get; }

    public string? ErrorReason { get; }

    public string? Parameter { get; }

    [MemberNotNullWhen(true, nameof(Task))]
    [MemberNotNullWhen(false, nameof(ErrorReason), nameof(Parameter))]
    public bool IsSuccess => Task is not null;

    public static TaskBindResult Success(BoundTask task) => new(task ?? throw new ArgumentNullException(nameof(task)), null, null);

    public static TaskBindResult Failure(string reason, string parameter) => new(null, reason, parameter);
}
