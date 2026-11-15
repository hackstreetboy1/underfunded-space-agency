using Usa.Simulation.Kernel.Geometry;

namespace Usa.Simulation.Attitude;

/// <summary>Mass and diagonal inertia about the body's own centre of mass, along its body axes (roll, pitch, yaw).</summary>
/// <param name="ComXM">
/// Centre of mass along the vehicle X axis, measured from the reference point the thruster positions are given
/// relative to (the centre of mass when the vehicle was built). Jettison and propellant use move it; the thrusters'
/// lever arms and the docked stack's parallel-axis terms follow. Off-axis shifts are not modelled.
/// </param>
public readonly record struct MassProperties(double MassKg, Vec3d InertiaKgM2, double ComXM = 0)
{
    public bool IsValid =>
        double.IsFinite(MassKg) && MassKg > 0 &&
        IsValidInertia(InertiaKgM2) &&
        double.IsFinite(ComXM);

    /// <summary>
    /// Combines two bodies whose centres of mass lie on the stack X (roll) axis at the given positions.
    /// Both inertias must be along the stack axes; a half turn about yaw keeps a diagonal inertia
    /// unchanged, so a nose-to-nose dock needs no conversion. <see cref="ComXM"/> of the inputs is ignored:
    /// the positions are passed explicitly.
    /// </summary>
    public static MergedMassProperties Merge(MassProperties a, double aComXM, MassProperties b, double bComXM)
    {
        var massKg = a.MassKg + b.MassKg;
        var comXM = (a.MassKg * aComXM + b.MassKg * bComXM) / massKg;
        var aArmM = aComXM - comXM;
        var bArmM = bComXM - comXM;

        // Parallel-axis theorem: offsets along X add m·d² to pitch and yaw only.
        var transverseKgM2 = a.MassKg * aArmM * aArmM + b.MassKg * bArmM * bArmM;
        var inertia = new Vec3d(
            a.InertiaKgM2.X + b.InertiaKgM2.X,
            a.InertiaKgM2.Y + b.InertiaKgM2.Y + transverseKgM2,
            a.InertiaKgM2.Z + b.InertiaKgM2.Z + transverseKgM2);
        return new MergedMassProperties(new MassProperties(massKg, inertia), comXM);
    }

    internal static bool IsValidInertia(Vec3d inertiaKgM2) =>
        double.IsFinite(inertiaKgM2.X) && inertiaKgM2.X > 0 &&
        double.IsFinite(inertiaKgM2.Y) && inertiaKgM2.Y > 0 &&
        double.IsFinite(inertiaKgM2.Z) && inertiaKgM2.Z > 0;
}

/// <param name="ComXM">Stack centre of mass on the stack X axis, in the same coordinates as the inputs.</param>
public readonly record struct MergedMassProperties(MassProperties Combined, double ComXM);
