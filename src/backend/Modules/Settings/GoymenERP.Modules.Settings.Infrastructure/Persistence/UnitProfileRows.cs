namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// One immutable unit profile version of a company for one unit (F-029 S1; insert-only). The unit's code, dimension and
/// kilogram flag are a snapshot of the Settings unit identity at the time of recording, so a stored row reads back exactly as
/// the port answers it. A baseline row (<c>CompiledBaselineV0</c>) carries the approved catalog entry's source text in
/// <see cref="DecisionReference"/>.
/// </summary>
internal sealed class UnitProfileVersionRow
{
    public Guid CompanyId { get; set; }
    public Guid UnitId { get; set; }
    public long Version { get; set; }
    public Guid ProfileId { get; set; }
    public string UnitCode { get; set; } = string.Empty;
    public string DimensionCode { get; set; } = string.Empty;
    public bool IsKilogram { get; set; }
    public int Scale { get; set; }
    public bool IsActive { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string SourceKind { get; set; } = string.Empty;
    public string? DecisionReference { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// The marker of a company's one-time unit profile baseline transfer (URN-D5). Its existence switches the company's unit
/// profile source from the approved catalog to the Settings table for good; the primary key on the company makes the transfer
/// happen once.
/// </summary>
internal sealed class UnitProfileBaselineTransferRow
{
    public Guid CompanyId { get; set; }
    public int EntryCount { get; set; }
    public DateTimeOffset TransferredAtUtc { get; set; }
    public string TransferredBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
