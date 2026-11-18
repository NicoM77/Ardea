namespace Ardea.Iam.Contracts;

/// <summary>Authorization policies other modules and the host may require.</summary>
public static class IamPolicies
{
    /// <summary>The instance administrator: sources, providers, SMTP, registration; no other organisation's data (master plan 9.1). Same value as the platform's <c>Policies.InstanceAdmin</c>.</summary>
    public const string InstanceAdmin = "instance-admin";

    public const string InstanceAdminClaim = "ardea:instance_admin";
}
