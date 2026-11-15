using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Security;

/// <summary>
/// ID-2 password fence (spec "Durum geçişleri"): while the session's credential has <c>MustChangePassword=true</c>
/// every endpoint answers 403 <see cref="IdentitySessionProblemCodesV1.PasswordChangeRequired"/> except the ones
/// carrying <see cref="Allowed"/>: the password change and logout (spec), plus <c>GET /me</c> and the antiforgery
/// token read the client needs to show and submit the change form (ASSUMPTION candidate, reported). Runs after
/// authentication and the antiforgery check and before the company context and authorization, so a fenced request
/// never reaches a handler. A no-op outside the <c>LocalAccount</c> mode and for every other scheme: only the
/// cookie handler sets <see cref="LocalAccountSession.PasswordChangeRequiredItem"/>.
/// </summary>
internal static class LocalAccountPasswordChangeFence
{
    /// <summary>The endpoint metadata that lets a fenced session through.</summary>
    public static readonly AllowedMetadata Allowed = new();

    public static IApplicationBuilder UseLocalAccountPasswordChangeFence(this IApplicationBuilder app)
    {
        return app.ApplicationServices.GetService<LocalAccountAntiforgery.EnabledMarker>() is null
            ? app
            : app.UseMiddleware<Middleware>();
    }

    internal static bool IsFenced(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(LocalAccountSession.PasswordChangeRequiredItem, out var flag)
        && flag is true
        && httpContext.GetEndpoint()?.Metadata.GetMetadata<AllowedMetadata>() is null;

    internal static Task RefuseAsync(HttpContext httpContext)
    {
        var correlationContext = httpContext.RequestServices.GetRequiredService<ICorrelationContext>();
        httpContext.Response.Headers.CacheControl = "private, no-store";
        return Results.Problem(
                title: "Parola değişikliği gerekli.",
                detail: "Devam etmeden önce parolanızı değiştirin.",
                statusCode: StatusCodes.Status403Forbidden,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = IdentitySessionProblemCodesV1.PasswordChangeRequired,
                    ["correlationId"] = correlationContext.IsResolved ? correlationContext.CorrelationId : null,
                })
            .ExecuteAsync(httpContext);
    }

    internal sealed class AllowedMetadata
    {
    }

    internal sealed class Middleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext httpContext)
        {
            if (IsFenced(httpContext))
            {
                await RefuseAsync(httpContext);
                return;
            }

            await next(httpContext);
        }
    }
}
