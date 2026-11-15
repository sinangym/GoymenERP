using System.Diagnostics;
using System.Globalization;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace GoymenERP.Api.Observability;

/// <summary>
/// F-032 / KLN-D51 (Sinan 2026-11-15): a configured share of traces is sampled
/// (<c>Observability:Otlp:TraceSamplingRatio</c>) and failed requests are always traced. A head sampler cannot know
/// at the start whether a request will fail, so a trace outside the sampled share is still recorded locally
/// (<see cref="SamplingDecision.RecordOnly"/>) but not marked for export. <see cref="ErpFailedTraceProcessor"/> then
/// marks a recorded span for export when it ends failed. The decision is made from the trace id, so every span of
/// one trace gets the same decision.
/// </summary>
public sealed class ErpFailureAwareSampler : Sampler
{
    private readonly TraceIdRatioBasedSampler ratioSampler;

    public ErpFailureAwareSampler(double samplingRatio)
    {
        if (double.IsNaN(samplingRatio) || samplingRatio < 0d || samplingRatio > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(samplingRatio));
        }

        ratioSampler = new TraceIdRatioBasedSampler(samplingRatio);
        Description = "ErpFailureAwareSampler{"
            + samplingRatio.ToString(CultureInfo.InvariantCulture)
            + "}";
    }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        var result = ratioSampler.ShouldSample(samplingParameters);
        return result.Decision == SamplingDecision.RecordAndSample
            ? result
            : new SamplingResult(SamplingDecision.RecordOnly);
    }
}

/// <summary>
/// F-032 / KLN-D51: runs after <see cref="ErpActivityRedactionProcessor"/> and before the exporter. A recorded span
/// that is not in the sampled share is marked for export when it failed; every exported span carries
/// <see cref="SamplingTag"/> so the sampling decision is visible on the trace (ERP-000L §11.2). A span the
/// redaction processor dropped (fail closed) is never marked again.
/// <para>
/// "Failed" (implementation detail, reversible): the span status is <see cref="ActivityStatusCode.Error"/>, it
/// carries <c>error.type</c>, or its HTTP response status code is a server error (5xx). These are the
/// OpenTelemetry semantic-convention error signals of a server span.
/// </para>
/// </summary>
public sealed class ErpFailedTraceProcessor : BaseProcessor<Activity>
{
    public const string SamplingTag = "erp.trace_sampling";

    /// <summary>The span is in the configured sampled share.</summary>
    public const string SamplingRatio = "ratio";

    /// <summary>The span was outside the sampled share and is exported because it failed.</summary>
    public const string SamplingFailure = "failure";

    private const int StatusCodeCeiling = 600;

    public override void OnEnd(Activity data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!data.IsAllDataRequested)
        {
            // Dropped upstream (for example by the redaction processor): stays dropped.
            return;
        }

        if (data.Recorded)
        {
            data.SetTag(SamplingTag, SamplingRatio);
            return;
        }

        if (IsFailed(data))
        {
            data.ActivityTraceFlags |= ActivityTraceFlags.Recorded;
            data.SetTag(SamplingTag, SamplingFailure);
        }
    }

    public static bool IsFailed(Activity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        if (activity.Status == ActivityStatusCode.Error
            || activity.GetTagItem(ErpActivityRedactionProcessor.ErrorTypeTag) is not null)
        {
            return true;
        }

        var statusCode = activity.GetTagItem(ErpActivityRedactionProcessor.HttpResponseStatusCodeTag) switch
        {
            int value => value,
            long value when value is >= int.MinValue and <= int.MaxValue => (int)value,
            short value => value,
            string text when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                => parsed,
            _ => (int?)null,
        };

        return statusCode is { } code
            && code >= StatusCodes.Status500InternalServerError
            && code < StatusCodeCeiling;
    }
}
