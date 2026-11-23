using GoymenERP.Modules.Sales.Domain.SignedApprovals;

namespace GoymenERP.Modules.Sales.Infrastructure.Persistence;

/// <summary>
/// WP13 (YOL-D11): one signed approval record of a sales order (<c>sales.OrderSignedApprovals</c>). It points at the vault document
/// version (id, version no, SHA-256) and the order revision it covers. The facts never change; the only change is Active (1) →
/// Passive (2) once, with the version moving 1 → 2 (TR_OrderSignedApprovals_Guard). No row is deleted. At most one active record per
/// order (UX_OrderSignedApprovals_Active).
/// </summary>
internal sealed class OrderSignedApprovalRow : ISalesTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OrderId { get; set; }
    public int OrderRevision { get; set; }
    public long OrderVersion { get; set; }
    public Guid DocumentId { get; set; }
    public int DocumentVersionNo { get; set; }
    public string DocumentSha256 { get; set; } = string.Empty;
    public int State { get; set; }
    public long Version { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }

    /// <summary>1 Withdrawn (with a reason), 2 Superseded (names the successor); null while active (CK_OrderSignedApprovals_State).</summary>
    public int? DeactivationKind { get; set; }
    public string? DeactivatedBy { get; set; }
    public DateTimeOffset? DeactivatedAtUtc { get; set; }
    public string? DeactivationReason { get; set; }
    public Guid? SupersededById { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>WP13 (ADR-011 §2–§8): the signed approval commands' own attempt records (append-only).</summary>
internal sealed class OrderSignedApprovalAttemptRow : ISalesTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;

    /// <summary>1 = recorded (the order's records answer the replay); 2 = a recorded terminal refusal (<see cref="RefusalJson"/>).</summary>
    public int Outcome { get; set; }
    public Guid OrderId { get; set; }
    public string? RefusalJson { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

internal static class SalesSignedApprovalRows
{
    public static OrderSignedApprovalRow ToRow(SalesOrderSignedApproval item, string correlationId) => new()
    {
        Id = item.Id,
        CompanyId = item.CompanyId,
        OrderId = item.OrderId,
        OrderRevision = item.OrderRevision,
        OrderVersion = item.OrderVersion,
        DocumentId = item.Document.DocumentId,
        DocumentVersionNo = item.Document.VersionNo,
        DocumentSha256 = item.Document.Sha256,
        State = (int)item.State,
        Version = item.Version,
        RecordedBy = item.RecordedBy,
        RecordedAtUtc = item.RecordedAtUtc,
        CorrelationId = correlationId.Trim(),
    };

    /// <summary>Moves the row to the record's passive state (the only change a row takes).</summary>
    public static void ApplyDeactivation(OrderSignedApprovalRow row, SalesOrderSignedApproval item)
    {
        var deactivation = item.Deactivation ?? throw new InvalidOperationException("Pasifleşmemiş imzalı onay satıra uygulanmaz.");
        row.State = (int)item.State;
        row.Version = item.Version;
        row.DeactivationKind = (int)deactivation.Kind;
        row.DeactivatedBy = deactivation.DeactivatedBy;
        row.DeactivatedAtUtc = deactivation.DeactivatedAtUtc;
        row.DeactivationReason = deactivation.Reason;
        row.SupersededById = deactivation.SupersededById;
    }

    public static SalesOrderSignedApproval Restore(OrderSignedApprovalRow row) => SalesOrderSignedApproval.Restore(
        row.Id, row.CompanyId, row.OrderId, row.OrderRevision, row.OrderVersion,
        new SalesOrderSignedApprovalDocument(row.DocumentId, row.DocumentVersionNo, row.DocumentSha256),
        (SalesOrderSignedApprovalState)row.State, row.Version, row.RecordedBy, row.RecordedAtUtc,
        row.DeactivationKind is { } kind
            ? new SalesOrderSignedApprovalDeactivation((SalesOrderSignedApprovalDeactivationKind)kind,
                row.DeactivatedBy ?? throw new InvalidOperationException("Pasif imzalı onayın aktörü yok."),
                row.DeactivatedAtUtc ?? throw new InvalidOperationException("Pasif imzalı onayın zamanı yok."),
                row.DeactivationReason, row.SupersededById)
            : null);
}
