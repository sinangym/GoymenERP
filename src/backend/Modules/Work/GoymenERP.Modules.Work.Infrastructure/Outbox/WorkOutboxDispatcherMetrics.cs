using System.Diagnostics.Metrics;

namespace GoymenERP.Modules.Work.Infrastructure.Outbox;

/// <summary>
/// Dispatcher metrics (B-24, F-021). Low-cardinality tags only: the registered source name, the registered consumer
/// name and an event type that reached a consumer through an exact subscription. Never company, message, correlation
/// or payload values. Counters that a source alone decides (unrouted, invalid envelope, source failure) carry only the
/// source tag, so a faulty source cannot grow the tag set.
/// </summary>
public static class WorkOutboxDispatcherMetrics
{
    public const string MeterName = "GoymenERP.Work.OutboxDispatcher";

    public const string DeliveredCounterName = "work_outbox_delivered";
    public const string RetryCounterName = "work_outbox_retry";
    public const string PoisonCounterName = "work_outbox_poison";
    public const string DuplicateCounterName = "work_outbox_duplicate";
    public const string CrossSourceDuplicateCounterName = "work_outbox_cross_source_duplicate";
    public const string InvalidEnvelopeCounterName = "work_outbox_invalid_envelope";
    public const string UnroutedCounterName = "work_outbox_unrouted";
    public const string SourceFailureCounterName = "work_outbox_source_failure";
    public const string HandlerDurationHistogramName = "work_outbox_handler_duration";
    public const string CycleDurationHistogramName = "work_outbox_cycle_duration";

    public const string SourceTag = "source";
    public const string ConsumerTag = "consumer";
    public const string EventTypeTag = "event_type";
    public const string OutcomeTag = "outcome";

    public const string OutcomeSucceeded = "succeeded";
    public const string OutcomeFailed = "failed";
    public const string OutcomeRefused = "refused";

    public const string SecondsUnit = "s";

    /// <summary>Tag value for a source whose name is not a valid stored name (the raw value is never a tag).</summary>
    public const string InvalidSourceName = "invalid";

    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Delivered = Meter.CreateCounter<long>(DeliveredCounterName);
    private static readonly Counter<long> Retry = Meter.CreateCounter<long>(RetryCounterName);
    private static readonly Counter<long> Poison = Meter.CreateCounter<long>(PoisonCounterName);
    private static readonly Counter<long> Duplicate = Meter.CreateCounter<long>(DuplicateCounterName);
    private static readonly Counter<long> CrossSourceDuplicate = Meter.CreateCounter<long>(CrossSourceDuplicateCounterName);
    private static readonly Counter<long> InvalidEnvelope = Meter.CreateCounter<long>(InvalidEnvelopeCounterName);
    private static readonly Counter<long> Unrouted = Meter.CreateCounter<long>(UnroutedCounterName);
    private static readonly Counter<long> SourceFailure = Meter.CreateCounter<long>(SourceFailureCounterName);
    private static readonly Histogram<double> HandlerDuration =
        Meter.CreateHistogram<double>(HandlerDurationHistogramName, SecondsUnit);
    private static readonly Histogram<double> CycleDuration =
        Meter.CreateHistogram<double>(CycleDurationHistogramName, SecondsUnit);

    internal static void RecordDelivered(string source, string consumer, string eventType) =>
        Delivered.Add(1, Tag(SourceTag, source), Tag(ConsumerTag, consumer), Tag(EventTypeTag, eventType));

    internal static void RecordRetry(string source, string consumer, string eventType) =>
        Retry.Add(1, Tag(SourceTag, source), Tag(ConsumerTag, consumer), Tag(EventTypeTag, eventType));

    internal static void RecordPoison(string source, string consumer, string eventType) =>
        Poison.Add(1, Tag(SourceTag, source), Tag(ConsumerTag, consumer), Tag(EventTypeTag, eventType));

    internal static void RecordDuplicate(string source, string consumer, string eventType) =>
        Duplicate.Add(1, Tag(SourceTag, source), Tag(ConsumerTag, consumer), Tag(EventTypeTag, eventType));

    internal static void RecordCrossSourceDuplicate(string source, string consumer, string eventType) =>
        CrossSourceDuplicate.Add(1, Tag(SourceTag, source), Tag(ConsumerTag, consumer), Tag(EventTypeTag, eventType));

    internal static void RecordInvalidEnvelope(string source) => InvalidEnvelope.Add(1, Tag(SourceTag, source));

    internal static void RecordUnrouted(string source) => Unrouted.Add(1, Tag(SourceTag, source));

    internal static void RecordSourceFailure(string source) => SourceFailure.Add(1, Tag(SourceTag, source));

    internal static void RecordHandlerDuration(TimeSpan elapsed, string source, string consumer, string eventType, string outcome) =>
        HandlerDuration.Record(
            elapsed.TotalSeconds,
            Tag(SourceTag, source), Tag(ConsumerTag, consumer), Tag(EventTypeTag, eventType), Tag(OutcomeTag, outcome));

    internal static void RecordCycleDuration(TimeSpan elapsed) => CycleDuration.Record(elapsed.TotalSeconds);

    private static KeyValuePair<string, object?> Tag(string name, string value) => new(name, value);
}
