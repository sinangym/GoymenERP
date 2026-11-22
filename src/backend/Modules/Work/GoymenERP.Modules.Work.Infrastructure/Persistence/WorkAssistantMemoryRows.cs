namespace GoymenERP.Modules.Work.Infrastructure.Persistence;

/// <summary>
/// WP-C6: one personal assistant memory entry. The owner revises it (version + 1) or deletes it; an overseer closes it.
/// Deleting and closing are state changes, never a physical delete; destroying the text of a deleted or expired entry is a
/// retention policy that is not decided yet (AI-D30, NEEDS-SINAN), so the row stays and is shown to nobody once deleted.
/// </summary>
internal sealed class WorkAssistantMemoryEntryRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid OwnerUserId { get; set; }
    public int Kind { get; set; }
    public string Body { get; set; } = "";
    public int State { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
    public Guid? EndedByUserId { get; set; }
    public string? CloseReason { get; set; }
    public string CorrelationId { get; set; } = "";
    public string? CausationId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>WP-C6: an overseer's closure (or reopening) of one person's whole memory. One row per person, versioned.</summary>
internal sealed class WorkAssistantMemoryUserClosureRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid SubjectUserId { get; set; }
    public bool IsClosed { get; set; }
    public long Version { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
    public Guid ChangedByUserId { get; set; }
    public string Reason { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>WP-C6 (AI-D30): one overseer view of a person's memory. Append-only evidence next to the audit event.</summary>
internal sealed class WorkAssistantMemoryOversightViewRow : IWorkTenantRow
{
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; }
    public Guid ViewerUserId { get; set; }
    public Guid SubjectUserId { get; set; }
    public DateTimeOffset ViewedAtUtc { get; set; }
    public int EntryCount { get; set; }
    public string CorrelationId { get; set; } = "";
}
