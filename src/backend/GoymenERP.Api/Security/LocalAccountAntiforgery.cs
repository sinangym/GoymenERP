using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Identity.Application.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

namespace GoymenERP.Api.Security;

/// <summary>
/// KIM-D31: the antiforgery header of the local account session. The login and every write (POST, PUT, PATCH,
/// DELETE) that the <c>LocalAccount</c> cookie authenticates must carry <see cref="HeaderName"/> with the request
/// token from <c>GET /api/v1/identity/session/antiforgery</c>; a missing or invalid header is a 400 with
/// <see cref="IdentitySessionProblemCodesV1.AntiforgeryTokenInvalid"/>. Bearer requests are exempt: they never reach
/// the cookie scheme. The session cookie keeps <c>SameSite=Strict</c> and its <c>__Host-</c> prefix; the
/// antiforgery cookie has the same attributes. Registered only in the <c>LocalAccount</c> mode, so the other modes
/// keep their pipeline unchanged.
/// </summary>
internal static class LocalAccountAntiforgery
{
    public const string HeaderName = "X-Goymen-Antiforgery";

    public const string CookieName = "__Host-goymen-antiforgery";

    /// <summary>The endpoint metadata that requires the header whatever the authentication (the login itself).</summary>
    public static readonly RequiredMetadata Required = new();

    public static void Configure(AntiforgeryOptions options)
    {
        options.HeaderName = HeaderName;
        options.FormFieldName = "__goymen_antiforgery";
        options.Cookie.Name = CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.Cookie.IsEssential = true;
    }

    public static IServiceCollection AddLocalAccountAntiforgery(this IServiceCollection services)
    {
        services.AddAntiforgery(Configure);
        services.AddSingleton<EnabledMarker>();
        return services;
    }

    /// <summary>
    /// Adds the check right after authentication, before the company context and authorization, so a refused
    /// request never reaches a handler, an idempotency record or a body read. A no-op outside the
    /// <c>LocalAccount</c> mode.
    /// </summary>
    public static IApplicationBuilder UseLocalAccountAntiforgery(this IApplicationBuilder app)
    {
        return app.ApplicationServices.GetService<EnabledMarker>() is null
            ? app
            : app.UseMiddleware<Middleware>();
    }

    internal static bool IsWrite(string method) =>
        HttpMethods.IsPost(method)
        || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method)
        || HttpMethods.IsDelete(method);

    internal static bool RequiresValidation(HttpContext httpContext)
    {
        if (!IsWrite(httpContext.Request.Method))
        {
            return false;
        }

        if (httpContext.GetEndpoint()?.Metadata.GetMetadata<RequiredMetadata>() is not null)
        {
            return true;
        }

        var authenticateResult = httpContext.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult;
        return authenticateResult is { Succeeded: true }
            && string.Equals(
                authenticateResult.Ticket?.AuthenticationScheme,
                LocalAccountSession.SchemeName,
                StringComparison.Ordinal);
    }

    internal static Task RefuseAsync(HttpContext httpContext)
    {
        var correlationContext = httpContext.RequestServices.GetRequiredService<ICorrelationContext>();
        httpContext.Response.Headers.CacheControl = "private, no-store";
        return Results.Problem(
                title: "Güvenlik doğrulaması geçersiz.",
                detail: "İstek güvenlik doğrulamasını geçemedi. Sayfayı yenileyip yeniden deneyin.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = IdentitySessionProblemCodesV1.AntiforgeryTokenInvalid,
                    ["correlationId"] = correlationContext.IsResolved ? correlationContext.CorrelationId : null,
                })
            .ExecuteAsync(httpContext);
    }

    internal sealed class RequiredMetadata
    {
    }

    internal sealed class EnabledMarker
    {
    }

    internal sealed class Middleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext httpContext, IAntiforgery antiforgery)
        {
            if (!RequiresValidation(httpContext))
            {
                await next(httpContext);
                return;
            }

            // A missing header is refused before anything (form or body) is read.
            if (!httpContext.Request.Headers.TryGetValue(HeaderName, out var header)
                || header.Count != 1
                || string.IsNullOrWhiteSpace(header[0]))
            {
                await RefuseAsync(httpContext);
                return;
            }

            try
            {
                await antiforgery.ValidateRequestAsync(httpContext);
            }
            catch (AntiforgeryValidationException)
            {
                await RefuseAsync(httpContext);
                return;
            }

            await next(httpContext);
        }
    }
}
