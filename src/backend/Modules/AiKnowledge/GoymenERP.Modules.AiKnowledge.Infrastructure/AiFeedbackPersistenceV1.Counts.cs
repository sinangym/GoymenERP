using GoymenERP.Modules.AiKnowledge.Domain;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.AiKnowledge.Infrastructure;

public sealed partial class AiFeedbackPersistenceV1
{
    public async Task<IReadOnlyList<AiFeedbackMonthCountV1>> GetMonthCountsAsync(Guid companyId, Guid actorId,
        AiFeedbackCountWindowV1 window, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window); ArgumentNullException.ThrowIfNull(freshConsumerFence);
        var now = clock.GetUtcNow();
        if (window.StartUtc.Offset != TimeSpan.Zero || window.ExclusiveEndUtc.Offset != TimeSpan.Zero || window.AsOfUtc.Offset != TimeSpan.Zero
            || window.StartUtc >= window.ExclusiveEndUtc || window.AsOfUtc < window.StartUtc || window.AsOfUtc > window.ExclusiveEndUtc
            || window.AsOfUtc > now || window.StartUtc < now.AddDays(-AiAnswerReceiptRulesV1.RetentionDays))
            throw new InvalidOperationException("Feedback month is outside the supported retained window.");
        await Fence(companyId, actorId, freshConsumerFence, cancellationToken);
        // One SQL statement: the answer cohort and its highest history version share one read boundary.
        var values = await database.AnswerReceipts.AsNoTracking()
            .Where(answer => answer.CompanyId == companyId && answer.CreatedAtUtc >= window.StartUtc
                && answer.CreatedAtUtc < window.ExclusiveEndUtc && answer.CreatedAtUtc <= window.AsOfUtc)
            .Select(answer => new { answer.Feature, Kind = database.FeedbackEvents
                .Where(feedback => feedback.CompanyId == companyId && feedback.AnswerId == answer.AnswerId
                    && feedback.CreatedAtUtc <= window.AsOfUtc && feedback.CreatedAtUtc < window.ExclusiveEndUtc)
                .OrderByDescending(feedback => feedback.Version).Select(feedback => (int?)feedback.Kind).FirstOrDefault() })
            .GroupBy(value => value.Feature).Select(group => new {
                Feature = group.Key,
                Accepted = group.Sum(value => value.Kind == (int)AiFeedbackKindV1.Accepted ? 1 : 0),
                Corrected = group.Sum(value => value.Kind == (int)AiFeedbackKindV1.Corrected ? 1 : 0),
                Rejected = group.Sum(value => value.Kind == (int)AiFeedbackKindV1.Rejected ? 1 : 0),
                Reverted = group.Sum(value => value.Kind == (int)AiFeedbackKindV1.Reverted ? 1 : 0),
                Unreviewed = group.Sum(value => value.Kind == null ? 1 : 0)
            }).ToArrayAsync(cancellationToken);
        await Fence(companyId, actorId, freshConsumerFence, cancellationToken);
        if (values.Any(value => value.Feature != "Panel")) throw new InvalidOperationException("Unsupported answer feature.");
        return values.Select(value => new AiFeedbackMonthCountV1(value.Feature, value.Accepted, value.Corrected,
            value.Rejected, value.Reverted, value.Unreviewed, window.AsOfUtc)).ToArray();
    }
}
