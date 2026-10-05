using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.WorkRuntime;

internal sealed class FinanceInvoiceImportPendingApprovalAdapterV1(
    IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1 owner)
    : INativeApprovalReadAdapterV1
{
    public async Task<ReadResult<NativeApprovalReadPageV1>> ListAsync(
        FinanceContext context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var result = await owner.ListAsync(context, cursor, cancellationToken);
        if (result.Outcome != ReadOutcome.Found)
        {
            return StrictFailure<NativeApprovalReadPageV1>(result.Outcome, result.Resource);
        }
        if (!TryMapPage(context, result.Resource, out var page))
        {
            return new(ReadOutcome.Unavailable, null);
        }
        return new(ReadOutcome.Found, page);
    }

    public async Task<ReadResult<NativeApprovalReadDetailV1>> GetAsync(
        FinanceContext context,
        string kind,
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(kind, NativeApprovalKindsV1.FinanceOfficialInvoiceImport, StringComparison.Ordinal)
            || approvalId == Guid.Empty)
        {
            return new(ReadOutcome.NotFound, null);
        }

        var result = await owner.GetAsync(context, approvalId, cancellationToken);
        if (result.Outcome != ReadOutcome.Found)
        {
            return StrictFailure<NativeApprovalReadDetailV1>(result.Outcome, result.Resource);
        }
        if (!TryMapDetail(context, approvalId, result.Resource, out var detail))
        {
            return new(ReadOutcome.Unavailable, null);
        }
        return new(ReadOutcome.Found, detail);
    }

    private static ReadResult<TTarget> StrictFailure<TTarget>(ReadOutcome outcome, object? resource)
        where TTarget : class =>
        resource is null && outcome is ReadOutcome.Unavailable or ReadOutcome.Denied or ReadOutcome.NotFound
            ? new(outcome, null)
            : new(ReadOutcome.Unavailable, null);

    private static bool TryMapPage(
        FinanceContext context,
        FinanceOfficialInvoiceImportPendingBatchPageV1? source,
        out NativeApprovalReadPageV1 page)
    {
        page = null!;
        if (source is null
            || source.CompanyId != context.CompanyId
            || source.Items is null
            || source.NextCursor is { Length: 0 })
        {
            return false;
        }

        var items = new NativeApprovalReadItemV1[source.Items.Count];
        for (var index = 0; index < source.Items.Count; index++)
        {
            if (!TryMapItem(context, source.Items[index], out items[index]))
            {
                return false;
            }
        }

        page = new(source.CompanyId, items, source.NextCursor);
        return true;
    }

    private static bool TryMapDetail(
        FinanceContext context,
        Guid approvalId,
        FinanceOfficialInvoiceImportPendingBatchDetailV1? source,
        out NativeApprovalReadDetailV1 detail)
    {
        detail = null!;
        if (source is null
            || source.Batch is null
            || source.Preview is null
            || source.Batch.BatchId != approvalId
            || !TryMapItem(context, source.Batch, out var item)
            || !source.Preview.IsValid
            || source.Preview.Batch is null
            || source.Preview.Batch.BatchId != source.Batch.BatchId
            || source.Preview.Batch.CompanyId != source.Batch.CompanyId
            || !string.Equals(source.Preview.Batch.BatchVersion, source.Batch.BatchVersion, StringComparison.Ordinal)
            || !string.Equals(source.Preview.Batch.BatchChecksum, source.Batch.BatchChecksum, StringComparison.Ordinal)
            || !string.Equals(source.Preview.SourceSha256, source.Batch.BatchChecksum, StringComparison.Ordinal)
            || !string.Equals(source.Preview.TemplateVersion, source.Batch.TemplateVersion, StringComparison.Ordinal)
            || source.Preview.Batch.Rows is null
            || source.Preview.Batch.Rows.Count != source.Batch.RowCount)
        {
            return false;
        }

        detail = new(item, source.Preview);
        return true;
    }

    private static bool TryMapItem(
        FinanceContext context,
        FinanceOfficialInvoiceImportBatchViewV1? source,
        out NativeApprovalReadItemV1 item)
    {
        item = null!;
        if (source is null
            || source.BatchId == Guid.Empty
            || source.CompanyId != context.CompanyId
            || source.Status != FinanceOfficialInvoiceImportApprovalStatusV1.Pending
            || string.IsNullOrWhiteSpace(source.Version)
            || string.IsNullOrWhiteSpace(source.BatchVersion)
            || string.IsNullOrWhiteSpace(source.BatchChecksum)
            || string.IsNullOrWhiteSpace(source.TemplateVersion)
            || source.RowCount < 0
            || string.IsNullOrWhiteSpace(source.UploadedBy)
            || string.Equals(source.UploadedBy, context.ActorId, StringComparison.Ordinal)
            || source.ApprovedBy is not null
            || source.ApprovedAtUtc is not null)
        {
            return false;
        }

        item = new(
            NativeApprovalKindsV1.FinanceOfficialInvoiceImport,
            source.BatchId,
            source.CompanyId,
            source.Status,
            source.Version,
            source.BatchVersion,
            source.BatchChecksum,
            source.TemplateVersion,
            source.RowCount,
            source.UploadedBy,
            source.UploadedAtUtc);
        return true;
    }
}
