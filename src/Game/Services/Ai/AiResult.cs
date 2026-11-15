using System.Diagnostics.CodeAnalysis;

namespace Usa.Game.Services.Ai;

public enum AiFailureKind
{
    /// <summary>The proxy answered with a non-2xx status and (usually) an <c>{error_code, retryable}</c> body.</summary>
    Http,

    /// <summary>No response within the endpoint's attempt timeout.</summary>
    Timeout,

    /// <summary>The proxy could not be reached (connection refused, reset, DNS).</summary>
    Unreachable,

    /// <summary>A 2xx response that does not match the contract.</summary>
    InvalidResponse,

    /// <summary>The caller cancelled.</summary>
    Cancelled,
}

/// <param name="ErrorCode">The proxy's <c>error_code</c>, or a client code such as <c>client_timeout</c>.</param>
/// <param name="HttpStatus">Status code for <see cref="AiFailureKind.Http"/>, otherwise null.</param>
public sealed record AiError(string ErrorCode, bool Retryable, AiFailureKind Kind, int? HttpStatus = null)
{
    /// <summary>Failures that say something about service health (feed the connection monitor).</summary>
    public bool IndicatesOutage => Kind is AiFailureKind.Timeout or AiFailureKind.Unreachable
                                   || (Kind == AiFailureKind.Http && HttpStatus is >= 500);

    public static AiError Cancelled { get; } = new("cancelled", false, AiFailureKind.Cancelled);

    public static AiError ClientTimeout { get; } = new("client_timeout", true, AiFailureKind.Timeout);

    public static AiError Unreachable { get; } = new("backend_unreachable", true, AiFailureKind.Unreachable);

    public static AiError InvalidResponse { get; } = new("invalid_response", false, AiFailureKind.InvalidResponse);
}

/// <summary>Outcome of a gateway call: a value or an <see cref="AiError"/>, never an exception.</summary>
public sealed class AiResult<T>
    where T : class
{
    internal AiResult(T? value, AiError? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }

    public AiError? Error { get; }

    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>Attempts made, including the first (1-3).</summary>
    public int Attempts { get; init; } = 1;

    public override string ToString() => IsSuccess ? $"Success({typeof(T).Name})" : $"Failure({Error.ErrorCode})";
}

public static class AiResult
{
    public static AiResult<T> Success<T>(T value, int attempts = 1)
        where T : class => new(value, null) { Attempts = attempts };

    public static AiResult<T> Failure<T>(AiError error, int attempts = 1)
        where T : class => new(null, error) { Attempts = attempts };
}
