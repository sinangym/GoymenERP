using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>One company-filtered owner query; recipients remain recorded actors, never the event actor.</summary>
public sealed class PurchaseNotificationRecipients(PurchasingDbContext database, ICompanyContext company)
    : IPurchaseNotificationRecipientsV1
{
    public async Task<PurchaseNotificationRecipientsV1> ReadAsync(Guid companyId, Guid purchaseOrderId,
        Guid generalLineId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireCompany(companyId);
        if (purchaseOrderId == Guid.Empty || generalLineId == Guid.Empty)
            return Missing(companyId, purchaseOrderId, generalLineId);

        var row = await (from order in database.Orders.AsNoTracking()
            join line in database.OrderLines.AsNoTracking()
                on new { order.CompanyId, OrderId = order.Id } equals new { line.CompanyId, line.OrderId }
            where order.CompanyId == companyId && order.Id == purchaseOrderId && line.LineId == generalLineId
                && line.Kind == (int)PurchaseOrderLineKind.General && line.ParentLineId == null
            select new PurchaseNotificationRecipientFacts(order.CompanyId, order.Id, line.LineId,
                order.Version, order.Status, order.CreatedBy, order.ApprovedBy, order.ApprovedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
        RequireCompany(companyId);
        return row is null ? Missing(companyId, purchaseOrderId, generalLineId)
            : Compose(companyId, purchaseOrderId, generalLineId, row);
    }

    internal static PurchaseNotificationRecipientsV1 Compose(Guid companyId, Guid orderId, Guid lineId,
        PurchaseNotificationRecipientFacts facts)
    {
        if (facts.CompanyId != companyId || facts.OrderId != orderId || facts.GeneralLineId != lineId
            || facts.Version < 1 || (facts.Status != (int)PurchaseOrderStatus.Approved && facts.Status != (int)PurchaseOrderStatus.Cancelled))
            return new(PurchaseNotificationRecipientsOutcomeV1.Unavailable, companyId, orderId, lineId,
                null, [], "purchase_notification_source_unreadable");
        // Legacy orders can lack approval evidence. A partial recipient set is not reported as complete delivery.
        if (string.IsNullOrWhiteSpace(facts.CreatedBy) || string.IsNullOrWhiteSpace(facts.ApprovedBy)
            || facts.ApprovedAtUtc is null)
            return new(PurchaseNotificationRecipientsOutcomeV1.Unassigned, companyId, orderId, lineId,
                facts.Version, [], "purchase_notification_actors_unassigned");
        if (!Guid.TryParse(facts.CreatedBy, out var preparer) || preparer == Guid.Empty
            || !Guid.TryParse(facts.ApprovedBy, out var approver) || approver == Guid.Empty)
            return new(PurchaseNotificationRecipientsOutcomeV1.Unavailable, companyId, orderId, lineId,
                facts.Version, [], "purchase_notification_actor_identity_unreadable");
        return new(PurchaseNotificationRecipientsOutcomeV1.Found, companyId, orderId, lineId,
            facts.Version, new[] { preparer, approver }.Distinct().Order().ToArray());
    }

    private void RequireCompany(Guid companyId)
    {
        if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId)
            throw new InvalidOperationException("Purchase notification recipients require the original resolved company.");
    }
    private static PurchaseNotificationRecipientsV1 Missing(Guid companyId, Guid orderId, Guid lineId) =>
        new(PurchaseNotificationRecipientsOutcomeV1.NotFound, companyId, orderId, lineId, null, []);
}

internal sealed record PurchaseNotificationRecipientFacts(Guid CompanyId, Guid OrderId, Guid GeneralLineId,
    long Version, int Status, string CreatedBy, string? ApprovedBy, DateTimeOffset? ApprovedAtUtc);
