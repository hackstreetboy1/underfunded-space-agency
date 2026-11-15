using System.Globalization;
using Usa.Game.Content;
using Usa.Simulation.Engineering;

namespace Usa.Game.Presentation.Workspace;

/// <summary>Number formatting (static) and player-facing words (<c>workspace.*</c> strings) for the workspace (UI 6.2, 6.3).</summary>
internal sealed class WorkspaceText
{
    private readonly ITextSource _text;

    public WorkspaceText(ITextSource text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
    }

    public static string Amps(double value) => Number(value) + " A";

    public static string Metres(double value) => Number(value) + " m";

    /// <summary><c>45 s</c>, <c>6 min</c>, <c>6 min 30 s</c>, <c>1 h 05 min</c>.</summary>
    public static string Duration(double seconds)
    {
        var total = (long)Math.Round(Math.Max(0, seconds), MidpointRounding.AwayFromZero);
        if (total < 60) return Integer(total) + " s";
        if (total < 3600)
        {
            var rest = total % 60;
            return rest == 0 ? $"{Integer(total / 60)} min" : $"{Integer(total / 60)} min {Integer(rest)} s";
        }
        var minutes = (total % 3600) / 60;
        return $"{Integer(total / 3600)} h {minutes.ToString("00", CultureInfo.InvariantCulture)} min";
    }

    public string Likelihood(LikelihoodBand band) => _text.Get(band switch
    {
        LikelihoodBand.High => "workspace.likelihoodHigh",
        LikelihoodBand.Medium => "workspace.likelihoodMedium",
        _ => "workspace.likelihoodLow",
    });

    /// <summary>Workspace part icon key (UI 8.1).</summary>
    public static string IconKey(PartDefinition part) => part.Kind switch
    {
        // The catalog has one harness kind; the icon set draws extensions separately.
        PartKind.Harness => part.Id.Value.StartsWith("harness_ext", StringComparison.Ordinal) ? "extension" : "harness",
        PartKind.Adapter => "adapter",
        PartKind.Fuse => "fuse",
        PartKind.Breaker => "breaker",
        PartKind.Tape => "tape",
        PartKind.Canister => "lioh_canister",
        _ => "generic_spare",
    };

    public string Side(int port) => _text.Get(port == 0 ? "workspace.sideLeft" : "workspace.sideRight");

    public string RemovePartAction(string partName) => _text.Format("workspace.actionRemovePart", ("part", partName));

    public string DisconnectAction(string label) => _text.Format("workspace.actionDisconnect", ("label", label));

    public string DisconnectConnectorAction(int port, string connectorName) =>
        _text.Format("workspace.actionDisconnectConnector", ("side", Side(port)), ("connector", connectorName));

    /// <summary><c>Source: … (…) → Sink: … (…) · Load 2 A</c>.</summary>
    public string GoalSummary(PortSummary source, PortSummary target, string load) => _text.Format(
        "workspace.goalSummary", ("source", source.Label), ("sourceConnector", source.ConnectorName), ("sink", target.Label),
        ("sinkConnector", target.ConnectorName), ("load", load));

    public string Problem(EditProblem problem, string partName) => problem switch
    {
        EditProblem.NotOpen => _text.Get("workspace.problemNotOpen"),
        EditProblem.Locked => _text.Get("workspace.problemLocked"),
        EditProblem.OutsideBoard => _text.Get("workspace.problemOutsideBoard"),
        EditProblem.Overlaps => _text.Get("workspace.problemOverlaps"),
        EditProblem.NoneAvailable => _text.Format("workspace.problemNoneAvailable", ("part", partName)),
        EditProblem.NothingToUndo => _text.Get("workspace.problemNothingToUndo"),
        EditProblem.NothingToRedo => _text.Get("workspace.problemNothingToRedo"),
        EditProblem.NoGesture => _text.Get("workspace.problemNoGesture"),
        _ => "",
    };

    public string Failure(AssemblyFailure failure, string partName) => failure switch
    {
        AssemblyFailure.None => "",
        AssemblyFailure.UnknownPart => _text.Get("workspace.failureUnknownPart"),
        AssemblyFailure.NotAnAssemblyPart => _text.Format("workspace.failureNotAnAssemblyPart", ("part", partName)),
        AssemblyFailure.UnknownInstance => _text.Get("workspace.failureUnknownInstance"),
        AssemblyFailure.UnknownPort => _text.Get("workspace.failureUnknownPort"),
        AssemblyFailure.PortInUse => _text.Get("workspace.failurePortInUse"),
        AssemblyFailure.SamePart => _text.Get("workspace.failureSamePart"),
        AssemblyFailure.NotConnected => _text.Get("workspace.failureNotConnected"),
        AssemblyFailure.EndpointFixed => _text.Get("workspace.failureEndpointFixed"),
        AssemblyFailure.UnknownSink => _text.Get("workspace.failureUnknownSink"),
        _ => _text.Get("workspace.failureImpossible"),
    };

