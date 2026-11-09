namespace GoymenERP.Modules.Work.Infrastructure.Outbox;

/// <summary>
/// Configuration section <c>Work:OutboxDispatcher</c>. The dispatcher is OFF unless <see cref="Enabled"/> is true
/// (production activation is a human gate). No business value is defaulted: when enabled, every number below must
/// be configured explicitly or start-up is refused (NEEDS-SINAN lists the values to choose).
/// </summary>
public sealed class WorkOutboxDispatcherOptions
{
    public const string SectionName = "Work:OutboxDispatcher";

    public bool Enabled { get; set; }
    public int? BatchSize { get; set; }
    public int? MaxAttempts { get; set; }
    public TimeSpan? InitialBackoff { get; set; }
    public TimeSpan? MaxBackoff { get; set; }
    public TimeSpan? PollInterval { get; set; }

    internal WorkOutboxDispatcherSettings Resolve()
    {
        var missing = new List<string>();
        if (BatchSize is not > 0) missing.Add(nameof(BatchSize));
        if (MaxAttempts is not > 0) missing.Add(nameof(MaxAttempts));
        if (InitialBackoff is not { } initial || initial <= TimeSpan.Zero) missing.Add(nameof(InitialBackoff));
        if (MaxBackoff is not { } max || max <= TimeSpan.Zero || (InitialBackoff is { } i && max < i)) missing.Add(nameof(MaxBackoff));
        if (PollInterval is not { } poll || poll <= TimeSpan.Zero) missing.Add(nameof(PollInterval));
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"{SectionName} is enabled but these values are not configured with valid positive values: {string.Join(", ", missing)}.");
        }

        return new WorkOutboxDispatcherSettings(
            BatchSize!.Value, MaxAttempts!.Value, InitialBackoff!.Value, MaxBackoff!.Value, PollInterval!.Value);
    }
}

internal sealed record WorkOutboxDispatcherSettings(
    int BatchSize,
    int MaxAttempts,
    TimeSpan InitialBackoff,
    TimeSpan MaxBackoff,
    TimeSpan PollInterval)
{
    /// <summary>Exponential from the initial value, capped; attempt is the 1-based number of the failed attempt.</summary>
    internal TimeSpan BackoffAfter(int failedAttempt)
    {
        var delay = InitialBackoff;
        for (var step = 1; step < failedAttempt; step++)
        {
            if (delay >= MaxBackoff || delay > MaxBackoff / 2)
            {
                return MaxBackoff;
            }

            delay += delay;
        }

        return delay < MaxBackoff ? delay : MaxBackoff;
    }
}
