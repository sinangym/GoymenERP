namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

internal sealed class WorkNotificationInboxRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public string SourceName { get; set; } = "";
    public Guid EventId { get; set; }
    public string EventType { get; set; } = "";
    public int SchemaVersion { get; set; }
    public string PayloadSha256 { get; set; } = "";
    public DateTimeOffset RecordedAtUtc { get; set; }
}
internal sealed class WorkNotificationRow : IWorkTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string SourceName { get; set; } = "";
    public Guid EventId { get; set; }
    public int Kind { get; set; }
    public Guid RecipientUserId { get; set; }
    public Guid? SalesOrderId { get; set; }
    public Guid? SalesOrderLineId { get; set; }
    public long? ObservedSalesOrderVersion { get; set; }
    public Guid? PurchaseOrderId { get; set; }
    public Guid? PurchaseOrderLineId { get; set; }
    public long? ObservedPurchaseOrderVersion { get; set; }
    public Guid SourceEntityId { get; set; }
    public string SourceVersion { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public string ReasonText { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string CorrelationId { get; set; } = "";
    public string? CausationId { get; set; }
}
internal sealed class WorkNotificationPassivationRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid NotificationId { get; set; }
    public DateTimeOffset PassivatedAtUtc { get; set; }
    public int ActiveDays { get; set; }
    public string PolicyVersion { get; set; } = "";
}