    public static string CheckMark(CheckStatus status) => status switch
    {
        CheckStatus.Passed => "✓",
        CheckStatus.Failed => "✗",
        _ => "◌",
    };

    public string CheckWord(CheckStatus status) => _text.Get(status switch
    {
        CheckStatus.Passed => "workspace.checkPass",
        CheckStatus.Failed => "workspace.checkFail",
        _ => "workspace.checkNotChecked",
    });

    /// <summary>The plain check name shown on Easy (UI 4.6), e.g. <c>Plugs fit</c>.</summary>
    public string CheckTitle(CheckKind check) => _text.Get(check switch
    {
        CheckKind.Continuity => "workspace.checkTitleContinuity",
        CheckKind.ConnectorCompatibility => "workspace.checkTitleConnectorCompatibility",
        CheckKind.Protection => "workspace.checkTitleProtection",
        CheckKind.Length => "workspace.checkTitleLength",
        CheckKind.ShortToGround => "workspace.checkTitleShortToGround",
        CheckKind.Inventory => "workspace.checkTitleInventory",
        _ => throw new ArgumentOutOfRangeException(nameof(check), check, "Unknown check."),
    });

    /// <summary>The Easy line for a check: its findings worded and joined.</summary>
    public string Explanation(CheckResult check)
    {
        ArgumentNullException.ThrowIfNull(check);
        var sentences = new string[check.Findings.Count];
        for (var i = 0; i < sentences.Length; i++) sentences[i] = Finding(check.Findings[i]);
        return string.Join(' ', sentences);
    }

    public string Finding(CheckFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var values = new Dictionary<string, string>(finding.Args.Count, StringComparer.Ordinal);
        foreach (var arg in finding.Args)
            values[arg.Name] = arg.IsPlacedPart ? _text.Format("workspace.checkPartReference", ("part", arg.Value)) : arg.Value;
        return _text.Format(FindingKey(finding.Note), values);
    }

    private static string FindingKey(CheckNote note) => note switch
    {
        CheckNote.ContinuityOk => "workspace.checkContinuityOk",
        CheckNote.SourceNotConnected => "workspace.checkSourceNotConnected",
        CheckNote.OpenEnd => "workspace.checkOpenEnd",
        CheckNote.OpenPartway => "workspace.checkOpenPartway",
        CheckNote.ConnectorsMate => "workspace.checkConnectorsMate",
        CheckNote.ConnectorMismatch => "workspace.checkConnectorMismatch",
        CheckNote.AdapterHint => "workspace.checkAdapterHint",
        CheckNote.ProtectionOk => "workspace.checkProtectionOk",
        CheckNote.ProtectionOkByPart => "workspace.checkProtectionOkByPart",
        CheckNote.ProtectionUnderrated => "workspace.checkProtectionUnderrated",
        CheckNote.PartUnderrated => "workspace.checkPartUnderrated",
        CheckNote.NoProtection => "workspace.checkNoProtection",
        CheckNote.ProtectionOverrated => "workspace.checkProtectionOverrated",
        CheckNote.ProtectionOverratedByPart => "workspace.checkProtectionOverratedByPart",
        CheckNote.LengthOk => "workspace.checkLengthOk",
        CheckNote.TooShort => "workspace.checkTooShort",
        CheckNote.LiveOpenPort => "workspace.checkLiveOpenPort",
        CheckNote.StrayParts => "workspace.checkStrayParts",
        CheckNote.NoLiveOpenPort => "workspace.checkNoLiveOpenPort",
        CheckNote.AllPartsOnBoard => "workspace.checkAllPartsOnBoard",
        CheckNote.NoneOnBoard => "workspace.checkNoneOnBoard",
        CheckNote.OneOnBoard => "workspace.checkOneOnBoard",
        CheckNote.SomeOnBoard => "workspace.checkSomeOnBoard",
        CheckNote.NeedsContinuity => "workspace.checkNeedsContinuity",
        _ => throw new ArgumentOutOfRangeException(nameof(note), note, "Unknown check note."),
    };

