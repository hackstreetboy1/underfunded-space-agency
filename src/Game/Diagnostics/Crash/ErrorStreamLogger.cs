using System.Collections.Concurrent;
using Godot;
using Usa.Game.Diagnostics.Repro;

namespace Usa.Game.Diagnostics.Crash;

/// <summary>
/// Listens to Godot's error stream (<see cref="OS.AddLogger"/>) and queues the entries that <see cref="UnhandledErrorFilter"/>
/// treats as crashes; <see cref="CrashReporter"/> drains the queue on the main thread. Called from any thread.
/// </summary>
/// <remarks>
/// With the editor's debugger attached (F5 from the editor) Godot sends C# exceptions to the debugger's Errors tab instead of
/// the error stream, so only exceptions that handlers log reach this logger there; command-line and exported runs see both.
/// </remarks>
public sealed partial class ErrorStreamLogger : Logger
{
    private const int MaxQueued = 64;

    private readonly ConcurrentQueue<ReproFailure> _queue = new();
    private int _suppressedThreadId;

    public bool TryDequeue(out ReproFailure failure) => _queue.TryDequeue(out failure!);

    /// <summary>Ignores errors raised on the calling thread until disposed (the reporter's own log lines).</summary>
    public IDisposable SuppressCurrentThread()
    {
        var previous = Interlocked.Exchange(ref _suppressedThreadId, System.Environment.CurrentManagedThreadId);
        return new Suppression(this, previous);
    }

    public void Enqueue(ReproFailure failure)
    {
        if (_queue.Count < MaxQueued) _queue.Enqueue(failure);
    }

    public override void _LogError(
        string function,
        string file,
        int line,
        string code,
        string rationale,
        bool editorNotify,
        int errorType,
        Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        if (errorType is (int)ErrorType.Warning or (int)ErrorType.Shader) return;
        if (Volatile.Read(ref _suppressedThreadId) == System.Environment.CurrentManagedThreadId) return;
        if (UnhandledErrorFilter.TryMatch(string.IsNullOrEmpty(rationale) ? code : rationale, out var failure)) Enqueue(failure);
    }

    private sealed class Suppression(ErrorStreamLogger owner, int previous) : IDisposable
    {
        public void Dispose() => Interlocked.Exchange(ref owner._suppressedThreadId, previous);
    }
}
