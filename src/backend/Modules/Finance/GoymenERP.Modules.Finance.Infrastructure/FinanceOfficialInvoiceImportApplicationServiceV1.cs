using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// Connects the strict source-only preview to the existing atomic admission command, then reopens
/// the Finance-owned rows through an independently authority-fenced fresh read.
/// </summary>
public sealed class FinanceOfficialInvoiceImportApplicationServiceV1(
    IFinanceOfficialInvoiceImportParserV1 parser,
    IApprovedFinanceOpenItemSourcePort approvedSource,
    IFinanceOpenItemSourceAdmissionService admission,
    IFinanceOfficialInvoiceImportReadPortV1 readPort)
    : IFinanceOfficialInvoiceImportApplicationServiceV1
{
    public FinanceOfficialInvoiceImportPreviewV1 Preview(
        FinanceContext context,
        FinanceOfficialInvoiceImportPreviewRequestV1 request)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        return parser.Preview(context, request);
    }

    public async Task<FinanceOfficialInvoiceImportApplyResultV1> ApplyAsync(
        FinanceContext context,
        CommandMetadata metadata,
        FinanceOfficialInvoiceImportPreviewRequestV1 request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var preview = parser.Preview(context, request);
        if (!preview.IsValid || preview.Batch is not { } batch)
        {
            return new(preview, null, null, null);
        }

        var source = new FinanceOpenItemSourceReference(
            batch.BatchId,
            batch.BatchVersion,
            batch.BatchChecksum);
        var approved = await approvedSource.GetApprovedAsync(context, source, cancellationToken);
        if (approved.Outcome != ReadOutcome.Found
            || approved.Resource is null
            || !Matches(batch, approved.Resource))
        {
            var refused = approved.Outcome == ReadOutcome.Found
                ? new ReadResult<ApprovedFinanceOpenItemSourceBatch>(ReadOutcome.Unavailable, null)
                : approved;
            return new(preview, refused, null, null);
        }

        var admitted = await admission.AdmitAsync(context, metadata, source, cancellationToken);
        if (admitted.Outcome != CommandOutcome.Succeeded)
        {
            return new(preview, approved, admitted, null);
        }

        var fresh = await readPort.ReadAsync(context, batch.BatchId, cancellationToken);
        if (!Matches(preview.Batch, admitted.OpenItemIds, fresh))
        {
            fresh = new(ReadOutcome.Unavailable, null);
        }

        return new(preview, approved, admitted, fresh);
    }

    public Task<ReadResult<FinanceOfficialInvoiceAppliedBatchV1>> ReopenAsync(
        FinanceContext context,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        return readPort.ReadAsync(context, batchId, cancellationToken);
    }

    private static bool Matches(
        ApprovedFinanceOpenItemSourceBatch expected,
        IReadOnlyList<Guid> admittedIds,
        ReadResult<FinanceOfficialInvoiceAppliedBatchV1> fresh)
    {
        if (fresh.Outcome != ReadOutcome.Found || fresh.Resource is null)
        {
            return false;
        }

        var actual = fresh.Resource;
        if (actual.BatchId != expected.BatchId
            || actual.CompanyId != expected.CompanyId
            || !string.Equals(actual.BatchVersion, expected.BatchVersion, StringComparison.Ordinal)
            || !string.Equals(actual.BatchChecksum, expected.BatchChecksum, StringComparison.Ordinal)
            || actual.OpenItems.Count != expected.Rows.Count
            || admittedIds.Count != expected.Rows.Count)
        {
            return false;
        }

        var expectedIds = expected.Rows.Select(row => row.OpenItemId).Order().ToArray();
        var admitted = admittedIds.Order().ToArray();
        var actualIds = actual.OpenItems.Select(row => row.Id).Order().ToArray();
        if (!expectedIds.SequenceEqual(admitted) || !expectedIds.SequenceEqual(actualIds))
        {
            return false;
        }

        var actualById = actual.OpenItems.ToDictionary(row => row.Id);
        return expected.Rows.All(row => actualById.TryGetValue(row.OpenItemId, out var item)
            && item.CompanyId == expected.CompanyId
            && string.Equals(item.SourceKind,
                FinanceOpenItemSourceKinds.ApprovedOfficialInvoiceInstallment, StringComparison.Ordinal)
            && item.SourceDocumentId == row.SourceDocumentId
            && item.SourceInstallmentId == row.SourceInstallmentId
            && string.Equals(item.SourceVersion, row.SourceVersion, StringComparison.Ordinal)
            && item.SourceBatchId == expected.BatchId
            && string.Equals(item.SourceBatchVersion, expected.BatchVersion, StringComparison.Ordinal)
            && string.Equals(item.SourceBatchChecksum, expected.BatchChecksum, StringComparison.Ordinal)
            && item.PartyId == row.PartyId
            && item.CompanyAccountId == row.CompanyAccountId
            && item.CurrencyId == row.CurrencyId
            && string.Equals(item.OriginalAmount, row.Amount, StringComparison.Ordinal)
            && item.DueOn == row.DueOn);
    }

    private static bool Matches(
        ApprovedFinanceOpenItemSourceBatch expected,
        ApprovedFinanceOpenItemSourceBatch approved)
    {
        if (approved.BatchId != expected.BatchId
            || approved.CompanyId != expected.CompanyId
            || !string.Equals(approved.BatchVersion, expected.BatchVersion, StringComparison.Ordinal)
            || !string.Equals(approved.BatchChecksum, expected.BatchChecksum, StringComparison.Ordinal)
            || approved.Rows.Count != expected.Rows.Count)
        {
            return false;
        }

        var approvedById = new Dictionary<Guid, ApprovedFinanceOpenItemSourceRow>();
        foreach (var row in approved.Rows)
        {
            if (!approvedById.TryAdd(row.OpenItemId, row))
            {
                return false;
            }
        }

        return expected.Rows.All(row => approvedById.TryGetValue(row.OpenItemId, out var item)
            && item.SourceDocumentId == row.SourceDocumentId
            && item.SourceInstallmentId == row.SourceInstallmentId
            && string.Equals(item.SourceVersion, row.SourceVersion, StringComparison.Ordinal)
            && string.Equals(item.DocumentKind, row.DocumentKind, StringComparison.Ordinal)
            && item.PartyId == row.PartyId
            && item.CompanyAccountId == row.CompanyAccountId
            && item.CurrencyId == row.CurrencyId
            && string.Equals(item.Amount, row.Amount, StringComparison.Ordinal)
            && item.DueOn == row.DueOn);
    }
}
