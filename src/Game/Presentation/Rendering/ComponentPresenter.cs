using Godot;

namespace Usa.Game.Presentation.Rendering;

/// <summary>
/// Shows one revealable component the way the player knows it (GDD 4.4, MODELING 1.4/6, SCRIPTING 12.4):
/// unknown grey, confirmed palette colours, the confirmed damage variant, the stale tag anchor and the
/// reveal transition. Its only input is knowledge (state, confirmed variant, staleness), never
/// simulation truth: a damaged component the player has not inspected stays grey.
/// <para>
/// Lives as a child of the imported component node (the one carrying <c>component_id</c> metadata) and
/// positions itself at the top centre of the component, which is where the STALE tag is anchored.
/// </para>
/// </summary>
public sealed partial class ComponentPresenter : Node3D
{
    [Signal]
    public delegate void StaleChangedEventHandler(bool stale);

    private readonly List<PaletteSurface> _baseSurfaces = [];
    private readonly Dictionary<MeshInstance3D, Mesh> _hiddenBaseMeshes = new();
    private readonly List<DamageVariant> _variants = [];
    private readonly Dictionary<string, DamageVariant> _variantsByName = new(StringComparer.Ordinal);
    private Material? _unknownMaterial;
    private RevealController? _reveal;
    private Tween? _activeReveal;
    private bool _warnedMissingVariant;

    public string ComponentId { get; private set; } = string.Empty;
    public ComponentLookState State { get; private set; } = ComponentLookState.Unknown;

    /// <summary>The confirmed damage variant (knowledge word, e.g. <c>stuck_open</c>), or null.</summary>
    public string? Variant { get; private set; }

    public bool IsStale { get; private set; }

    public bool IsRevealed => State is ComponentLookState.ConfirmedNominal or ComponentLookState.ConfirmedDamaged;

    /// <summary>Variant mesh names and the knowledge words they declare in <c>damage_conditions</c>.</summary>
    public IReadOnlyCollection<string> AvailableVariants => _variantsByName.Keys;

    /// <summary>
    /// Creates a presenter under every node of <paramref name="root"/> that has <c>component_id</c> metadata
    /// and returns them by id. Every component starts unknown (grey).
    /// </summary>
    public static IReadOnlyDictionary<string, ComponentPresenter> BindAll(
        Node root, RevealController? reveal, Material? unknownMaterial = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        var components = new List<Node3D>();
        FindComponents(root, components);

        var presenters = new Dictionary<string, ComponentPresenter>(StringComparer.Ordinal);
        foreach (var node in components)
        {
            var id = node.GetMeta(RenderingConventions.ComponentIdMeta).AsString();
            if (presenters.ContainsKey(id))
            {
                GD.PushError($"[ComponentPresenter] duplicate component_id '{id}' at {node.GetPath()}; ignored.");
                continue;
            }

            var presenter = new ComponentPresenter { Name = "ComponentPresenter" };
            presenter.Initialize(reveal, unknownMaterial);
            node.AddChild(presenter);
            presenters.Add(id, presenter);
        }
        return presenters;
    }

    /// <summary>Must be called before the presenter enters the tree; both arguments are optional.</summary>
    public void Initialize(RevealController? reveal, Material? unknownMaterial)
    {
        _reveal = reveal;
        _unknownMaterial = unknownMaterial;
    }

    public override void _Ready()
    {
        var component = GetParent<Node3D>();
        ComponentId = component.GetMeta(RenderingConventions.ComponentIdMeta, string.Empty).AsString();
        _unknownMaterial ??= GD.Load<Material>(component
            .GetMeta(RenderingConventions.UnknownOverrideMeta, RenderingConventions.UnknownMaterialPath).AsString());

        Collect(component, isRoot: true);
        foreach (var variant in _variants) variant.Root.Visible = false;

        var bounds = WorldBounds(_baseSurfaces);
        if (bounds is { } box) GlobalPosition = new Vector3(box.GetCenter().X, box.End.Y, box.GetCenter().Z);

        ApplyLook(wasRevealed: false, revealOriginWorld: null, animate: false);
    }

    /// <summary>
    /// Updates the look from knowledge. <paramref name="variant"/> is the confirmed damage variant and is
    /// only used for <see cref="ComponentLookState.ConfirmedDamaged"/>. A change from not revealed to
    /// revealed plays the reveal from <paramref name="revealOriginWorld"/> (the inspected point; default the
    /// component's centre) unless <paramref name="animate"/> is false, e.g. when loading a save.
    /// </summary>
    public void SetKnowledge(
        ComponentLookState state, string? variant, bool stale, Vector3? revealOriginWorld = null, bool animate = true)
    {
        var shownVariant = state == ComponentLookState.ConfirmedDamaged ? variant : null;
        var lookChanged = state != State || !string.Equals(shownVariant, Variant, StringComparison.Ordinal);
        var wasRevealed = IsRevealed;

        State = state;
        Variant = shownVariant;
        if (lookChanged) ApplyLook(wasRevealed, revealOriginWorld, animate);

        if (stale != IsStale)
        {
            IsStale = stale;
            EmitSignal(SignalName.StaleChanged, stale);
        }
    }

    public override void _ExitTree() => CancelReveal();

    private static void FindComponents(Node node, List<Node3D> found)
    {
        if (node is Node3D node3D && node.HasMeta(RenderingConventions.ComponentIdMeta)) found.Add(node3D);
        foreach (var child in node.GetChildren()) FindComponents(child, found);
    }

