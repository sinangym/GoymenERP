using GoymenERP.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace GoymenERP.Api.Errors;

public sealed class ErpAuthorizationMiddlewareResultHandler(
    IProblemDetailsService problemDetailsService)
    : IAuthorizationMiddlewareResultHandler
{
    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Succeeded)
        {
            await next(context);
            return;
        }

        var statusCode = authorizeResult.Challenged
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status403Forbidden;

        // F023-2 / KIM-D10: the refusal is recorded once, as authz.denied, before the response is written.
        await AuthorizationDenialHttp.RecordAsync(
            context,
            authorizeResult.Challenged
                ? AuthorizationDenialHttp.SessionInvalid()
                : AuthorizationDenialHttp.ForForbidden(
                    context,
                    policy.Requirements.OfType<ErpPermissionRequirement>().Select(requirement => requirement.Permission)));

        if (context.GetEndpoint()?.Metadata
                .GetMetadata<PrivateNoStoreOnAuthorizationFailureMetadata>() is not null)
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.Pragma = "no-cache";
        }

        if (authorizeResult.Challenged)
        {
            await context.ChallengeAsync();
        }
        else
        {
            await context.ForbidAsync();
        }

        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = statusCode;
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
            },
        });
    }
}

/// <summary>
/// Marks a sensitive read whose authorization challenge/forbid response must not be cached.
/// The marker changes response headers only; it does not alter authentication or authorization.
/// </summary>
public sealed class PrivateNoStoreOnAuthorizationFailureMetadata;
