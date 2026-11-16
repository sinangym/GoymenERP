namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// One immutable capacity version of a work center of a company (URT-D89; insert-only). The latest version of a work center is
/// its capacity in force; no row means no capacity (the due-date suggestion answers "hesaplanamadı"). The buffer pair is null
/// together when no buffer is recorded (counted as zero).
/// </summary>
internal sealed class WorkCenterCapacityVersionRow
{
    public Guid CompanyId { get; set; }
    public Guid WorkCenterId { get; set; }
    public long Version { get; set; }
    public Guid ProfileId { get; set; }
    public decimal InputCapacityKgPerHour { get; set; }
    public decimal? SafetyBufferValue { get; set; }
    public string? SafetyBufferUnit { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
