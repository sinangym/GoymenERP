using Microsoft.Net.Http.Headers;

namespace GoymenERP.Api.Shell;

public sealed class ShellCapabilityTransportMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        if (httpContext.GetEndpoint()?
                .Metadata.GetMetadata<ShellCapabilityPrivateNoStoreMetadata>() is null)
        {
            await next(httpContext);
            return;
        }

        httpContext.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;
            headers[HeaderNames.CacheControl] = "private, no-store";
            headers[HeaderNames.Pragma] = "no-cache";
            headers.Remove(HeaderNames.ETag);
            headers.Remove(HeaderNames.LastModified);
            headers.Remove(HeaderNames.Expires);
            return Task.CompletedTask;
        }, httpContext);

        await next(httpContext);
    }
}

public sealed class ShellCapabilityPrivateNoStoreMetadata
{
    private ShellCapabilityPrivateNoStoreMetadata()
    {
    }

    public static ShellCapabilityPrivateNoStoreMetadata Instance { get; } = new();
}
