namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

/// <summary>W1-A inbox measurement event (append-only, no actor column).</summary>
internal sealed class WorkApprovalInboxEventRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";
    public Guid ItemId { get; set; }
    public DateTimeOffset CycleStartedAtUtc { get; set; }
    public int EventType { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public Guid? ClientEventId { get; set; }
}
