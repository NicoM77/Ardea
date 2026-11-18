using Ardea.Platform.Tenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ardea.Platform.Http;

/// <summary>The role an organisation endpoint needs; without it, every member (viewer and up) may call it.</summary>
public sealed record RequiredOrganizationRole(OrganizationRole Role);

/// <summary>
/// The group <c>/orgs/{orgId}</c> every module maps its organisation endpoints into (master plan
/// 9.2, 10.1). Before any handler runs, its filter sets the organisation as the tenant of the unit
/// of work, checks the membership, the role the endpoint needs and the organisation's MFA
/// requirement. An organisation the user does not belong to does not exist for them (404).
/// </summary>
public static class OrganizationEndpoints
{
    private const string AccessItem = "ardea.organization-access";

    public static RouteGroupBuilder MapOrganization(this IEndpointRouteBuilder api) =>
        api.MapGroup("/orgs/{orgId:guid}")
            .RequireAuthorization()
            .AddEndpointFilter(CheckAccessAsync);

    /// <summary>Requires at least <paramref name="role"/> in the organisation of the route.</summary>
    public static TBuilder RequireOrganizationRole<TBuilder>(this TBuilder builder, OrganizationRole role)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RequiredOrganizationRole(role));

    /// <summary>The checked access of the current request; only inside the organisation group.</summary>
    public static OrganizationAccess OrganizationAccess(this HttpContext http) =>
        http.Items[AccessItem] as OrganizationAccess ?? throw new InvalidOperationException("Not an organisation endpoint.");

    private static async ValueTask<object?> CheckAccessAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var tenant = http.RequestServices.GetRequiredService<TenantContext>();
        if (tenant.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        if (!Guid.TryParse(http.GetRouteValue("orgId")?.ToString(), out var organizationId))
        {
            return NotFound();
        }

        tenant.SetOrganization(organizationId);
        var access = await http.RequestServices.GetRequiredService<IOrganizationMembership>().FindAsync(organizationId, userId, http.RequestAborted);
        if (access is null)
        {
            return NotFound();
        }

        if (access.MfaRequired && !access.UserHasMfa)
        {
            return ApiProblems.Problem("mfa_required", StatusCodes.Status403Forbidden,
                "This organisation requires two-factor authentication. Turn it on in your account first.");
        }

        var required = http.GetEndpoint()?.Metadata.GetMetadata<RequiredOrganizationRole>()?.Role ?? OrganizationRole.Viewer;
        if (access.Role < required)
        {
            return ApiProblems.Problem("insufficient_role", StatusCodes.Status403Forbidden, "Your role in this organisation does not allow this.");
        }

        http.Items[AccessItem] = access;
        return await next(context);
    }

    private static IResult NotFound() =>
        ApiProblems.Problem("organization_not_found", StatusCodes.Status404NotFound, "No such organisation.");
}
