using Usa.Simulation.Crew.Health;
using Usa.Simulation.Kernel;
using Usa.Simulation.LifeSupport.Consumables;

namespace Usa.Simulation.Session;

/// <summary>
/// Crew exposures for the health model and the <c>crew.&lt;id&gt;.env.*</c> paths: the configured state paths resolved for
/// where each member is (<see cref="StateExposureSource"/> built from the roster: <c>{cabin}</c> fields read suit values on EVA,
/// and each location's <c>angularRateFactor</c> scales the felt rate, the only place that factor applies).
/// Hydration and nutrition deficits come from the provisions model when <c>config/health.json</c> leaves those paths empty.
/// </summary>
internal sealed class SessionExposureSource(StateExposureSource paths, Provisions provisions, CrewExposurePaths configured) : ICrewExposureSource
{
    private readonly bool _readHydration = configured.HydrationDeficitL.Length == 0;
    private readonly bool _readNutrition = configured.NutritionDeficitKcal.Length == 0;

    public CrewExposure Read(CrewId crew)
    {
        var exposure = paths.Read(crew);
        var index = provisions.IndexOf(crew);
        if (index < 0) return exposure;
        if (_readHydration) exposure = exposure with { HydrationDeficitL = provisions.HydrationDeficitL(index) };
        if (_readNutrition) exposure = exposure with { NutritionDeficitKcal = provisions.NutritionDeficitKcal(index) };
        return exposure;
    }
}
