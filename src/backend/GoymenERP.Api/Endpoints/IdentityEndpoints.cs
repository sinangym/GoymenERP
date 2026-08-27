using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Endpoints;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/identity")
            .RequireAuthorization()
            .WithTags("Identity");

        group.MapGet("/me", async (
            IIdentityProfileService profileService,
            CancellationToken cancellationToken) =>
        {
            var profile = await profileService.GetCurrentAsync(cancellationToken);
            return profile is null
                ? Results.Problem(
                    title: "Kimlik bulunamadı.",
                    detail: "Oturum kimliği etkin bir ERP kullanıcısıyla eşleşmiyor.",
                    statusCode: StatusCodes.Status403Forbidden)
                : Results.Ok(profile);
        });

        return endpoints;
    }
}
