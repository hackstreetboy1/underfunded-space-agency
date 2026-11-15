using System.Globalization;
using System.Text;
using Usa.Game.Services.Logging;

namespace Usa.Game.Services.Storage;

/// <summary>
/// Small text files under one root (the globalized <c>user://</c> in the game, a temp folder in tests).
/// Writes are atomic (temp file, flushed to disk, then rename); <see cref="WriteTextAtomicAsync"/> does the same off the
/// calling thread. Thread-safe: the store keeps no mutable state. IO failures are logged and reported as values, never thrown;
/// a path escaping the root, or a file path naming no file (empty, the root itself, a trailing separator), is a
/// programmer error and throws <see cref="ArgumentException"/> before anything touches the disk.
/// </summary>
public sealed class UserFileStore(string rootDirectory, Log log, TimeProvider? time = null)
{
    public const int DefaultCorruptCopiesKept = 3;
    public static readonly TimeSpan StaleTempAge = TimeSpan.FromMinutes(10);

    private const string Category = "Storage";
    private const string TempMarker = ".tmp-";
    private const string CorruptMarker = ".corrupt-";
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public string RootDirectory { get; } = Path.GetFullPath(rootDirectory);

    public bool Exists(string relativePath) => File.Exists(ResolveFile(relativePath));

    public StoreReadResult ReadText(string relativePath)
    {
        var path = ResolveFile(relativePath);
        try
        {
            return File.Exists(path) ? new StoreReadResult(StoreReadStatus.Ok, File.ReadAllText(path, Encoding.UTF8)) : StoreReadResult.Missing;
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            log.Warn(Category, $"read failed {relativePath}: {ex.GetType().Name}");
            return StoreReadResult.IoError;
        }
    }

    public bool WriteTextAtomic(string relativePath, string text)
    {
        var path = ResolveFile(relativePath);
        var temp = $"{path}{TempMarker}{Guid.NewGuid():N}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Utf8NoBom.GetBytes(text));
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            log.Warn(Category, $"write failed {relativePath}: {ex.GetType().Name}");
            TryDelete(temp);
            return false;
        }
    }

    /// <summary>
    /// <see cref="WriteTextAtomic"/> on a thread-pool thread, so the fsync never stalls the caller (the main thread).
    /// Same semantics: temp file, flush to disk, atomic rename; IO failures are logged and return false; a bad path
    /// throws here, on the caller's thread. The store holds no state, so this is safe beside other calls, but writes to
    /// one path should be serialized by the caller: two in flight race to the rename and the older text could land last.
    /// </summary>
    public Task<bool> WriteTextAtomicAsync(string relativePath, string text)
    {
        ResolveFile(relativePath);
        ArgumentNullException.ThrowIfNull(text);
        return Task.Run(() => WriteTextAtomic(relativePath, text));
    }

    /// <summary>Renames a damaged file to <c>&lt;name&gt;.corrupt-&lt;utc stamp&gt;</c>. Returns the new relative path, or null.</summary>
    public string? MoveAside(string relativePath)
    {
        var path = ResolveFile(relativePath);
        var stamp = _time.GetUtcNow().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var target = $"{path}{CorruptMarker}{stamp}";
        try
        {
            if (!File.Exists(path)) return null;
            if (File.Exists(target)) target = $"{target}-{Guid.NewGuid():N}";
            File.Move(path, target);
            var moved = Path.GetRelativePath(RootDirectory, target).Replace('\\', '/');
            log.Warn(Category, $"moved damaged file {relativePath} to {moved}");
            return moved;
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            log.Error(Category, $"could not move damaged file {relativePath}: {ex.GetType().Name}");
            return null;
        }
    }

    public bool Delete(string relativePath)
    {
        var path = ResolveFile(relativePath);
        try
        {
            File.Delete(path);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            // First run: the folder does not exist yet, so neither does the file.
            return true;
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            log.Warn(Category, $"delete failed {relativePath}: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>Relative paths of files directly inside <paramref name="relativeDirectory"/>, sorted ordinally.</summary>
    public IReadOnlyList<string> List(string relativeDirectory, string searchPattern)
    {
        var directory = Resolve(relativeDirectory);
        try
        {
            if (!Directory.Exists(directory)) return [];
            var files = Directory.GetFiles(directory, searchPattern);
            var result = new string[files.Length];
            for (var i = 0; i < files.Length; i++) result[i] = Path.GetRelativePath(RootDirectory, files[i]).Replace('\\', '/');
            Array.Sort(result, StringComparer.Ordinal);
            return result;
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            log.Warn(Category, $"list failed {relativeDirectory}: {ex.GetType().Name}");
            return [];
        }
    }

    /// <summary>
    /// Deletes debris directly inside <paramref name="relativeDirectory"/> (<c>""</c> is the root): temp files a crash
    /// left behind (older than <see cref="StaleTempAge"/>, so a write in progress elsewhere is safe) and all but the newest
    /// <paramref name="keepCorruptPerFile"/> moved-aside copies of each file. Best effort; returns the number deleted.
    /// </summary>
    public int PruneLeftovers(string relativeDirectory, int keepCorruptPerFile = DefaultCorruptCopiesKept)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepCorruptPerFile);
        var directory = Resolve(relativeDirectory);
        var deleted = 0;
        try
        {
            if (!Directory.Exists(directory)) return 0;

            var cutoff = _time.GetUtcNow() - StaleTempAge;
            foreach (var temp in Directory.GetFiles(directory, "*" + TempMarker + "*"))
            {
                if (File.GetLastWriteTimeUtc(temp) < cutoff.UtcDateTime && TryDelete(temp)) deleted++;
            }

            // Stamps are UTC yyyyMMddTHHmmssZ, so ordinal order is age order within one file's copies.
            var copies = Directory.GetFiles(directory, "*" + CorruptMarker + "*");
            Array.Sort(copies, StringComparer.Ordinal);
            var kept = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = copies.Length - 1; i >= 0; i--)
            {
                var original = copies[i][..copies[i].LastIndexOf(CorruptMarker, StringComparison.Ordinal)];
                var count = kept.GetValueOrDefault(original);
                kept[original] = count + 1;
                if (count >= keepCorruptPerFile && TryDelete(copies[i])) deleted++;
            }
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            log.Warn(Category, $"prune failed {relativeDirectory}: {ex.GetType().Name}");
        }

        if (deleted > 0) log.Info(Category, $"pruned {deleted} leftover file(s) in '{relativeDirectory}'");
        return deleted;
    }

    // Without this, WriteTextAtomic("") resolves to the root and writes "<root>.tmp-*" beside it before the rename fails.
    private string ResolveFile(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var full = Resolve(relativePath);
        if (Path.EndsInDirectorySeparator(full)
            || string.Equals(Path.TrimEndingDirectorySeparator(full), Path.TrimEndingDirectorySeparator(RootDirectory), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Path does not name a file inside the store root: {relativePath}", nameof(relativePath));
        return full;
    }

    private string Resolve(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        var full = Path.GetFullPath(Path.Combine(RootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = Path.EndsInDirectorySeparator(RootDirectory) ? RootDirectory : RootDirectory + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) && !string.Equals(full, RootDirectory, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Path escapes the store root: {relativePath}", nameof(relativePath));
        return full;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (IsIoFailure(ex))
        {
            // A leftover file is harmless; PruneLeftovers tries again on the next start.
            return false;
        }
    }

    private static bool IsIoFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or NotSupportedException;
}
