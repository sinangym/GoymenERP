namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal sealed class DocumentNumberFormatRow
{
    public Guid CompanyId { get; set; }
    public int Series { get; set; }
    public long Version { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public int SequenceDigits { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
}
internal sealed class DocumentNumberCounterRow
{
    public Guid CompanyId { get; set; }
    public int Series { get; set; }
    public int Year { get; set; }
    public long LastSequence { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
internal sealed class DocumentNumberReservationRow
{
    public Guid CompanyId { get; set; }
    public int Series { get; set; }
    public Guid DocumentId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public int Year { get; set; }
    public long Sequence { get; set; }
    public long FormatVersion { get; set; }
    public string Number { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string RecordedBy { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}
internal sealed class DocumentNumberCancellationRow
{
    public Guid CompanyId { get; set; }
    public int Series { get; set; }
    public Guid DocumentId { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string KeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
}
