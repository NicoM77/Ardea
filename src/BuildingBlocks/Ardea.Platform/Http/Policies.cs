namespace Ardea.Platform.Http;

public static class Policies
{
    public const string InstanceAdmin = "instance-admin";
}

public static class RateLimits
{
    public const string Outbound = "ardea-outbound";

    public const string Lookup = "ardea-auth-read";
}
