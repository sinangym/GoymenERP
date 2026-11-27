using GoymenERP.Modules.Finance.Contracts;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceOfficialInvoiceImportPendingApprovalReadServiceV1
{
    /// <summary>Canonical existing native preview, for matching two authorized reads; never a command fingerprint.</summary>
    public static string DecisionPreviewDigest(FinanceOfficialInvoiceImportPendingBatchDetailV1 detail) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(detail.Preview, Json))).ToLowerInvariant();

    public async Task<ReadResult<FinanceOfficialInvoiceImportPendingBatchDetailV1>> GetDecisionAsync(
        FinanceContext context, Guid batchId, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context); token.ThrowIfCancellationRequested();
        if (batchId == Guid.Empty) return new(ReadOutcome.NotFound, null);
        if (await FenceAsync<FinanceOfficialInvoiceImportPendingBatchDetailV1>(context, batchId, token) is { } denied) return denied;
        try
        {
            var batch = await dbContext.OfficialInvoiceImportBatches.AsNoTracking()
                .SingleOrDefaultAsync(row => row.CompanyId == context.CompanyId && row.Id == batchId, token);
            var approval = await dbContext.OfficialInvoiceImportApprovals.AsNoTracking()
                .SingleOrDefaultAsync(row => row.CompanyId == context.CompanyId && row.BatchId == batchId, token);
            if (await FenceAsync<FinanceOfficialInvoiceImportPendingBatchDetailV1>(context, batchId, token) is { } afterRead) return afterRead;
            if (batch is null) return new(ReadOutcome.NotFound, null);
            if (batch.UploadedBy == context.ActorId) return new(ReadOutcome.Denied, null);
            var parsed = Reparse(context, batch);
            if (parsed is null) return new(ReadOutcome.Unavailable, null);
            var view = View(batch) with { Status = approval is null ? FinanceOfficialInvoiceImportApprovalStatusV1.Pending
                : FinanceOfficialInvoiceImportApprovalStatusV1.Approved, Version = approval is null ? "1" : "2",
                ApprovedBy = approval?.ApprovedBy, ApprovedAtUtc = approval?.ApprovedAtUtc };
            var detail = new FinanceOfficialInvoiceImportPendingBatchDetailV1(view, parsed);
            // The append-only approval can appear during the separate reads. Never mix Pending1 with terminal facts.
            var finalApprovalId = await dbContext.OfficialInvoiceImportApprovals.AsNoTracking()
                .Where(row => row.CompanyId == context.CompanyId && row.BatchId == batchId)
                .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
            if (await FenceAsync<FinanceOfficialInvoiceImportPendingBatchDetailV1>(context, batchId, token) is { } finalFence) return finalFence;
            return finalApprovalId != approval?.Id || !ValidDecisionDetail(context, batchId, detail)
                ? new(ReadOutcome.Unavailable, null) : new(ReadOutcome.Found, detail);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        { return await FailureAfterReadAsync<FinanceOfficialInvoiceImportPendingBatchDetailV1>(context, batchId, token); }
    }

    /// <summary>Pure native policy reuse for the typed human boundary; neither mapping approval nor a second parser.</summary>
    public static bool ValidDecisionDetail(FinanceContext context, Guid id, FinanceOfficialInvoiceImportPendingBatchDetailV1? detail)
    {
        if (detail is not { Batch: { } b, Preview: { Batch: { Rows: { } rows } parsed, Issues: { Count: 0 } } p }
            || !ValidDecisionHeader(context.CompanyId, id, b) || rows.Count != b.RowCount
            || b.TemplateVersion != p.TemplateVersion || b.BatchChecksum != p.SourceSha256
            || b.TemplateVersion is not (FinanceOfficialInvoiceImportTemplateV1.Version or FinanceOfficialSalesInvoiceImportTemplateV2.Version or FinanceOfficialSalesInvoiceImportTemplateV3.Version)
            || FinanceOpenItemSourcePolicy.ValidateReference(new(id, b.BatchVersion, b.BatchChecksum)).Count != 0
            || rows.Any(row => row is null)
            || FinanceOpenItemSourcePolicy.ValidateBatch(context, new(id, b.BatchVersion, b.BatchChecksum), parsed).Count != 0)
            return false;
        return p.TaxWarnings is null || b.TemplateVersion != FinanceOfficialInvoiceImportTemplateV1.Version
            && p.TaxWarnings.All(warning => warning is not null && warning.Code == FinanceOfficialInvoiceTaxWarningV1.InconsistentCode
                && warning.SourceDocumentId is { } source && rows.Any(row => row.SourceDocumentId == source)
                && SignedNativeAmount(warning.ExpectedPayableFunctionalAmount)
                && FinanceExactDecimalText.TryParseNonNegative(warning.PayableFunctionalAmount, out _)
                && FinanceExactDecimalText.TryParsePositive(warning.DifferenceFunctionalAmount, out _));

        // The existing tax calculator emits a minus only for expected gross less withholding; no recalculation here.
        static bool SignedNativeAmount(string? value) => value is not null
            && FinanceExactDecimalText.TryParseNonNegative(value.StartsWith('-') ? value[1..] : value, out _);
    }

    public static bool ValidDecisionHeader(Guid companyId, Guid id, FinanceOfficialInvoiceImportBatchViewV1? b)
    {
        if (b is null || b.BatchId != id || id == Guid.Empty || companyId == Guid.Empty || b.CompanyId != companyId
            || b.RowCount is <= 0 or > FinanceOfficialInvoiceImportTemplateV1.MaximumRows
            || string.IsNullOrWhiteSpace(b.UploadedBy) || b.UploadedBy.Length > 128 || b.UploadedBy != b.UploadedBy.Trim()
            || b.UploadedAtUtc == default || b.UploadedAtUtc.Offset != TimeSpan.Zero
            || b.TemplateVersion is not (FinanceOfficialInvoiceImportTemplateV1.Version or FinanceOfficialSalesInvoiceImportTemplateV2.Version or FinanceOfficialSalesInvoiceImportTemplateV3.Version)
            || FinanceOpenItemSourcePolicy.ValidateReference(new(id, b.BatchVersion, b.BatchChecksum)).Count != 0) return false;
        return b.Status == FinanceOfficialInvoiceImportApprovalStatusV1.Pending
            ? b.Version == "1" && b.ApprovedBy is null && b.ApprovedAtUtc is null
            : b.Status == FinanceOfficialInvoiceImportApprovalStatusV1.Approved && b.Version == "2"
                && !string.IsNullOrWhiteSpace(b.ApprovedBy) && b.ApprovedBy.Length <= 128 && b.ApprovedBy == b.ApprovedBy.Trim()
                && b.ApprovedBy != b.UploadedBy && b.ApprovedAtUtc is { } at && at.Offset == TimeSpan.Zero && at >= b.UploadedAtUtc;
    }
}
