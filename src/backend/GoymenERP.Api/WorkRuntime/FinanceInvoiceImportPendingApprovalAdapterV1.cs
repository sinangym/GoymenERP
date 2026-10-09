using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Api.WorkRuntime;

internal sealed class FinanceInvoiceImportPendingApprovalAdapterV1(
    IFinanceOfficialInvoiceImportPendingApprovalReadServiceV1 owner)
    : INativeApprovalReadAdapterV1
{
    public string Kind => NativeApprovalKindsV1.FinanceOfficialInvoiceImport;

    public async Task<NativeApprovalReadResultV1<NativeApprovalReadPageV1>> ListAsync(
        NativeApprovalReadContextV1 context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var result = await owner.ListAsync(Finance(context), cursor, cancellationToken);
        if (result.Outcome != ReadOutcome.Found)
        {
            return StrictFailure<NativeApprovalReadPageV1>(result.Outcome, result.Resource);
        }
        if (!TryMapPage(context, result.Resource, out var page))
        {
            return new(NativeApprovalReadOutcomeV1.Unavailable, null);
        }
        return new(NativeApprovalReadOutcomeV1.Found, page);
    }

    public async Task<NativeApprovalReadResultV1<NativeApprovalReadDetailV1>> GetAsync(
        NativeApprovalReadContextV1 context,
        Guid approvalId,
        CancellationToken cancellationToken)
    {
        if (approvalId == Guid.Empty)
        {
            return new(NativeApprovalReadOutcomeV1.NotFound, null);
        }

        var result = await owner.GetAsync(Finance(context), approvalId, cancellationToken);
        if (result.Outcome != ReadOutcome.Found)
        {
            return StrictFailure<NativeApprovalReadDetailV1>(result.Outcome, result.Resource);
        }
        if (!TryMapDetail(context, approvalId, result.Resource, out var detail))
        {
            return new(NativeApprovalReadOutcomeV1.Unavailable, null);
        }
        return new(NativeApprovalReadOutcomeV1.Found, detail);
    }

    private static NativeApprovalReadResultV1<TTarget> StrictFailure<TTarget>(
        ReadOutcome outcome,
        object? resource)
        where TTarget : class =>
        resource is null && outcome is ReadOutcome.Unavailable or ReadOutcome.Denied or ReadOutcome.NotFound
            ? new(Map(outcome), null)
            : new(NativeApprovalReadOutcomeV1.Unavailable, null);

    private static bool TryMapPage(
        NativeApprovalReadContextV1 context,
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
            if (!TryMapItem(context, source.Items[index], out var item))
            {
                return false;
            }
            items[index] = item;
        }

        page = new(source.CompanyId, items, source.NextCursor);
        return true;
    }

    private static bool TryMapDetail(
        NativeApprovalReadContextV1 context,
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

        detail = new FinanceInvoiceImportNativeApprovalReadDetailV1(item, source.Preview);
        return true;
    }

    private static bool TryMapItem(
        NativeApprovalReadContextV1 context,
        FinanceOfficialInvoiceImportBatchViewV1? source,
        out FinanceInvoiceImportNativeApprovalReadItemV1 item)
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

        item = new FinanceInvoiceImportNativeApprovalReadItemV1(
            source.BatchId,
            source.CompanyId,
            source.Status.ToString(),
            source.Version,
            source.BatchVersion,
            source.BatchChecksum,
            source.TemplateVersion,
            source.RowCount,
            source.UploadedBy,
            source.UploadedAtUtc);
        return true;
    }

    private static FinanceContext Finance(NativeApprovalReadContextV1 context) => new(
        context.CompanyId,
        context.ActorId,
        context.CorrelationId,
        context.CausationId);

    private static NativeApprovalReadOutcomeV1 Map(ReadOutcome outcome) => outcome switch
    {
        ReadOutcome.Found => NativeApprovalReadOutcomeV1.Found,
        ReadOutcome.NotFound => NativeApprovalReadOutcomeV1.NotFound,
        ReadOutcome.Denied => NativeApprovalReadOutcomeV1.Denied,
        ReadOutcome.Unavailable => NativeApprovalReadOutcomeV1.Unavailable,
        _ => NativeApprovalReadOutcomeV1.Unavailable,
    };
}
