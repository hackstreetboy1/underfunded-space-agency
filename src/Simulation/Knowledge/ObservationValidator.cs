using Usa.Simulation.Kernel;
using Usa.Simulation.Knowledge.Catalog;

namespace Usa.Simulation.Knowledge;

/// <summary>
/// Accepts an observation only when the observer is alive and had access, the method can establish the
/// fact, and an observation that confirms a component actually covered it and may reveal it (GDD 4.3-4.4).
/// </summary>
public sealed class ObservationValidator
{
    private readonly FactCatalog _catalog;
    private readonly IObservationAccess _access;
    private readonly RevealableComponents _revealable;

    /// <param name="revealableComponents">When given, an observation may only confirm one of these components.</param>
    public ObservationValidator(FactCatalog catalog, IObservationAccess access, IEnumerable<ComponentId>? revealableComponents = null)
        : this(catalog, access, new RevealableComponents(revealableComponents))
    {
    }

    internal ObservationValidator(FactCatalog catalog, IObservationAccess access, RevealableComponents revealable)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _revealable = revealable;
    }

    public ObservationVerdict Validate(ObservationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!_catalog.TryResolve(report.Fact, out var fact))
            return Reject(ObservationRejection.UnknownFact, $"No fact '{report.Fact}'.");
        return Validate(report, fact);
    }

    internal ObservationVerdict Validate(ObservationReport report, FactDefinition fact)
    {
        var capabilities = _catalog.Capabilities(report.Method);

        if (fact.ConfirmsComponent is not null && !capabilities.CanConfirmComponent)
            return Reject(ObservationRejection.CannotConfirmComponent, $"A {Name(report.Method)} cannot confirm a component's condition.");
        if (!fact.Allows(report.Method))
            return Reject(ObservationRejection.MethodNotAllowed, $"'{fact.Id}' cannot be established by {Name(report.Method)}.");

        if (capabilities.RequiresObserver && report.Observer is null)
            return Reject(ObservationRejection.ObserverRequired, $"A {Name(report.Method)} needs a crew member.");
        if (!capabilities.RequiresObserver && report.Observer is not null)
            return Reject(ObservationRejection.ObserverNotAllowed, $"A {Name(report.Method)} has no crew observer.");

        var valueCheck = ValidateValue(report, fact, capabilities.Yield);
        if (!valueCheck.Accepted) return valueCheck;
        if (!(report.Confidence is >= 0 and <= 1))
            return Reject(ObservationRejection.InvalidConfidence, "Confidence must be between 0 and 1.");

        if (fact.ConfirmsComponent is { } confirmed && !Covers(report.Scope, confirmed))
            return Reject(ObservationRejection.ScopeDoesNotCover, $"The observation did not cover '{confirmed}'.");
        if (fact.ConfirmsComponent is { } revealed && !_revealable.Contains(revealed))
            return Reject(ObservationRejection.NotRevealable, $"'{revealed}' is not a revealable component.");

        if (report.Observer is not { } observer) return ObservationVerdict.Accept;
        if (!_access.IsAlive(observer))
            return Reject(ObservationRejection.ObserverDead, $"'{observer}' is not alive.");
        var target = fact.ConfirmsComponent ?? fact.SourceComponent;
        if (target is { } component && !_access.CanAccess(observer, component, report.Method))
            return Reject(ObservationRejection.NoAccess, $"'{observer}' has no access to '{component}' for a {Name(report.Method)}.");
        return ObservationVerdict.Accept;
    }

    private static ObservationVerdict ValidateValue(ObservationReport report, FactDefinition fact, ObservationYield yield)
    {
        if (yield == ObservationYield.Band)
        {
            if (report.Value is not null) return Reject(ObservationRejection.ValueNotAllowed, "Ground bands never carry a precise value.");
            if (string.IsNullOrEmpty(report.Qualitative)) return Reject(ObservationRejection.ValueMissing, "A ground band needs its word.");
            return fact.Downlink is { } downlink && downlink.HasWord(report.Qualitative)
                ? ObservationVerdict.Accept
                : Reject(ObservationRejection.ValueNotAllowed, $"'{report.Qualitative}' is not a ground band of '{fact.Id}'.");
        }

        if (fact.Kind == FactValueKind.Number)
        {
            if (report.Value is not { } value || !double.IsFinite(value))
                return Reject(ObservationRejection.ValueMissing, $"'{fact.Id}' needs a finite number.");
            return report.Qualitative is null
                ? ObservationVerdict.Accept
                : Reject(ObservationRejection.ValueNotAllowed, $"'{fact.Id}' is a number fact.");
        }

        if (string.IsNullOrEmpty(report.Qualitative))
            return Reject(ObservationRejection.ValueMissing, $"'{fact.Id}' needs a text value.");
        return report.Value is null
            ? ObservationVerdict.Accept
            : Reject(ObservationRejection.ValueNotAllowed, $"'{fact.Id}' is a text fact.");
    }

    private static bool Covers(IReadOnlyList<ComponentId> scope, ComponentId component)
    {
        for (var i = 0; i < scope.Count; i++)
        {
            if (scope[i] == component) return true;
        }
        return false;
    }

    private static string Name(ObservationMethod method) => method.ToString().ToLowerInvariant();

    private static ObservationVerdict Reject(ObservationRejection rejection, string detail) => new(rejection, detail);
}
