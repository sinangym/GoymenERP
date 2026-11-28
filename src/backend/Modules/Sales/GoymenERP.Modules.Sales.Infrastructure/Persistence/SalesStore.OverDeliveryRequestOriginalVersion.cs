using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.ApprovalRequests;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

public sealed partial class SalesStore
{
    public async Task<SalesOverDeliveryRequestOriginalVersionReadResultV1> ReadOverDeliveryRequestOriginalVersionAsync(Guid requestId,
        long expectedRequestVersion, string actorId, Func<CancellationToken, Task> freshFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshFence); cancellationToken.ThrowIfCancellationRequested();
        var company = RequireResolvedCompany(); EnterOverRequestRead(freshFence);
        try
        {
            var current = await OverDeliveryRequestApprovalReadAsync(() => (from request in dbContext.ApprovalRequests.AsNoTracking()
                join order in dbContext.Drafts.AsNoTracking() on request.SubjectId equals order.Id
                where request.CompanyId == company && order.CompanyId == company && request.Id == requestId && request.Kind == (int)SalesApprovalRequestKind.OverDelivery
                select new { Request = request, OrderId = order.Id, order.DocumentNo }).SingleOrDefaultAsync(cancellationToken), cancellationToken);
            if (current is null) return new(SalesOverDeliveryRequestDecisionReadStateV1.NotFound, null);
            if (expectedRequestVersion < 1 || expectedRequestVersion == long.MaxValue || string.IsNullOrWhiteSpace(actorId))
                return new(SalesOverDeliveryRequestDecisionReadStateV1.Unavailable, null);
            var entityId = requestId.ToString("D");
            // Exact company/request/original actor/command/audit-before version and attempt-key join BEFORE ambiguity bound.
            var candidates = await OverDeliveryRequestApprovalReadAsync(() => (from attempt in dbContext.ApprovalRequestAttempts.AsNoTracking()
                join audit in dbContext.Set<AuditEvent>().AsNoTracking() on new { CompanyId = (Guid?)attempt.CompanyId, Key = attempt.IdempotencyKeyHash } equals new { audit.CompanyId, Key = audit.IdempotencyKeyHash }
                where attempt.CompanyId == company && audit.CompanyId == company && attempt.RequestId == requestId && attempt.SubjectId == current.OrderId
                    && attempt.ActorId == actorId && audit.ActorId == actorId && attempt.Outcome == AttemptOutcomeRecorded && attempt.RefusalJson == null
                    && audit.SubjectKind == AuditEvent.CompanySubject && audit.EntityType == ApprovalRequestEntityType && audit.EntityId == entityId
                    && audit.Outcome == AuditOutcomes.Succeeded && audit.EntityVersionBefore == expectedRequestVersion
                    && ((attempt.CommandName == SalesApprovalRequestCommands.ApproveOverDeliveryV1 && audit.EventType == "sales.order.over-delivery-request-approved.v1")
                        || (attempt.CommandName == SalesApprovalRequestCommands.RejectOverDeliveryV1 && audit.EventType == "sales.order.over-delivery-request-rejected.v1"))
                select new { Attempt = attempt, Audit = audit }).Take(2).ToArrayAsync(cancellationToken), cancellationToken);
            if (candidates.Length == 0) return new(SalesOverDeliveryRequestDecisionReadStateV1.NotFound, null) { CurrentVersion = current.Request.Version };
            if (candidates.Length != 1) return new(SalesOverDeliveryRequestDecisionReadStateV1.Unavailable, null);
            var candidate = candidates[0]; AuditEvent[] orderEvents = [];
            if (candidate.Attempt.CommandName == SalesApprovalRequestCommands.ApproveOverDeliveryV1)
                orderEvents = await OverDeliveryRequestApprovalReadAsync(() => dbContext.Set<AuditEvent>().AsNoTracking().Where(x =>
                    x.CompanyId == company && x.EntityType == DraftEntityType && x.EntityId == current.OrderId.ToString("D")
                    && x.EventType == "sales.order-draft.over-delivery-approved.v1" && x.IdempotencyKeyHash == candidate.Attempt.IdempotencyKeyHash)
                    .Take(2).ToArrayAsync(cancellationToken), cancellationToken);
            var receipt = RestoreOverDeliveryRequestReceipt(candidate.Attempt, [candidate.Audit], orderEvents, current.DocumentNo, actorId);
            var retained = SalesDraftService.ProjectOverDeliveryRequest(SalesApprovalRequestRows.Restore(current.Request));
            var retainedSourceDigest = SalesOverDeliveryRequestDecisionDigestsV1.SourceFactsDigest(company, requestId, retained, current.DocumentNo);
            return receipt is null || receipt.ExpectedRequestVersion != expectedRequestVersion || receipt.SourceFactsDigest != retainedSourceDigest
                ? new(SalesOverDeliveryRequestDecisionReadStateV1.Unavailable, null)
                : new(SalesOverDeliveryRequestDecisionReadStateV1.Found, receipt);
        }
        finally { overDeliveryRequestDecisionReadFence = null; }
    }
}
