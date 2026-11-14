using System.Diagnostics;
using GoymenERP.BuildingBlocks.Observability;
using Microsoft.AspNetCore.Routing;
using OpenTelemetry;

namespace GoymenERP.Api.Observability;

/// <summary>
/// F-032 / ERP-000L §6.3: runs before the OTLP exporter and keeps only an allowlist of span tags. URL paths and
/// query strings, client addresses, user agents, exception messages and any instrumentation-added value are
/// removed before a span leaves the process; the span name becomes the route template, never the raw path.
/// A span whose event tags cannot be cleaned is not exported (fail closed).
/// </summary>
public sealed class ErpActivityRedactionProcessor : BaseProcessor<Activity>
{
    public const string HttpRequestMethodTag = "http.request.method";

    public const string HttpRouteTag = "http.route";

    public const string HttpResponseStatusCodeTag = "http.response.status_code";

    public const string ErrorTypeTag = "error.type";

    public const string CorrelationIdTag = "erp.correlation_id";

    public const string CausationIdTag = "erp.causation_id";

    public const string SourceChannelTag = "erp.source_channel";

    public const string ExceptionTypeEventTag = "exception.type";

    public static readonly IReadOnlySet<string> AllowedTagNames = new HashSet<string>(StringComparer.Ordinal)
    {
        HttpRequestMethodTag,
        HttpRouteTag,
        HttpResponseStatusCodeTag,
        ErrorTypeTag,
        CorrelationIdTag,
        CausationIdTag,
        SourceChannelTag,
    };

    /// <summary>
    /// Copies the request's bounded, already-validated values onto the current server span so they survive the
    /// allowlist. The route is the matched template (for example <c>/api/v1/work/items/{id}</c>), never the path.
    /// The status code is copied only when the pipeline completed; a request that threw gets its status from the
    /// exception handler later, so a premature value is not written.
    /// </summary>
    public static void EnrichRequestActivity(
        Activity? activity,
        HttpContext httpContext,
        ICorrelationContext correlationContext,
        bool includeStatusCode)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(correlationContext);

        if (activity is null || !activity.IsAllDataRequested)
        {
            return;
        }

        activity.SetTag(CorrelationIdTag, correlationContext.CorrelationId);
        activity.SetTag(CausationIdTag, correlationContext.CausationId);
        activity.SetTag(SourceChannelTag, correlationContext.SourceChannel);
        activity.SetTag(HttpRequestMethodTag, NormalizeMethod(httpContext.Request.Method));
        if (includeStatusCode)
        {
            activity.SetTag(HttpResponseStatusCodeTag, httpContext.Response.StatusCode);
        }

        var route = (httpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        if (!string.IsNullOrWhiteSpace(route))
        {
            activity.SetTag(HttpRouteTag, route);
        }
    }

    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var tagNames = new List<string>();
        foreach (var tag in data.TagObjects)
        {
            if (!AllowedTagNames.Contains(tag.Key))
            {
                tagNames.Add(tag.Key);
            }
        }

        foreach (var tagName in tagNames)
        {
            data.SetTag(tagName, null);
        }

        foreach (var link in data.Links)
        {
            if (link.Tags is not null && link.Tags.Any())
            {
                // Links are not produced by the ERP sources; one carrying tags is not inspected, it is dropped.
                Unrecord(data);
                return;
            }
        }

        if (!TryCleanEvents(data))
        {
            Unrecord(data);
            return;
        }

        data.DisplayName = BuildDisplayName(data);
        if (data.Status == ActivityStatusCode.Error)
        {
            // The description can repeat an exception message; the status code alone is kept.
            data.SetStatus(ActivityStatusCode.Error);
        }
    }

    private static bool TryCleanEvents(Activity data)
    {
        foreach (var activityEvent in data.Events)
        {
            if (activityEvent.Tags is not ActivityTagsCollection tags)
            {
                if (activityEvent.Tags.Any())
                {
                    return false;
                }

                continue;
            }

            var eventTagNames = new List<string>();
            foreach (var tag in tags)
            {
                if (!string.Equals(tag.Key, ExceptionTypeEventTag, StringComparison.Ordinal))
                {
                    eventTagNames.Add(tag.Key);
                }
            }

            foreach (var eventTagName in eventTagNames)
            {
                tags.Remove(eventTagName);
            }
        }

        return true;
    }

    private static string BuildDisplayName(Activity data)
    {
        var method = data.GetTagItem(HttpRequestMethodTag) as string;
        var route = data.GetTagItem(HttpRouteTag) as string;
        if (string.IsNullOrEmpty(method))
        {
            return data.Source.Name == ErpTelemetry.ActivitySourceName ? data.OperationName : data.Source.Name;
        }

        return string.IsNullOrEmpty(route) ? method : method + " " + route;
    }

    private static string NormalizeMethod(string method)
    {
        return method switch
        {
            "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" => method,
            _ => "_OTHER",
        };
    }

    private static void Unrecord(Activity data)
    {
        data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        data.IsAllDataRequested = false;
    }
}
