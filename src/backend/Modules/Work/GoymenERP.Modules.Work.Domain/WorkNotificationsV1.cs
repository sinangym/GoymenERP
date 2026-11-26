namespace GoymenERP.Modules.Work.Domain;

public enum WorkNotificationKindV1 { AllocationReleased = 1, PurchaseSupplyRevived = 2, AccountingCostProposalStale = 3 }
public enum WorkNotificationRecipientOutcomeV1 { Found = 1, NotFound = 2, Unassigned = 3, Unavailable = 4 }
public sealed record WorkNotificationRecipientV1(WorkNotificationRecipientOutcomeV1 Outcome, Guid CompanyId,
    Guid SalesOrderId, Guid SalesOrderLineId, Guid? UserId, long? ObservedSalesOrderVersion);
public interface IWorkNotificationRecipientReaderV1
{
    Task<WorkNotificationRecipientV1> ReadAsync(Guid salesOrderId, Guid salesOrderLineId, CancellationToken cancellationToken = default);
}
public sealed record WorkNotificationRetentionV1(Guid CompanyId, int ActiveDays, string Version);
public interface IWorkNotificationRetentionReaderV1
{
    Task<WorkNotificationRetentionV1> ReadAsync(CancellationToken cancellationToken = default);
}
public interface IWorkNotificationActorMembershipV1
{
    Task<bool> IsActiveAsync(Guid companyId, Guid actorId, CancellationToken cancellationToken = default);
}
public sealed record WorkNotificationTargetV1(Guid SalesOrderId, Guid SalesOrderLineId);
public sealed record WorkPurchaseNotificationTargetV1(Guid PurchaseOrderId, Guid PurchaseOrderLineId);
public sealed record WorkPurchaseNotificationRecipientsV1(WorkNotificationRecipientOutcomeV1 Outcome, Guid CompanyId,
    Guid PurchaseOrderId, Guid PurchaseOrderLineId, long? ObservedOrderVersion, IReadOnlyList<Guid> UserIds);
public interface IWorkPurchaseNotificationRecipientReaderV1
{
    Task<WorkPurchaseNotificationRecipientsV1> ReadAsync(Guid companyId, Guid purchaseOrderId, Guid purchaseOrderLineId,
        CancellationToken cancellationToken = default);
}
public sealed record DeliverWorkNotificationV1(Guid CompanyId, string SourceName, Guid EventId, string EventType,
    int SchemaVersion, string PayloadJson, WorkNotificationKindV1 Kind, Guid SourceEntityId, string SourceVersion,
    string ReasonCode, string ReasonText, DateTimeOffset OccurredAtUtc, string CorrelationId, string? CausationId,
    IReadOnlyList<WorkNotificationTargetV1> Targets, WorkPurchaseNotificationTargetV1? PurchaseTarget = null,
    Guid? PreparedByUserId = null);
public interface IWorkNotificationDeliveryV1
{
    Task DeliverAsync(DeliverWorkNotificationV1 command, CancellationToken cancellationToken = default);
}
public sealed record WorkNotificationItemV1(Guid Id, Guid CompanyId, string Kind, Guid? SalesOrderId, Guid? SalesOrderLineId,
    Guid SourceEntityId, string SourceVersion, string ReasonCode, string ReasonText, DateTimeOffset OccurredAtUtc,
    DateTimeOffset RecordedAtUtc, DateTimeOffset? PassivatedAtUtc, Guid? PurchaseOrderId = null, Guid? PurchaseOrderLineId = null);
public sealed record WorkNotificationPageV1(Guid CompanyId, IReadOnlyList<WorkNotificationItemV1> Items, bool HasMore, Guid? NextCursor);
public interface IWorkNotificationReadV1
{
    Task<WorkNotificationPageV1> ListMineAsync(bool includePassive, CancellationToken cancellationToken = default, Guid? beforeId = null);
}
public interface IWorkNotificationMaintenanceV1
{
    Task PassivateExpiredAsync(CancellationToken cancellationToken = default);
}
