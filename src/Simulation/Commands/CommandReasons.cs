namespace Usa.Simulation.Commands;

/// <summary>
/// Reason codes the command pipeline produces itself. Precondition reason codes come from
/// <c>catalog/actions.json</c>. The crew explains refusals from these codes.
/// </summary>
public static class CommandReasons
{
    public const string UnknownAction = "unknown_action";
    public const string NotAllowedInPhase = "not_allowed_in_phase";
    public const string UnknownCrewMember = "unknown_crew_member";
    public const string CrewMemberDead = "crew_member_dead";
    public const string CrewMemberIncapacitated = "crew_member_incapacitated";
    public const string NoCrewAvailable = "no_crew_available";
    public const string MissingParameter = "missing_parameter";
    public const string UnknownParameter = "unknown_parameter";
    public const string InvalidParameterType = "invalid_parameter_type";
    public const string InvalidParameterValue = "invalid_parameter_value";
    public const string ParameterOutOfRange = "parameter_out_of_range";
    public const string RemoteControlUnavailable = "remote_control_unavailable";
    public const string CrewBusy = "crew_busy";
    public const string ExecutionFailed = "execution_failed";
    public const string DuplicateCommand = "duplicate_command";

    /// <summary>The action records workspace state (a repair design) and only the workspace or a replay may send it.</summary>
    public const string WorkspaceOnly = "workspace_only";

    /// <summary>The request has an empty or default <c>CommandId</c>, which can never be a dedupe key.</summary>
    public const string InvalidCommandId = "invalid_command_id";
}
