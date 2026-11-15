namespace Usa.Game.Presentation.Spacecraft;

/// <summary>Node names, metadata keys, layers and material paths shared by the spacecraft scene and the models (MODELING 5.4, 7, 9).</summary>
public static class SpacecraftConventions
{
    /// <summary>Plume sockets are empties named <c>Socket_&lt;component_id with '/' → '_'&gt;</c>, +X along the plume.</summary>
    public const string SocketPrefix = "Socket_";

    /// <summary>Exact component id of a socket (Blender custom property; Godot keeps it in the glTF <c>extras</c>).</summary>
    public const string SocketForMeta = "socket_for";

    /// <summary>Dictionary meta where Godot's glTF importer keeps Blender custom properties.</summary>
    public const string GltfExtrasMeta = "extras";

    /// <summary>Section id (<c>adapter</c>, <c>retro</c>) on a section root; real models are found by <see cref="SectionNodeName"/>.</summary>
    public const string SectionIdMeta = "section_id";

    /// <summary>Chute id on a canopy root under a rig's chute node.</summary>
    public const string ChuteForMeta = "chute_for";

    /// <summary>Generated VFX meshes; never picked, never bound to presenters.</summary>
    public const string VfxGroup = "usa_vfx";

    /// <summary>Render layer bits: vehicles on layer 1, the Earth backdrop on layer 2 (the Earth-shine fill skips it).</summary>
    public const uint VehicleLayers = 1u;
    public const uint BackdropLayers = 1u << 1;

    // Asset files keep their mission-1 names; the view picks them by plume kind and vehicle role.
    public const string PlumeAttitudePath = "res://assets/materials/plume_oams_rcs.tres";
    public const string PlumeStuckPath = "res://assets/materials/plume_oams_stuck.tres";
    public const string PlumeRetroPath = "res://assets/materials/plume_retro.tres";
    public const string PlumeTargetAttitudePath = "res://assets/materials/plume_lodestar_acs.tres";
    public const string PlumeTargetEnginePath = "res://assets/materials/plume_lodestar_engine.tres";

    /// <summary>
    /// Indicator meshes under the target's status panel are named <c>Light_1</c>..<c>Light_4</c> and use the palette's
    /// <c>light</c> swatch: 1 attitude-thruster activity, 2 docked/latched, 3 engine armed, 4 power.
    /// </summary>
    public const string StatusLightPrefix = "Light_";

    public const int StatusLightCount = 4;

    /// <summary>Splashdown float action of the crewed capsule (MODELING 7, TAN-03): nose up, waterline at the model's y = 0.</summary>
    public const string FloatAnimation = "anim_float";

    public static string NodeNameFor(string componentId) => componentId.Replace('/', '_');

    /// <summary>Plume look (VFX-01, VFX-02a) by kind; attitude thrusters of the crewed vehicle and of the target differ.</summary>
    public static string? PlumeMaterialPath(PlumeKind kind, bool primaryVehicle) => kind switch
    {
        PlumeKind.RetroRocket => PlumeRetroPath,
        PlumeKind.Engine => PlumeTargetEnginePath,
        PlumeKind.AttitudeThruster => primaryVehicle ? PlumeAttitudePath : PlumeTargetAttitudePath,
        _ => null,
    };

    /// <summary>The crewed vehicle's pulse thrusters switch to the hot, slightly orange plume when they fire without a break.</summary>
    public static bool HasContinuousLook(PlumeKind kind, bool primaryVehicle) => kind == PlumeKind.AttitudeThruster && primaryVehicle;

    /// <summary>Section empties made by the model integration step (<c>integrate.py</c>): <c>Tandem_Adapter</c> for <c>tandem</c>/<c>adapter</c>.</summary>
    public static string SectionNodeName(string vehicleId, string sectionId) => $"{Pascal(vehicleId)}_{Pascal(sectionId)}";

    /// <summary>Separation action of a section (MODELING 7, TAN-03), named after its separation event: <c>equipment_sep</c> → <c>anim_sep_equipment</c>.</summary>
    public static string SeparationAnimation(string separationEventId)
    {
        const string suffix = "_sep";
        var stem = separationEventId.EndsWith(suffix, StringComparison.Ordinal) ? separationEventId[..^suffix.Length] : separationEventId;
        return "anim_sep_" + stem;
    }

    private static string Pascal(string id) =>
        id.Length == 0 ? id : string.Concat(id.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
}
