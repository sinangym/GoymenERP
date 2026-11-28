using System.Text.Json;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    public async Task<SalesDiscountOriginalVersionReadResultV1> ReadDiscountOriginalVersionAsync(Guid draftId, long expectedVersion,
        string actorId, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshFence); var company = RequireResolvedCompany(); var entity = draftId.ToString("D");
        await Fence();
        // Exact successful command/version/actor candidates BEFORE the ambiguity bound. Unrelated history cannot age out recovery.
        var candidates = await (from attempt in dbContext.DraftAttempts.AsNoTracking()
                                join audit in dbContext.Set<AuditEvent>().AsNoTracking()
                                    on new { CompanyId = (Guid?)attempt.CompanyId, Key = attempt.IdempotencyKeyHash }
                                    equals new { audit.CompanyId, Key = audit.IdempotencyKeyHash }
                                where attempt.CompanyId == company && attempt.DraftId == draftId
                                    && attempt.CommandName == SalesDraftCommands.ApproveDiscountExceptionsV1
                                    && attempt.ActorId == actorId && attempt.Outcome == AttemptOutcomeRecorded
                                    && attempt.ResultJson != null && attempt.RefusalJson == null
                                    && audit.CompanyId == company && audit.EntityType == DraftEntityType && audit.EntityId == entity
                                    && audit.EventType == "sales.order-draft.discount-exceptions-approved.v1"
                                    && audit.ActorId == actorId && audit.EntityVersionBefore == expectedVersion
                                select new { Attempt = attempt, Audit = audit }).Take(2).ToArrayAsync(cancellationToken);
        await Fence();
        if (candidates.Length == 0)
        {
            var current = await dbContext.Drafts.AsNoTracking().Where(x => x.CompanyId == company && x.Id == draftId)
                .Select(x => (long?)x.Version).SingleOrDefaultAsync(cancellationToken);
            await Fence();
            return new(SalesDiscountDecisionReadStateV1.NotFound, null) { CurrentVersion = current };
        }
        if (candidates.Length != 1) return new(SalesDiscountDecisionReadStateV1.Unavailable, null);
        try
        {
            var candidate = candidates[0];
            var original = JsonSerializer.Deserialize<SalesDraftReplayResult>(candidate.Attempt.ResultJson!, SalesDraftJson.Options);
            if (original?.Withheld is null) return new(SalesDiscountDecisionReadStateV1.Unavailable, null);
            var response = new SalesOrderDraftResponse(original.Withheld, true, candidate.Attempt.CorrelationId);
            var receipt = RestoreOriginalDiscountReceiptBound(candidate.Attempt, [candidate.Audit], actorId,
                SalesPermissionManifestV1.OrdersDiscountExceptionApprove, candidate.Attempt.IdempotencyKeyHash, candidate.Attempt.RequestHash, response);
            return receipt?.ExpectedVersion == expectedVersion
                ? new(SalesDiscountDecisionReadStateV1.Found, receipt) : new(SalesDiscountDecisionReadStateV1.Unavailable, null);
        }
        catch (Exception failure) when (failure is JsonException or InvalidOperationException or ArgumentException or NotSupportedException)
        { return new(SalesDiscountDecisionReadStateV1.Unavailable, null); }

        async Task Fence()
        {
            cancellationToken.ThrowIfCancellationRequested(); RequireCompany(company);
            await freshFence(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested(); RequireCompany(company);
        }
    }
}
