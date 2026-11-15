using System.Globalization;
using System.Text;

namespace Usa.Game.Services.Logging;

/// <summary>
/// Appends to <c>usa_yyyy-MM-dd.log</c> (UTC date, invariant digits, like the line timestamps) in
/// <paramref name="directory"/>, rolling over at UTC midnight and keeping the newest <paramref name="retainFiles"/>
/// files. Thread-safe. After an IO failure on its own file the sink goes quiet instead of throwing or spamming the
/// console; failing to prune an old file never stops logging.
/// </summary>
public sealed class FileLogSink(string directory, int retainFiles = 7, TimeProvider? time = null) : ILogSink, IDisposable
{
    private const string FilePrefix = "usa_";
    private const string FileExtension = ".log";

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private StreamWriter? _writer;
    private DateOnly _writerDate;
    private bool _failed;

    public string Directory { get; } = directory;

    /// <summary>The file name for a UTC date; the same in every culture, so ordinal order is date order.</summary>
    public static string FileNameFor(DateOnly utcDate) =>
        string.Create(CultureInfo.InvariantCulture, $"{FilePrefix}{utcDate:yyyy-MM-dd}{FileExtension}");

    public void Write(in LogEntry entry)
    {
        var line = string.Create(CultureInfo.InvariantCulture,
            $"{entry.TimestampUtc:yyyy-MM-ddTHH:mm:ss.fffZ} {LogFormat.LevelTag(entry.Level),-5} [{entry.Category}] {entry.Message}");
        lock (_gate)
        {
            if (_failed) return;
            try
            {
                EnsureWriter()?.WriteLine(line);
            }
            catch (IOException)
            {
                Fail();
            }
            catch (UnauthorizedAccessException)
            {
                Fail();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private StreamWriter? EnsureWriter()
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        if (_writer is not null && today == _writerDate) return _writer;

        _writer?.Dispose();
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, FileNameFor(today));
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
        _writerDate = today;
        PruneOldFiles(path);
        return _writer;
    }

    // Best effort: another game instance may hold an old log open, which must not silence this one.
    private void PruneOldFiles(string currentPath)
    {
        string[] files;
        try
        {
            files = System.IO.Directory.GetFiles(Directory, $"{FilePrefix}*{FileExtension}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (files.Length <= retainFiles) return;
        Array.Sort(files, StringComparer.Ordinal);
        for (var i = 0; i < files.Length - retainFiles; i++)
        {
            if (string.Equals(files[i], currentPath, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                File.Delete(files[i]);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left for the next roll-over.
            }
        }
    }

    private void Fail()
    {
        _failed = true;
        _writer?.Dispose();
        _writer = null;
    }
}
