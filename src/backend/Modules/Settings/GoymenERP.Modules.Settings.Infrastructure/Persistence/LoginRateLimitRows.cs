namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// One immutable version of the group-wide login attempt limit (KIM-D34; insert-only). The record is group-wide, so the row
/// carries no company in its key and no company query filter; <see cref="RecordedInCompanyId"/> only names the company the
/// person worked in (the audit event and the command execution are recorded there).
/// </summary>
internal sealed class LoginRateLimitVersionRow
{
    public long Version { get; set; }
    public Guid ProfileId { get; set; }
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }
    public string PartitionBy { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public Guid RecordedInCompanyId { get; set; }
    public string Reason { get; set; } = string.Empty;
}
