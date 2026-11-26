namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

internal sealed class WorkDelegationRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";
    public string ApproverPermission { get; set; } = "";
    public Guid DelegatorUserId { get; set; }
    public Guid DelegateUserId { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public DateTimeOffset EndsAtUtc { get; set; }
    public long Version { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public Guid RecordedByUserId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string Reason { get; set; } = "";
    public Guid? RevokedByUserId { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string? RevocationReason { get; set; }
}
