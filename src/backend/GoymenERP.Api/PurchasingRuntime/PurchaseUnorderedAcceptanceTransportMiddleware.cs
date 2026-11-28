using GoymenERP.Api.Endpoints;
using Microsoft.Net.Http.Headers;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>Keep the occurrence command private even when routing rejects its media type before the owner runs.</summary>
public sealed class PurchaseUnorderedAcceptanceTransportMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (HttpMethods.IsPost(context.Request.Method)
            && (string.Equals(path, PurchasingEndpoints.UnorderedAcceptancesPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, PurchasingEndpoints.UnorderedAcceptancesPath + "/", StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.OnStarting(static state =>
            {
                var headers = ((HttpContext)state).Response.Headers;
                headers[HeaderNames.CacheControl] = "private, no-store";
                headers[HeaderNames.Pragma] = "no-cache";
                headers.Remove(HeaderNames.ETag);
                headers.Remove(HeaderNames.LastModified);
                headers.Remove(HeaderNames.Expires);
                return Task.CompletedTask;
            }, context);
        }

        await next(context);
    }
}
