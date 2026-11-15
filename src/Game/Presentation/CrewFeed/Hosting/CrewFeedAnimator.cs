using Godot;

namespace Usa.Game.Presentation.CrewFeed.Hosting;

/// <summary>
/// Applies a <see cref="CrewFeedFrame"/> from <see cref="CrewFeedStateMapper"/> to a comm-feed scene. No logic of its own.
/// Either call <see cref="Apply"/> each frame or <see cref="Follow"/> a mapper; <see cref="Reset"/> before the feed shows another run.
/// </summary>
/// <remarks>
/// Expected scene (built once the visual gate is approved): <c>%FeedTree</c>, an AnimationTree whose root blend tree
/// holds a state machine <c>state</c> (states named by <see cref="CrewFeedClips.StateName"/>; <c>spin_strain</c> and
/// <c>g_load</c> are blend trees with a Blend2 <c>intensity</c> from the idle pose to their clip), a Transition
/// <c>gesture</c> with inputs <c>nod</c>, <c>wave</c>, <c>relief</c>, and a OneShot <c>gesture_shot</c> layering it
/// over the state; and <c>%FeedBody</c>, the crew MeshInstance3D carrying the 11 face blend shapes.
/// </remarks>
public sealed partial class CrewFeedAnimator : Node
{
    public const string StatePlaybackPath = "parameters/state/playback";
    public const string SpinIntensityPath = "parameters/state/spin_strain/intensity/blend_amount";
    public const string GIntensityPath = "parameters/state/g_load/intensity/blend_amount";
    public const string GestureTransitionPath = "parameters/gesture/transition_request";
    public const string GestureRequestPath = "parameters/gesture_shot/request";

    // Cached: a string argument would allocate a StringName (a finalizable native wrapper) on every call.
    private static readonly StringName SpinIntensity = SpinIntensityPath;
    private static readonly StringName GIntensity = GIntensityPath;
    private static readonly StringName GestureTransition = GestureTransitionPath;
    private static readonly StringName GestureRequest = GestureRequestPath;
    private static readonly StringName[] StateNames = Enum.GetValues<CrewFeedState>().Select(s => new StringName(CrewFeedClips.StateName(s))).ToArray();

    private readonly int[] _blendShapes = new int[ShapeKeys.Count];
    private AnimationTree? _tree;
    private AnimationNodeStateMachinePlayback? _playback;
    private MeshInstance3D? _body;
    private CrewFeedState? _state;
    private CrewFeedStateMapper? _feed;

    /// <summary>The crew member died; the UI fades the feed out. Cleared by <see cref="Reset"/>.</summary>
    public bool IsLost { get; private set; }

    public override void _Ready()
    {
        _tree = GetNode<AnimationTree>("%FeedTree");
        _body = GetNode<MeshInstance3D>("%FeedBody");
        _playback = _tree.Get(StatePlaybackPath).As<AnimationNodeStateMachinePlayback>();
        for (var i = 0; i < ShapeKeys.Count; i++) _blendShapes[i] = _body.FindBlendShapeByName(ShapeKeys.Names[i]);
    }

    public override void _Process(double delta)
    {
        if (_feed is { } feed) Apply(feed.Current);
    }

    /// <summary>Applies <paramref name="feed"/>'s current frame every frame until <see cref="Reset"/> (null stops following).</summary>
    public void Follow(CrewFeedStateMapper? feed) => _feed = feed;

    /// <summary>Back to a live, neutral feed with nothing followed: the run it showed is over.</summary>
    public void Reset()
    {
        _feed = null;
        _state = null;
        IsLost = false;
        if (_tree is null || _body is null) return;
        _tree.Active = true;
        for (var i = 0; i < ShapeKeys.Count; i++)
        {
            if (_blendShapes[i] >= 0) _body.SetBlendShapeValue(_blendShapes[i], 0f);
        }
        _tree.Set(SpinIntensity, 0f);
        _tree.Set(GIntensity, 0f);
        _playback?.Start(StateNames[(int)CrewFeedState.Idle]);
    }

    public void Apply(in CrewFeedFrame frame)
    {
        if (_tree is null || _body is null || IsLost) return;
        ApplyFace(frame.Face);
        if (frame.Lost)
        {
            IsLost = true;
            _tree.Active = false;
            return;
        }

        if (_state != frame.State)
        {
            _playback?.Travel(StateNames[(int)frame.State]);
            _state = frame.State;
        }

        _tree.Set(SpinIntensity, frame.SpinIntensity);
        _tree.Set(GIntensity, frame.GIntensity);
        if (frame.OneShot == CrewFeedOneShot.None) return;
        _tree.Set(GestureTransition, CrewFeedClips.OneShotName(frame.OneShot));
        _tree.Set(GestureRequest, (int)AnimationNodeOneShot.OneShotRequest.Fire);
    }

    private void ApplyFace(FaceWeights face)
    {
        for (var i = 0; i < ShapeKeys.Count; i++)
        {
            if (_blendShapes[i] >= 0) _body!.SetBlendShapeValue(_blendShapes[i], face[(ShapeKey)i]);
        }
    }
}