    private void Collect(Node node, bool isRoot)
    {
        if (node == this) return;
        // A nested component gets its own presenter.
        if (!isRoot && node.HasMeta(RenderingConventions.ComponentIdMeta)) return;

        if (node is Node3D variantRoot && node.HasMeta(RenderingConventions.DamageVariantMeta))
        {
            var name = node.GetMeta(RenderingConventions.DamageVariantMeta).AsString();
            var replace = string.Equals(
                node.GetMeta(RenderingConventions.VariantModeMeta, string.Empty).AsString(),
                RenderingConventions.ReplaceVariantMode, StringComparison.Ordinal);
            var variant = new DamageVariant(variantRoot, replace, []);
            CollectSurfaces(node, variant.Surfaces);
            _variants.Add(variant);
            _variantsByName[name] = variant;
            // The simulation names conditions (failed:stuck_open); the mesh names what it shows
            // (stuck_scorched). The asset declares which condition words it depicts.
            var conditions = node.GetMeta(RenderingConventions.DamageConditionsMeta, string.Empty).AsString();
            foreach (var condition in conditions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                _variantsByName.TryAdd(condition, variant);
            }
            return;
        }

        if (node is MeshInstance3D mesh) AddPaletteSurfaces(mesh, _baseSurfaces);
        foreach (var child in node.GetChildren()) Collect(child, isRoot: false);
    }

    private static void CollectSurfaces(Node node, List<PaletteSurface> into)
    {
        if (node is MeshInstance3D mesh) AddPaletteSurfaces(mesh, into);
        foreach (var child in node.GetChildren())
        {
            if (!child.HasMeta(RenderingConventions.ComponentIdMeta)) CollectSurfaces(child, into);
        }
    }

    private static void AddPaletteSurfaces(MeshInstance3D mesh, List<PaletteSurface> into)
    {
        if (mesh.Mesh is null) return;
        if (mesh.HasMeta(RenderingConventions.PaletteSurfacesMeta))
        {
            foreach (var index in mesh.GetMeta(RenderingConventions.PaletteSurfacesMeta).AsInt32Array())
            {
                into.Add(new PaletteSurface(mesh, index));
            }
            return;
        }

        // Not imported through ModelImportPostprocess (e.g. a hand-built test scene): match by material.
        for (var i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
        {
            var material = mesh.Mesh.SurfaceGetMaterial(i);
            if (material is not null &&
                (material.ResourcePath == RenderingConventions.PaletteMaterialPath ||
                 material.ResourceName == RenderingConventions.PaletteMaterialName))
            {
                into.Add(new PaletteSurface(mesh, i));
            }
        }
    }

    private void ApplyLook(bool wasRevealed, Vector3? revealOriginWorld, bool animate)
    {
        CancelReveal();

        DamageVariant? shown = null;
        if (Variant is not null && !_variantsByName.TryGetValue(Variant, out shown) && !_warnedMissingVariant)
        {
            _warnedMissingVariant = true;
            GD.PushWarning($"[ComponentPresenter] '{ComponentId}' has no damage variant '{Variant}'; showing nominal.");
        }

        foreach (var variant in _variants) variant.Root.Visible = ReferenceEquals(variant, shown);
        SetBaseHidden(shown?.Replace == true);

        var visible = new List<PaletteSurface>();
        if (_hiddenBaseMeshes.Count == 0) visible.AddRange(_baseSurfaces);
        if (shown is not null) visible.AddRange(shown.Surfaces);

        if (IsRevealed && !wasRevealed && animate && _reveal is not null && WorldBounds(visible) is { } bounds)
        {
            var origin = revealOriginWorld ?? bounds.GetCenter();
            _activeReveal = _reveal.Play(visible, origin, FurthestCornerM(bounds, origin), () =>
            {
                _activeReveal = null;
                if (IsInstanceValid(this)) SetOverrides(null);
            });
            return;
        }

        SetOverrides(IsRevealed ? null : _unknownMaterial);
    }

    private void SetOverrides(Material? material)
    {
        if (_hiddenBaseMeshes.Count == 0)
        {
            foreach (var surface in _baseSurfaces) surface.SetOverride(material);
        }
        foreach (var variant in _variants)
        {
            foreach (var surface in variant.Surfaces) surface.SetOverride(material);
        }
    }

    // Hiding the component node would hide its variant children too, so a "replace" variant takes the
    // base meshes away instead and gives them back afterwards.
    private void SetBaseHidden(bool hidden)
    {
        if (hidden && _hiddenBaseMeshes.Count == 0)
        {
            foreach (var surface in _baseSurfaces)
            {
                if (surface.Mesh.Mesh is { } mesh && _hiddenBaseMeshes.TryAdd(surface.Mesh, mesh)) surface.Mesh.Mesh = null;
            }
        }
        else if (!hidden && _hiddenBaseMeshes.Count > 0)
        {
            foreach (var (instance, mesh) in _hiddenBaseMeshes) instance.Mesh = mesh;
            _hiddenBaseMeshes.Clear();
        }
    }

    private void CancelReveal()
    {
        if (_activeReveal is not null && _activeReveal.IsValid()) _activeReveal.Kill();
        _activeReveal = null;
    }

    private static Aabb? WorldBounds(List<PaletteSurface> surfaces)
    {
        Aabb? bounds = null;
        foreach (var surface in surfaces)
        {
            var box = surface.Mesh.GlobalTransform * surface.Mesh.GetAabb();
            bounds = bounds is { } merged ? merged.Merge(box) : box;
        }
        return bounds;
    }

    private static float FurthestCornerM(Aabb bounds, Vector3 origin)
    {
        var furthest = 0f;
        for (var i = 0; i < 8; i++) furthest = Mathf.Max(furthest, bounds.GetEndpoint(i).DistanceTo(origin));
        return furthest;
    }

    private sealed record DamageVariant(Node3D Root, bool Replace, List<PaletteSurface> Surfaces);
}
