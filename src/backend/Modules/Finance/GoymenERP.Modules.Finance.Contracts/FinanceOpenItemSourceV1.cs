namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>
/// Canonical owner identity for approved external/official invoice-installment rows. A future
/// native invoice producer must reuse this identity instead of minting a second open item.
/// The carrier (CSV/Excel/native) is deliberately not part of the identity.
/// </summary>
public static class FinanceOpenItemSourceKinds
{
    public const string ApprovedOfficialInvoiceInstallment = "official.invoice-installment.v1";
    public const string OfficialInvoiceInstallmentDocument = "official-invoice-installment";
}

public sealed record FinanceOpenItemSourceReference(
    Guid BatchId,
    string BatchVersion,
    string BatchChecksum);

public sealed record ApprovedFinanceOpenItemSourceRow(
    Guid OpenItemId,
    Guid SourceDocumentId,
    Guid SourceInstallmentId,
    string SourceVersion,
    string DocumentKind,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    string Amount,
    DateOnly? DueOn);

/// <summary>
/// Canonical output of the still-external approved-file adapter. This contract does not define a
/// file layout, parse a file, approve a mapping or grant authority.
/// </summary>
public sealed record ApprovedFinanceOpenItemSourceBatch(
    Guid BatchId,
    Guid CompanyId,
    string BatchVersion,
    string BatchChecksum,
    IReadOnlyList<ApprovedFinanceOpenItemSourceRow> Rows);

public interface IApprovedFinanceOpenItemSourcePort
{
    Task<ReadResult<ApprovedFinanceOpenItemSourceBatch>> GetApprovedAsync(
        FinanceContext context,
        FinanceOpenItemSourceReference source,
        CancellationToken cancellationToken);
}

/// <summary>
/// One company-scoped page of approved canonical batches that an authenticated user may select.
/// The page is produced by the trusted official-file adapter; it is not an upload contract and
/// does not let a browser assert that a file, mapping or invoice is approved.
/// </summary>
public sealed record ApprovedFinanceOpenItemSourcePage(
    Guid CompanyId,
    IReadOnlyList<ApprovedFinanceOpenItemSourceBatch> Items,
    string? NextCursor);

/// <summary>
/// Read side of the approved-file adapter. Implementations must return only batches whose actual
/// carrier checksum and mapping approval were verified outside Finance. Finance validates every
/// returned canonical batch again before exposing it and re-reads the selected batch on admission.
/// </summary>
public interface IApprovedFinanceOpenItemSourceCatalogPort
{
    Task<ReadResult<ApprovedFinanceOpenItemSourcePage>> ListApprovedAsync(
        FinanceContext context,
        string? cursor,
        CancellationToken cancellationToken);
}

/// <summary>
/// Host-owned fresh authority measurement. No permission string or grant is declared here; an
/// unmapped production composition must remain unavailable.
/// </summary>
public interface IFinanceOpenItemSourceAuthority
{
    Task<PermissionDecision> CheckAsync(
        FinanceContext context,
        Guid batchId,
        CancellationToken cancellationToken);
}

public sealed record FinanceOpenItemAdmissionResult(
    CommandOutcome Outcome,
    IReadOnlyList<Guid> OpenItemIds,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

public interface IFinanceOpenItemSourceAdmissionService
{
    Task<FinanceOpenItemAdmissionResult> AdmitAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOpenItemSourceReference source,
        CancellationToken cancellationToken);
}

public interface IFinanceOpenItemSourceSelectionService
{
    Task<ReadResult<ApprovedFinanceOpenItemSourcePage>> ListApprovedAsync(
        FinanceContext context,
        string? cursor,
        CancellationToken cancellationToken);
}
