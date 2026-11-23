using GoymenERP.Modules.Audit.Application.Contracts;

namespace GoymenERP.Modules.Audit.Infrastructure.Services;

/// <summary>
/// One closed aggregation window that folded at least one refusal (F-023, F023-4). <see cref="Template"/> is the first
/// refusal folded into the window; it gives the summary row its actor, command, permission and class.
/// </summary>
internal sealed record RefusedAttemptAggregationSummary(
    RefusedAttemptV1 Template,
    long Count,
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc);

/// <summary>
/// The in-process counter of the separate short refused attempt writes (F-023, F023-4). Only the standalone path asks
/// it; terminal refusals, authorization refusals (security channel) and AI refusals are outside it. The key is
/// <c>(CompanyId, ActorId, CommandName, ProblemCode)</c>. A window starts at the first refusal of a key; inside it, the
/// first <c>Threshold</c> refusals are written as their own rows, the rest are only counted, and once the window has
/// ended its count becomes one append-only summary row. The recorder writes the ended summaries of a company on that
/// company's next standalone refusal (<see cref="TakeEnded"/>), so a request only ever writes its own company's rows
/// and no background writer is needed. The counter is volatile by design (volume protection, not a durability
/// guarantee): what the process held when it stopped is lost; a summary that cannot be written is counted in
/// <c>audit_refused_attempt_aggregation_lost</c>.
/// With <see cref="RefusedAttemptAggregationOptions.Disabled"/> (the shipped KIM-D11 start state) every refusal is
/// admitted as its own row and nothing is held.
/// </summary>
internal sealed class RefusedAttemptAggregator(RefusedAttemptAggregationOptions options, TimeProvider time)
{
    private readonly Lock gate = new();
    private readonly Dictionary<AggregationKey, AggregationWindow> windows = [];
    private readonly List<RefusedAttemptAggregationSummary> closed = [];

    public bool IsEnabled => options.IsEnabled;

    /// <summary>
    /// Whether a validated, company-checked refusal is written as its own row (<see langword="true"/>) or only counted
    /// into its window's summary (<see langword="false"/>).
    /// </summary>
    public bool Admit(RefusedAttemptV1 attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (options.Window is not { } length || options.Threshold is not { } threshold || !IsAggregatable(attempt))
        {
            return true;
        }

        var now = time.GetUtcNow().ToUniversalTime();
        var key = new AggregationKey(attempt.CompanyId, attempt.ActorId, attempt.CommandName, attempt.ProblemCode);
        lock (gate)
        {
            if (windows.TryGetValue(key, out var window) && now >= window.EndUtc)
            {
                windows.Remove(key);
                Close(window, window.EndUtc);
                window = null;
            }

            if (window is null)
            {
                window = new AggregationWindow(now, now + length);
                windows[key] = window;
            }

            window.Seen++;
            if (window.Seen <= threshold)
            {
                return true;
            }

            window.Template ??= attempt;
            window.Folded++;
        }

        RefusedAttemptMetrics.RecordAggregated(attempt.RefusalClass);
        return false;
    }

    /// <summary>
    /// Takes the summaries of the ended windows of <paramref name="companyId"/>. Ended windows of other companies are
    /// closed too: one that folded nothing is dropped, one that folded refusals waits for its own company's next
    /// standalone refusal, so the held state stays bounded by the open keys and the folded windows.
    /// </summary>
    public IReadOnlyList<RefusedAttemptAggregationSummary> TakeEnded(Guid companyId)
    {
        if (!IsEnabled)
        {
            return [];
        }

        var now = time.GetUtcNow().ToUniversalTime();
        lock (gate)
        {
            foreach (var (key, window) in windows.ToArray())
            {
                if (now >= window.EndUtc)
                {
                    windows.Remove(key);
                    Close(window, window.EndUtc);
                }
            }

            var taken = closed.Where(summary => summary.Template.CompanyId == companyId).ToArray();
            if (taken.Length > 0)
            {
                closed.RemoveAll(summary => summary.Template.CompanyId == companyId);
            }

            return taken;
        }
    }

    /// <summary>AI refusals stay outside the aggregation (F-023 §Hacim kontrolü): every one is its own row.</summary>
    private static bool IsAggregatable(RefusedAttemptV1 attempt) =>
        string.Equals(attempt.AiInvolvement, AuditAiInvolvement.None, StringComparison.Ordinal)
        && attempt.AiContext is null
        && !string.Equals(attempt.ActorType, AuditActorTypes.Ai, StringComparison.Ordinal);

    private void Close(AggregationWindow window, DateTimeOffset endUtc)
    {
        if (window.Folded > 0 && window.Template is { } template)
        {
            closed.Add(new RefusedAttemptAggregationSummary(template, window.Folded, window.StartUtc, endUtc));
        }
    }

    private readonly record struct AggregationKey(Guid CompanyId, string ActorId, string CommandName, string ProblemCode);

    private sealed class AggregationWindow(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        public DateTimeOffset StartUtc { get; } = startUtc;

        public DateTimeOffset EndUtc { get; } = endUtc;

        public long Seen { get; set; }

        public long Folded { get; set; }

        public RefusedAttemptV1? Template { get; set; }
    }
}
