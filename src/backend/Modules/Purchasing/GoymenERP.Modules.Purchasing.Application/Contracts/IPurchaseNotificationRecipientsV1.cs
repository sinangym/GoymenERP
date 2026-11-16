namespace GoymenERP.Modules.Purchasing.Application.Contracts;

/// <summary>ALC-S6: the recorded preparer and approver of the order whose general line reopened.
/// No responsible-person field, role assignment, caller fallback or guessed legacy actor is created.</summary>
public interface IPurchaseNotificationRecipientsV1
{
    Task<PurchaseNotificationRecipientsV1> ReadAsync(Guid companyId, Guid purchaseOrderId,
        Guid generalLineId, CancellationToken cancellationToken = default);
}

public enum PurchaseNotificationRecipientsOutcomeV1 { Found = 1, NotFound = 2, Unassigned = 3, Unavailable = 4 }

public sealed record PurchaseNotificationRecipientsV1(PurchaseNotificationRecipientsOutcomeV1 Outcome,
    Guid CompanyId, Guid PurchaseOrderId, Guid GeneralLineId, long? ObservedOrderVersion,
    IReadOnlyList<Guid> UserIds, string? UnavailableReason = null);
