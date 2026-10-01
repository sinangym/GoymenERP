namespace GoymenERP.Modules.Finance.Contracts;

public sealed record FinanceOfficialInvoiceImportPendingBatchPageV1(
    Guid CompanyId,
    IReadOnlyList<FinanceOfficialInvoiceImportBatchViewV1> Items,
    string? NextCursor);

public sealed record FinanceOfficialInvoiceImportPendingBatchDetailV1(
    FinanceOfficialInvoiceImportBatchViewV1 Batch,
    FinanceOfficialInvoiceImportPreviewV1 Preview);

/// <summary>
/// Company-scoped checker read side for valid, uploaded and still-pending official invoice batches.
/// It reuses the existing approve authority and never publishes pending rows through approved sources.
/// </summary>
public interface IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1
{
    Task<ReadResult<FinanceOfficialInvoiceImportPendingBatchPageV1>> ListAsync(
        FinanceContext context,
        string? cursor,
        CancellationToken cancellationToken);

    Task<ReadResult<FinanceOfficialInvoiceImportPendingBatchDetailV1>> GetAsync(
        FinanceContext context,
        Guid batchId,
        CancellationToken cancellationToken);
}
