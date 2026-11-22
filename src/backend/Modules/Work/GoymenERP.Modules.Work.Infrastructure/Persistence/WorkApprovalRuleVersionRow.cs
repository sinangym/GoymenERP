namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

/// <summary>
/// WP1 (YOL-D17, YOL-D18): one recorded version of one approval kind's rule in the company's rule table — the owner's
/// approver permission it was recorded against, its duration class and its escalation permission — with who recorded it,
/// when and why. Append-only: a change is a new version; version 0 is the decided default and is never stored.
/// </summary>
internal sealed class WorkApprovalRuleVersionRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";
    public long Version { get; set; }
    public int DurationClass { get; set; }
    public string ApproverPermission { get; set; } = "";
    public string? EscalationPermission { get; set; }
    public Guid RecordedByUserId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string Reason { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string? CausationId { get; set; }
}
