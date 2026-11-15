using Godot;
using Usa.Game.Presentation.CrewFeed;
using Usa.Game.Presentation.CrewFeed.Hosting;

namespace Usa.Game.Presentation.Views;

/// <summary>
/// Root of <c>res://scenes/crew/CrewFeed.tscn</c>: one crew member's live comm feed (GDD 4.3, UI 3.4, gate-2 G2-04/05),
/// a 384 × 288 viewport of the crew body in front of the feed backdrop, lit by the comm-feed key/fill/rim rig. No cabin
/// behind them. The feed follows its container's size (any 4:3 slot, e.g. 192 × 144 in the crew card).
/// <para>
/// Set <see cref="CrewId"/> (mission crew id, e.g. <c>cal_brandt</c>) or <see cref="CrewModel"/> before the node enters the
/// tree; the crew's .glb is placed then, so it cannot change afterwards. Drive it with <see cref="Follow"/> (the runtime's
/// <see cref="CrewFeedStateMapper"/>) or <see cref="Apply"/>; <see cref="Reset"/> before it shows another run. When the crew
/// member is lost the last (neutral) frame is held, the viewport stops rendering and <see cref="FeedLost"/> fires so the
/// card can fade it.
/// </para>
/// </summary>
public sealed partial class CrewFeedView : SubViewportContainer
{
    public const string CrewModelDirectory = "res://assets/models/crew/";

    // CrewFeedAnimator finds the face mesh as %FeedBody.
    private const string BodyNodeName = "FeedBody";
    private const string FaceShape = "mouth_open";

    [Signal]
    public delegate void FeedLostEventHandler();

    private readonly NativePixelFit _fit = new();
    private SubViewport _viewport = null!;
    private CrewFeedAnimator _animator = null!;
    private bool _placed;
    private bool _lostShown;

    /// <summary>Mission crew id; picks <c>assets/models/crew/&lt;first name&gt;.glb</c> (<see cref="ModelPathFor"/>).</summary>
    [Export] public string CrewId { get; set; } = string.Empty;

    /// <summary>Optional crew scene that overrides the id convention (another mission's crew, a test rig).</summary>
    [Export] public PackedScene? CrewModel { get; set; }

    public CrewFeedAnimator Animator => _animator;

    public bool IsLost => _animator.IsLost;

    /// <summary><c>cal_brandt</c> → <c>res://assets/models/crew/cal.glb</c>.</summary>
    public static string ModelPathFor(string crewId)
    {
        ArgumentException.ThrowIfNullOrEmpty(crewId);
        var separator = crewId.IndexOf('_', StringComparison.Ordinal);
        return $"{CrewModelDirectory}{(separator > 0 ? crewId[..separator] : crewId)}.glb";
    }

    // The crew is placed before the children enter the tree, so the animator's _Ready finds %FeedBody and the tree its
    // AnimationPlayer.
    public override void _EnterTree()
    {
        if (_placed) return;
        _placed = true;
        PlaceCrew();
    }

    public override void _Ready()
    {
        _viewport = GetNode<SubViewport>("%FeedViewport");
        _animator = GetNode<CrewFeedAnimator>("%Animator");
    }

    public override void _Process(double delta)
    {
        // Renders at the screen's pixel density (1440p, 4K) instead of the 1920 x 1080 layout size.
        if (_fit.Update(this) && _lostShown) _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        if (_lostShown || !_animator.IsLost) return;
        _lostShown = true;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        EmitSignal(SignalName.FeedLost);
    }

    /// <summary>Applies <paramref name="feed"/>'s current frame every frame (null stops following).</summary>
    public void Follow(CrewFeedStateMapper? feed) => _animator.Follow(feed);

    public void Apply(in CrewFeedFrame frame) => _animator.Apply(frame);

    /// <summary>Back to a live, neutral, unfollowed feed.</summary>
    public void Reset()
    {
        _animator.Reset();
        _lostShown = false;
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.WhenVisible;
    }

    private void PlaceCrew()
    {
        var mount = GetNode<Node3D>("FeedViewport/CrewMount");
        var tree = GetNode<AnimationTree>("FeedViewport/FeedTree");
        var model = LoadCrewScene()?.Instantiate() as Node3D;
        MeshInstance3D? body = null;
        if (model is not null)
        {
            mount.AddChild(model);
            body = FindFaceBody(model);
            if (FindAnimationPlayer(model) is { } player)
            {
                SetClipLoops(player);
                tree.AnimPlayer = tree.GetPathTo(player);
            }
            else
            {
                GD.PushError($"[CrewFeed] {CrewId}: the crew model has no AnimationPlayer; the feed stays still.");
            }
        }
        if (body is null)
        {
            GD.PushError($"[CrewFeed] {CrewId}: no crew body with face shapes; the feed shows no face.");
            body = new MeshInstance3D();
            mount.AddChild(body);
        }
        body.Name = BodyNodeName;
        body.Owner = this;
        body.UniqueNameInOwner = true;
    }

    private PackedScene? LoadCrewScene()
    {
        if (CrewModel is not null) return CrewModel;
        if (string.IsNullOrEmpty(CrewId))
        {
            GD.PushError("[CrewFeed] CrewId is not set; set it (or CrewModel) before adding the feed to the tree.");
            return null;
        }
        var path = ModelPathFor(CrewId);
        if (ResourceLoader.Exists(path)) return GD.Load<PackedScene>(path);
        GD.PushError($"[CrewFeed] {CrewId}: no crew model at {path}.");
        return null;
    }

    private static MeshInstance3D? FindFaceBody(Node node)
    {
        if (node is MeshInstance3D mesh && mesh.FindBlendShapeByName(FaceShape) >= 0) return mesh;
        foreach (var child in node.GetChildren())
        {
            if (FindFaceBody(child) is { } found) return found;
        }
        return null;
    }

    private static AnimationPlayer? FindAnimationPlayer(Node node)
    {
        if (node is AnimationPlayer player) return player;
        foreach (var child in node.GetChildren())
        {
            if (FindAnimationPlayer(child) is { } found) return found;
        }
        return null;
    }

    // glTF carries no loop flag; the state clips loop (except the one-pass gauge read), gestures play once.
    private static void SetClipLoops(AnimationPlayer player)
    {
        foreach (var state in Enum.GetValues<CrewFeedState>())
        {
            SetLoop(player, CrewFeedClips.ClipName(state), CrewFeedClips.Loops(state));
        }
        foreach (var oneShot in Enum.GetValues<CrewFeedOneShot>())
        {
            if (oneShot != CrewFeedOneShot.None) SetLoop(player, CrewFeedClips.ClipName(oneShot), loops: false);
        }
    }

    private static void SetLoop(AnimationPlayer player, string clip, bool loops)
    {
        if (!player.HasAnimation(clip))
        {
            GD.PushWarning($"[CrewFeed] the crew model has no '{clip}' clip.");
            return;
        }
        player.GetAnimation(clip).LoopMode = loops ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
    }
}
