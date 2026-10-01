namespace GoymenERP.Modules.Finance.Contracts;

public static class FinanceOfficialInvoiceImportPermissionNamesV1
{
    public const string Upload = "finance.official-invoice-import.upload";
    public const string Approve = "finance.official-invoice-import.approve";
}

public enum FinanceOfficialInvoiceImportAuthorityActionV1 { Upload, Approve }
public enum FinanceOfficialInvoiceImportApprovalStatusV1 { Pending, Approved }

public sealed record FinanceOfficialInvoiceImportApproveRequestV1(Guid BatchId, string ExpectedVersion);

public sealed record FinanceOfficialInvoiceImportBatchViewV1(
    Guid BatchId,
    Guid CompanyId,
    string BatchVersion,
    string BatchChecksum,
    string TemplateVersion,
    FinanceOfficialInvoiceImportApprovalStatusV1 Status,
    string Version,
    int RowCount,
    string UploadedBy,
    DateTimeOffset UploadedAtUtc,
    string? ApprovedBy,
    DateTimeOffset? ApprovedAtUtc);

public sealed record FinanceOfficialInvoiceImportApprovalResultV1(
    CommandOutcome Outcome,
    FinanceOfficialInvoiceImportBatchViewV1? Resource,
    bool IsReplay,
    string OriginalCorrelationId,
    IReadOnlyList<ValidationIssue> Issues);

/// <summary>
/// Fresh host-owned upload/approval authority. The two technical names above are declarations only;
/// an unmapped central permission or a non-company-wide scope must answer Unavailable/Denied.
/// </summary>
public interface IFinanceOfficialInvoiceImportApprovalAuthorityV1
{
    Task<PermissionDecision> CheckAsync(
        FinanceContext context,
        FinanceOfficialInvoiceImportAuthorityActionV1 action,
        Guid batchId,
        CancellationToken cancellationToken);
}

public interface IFinanceOfficialInvoiceImportApprovalServiceV1
{
    Task<FinanceOfficialInvoiceImportApprovalResultV1> UploadAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOfficialInvoiceImportPreviewRequestV1 request,
        CancellationToken cancellationToken);

    Task<FinanceOfficialInvoiceImportApprovalResultV1> ApproveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOfficialInvoiceImportApproveRequestV1 request,
        CancellationToken cancellationToken);
}
