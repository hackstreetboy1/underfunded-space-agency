namespace Usa.Game.Flow;

/// <summary>The stages of building a mission behind the Loading screen, in order.</summary>
public enum LoadStage
{
    /// <summary>Nothing is loading.</summary>
    Idle,

    /// <summary>Reading and validating the mission data and building the session (or replaying a resume).</summary>
    DataLoad,

    /// <summary>Composing the runtime: view models, comms, tips, advisor, audio.</summary>
    SessionCompose,

    /// <summary>Loading and instancing the mission console scene.</summary>
    SceneInstancing,

    /// <summary>Everything is built; the router leaves the Loading screen.</summary>
    Ready,
}

/// <summary>
/// Real progress of the launch the Loading screen shows. The flow host reports <see cref="LoadStage.DataLoad"/> and
/// <see cref="LoadStage.SessionCompose"/>, each on its own frame so the screen can draw between them; at
/// <see cref="LoadStage.SceneInstancing"/> whoever instances the mission scene may <see cref="HoldScene"/>, report its own
/// progress with <see cref="ReportScene"/> and <see cref="ReleaseScene"/> when the scene is ready; the host completes the
/// launch once <see cref="SceneSettled"/>. <see cref="Fraction"/> never goes back within one launch. Main thread only.
/// </summary>
public sealed class LoadProgress
{
    /// <summary>Shares of the bar. Data load and the scene's resources dominate; composing is wiring.</summary>
    public const double DataLoadShare = 0.5;

    public const double SessionComposeShare = 0.2;

    public const double SceneShare = 1 - DataLoadShare - SessionComposeShare;

    private int _holds;

    /// <summary>Raised after every change of <see cref="Stage"/> or <see cref="Fraction"/>.</summary>
    public event Action? Changed;

    public LoadStage Stage { get; private set; } = LoadStage.Idle;

    /// <summary>0..1 over the whole launch.</summary>
    public double Fraction { get; private set; }

    /// <summary>True unless something holds the scene stage open.</summary>
    public bool SceneSettled => _holds == 0;

    /// <summary>Where <paramref name="stage"/> starts on the bar.</summary>
    public static double StartOf(LoadStage stage) => stage switch
    {
        LoadStage.Idle or LoadStage.DataLoad => 0,
        LoadStage.SessionCompose => DataLoadShare,
        LoadStage.SceneInstancing => DataLoadShare + SessionComposeShare,
        _ => 1,
    };

    public static double ShareOf(LoadStage stage) => stage switch
    {
        LoadStage.DataLoad => DataLoadShare,
        LoadStage.SessionCompose => SessionComposeShare,
        LoadStage.SceneInstancing => SceneShare,
        _ => 0,
    };

    /// <summary>A new launch: <see cref="LoadStage.DataLoad"/> at 0, any scene hold of an earlier launch dropped.</summary>
    public void Begin()
    {
        _holds = 0;
        Set(LoadStage.DataLoad, 0);
    }

    /// <summary>Moves on to <paramref name="stage"/>; a stage earlier than the current one is ignored.</summary>
    public void Enter(LoadStage stage)
    {
        if (stage <= Stage) return;
        Set(stage, Math.Max(Fraction, StartOf(stage)));
    }

    /// <summary>The scene stage stays open until <see cref="ReleaseScene"/>; call it when entering <see cref="LoadStage.SceneInstancing"/>.</summary>
    public void HoldScene() => _holds++;

    /// <param name="withinStage">The scene's own progress, 0..1; ignored outside <see cref="LoadStage.SceneInstancing"/>.</param>
    public void ReportScene(double withinStage)
    {
        if (Stage != LoadStage.SceneInstancing || !double.IsFinite(withinStage)) return;
        var fraction = StartOf(LoadStage.SceneInstancing) + (SceneShare * Math.Clamp(withinStage, 0, 1));
        if (fraction > Fraction) Set(Stage, fraction);
    }

    public void ReleaseScene()
    {
        if (_holds == 0) return;
        _holds--;
        if (_holds == 0 && Stage == LoadStage.SceneInstancing) Set(Stage, 1);
    }

    /// <summary>The launch is complete (<see cref="LoadStage.Ready"/>, full bar).</summary>
    public void Finish() => Set(LoadStage.Ready, 1);

    /// <summary>Nothing is loading any more (failed launch, or the Loading screen is gone).</summary>
    public void Reset()
    {
        _holds = 0;
        Set(LoadStage.Idle, 0);
    }

    private void Set(LoadStage stage, double fraction)
    {
        if (stage == Stage && fraction.Equals(Fraction)) return;
        Stage = stage;
        Fraction = fraction;
        Changed?.Invoke();
    }
}
