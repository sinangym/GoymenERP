namespace GoymenERP.Modules.Logistics.Domain.Transfers;

public enum TransferOutgoingDocumentStatus
{
    Draft = 1,
    Effective = 2,
    Cancelled = 3,
}

public sealed class TransferOutgoingDocumentRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// Logistics-owned outgoing document for one Inventory transfer. The document identity, transfer
/// identity and complete ordered line binding never change. Lifecycle changes only status/version.
/// No transition in this aggregate creates an Inventory movement.
/// </summary>
public sealed class TransferOutgoingDocument
{
    public const int MaximumLines = 200;
    private readonly Guid[] orderedTransferLineIds;

    private TransferOutgoingDocument(Guid companyId, Guid documentId, Guid transferId,
        IEnumerable<Guid> orderedTransferLineIds, string? eDispatchNumber, TransferOutgoingDocumentStatus status,
        int version, string createdBy, DateTimeOffset createdAtUtc, string? effectiveBy,
        DateTimeOffset? effectiveAtUtc, string? cancelledBy, DateTimeOffset? cancelledAtUtc,
        string? cancellationReason)
    {
        CompanyId = RequireId(companyId, nameof(companyId));
        DocumentId = RequireId(documentId, nameof(documentId));
        TransferId = RequireId(transferId, nameof(transferId));
        this.orderedTransferLineIds = RequireLines(orderedTransferLineIds);
        EDispatchNumber = NormalizeOptional(eDispatchNumber, 64, nameof(eDispatchNumber));
        Status = Enum.IsDefined(status) ? status : throw new ArgumentOutOfRangeException(nameof(status));
        Version = version >= 1 ? version : throw new ArgumentOutOfRangeException(nameof(version));
        CreatedBy = RequireText(createdBy, 128, nameof(createdBy));
        CreatedAtUtc = createdAtUtc;
        EffectiveBy = NormalizeOptional(effectiveBy, 128, nameof(effectiveBy));
        EffectiveAtUtc = effectiveAtUtc;
        CancelledBy = NormalizeOptional(cancelledBy, 128, nameof(cancelledBy));
        CancelledAtUtc = cancelledAtUtc;
        CancellationReason = NormalizeOptional(cancellationReason, 500, nameof(cancellationReason));
        RequireShape();
    }

    public Guid CompanyId { get; }
    public Guid DocumentId { get; }
    public Guid TransferId { get; }
    public IReadOnlyList<Guid> OrderedTransferLineIds => Array.AsReadOnly(orderedTransferLineIds);
    public string? EDispatchNumber { get; }
    public TransferOutgoingDocumentStatus Status { get; private set; }
    public int Version { get; private set; }
    public string CreatedBy { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public string? EffectiveBy { get; private set; }
    public DateTimeOffset? EffectiveAtUtc { get; private set; }
    public string? CancelledBy { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }

    public static TransferOutgoingDocument Create(Guid companyId, Guid documentId, Guid transferId,
        IEnumerable<Guid> orderedTransferLineIds, string? eDispatchNumber, string actorId, DateTimeOffset now) =>
        new(companyId, documentId, transferId, orderedTransferLineIds, eDispatchNumber,
            TransferOutgoingDocumentStatus.Draft, 1, actorId, now, null, null, null, null, null);

    public static TransferOutgoingDocument Restore(Guid companyId, Guid documentId, Guid transferId,
        IEnumerable<Guid> orderedTransferLineIds, string? eDispatchNumber, TransferOutgoingDocumentStatus status,
        int version, string createdBy, DateTimeOffset createdAtUtc, string? effectiveBy,
        DateTimeOffset? effectiveAtUtc, string? cancelledBy, DateTimeOffset? cancelledAtUtc,
        string? cancellationReason) =>
        new(companyId, documentId, transferId, orderedTransferLineIds, eDispatchNumber, status, version,
            createdBy, createdAtUtc, effectiveBy, effectiveAtUtc, cancelledBy, cancelledAtUtc,
            cancellationReason);

    public void Finalize(int expectedVersion, string actorId, DateTimeOffset now)
    {
        RequireExpectedVersion(expectedVersion);
        if (Status != TransferOutgoingDocumentStatus.Draft)
            throw Rule("logistics_transfer_outgoing_document_state", "Only a draft outgoing document can become effective.");
        EffectiveBy = RequireText(actorId, 128, nameof(actorId));
        EffectiveAtUtc = now;
        Status = TransferOutgoingDocumentStatus.Effective;
        Version++;
    }

    public void Cancel(int expectedVersion, string actorId, string reason, DateTimeOffset now)
    {
        RequireExpectedVersion(expectedVersion);
        if (Status == TransferOutgoingDocumentStatus.Cancelled)
            throw Rule("logistics_transfer_outgoing_document_state", "The outgoing document is already cancelled.");
        CancelledBy = RequireText(actorId, 128, nameof(actorId));
        CancellationReason = RequireText(reason, 500, nameof(reason));
        CancelledAtUtc = now;
        Status = TransferOutgoingDocumentStatus.Cancelled;
        Version++;
    }

    private void RequireExpectedVersion(int expectedVersion)
    {
        if (expectedVersion < 1 || expectedVersion != Version)
            throw Rule("logistics_transfer_outgoing_document_version_conflict", "The outgoing document version is stale.");
    }

    private void RequireShape()
    {
        var effective = Status == TransferOutgoingDocumentStatus.Effective
            || Status == TransferOutgoingDocumentStatus.Cancelled && Version == 3;
        if (effective != (EffectiveBy is not null && EffectiveAtUtc.HasValue))
            throw new ArgumentException("Effective lifecycle fields do not match document status.");
        var cancelled = Status == TransferOutgoingDocumentStatus.Cancelled;
        if (cancelled != (CancelledBy is not null && CancelledAtUtc.HasValue && CancellationReason is not null))
            throw new ArgumentException("Cancellation lifecycle fields do not match document status.");
        if (Status == TransferOutgoingDocumentStatus.Draft && Version != 1
            || Status == TransferOutgoingDocumentStatus.Effective && Version != 2
            || Status == TransferOutgoingDocumentStatus.Cancelled && Version is not (2 or 3))
            throw new ArgumentException("Document status and version are inconsistent.");
    }

    private static Guid[] RequireLines(IEnumerable<Guid> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var lines = values.ToArray();
        if (lines.Length is < 1 or > MaximumLines || lines.Any(id => id == Guid.Empty)
            || lines.Distinct().Count() != lines.Length)
            throw new ArgumentException("The complete ordered transfer-line set is required.", nameof(values));
        return lines;
    }

    private static Guid RequireId(Guid value, string name) => value != Guid.Empty
        ? value : throw new ArgumentException("A non-empty identity is required.", name);

    private static string RequireText(string value, int maximum, string name)
    {
        var normalized = value?.Trim();
        return !string.IsNullOrWhiteSpace(normalized) && normalized.Length <= maximum
            ? normalized : throw new ArgumentException("A bounded non-empty value is required.", name);
    }

    private static string? NormalizeOptional(string? value, int maximum, string name)
    {
        if (value is null) return null;
        var normalized = value.Trim();
        if (normalized.Length is < 1 || normalized.Length > maximum)
            throw new ArgumentException("A present optional value must be bounded and non-empty.", name);
        return normalized;
    }

    private static TransferOutgoingDocumentRuleException Rule(string code, string message) => new(code, message);
}
