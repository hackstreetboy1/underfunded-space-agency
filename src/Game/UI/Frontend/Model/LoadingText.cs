using Usa.Game.Flow;

namespace Usa.Game.UI.Frontend.Model;

/// <summary>The Loading screen's status line for each <see cref="LoadStage"/> (G6-10).</summary>
public static class LoadingText
{
    public static string StatusKey(LoadStage stage) => stage switch
    {
        LoadStage.SessionCompose => "briefing.loadingStageCompose",
        LoadStage.SceneInstancing or LoadStage.Ready => "briefing.loadingStageScene",
        _ => "briefing.loadingStatus",
    };

    /// <summary>The bar's value: a screen that opens before the launch began, or after it finished, shows an empty bar.</summary>
    public static double BarValue(LoadStage stage, double fraction) =>
        stage is LoadStage.Idle || !double.IsFinite(fraction) ? 0 : Math.Clamp(fraction, 0, 1);
}