    /// <summary>
    /// A card title: the part's short name (the length and rating sit on the spec line, Gate 5 rev 2) with a
    /// capital first letter, e.g. <c>Inline fuse</c>.
    /// </summary>
    public static string CardName(PartDefinition part)
    {
        ArgumentNullException.ThrowIfNull(part);
        var name = part.ShortName;
        return name.Length == 0 || !char.IsLower(name[0]) ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }

    public string TestSummary(TestState state, int failed)
    {
        if (state == TestState.Stale) return _text.Get("workspace.testStale");
        return failed switch
        {
            0 => _text.Get("workspace.testAllPassed"),
            1 => _text.Get("workspace.testOneFailed"),
            _ => _text.Format("workspace.testSomeFailed", ("count", Integer(failed))),
        };
    }

    /// <summary><c>run 07:06:31</c> beside the test result (Figma G5-01a).</summary>
    public string TestRunAt(double metS) => _text.Format("workspace.testRunAt", ("time", Met(metS)));

    /// <summary><c>Route 3.0 m</c> to the sink, <c>Route 2.0 m so far</c> while open, <c>No route yet</c> with nothing on the source.</summary>
    public string Route(double lengthM, int parts, bool reachesSink)
    {
        if (!reachesSink && parts == 0) return _text.Get("workspace.routeNone");
        return _text.Format(reachesSink ? "workspace.routeComplete" : "workspace.routeOpen", ("length", RouteMetres(lengthM)));
    }

    /// <summary><c>roll &lt; 10 °/s (now 0.4, OK)</c>; the stale reading with its MET; <c>(now unknown)</c> without one.</summary>
    public string RateRequirement(RateRequirement requirement, RequirementStatus status, double? knownDps, double? atMetS)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        var axis = ("axis", _text.Get(requirement.Axis switch
        {
            RateAxis.Roll => "workspace.rateAxisRoll",
            RateAxis.Pitch => "workspace.rateAxisPitch",
            RateAxis.Yaw => "workspace.rateAxisYaw",
            _ => "workspace.rateAxisTotal",
        }));
        var limit = ("limit", Number(requirement.LimitDps));
        if (knownDps is not { } value) return _text.Format("workspace.requirementRateUnknown", axis, limit);
        var reading = ("value", value.ToString("0.0", CultureInfo.InvariantCulture));
        if (status == RequirementStatus.Stale)
            return _text.Format("workspace.requirementRateStale", axis, limit, reading, ("time", atMetS is { } at ? Met(at) : "—"));
        var verdict = _text.Get(status == RequirementStatus.Met ? "workspace.requirementOk" : "workspace.requirementTooHigh");
        return _text.Format("workspace.requirementRate", axis, limit, reading, ("verdict", verdict));
    }

    /// <summary><c>inline fuse</c>, or <c>2 × inline fuse</c>.</summary>
    public string PartCount(string partName, int count) =>
        count == 1 ? partName : _text.Format("workspace.partCount", ("count", Integer(count)), ("part", partName));

    /// <summary><c>Complexity 3 · needs roll &lt; 10 °/s (now 0.4, OK) · uses inline fuse, spare harness</c>.</summary>
    public string RequirementsLine(int complexity, IReadOnlyList<string> needs, IReadOnlyList<string> uses)
    {
        ArgumentNullException.ThrowIfNull(needs);
        ArgumentNullException.ThrowIfNull(uses);
        var complexityText = ("complexity", Integer(complexity));
        var usesText = ("parts", string.Join(", ", uses));
        return needs.Count == 0
            ? _text.Format("workspace.requirementsLineNoNeeds", complexityText, usesText)
            : _text.Format("workspace.requirementsLine", complexityText, ("needs", string.Join(", ", needs)), usesText);
    }

    /// <summary>MET clock <c>07:06:31</c> (UI 6.2: HH:MM:SS; hours keep counting past 24).</summary>
    public static string Met(double metS)
    {
        if (!double.IsFinite(metS)) return "—";
        var whole = (long)Math.Floor(Math.Max(0, metS));
        return string.Create(CultureInfo.InvariantCulture, $"{whole / 3600:00}:{whole / 60 % 60:00}:{whole % 60:00}");
    }

    /// <summary>Route lengths keep one decimal (<c>3.0 m</c>), as the board legend shows them.</summary>
    public static string RouteMetres(double value) => value.ToString("0.0", CultureInfo.InvariantCulture) + " m";

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);
}
