namespace Ardea.Platform.Http;

/// <summary>
/// Authorization policy names every module may require. Iam registers them; modules only name them,
/// so they need no reference to Iam.
/// </summary>
public static class Policies
{
    /// <summary>The instance administrator (master plan 9.1).</summary>
    public const string InstanceAdmin = "instance-admin";
}
