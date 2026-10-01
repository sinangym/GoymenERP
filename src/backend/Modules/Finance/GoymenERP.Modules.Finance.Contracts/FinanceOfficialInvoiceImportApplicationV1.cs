namespace GoymenERP.Modules.Finance.Contracts;

public sealed record FinanceOfficialInvoiceOpenItemV1(
    Guid Id,
    Guid CompanyId,
    string SourceKind,
    Guid SourceDocumentId,
    Guid SourceInstallmentId,
    string SourceVersion,
    Guid SourceBatchId,
    string SourceBatchVersion,
    string SourceBatchChecksum,
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    string OriginalAmount,
    string OutstandingAmount,
    DateOnly? DueOn,
    int Status,
    string Version);

public sealed record FinanceOfficialInvoiceAppliedBatchV1(
    Guid BatchId,
    Guid CompanyId,
    string BatchVersion,
    string BatchChecksum,
    IReadOnlyList<FinanceOfficialInvoiceOpenItemV1> OpenItems);

public sealed record FinanceOfficialInvoiceImportApplyResultV1(
    FinanceOfficialInvoiceImportPreviewV1 Preview,
    ReadResult<ApprovedFinanceOpenItemSourceBatch>? ApprovedSource,
    FinanceOpenItemAdmissionResult? Admission,
    ReadResult<FinanceOfficialInvoiceAppliedBatchV1>? FreshRead)
{
    public bool IsSucceeded =>
        Preview.IsValid
        && ApprovedSource?.Outcome == ReadOutcome.Found
        && Admission?.Outcome == CommandOutcome.Succeeded
        && FreshRead?.Outcome == ReadOutcome.Found;
}

/// <summary>
/// Reads the Finance-owned result after an approved source admission. Implementations must enforce
/// current company scope and the existing open-item source authority both before and after the read.
/// </summary>
public interface IFinanceOfficialInvoiceImportReadPortV1
{
    Task<ReadResult<FinanceOfficialInvoiceAppliedBatchV1>> ReadAsync(
        FinanceContext context,
        Guid batchId,
        CancellationToken cancellationToken);
}

public interface IFinanceOfficialInvoiceImportApplicationServiceV1
{
    FinanceOfficialInvoiceImportPreviewV1 Preview(
        FinanceContext context,
        FinanceOfficialInvoiceImportPreviewRequestV1 request);

    Task<FinanceOfficialInvoiceImportApplyResultV1> ApplyAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOfficialInvoiceImportPreviewRequestV1 request,
        CancellationToken cancellationToken);

    Task<ReadResult<FinanceOfficialInvoiceAppliedBatchV1>> ReopenAsync(
        FinanceContext context,
        Guid batchId,
        CancellationToken cancellationToken);
}
