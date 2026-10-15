namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// Closed, fail-closed outcomes returned by the two typed transfer-document proof ports. Only
/// <see cref="CurrentEffective"/> carries a proof binding. Callers must treat every other outcome
/// as a refusal and must not infer whether a document belongs to another company or transfer.
/// </summary>
public enum InventoryTransferDocumentProofOutcomeV1
{
    CurrentEffective = 1,
    Missing = 2,
    Stale = 3,
    Draft = 4,
    Cancelled = 5,
    Reversed = 6,
    BindingMismatch = 7,
    Unavailable = 8,
}

/// <summary>
/// The exact Logistics-owned outgoing-document binding that Inventory needs before dispatch.
/// The ordered line identities are the complete transfer-line set, not a caller-selected subset.
/// </summary>
public sealed class InventoryTransferOutgoingDocumentProofQueryV1
{
    public InventoryTransferOutgoingDocumentProofQueryV1(
        Guid companyId,
        Guid transferId,
        Guid documentId,
        int expectedDocumentVersion,
        IEnumerable<Guid> orderedTransferLineIds)
    {
        CompanyId = RequireId(companyId, nameof(companyId));
        TransferId = RequireId(transferId, nameof(transferId));
        DocumentId = RequireId(documentId, nameof(documentId));
        ExpectedDocumentVersion = RequireVersion(expectedDocumentVersion);
        OrderedTransferLineIds = RequireCompleteOrderedLines(orderedTransferLineIds);
    }

    public Guid CompanyId { get; }
    public Guid TransferId { get; }
    public Guid DocumentId { get; }
    public int ExpectedDocumentVersion { get; }
    public IReadOnlyList<Guid> OrderedTransferLineIds { get; }

    private static Guid RequireId(Guid value, string parameterName) =>
        value != Guid.Empty ? value : throw new ArgumentException("A transfer proof identity is required.", parameterName);

    private static int RequireVersion(int value) =>
        value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(value), "A positive document version is required.");

    private static IReadOnlyList<Guid> RequireCompleteOrderedLines(IEnumerable<Guid> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var lines = values.ToArray();
        if (lines.Length is < 1 or > InventoryTransferApiV1.MaximumLines
            || lines.Any(lineId => lineId == Guid.Empty)
            || lines.Distinct().Count() != lines.Length)
        {
            throw new ArgumentException("The complete ordered transfer-line set must contain unique non-empty identities.",
                nameof(values));
        }

        return Array.AsReadOnly(lines);
    }
}

/// <summary>The exact Inventory-owned target-acceptance document binding for one transfer line.</summary>
public sealed class InventoryTransferTargetAcceptanceDocumentProofQueryV1
{
    public InventoryTransferTargetAcceptanceDocumentProofQueryV1(
        Guid companyId,
        Guid transferId,
        Guid transferLineId,
        Guid documentId,
        int expectedDocumentVersion)
    {
        CompanyId = RequireId(companyId, nameof(companyId));
        TransferId = RequireId(transferId, nameof(transferId));
        TransferLineId = RequireId(transferLineId, nameof(transferLineId));
        DocumentId = RequireId(documentId, nameof(documentId));
        ExpectedDocumentVersion = expectedDocumentVersion >= 1
            ? expectedDocumentVersion
            : throw new ArgumentOutOfRangeException(nameof(expectedDocumentVersion),
                "A positive document version is required.");
    }

    public Guid CompanyId { get; }
    public Guid TransferId { get; }
    public Guid TransferLineId { get; }
    public Guid DocumentId { get; }
    public int ExpectedDocumentVersion { get; }

    private static Guid RequireId(Guid value, string parameterName) =>
        value != Guid.Empty ? value : throw new ArgumentException("A transfer proof identity is required.", parameterName);
}

/// <summary>
/// Outgoing-document proof. A non-effective answer deliberately carries no binding, so stale,
/// foreign-company and otherwise mismatched documents cannot be reused as dispatch authority.
/// </summary>
public sealed class InventoryTransferOutgoingDocumentProofV1
{
    private InventoryTransferOutgoingDocumentProofV1(
        InventoryTransferDocumentProofOutcomeV1 outcome,
        InventoryTransferOutgoingDocumentProofQueryV1? effectiveBinding)
    {
        Outcome = outcome;
        EffectiveBinding = effectiveBinding;
    }

    public InventoryTransferDocumentProofOutcomeV1 Outcome { get; }
    public InventoryTransferOutgoingDocumentProofQueryV1? EffectiveBinding { get; }

    public static InventoryTransferOutgoingDocumentProofV1 CurrentEffective(
        InventoryTransferOutgoingDocumentProofQueryV1 binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new(InventoryTransferDocumentProofOutcomeV1.CurrentEffective, binding);
    }

    public static InventoryTransferOutgoingDocumentProofV1 Refused(InventoryTransferDocumentProofOutcomeV1 outcome) =>
        outcome == InventoryTransferDocumentProofOutcomeV1.CurrentEffective
            ? throw new ArgumentOutOfRangeException(nameof(outcome), "An effective result requires its exact binding.")
            : Enum.IsDefined(outcome)
                ? new(outcome, null)
                : throw new ArgumentOutOfRangeException(nameof(outcome));
}

/// <summary>Target-acceptance proof with the same fail-closed binding rule as the outgoing proof.</summary>
public sealed class InventoryTransferTargetAcceptanceDocumentProofV1
{
    private InventoryTransferTargetAcceptanceDocumentProofV1(
        InventoryTransferDocumentProofOutcomeV1 outcome,
        InventoryTransferTargetAcceptanceDocumentProofQueryV1? effectiveBinding)
    {
        Outcome = outcome;
        EffectiveBinding = effectiveBinding;
    }

    public InventoryTransferDocumentProofOutcomeV1 Outcome { get; }
    public InventoryTransferTargetAcceptanceDocumentProofQueryV1? EffectiveBinding { get; }

    public static InventoryTransferTargetAcceptanceDocumentProofV1 CurrentEffective(
        InventoryTransferTargetAcceptanceDocumentProofQueryV1 binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new(InventoryTransferDocumentProofOutcomeV1.CurrentEffective, binding);
    }

    public static InventoryTransferTargetAcceptanceDocumentProofV1 Refused(
        InventoryTransferDocumentProofOutcomeV1 outcome) =>
        outcome == InventoryTransferDocumentProofOutcomeV1.CurrentEffective
            ? throw new ArgumentOutOfRangeException(nameof(outcome), "An effective result requires its exact binding.")
            : Enum.IsDefined(outcome)
                ? new(outcome, null)
                : throw new ArgumentOutOfRangeException(nameof(outcome));
}

/// <summary>
/// Logistics-owned read-only proof port. Registration and a real producer are later integration work;
/// this contract creates no route, permission, stock movement, audit entry or outbox message.
/// </summary>
public interface IInventoryTransferOutgoingDocumentProofPortV1
{
    Task<InventoryTransferOutgoingDocumentProofV1> ReadAsync(
        InventoryTransferOutgoingDocumentProofQueryV1 query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Inventory target-acceptance read-only proof port. It is deliberately distinct from the outgoing
/// port, so no free-form document-kind value can select or impersonate an owner.
/// </summary>
public interface IInventoryTransferTargetAcceptanceDocumentProofPortV1
{
    Task<InventoryTransferTargetAcceptanceDocumentProofV1> ReadAsync(
        InventoryTransferTargetAcceptanceDocumentProofQueryV1 query,
        CancellationToken cancellationToken = default);
}
