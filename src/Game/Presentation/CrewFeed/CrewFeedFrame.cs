namespace Usa.Game.Presentation.CrewFeed;

/// <summary>What the comm feed shows this frame.</summary>
/// <param name="State">AnimationTree state to travel to (see <see cref="CrewFeedClips"/>).</param>
/// <param name="SpinIntensity">0..1 blend of <c>feed_spin_strain</c>, smoothed.</param>
/// <param name="GIntensity">0..1 blend of <c>feed_g_load</c>, smoothed.</param>
/// <param name="OneShot">A gesture to fire this frame; <see cref="CrewFeedOneShot.None"/> on every other frame.</param>
/// <param name="Expression">The expression preset the face is easing toward.</param>
/// <param name="Face">Final shape-key weights (expression, blink and lip-flap combined); reused between frames.</param>
/// <param name="Lost">The crew member died: hold a neutral, still pose for the UI to fade out (not the app freeze).</param>
public readonly record struct CrewFeedFrame(
    CrewFeedState State,
    float SpinIntensity,
    float GIntensity,
    CrewFeedOneShot OneShot,
    ExpressionPreset Expression,
    FaceWeights Face,
    bool Lost);
