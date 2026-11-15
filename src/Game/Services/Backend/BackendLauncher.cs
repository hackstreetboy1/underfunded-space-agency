using Usa.Game.Services.Logging;

namespace Usa.Game.Services.Backend;

/// <summary>
/// Starts <c>backend/run_backend.ps1</c> as a child process and stops it (with the proxy it runs) on quit. It passes no
/// arguments or secrets of its own: the proxy reads <c>backend/.env</c> itself. The demo runs on the real providers, so
/// unless <c>USE_FAKE_PROVIDERS</c> is already set the child inherits <c>USE_FAKE_PROVIDERS=false</c>. It only ever stops a
/// process it started. Main thread only.
/// </summary>
public sealed class BackendLauncher
{
    private const string Category = "Backend";
    private const string FakeProvidersVariable = "USE_FAKE_PROVIDERS";

    private readonly BackendLaunchOptions _options;
    private readonly IProcessRunner _runner;
    private readonly Log _log;
    private readonly string _shell;
    private int _processId = -1;

    /// <param name="shell">The PowerShell executable (<c>powershell.exe</c> on Windows, <c>pwsh</c> elsewhere).</param>
    public BackendLauncher(BackendLaunchOptions options, IProcessRunner runner, Log log, string shell = "powershell.exe")
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrWhiteSpace(shell);
        _options = options;
        _runner = runner;
        _log = log;
        _shell = shell;
    }

    public bool CanStart => _options.CanStart;

    /// <summary>A proxy this launcher started is still running.</summary>
    public bool IsRunning => _processId > 0 && _runner.IsRunning(_processId);

    /// <summary>
    /// Starts the proxy unless one it started is still running. The execution policy flag applies to this one
    /// PowerShell process only and changes no machine setting.
    /// </summary>
    public bool TryStart()
    {
        if (!CanStart) return false;
        if (IsRunning) return true;

        // The proxy defaults to fake providers; an explicit choice (tests, `-Offline`) wins over the demo's live default.
        if (Environment.GetEnvironmentVariable(FakeProvidersVariable) is null)
            Environment.SetEnvironmentVariable(FakeProvidersVariable, "false");

        var processId = _runner.Start(_shell, Arguments(_options.ScriptPath!));
        if (processId <= 0)
        {
            _log.Warn(Category, "the local proxy could not be started");
            _processId = -1;
            return false;
        }
        _processId = processId;
        _log.Info(Category, $"started the local proxy (process {processId})");
        return true;
    }

    /// <summary>Stops the proxy this launcher started, if it is still running (quit). Safe to call more than once.</summary>
    public void Stop()
    {
        if (_processId <= 0) return;
        var processId = _processId;
        _processId = -1;
        if (!_runner.IsRunning(processId)) return;
        _runner.StopTree(processId);
        _log.Info(Category, $"stopped the local proxy (process {processId})");
    }

    public static IReadOnlyList<string> Arguments(string scriptPath) =>
        ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath];
}
