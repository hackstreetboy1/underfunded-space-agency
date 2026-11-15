namespace Usa.Game.Services.Storage;

public enum StoreReadStatus
{
    Ok,
    Missing,
    IoError,
}

/// <param name="Text">File contents when <paramref name="Status"/> is <see cref="StoreReadStatus.Ok"/>.</param>
public readonly record struct StoreReadResult(StoreReadStatus Status, string? Text)
{
    public static StoreReadResult Missing { get; } = new(StoreReadStatus.Missing, null);

    public static StoreReadResult IoError { get; } = new(StoreReadStatus.IoError, null);
}
