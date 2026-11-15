using Godot;
using Usa.Game.Presentation.Rendering;

namespace Usa.Game.Presentation.Spacecraft;

/// <summary>
/// Click → component id for the inspector (UI 4.4). Casts the camera ray against the vehicles' visible mesh triangles
/// (the models carry no collision) and reports the nearest hit's owning component: the closest ancestor with
/// <c>component_id</c> metadata. Plumes and other VFX are ignored. A press that moves more than
/// <see cref="ClickSlopPx"/> is an orbit drag, not a click.
/// </summary>
public sealed partial class ComponentPicker : Node
{
    [Signal]
    public delegate void ComponentPickedEventHandler(string componentId, Vector3 hitWorld);

    /// <summary>A click hit no component (the inspector closes).</summary>
    [Signal]
    public delegate void PickClearedEventHandler();

    private readonly List<PickTarget> _targets = [];
    private readonly Dictionary<Mesh, Vector3[]> _faces = [];
    private Camera3D? _camera;
    private Vector2 _pressPosition;
    private bool _pressed;

    [Export] public float ClickSlopPx { get; set; } = 4f;

    public void Bind(Camera3D camera, Node vehiclesRoot)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(vehiclesRoot);
        _camera = camera;
        Collect(vehiclesRoot, null);
    }

    public bool TryPick(Vector2 screenPosition, out string componentId, out Vector3 hitWorld)
    {
        componentId = string.Empty;
        hitWorld = Vector3.Zero;
        if (_camera is null) return false;

        var from = _camera.ProjectRayOrigin(screenPosition);
        var direction = _camera.ProjectRayNormal(screenPosition);
        var best = float.PositiveInfinity;
        foreach (var target in _targets)
        {
            var instance = target.Mesh;
            if (!IsInstanceValid(instance) || !instance.IsVisibleInTree() || instance.Mesh is not { } mesh) continue;

            var toLocal = instance.GlobalTransform.AffineInverse();
            var localFrom = toLocal * from;
            var localDirection = toLocal.Basis * direction;
            if (!RayHitsBox(instance.GetAabb().Grow(0.01f), localFrom, localDirection)) continue;

            var faces = FacesOf(mesh);
            for (var i = 0; i + 2 < faces.Length; i += 3)
            {
                var hit = Geometry3D.RayIntersectsTriangle(localFrom, localDirection, faces[i], faces[i + 1], faces[i + 2]);
                if (hit.VariantType != Variant.Type.Vector3) continue;
                var world = instance.GlobalTransform * hit.AsVector3();
                var distance = from.DistanceSquaredTo(world);
                if (distance >= best) continue;
                best = distance;
                componentId = target.ComponentId;
                hitWorld = world;
            }
        }
        return best < float.PositiveInfinity;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left } button) return;
        if (button.Pressed)
        {
            _pressed = true;
            _pressPosition = button.Position;
            return;
        }
        if (!_pressed) return;
        _pressed = false;
        if (button.Position.DistanceTo(_pressPosition) > ClickSlopPx) return;

        if (TryPick(button.Position, out var id, out var hit)) EmitSignal(SignalName.ComponentPicked, id, hit);
        else EmitSignal(SignalName.PickCleared);
    }

    private void Collect(Node node, string? componentId)
    {
        if (node.IsInGroup(SpacecraftConventions.VfxGroup)) return;
        if (node.HasMeta(RenderingConventions.ComponentIdMeta)) componentId = node.GetMeta(RenderingConventions.ComponentIdMeta).AsString();
        if (node is MeshInstance3D mesh && componentId is not null) _targets.Add(new PickTarget(mesh, componentId));
        foreach (var child in node.GetChildren()) Collect(child, componentId);
    }

    // A presenter may swap a mesh out (replace variants), so faces are cached per mesh resource, not per instance.
    private Vector3[] FacesOf(Mesh mesh)
    {
        if (!_faces.TryGetValue(mesh, out var faces))
        {
            faces = mesh.GetFaces();
            _faces.Add(mesh, faces);
        }
        return faces;
    }

    // Slab test; the ray starts at the camera, so only hits ahead of it count.
    private static bool RayHitsBox(Aabb box, Vector3 from, Vector3 direction)
    {
        var near = 0f;
        var far = float.PositiveInfinity;
        for (var axis = 0; axis < 3; axis++)
        {
            var origin = from[axis];
            var step = direction[axis];
            var min = box.Position[axis];
            var max = box.End[axis];
            if (Mathf.Abs(step) < 1e-9f)
            {
                if (origin < min || origin > max) return false;
                continue;
            }
            var t1 = (min - origin) / step;
            var t2 = (max - origin) / step;
            near = Mathf.Max(near, Mathf.Min(t1, t2));
            far = Mathf.Min(far, Mathf.Max(t1, t2));
            if (near > far) return false;
        }
        return true;
    }

    private readonly record struct PickTarget(MeshInstance3D Mesh, string ComponentId);
}
