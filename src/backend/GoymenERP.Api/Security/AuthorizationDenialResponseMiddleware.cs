using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using Microsoft.AspNetCore.Authorization;

namespace GoymenERP.Api.Security;

/// <summary>
/// F023-2 / KIM-D10: records a 401 or 403 answered by an endpoint itself (a module service fence such as
/// <c>RequireAsync</c>/<c>RequireStillAsync</c>, or a fresh authority check that failed during the request) as one
/// <c>authz.denied</c> security event. It runs after authorization, so the refusals of the authorization result
/// handler and of the company context middleware are recorded there and never reach this point twice; the request
/// scoped recorder writes at most once per request either way. The response is not changed.
/// </summary>
/// <remarks>
/// An anonymous endpoint (login, health) is skipped: a refused login is <c>auth.login.failed</c>, not a denial.
/// Other refusal statuses (404 that hides a resource, 400, 409, 422) are not authorization denials and are not recorded here.
/// </remarks>
public sealed class AuthorizationDenialResponseMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        await next(httpContext);

        var statusCode = httpContext.Response.StatusCode;
        if (statusCode is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden))
        {
            return;
        }

        var endpoint = httpContext.GetEndpoint();
        if (endpoint is null || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
        {
            return;
        }

        var denial = statusCode == StatusCodes.Status401Unauthorized
            ? AuthorizationDenialHttp.SessionInvalid()
            : AuthorizationDenialHttp.ForForbidden(
                httpContext,
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(data => data.Policy));
        await AuthorizationDenialHttp.RecordAsync(httpContext, denial);
    }
}

/// <summary>
/// The host side of the single F023-2 helper: maps an HTTP refusal to an <see cref="AuthorizationDenialV1"/> draft
/// and hands it to the request scoped <see cref="IAuthorizationDenialRecorderV1"/>.
/// </summary>
internal static class AuthorizationDenialHttp
{
    public static AuthorizationDenialV1 SessionInvalid() => new()
    {
        ReasonCode = AuthorizationDenialReasonCodesV1.SessionInvalid,
    };

    public static AuthorizationDenialV1 CompanyContextMismatch(Guid? requestedCompanyId) => new()
    {
        ReasonCode = AuthorizationDenialReasonCodesV1.CompanyContextMismatch,
        RequestedCompanyId = requestedCompanyId,
    };

    /// <summary>
    /// A 403 is <c>permission_missing</c> for an authenticated user with an internal user id, and
    /// <c>session_invalid</c> otherwise. The permission is kept only when exactly one registered permission guards
    /// the endpoint; a fence behind the default policy has no endpoint permission, so none is stored.
    /// </summary>
    public static AuthorizationDenialV1 ForForbidden(HttpContext httpContext, IEnumerable<string?> permissions)
    {
        var currentUser = httpContext.RequestServices.GetService<ICurrentUser>();
        if (currentUser is null || !currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.UserId))
        {
            return SessionInvalid();
        }

        return new AuthorizationDenialV1
        {
            ReasonCode = AuthorizationDenialReasonCodesV1.PermissionMissing,
            PermissionCode = SingleRegisteredPermission(permissions),
        };
    }

    public static string? SingleRegisteredPermission(IEnumerable<string?> permissions)
    {
        var registered = permissions
            .Where(permission => permission is not null && ErpPermissions.IsKnown(permission))
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();
        return registered.Count == 1 ? registered[0] : null;
    }

    public static async Task RecordAsync(HttpContext httpContext, AuthorizationDenialV1 denial)
    {
        var recorder = httpContext.RequestServices.GetService<IAuthorizationDenialRecorderV1>();
        if (recorder is null || recorder.HasAttempted)
        {
            return;
        }

        // Not RequestAborted: a client that disconnects must not erase the record of its own refusal. The recorder
        // never throws for a write failure, and the refusal stands whatever the write result is.
        await recorder.RecordAsync(denial, CancellationToken.None);
    }
}
