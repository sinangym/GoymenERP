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
/// <summary>
/// D9 (WP2): the passivation of one event-derived notification (kinds 4 and 5 of <c>work.InboxNotifications</c>, the single
/// notification store). The row keeps its policy evidence; the notification itself is never deleted.
/// </summary>
internal sealed class WorkNotificationPassivationRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid NotificationId { get; set; }
    public DateTimeOffset PassivatedAtUtc { get; set; }
    public int ActiveDays { get; set; }
    public string PolicyVersion { get; set; } = "";
}
