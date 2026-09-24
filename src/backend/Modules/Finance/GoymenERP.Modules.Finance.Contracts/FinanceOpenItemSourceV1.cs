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
