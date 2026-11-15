using System.Globalization;

namespace Usa.Game.Diagnostics.Repro;

/// <summary>
/// Writes repros as <c>&lt;yyyyMMdd-HHmmss-fff&gt;.json</c> (UTC) in one folder (<c>user://repros</c> in the game), never
/// overwriting one, and keeps the newest <paramref name="retainFiles"/>. Writes go through a temporary file so a crash
/// mid-write never leaves half a repro.
/// </summary>
public sealed class ReproStore(string directory, int retainFiles = 100, TimeProvider? time = null)
{
    public const string Extension = ".json";

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();

    public string Directory { get; } = directory;

    public TimeProvider Time => _time;

    public static string FileNameFor(DateTimeOffset utc, int attempt = 0) =>
        string.Create(CultureInfo.InvariantCulture, $"{utc.UtcDateTime:yyyyMMdd-HHmmss-fff}{(attempt > 0 ? $"-{attempt + 1}" : "")}{Extension}");

    /// <returns>The written file's full path.</returns>
    /// <exception cref="IOException">The folder or file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public string Save(ReproDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var json = ReproJson.Serialize(document);
        lock (_gate)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var now = _time.GetUtcNow();
            string path;
            var attempt = 0;
            do path = Path.Combine(Directory, FileNameFor(now, attempt++));
            while (File.Exists(path));

            var temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, path);
            Prune();
            return path;
        }
    }

    /// <summary>Repro files, newest first (names sort by time).</summary>
    public IReadOnlyList<string> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var files = System.IO.Directory.GetFiles(Directory, "*" + Extension);
        Array.Sort(files, static (a, b) => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)));
        return files;
    }

    private void Prune()
    {
        var files = List();
        for (var i = retainFiles; i < files.Count; i++)
        {
            try
            {
                File.Delete(files[i]);
            }
            catch (IOException)
            {
                // An old repro that cannot be deleted never blocks writing a new one.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
