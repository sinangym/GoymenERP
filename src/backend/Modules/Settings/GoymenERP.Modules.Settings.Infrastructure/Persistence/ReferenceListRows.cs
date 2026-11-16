namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// One entry of a company's reference list (API-98/B-14; insert-only). The kind and the code are fixed when the entry is
/// created and never change; the name and the active state live in <see cref="ReferenceListEntryVersionRow"/>.
/// </summary>
internal sealed class ReferenceListEntryRow
{
    public Guid CompanyId { get; set; }
    public Guid EntryId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// One immutable version of a reference list entry (insert-only). The latest version is the entry's name and state in force.
/// </summary>
internal sealed class ReferenceListEntryVersionRow
{
    public Guid CompanyId { get; set; }
    public Guid EntryId { get; set; }
    public long Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>An entry with its latest version, as the reads return it.</summary>
internal sealed record ReferenceListEntrySnapshot(ReferenceListEntryRow Entry, ReferenceListEntryVersionRow? Latest);
