using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class FinanceOfficialSalesInvoiceServiceV1 : IFinanceOfficialSalesInvoiceDecisionReadV1
{
    public static bool MatchesDecisionRule(FinanceOfficialSalesInvoiceViewV1 view, IReadOnlyList<WorkApprovalRuleResultV1> rules) =>
        rules is { Count: 1 } && FinanceWorkInboxRead.Amount(view.PayableAmount) is { } amount
        && rules[0] == new WorkApprovalRuleResultV1(FinanceOfficialSalesInvoiceWorkInboxSourceV1.ApprovalRequiredRule, null, amount, null, null);

    public static string ApprovePayloadHash(FinanceOfficialSalesInvoiceApproveRequestV1 request) => Hash(JsonSerializer.Serialize(request, Json));
    public static string RejectPayloadHash(FinanceOfficialSalesInvoiceRejectRequestV1 request) => Hash(JsonSerializer.Serialize(request, Json));
    public static string DecisionPreviewDigest(FinanceOfficialSalesInvoiceViewV1 view) => Hash(JsonSerializer.Serialize(view, Json));
    public static string RecordedFactsDigest(FinanceOfficialSalesInvoiceViewV1 view) => Hash(JsonSerializer.Serialize(new
    {
        view.CompanyId, Record = RecordedRequest(view), view.PdfDocumentVersionNo, view.PdfSha256,
        view.RecordedBy, view.RecordedAtUtc,
    }, Json));
    private static FinanceOfficialSalesInvoiceRecordRequestV1 RecordedRequest(FinanceOfficialSalesInvoiceViewV1 view) => new(
        view.InvoiceId, view.OfficialNo, view.Ettn, view.CustomerPartyId, view.CurrencyId, view.PayableAmount,
        view.PayableFunctionalAmount, view.TaxBaseFunctionalAmount, view.VatFunctionalAmount,
        view.WithholdingFunctionalAmount, view.PdfDocumentId, view.InvoiceDate);

    public static bool ValidDecisionView(Guid companyId, Guid invoiceId, FinanceOfficialSalesInvoiceViewV1? view)
    {
        if (view is null || companyId == Guid.Empty || invoiceId == Guid.Empty || view.CompanyId != companyId || view.InvoiceId != invoiceId
            || Validate(RecordedRequest(view), out var ettn, out _).Count != 0 || ettn != view.Ettn
            || view.PdfDocumentVersionNo < 1 || view.PdfSha256 is not { Length: 64 } || !IsLowerHex(view.PdfSha256)
            || !ValidIdentity(view.RecordedBy) || view.RecordedAtUtc.Offset != TimeSpan.Zero) return false;
        var pending = view.Status == FinanceOfficialSalesInvoiceStatusV1.PendingApproval;
        if (view.Version != VersionText(pending ? PendingVersion : DecidedVersion)) return false;
        return view.Status switch
        {
            FinanceOfficialSalesInvoiceStatusV1.PendingApproval => view.ApprovedBy is null && view.ApprovedAtUtc is null
                && view.RejectedBy is null && view.RejectedAtUtc is null && view.RejectionReason is null && view.WithdrawnBy is null && view.WithdrawnAtUtc is null,
            FinanceOfficialSalesInvoiceStatusV1.Approved => ValidIdentity(view.ApprovedBy) && view.ApprovedBy != view.RecordedBy
                && view.ApprovedAtUtc is { Offset: var approvedOffset } && approvedOffset == TimeSpan.Zero
                && view.RejectedBy is null && view.RejectedAtUtc is null && view.RejectionReason is null && view.WithdrawnBy is null && view.WithdrawnAtUtc is null,
            FinanceOfficialSalesInvoiceStatusV1.Rejected => ValidIdentity(view.RejectedBy) && view.RejectedBy != view.RecordedBy
                && view.RejectedAtUtc is { Offset: var rejectedOffset } && rejectedOffset == TimeSpan.Zero
                && !string.IsNullOrWhiteSpace(view.RejectionReason) && view.RejectionReason == view.RejectionReason.Trim() && view.RejectionReason.Length <= RejectionReasonMaximumLength
                && view.ApprovedBy is null && view.ApprovedAtUtc is null && view.WithdrawnBy is null && view.WithdrawnAtUtc is null,
            FinanceOfficialSalesInvoiceStatusV1.Withdrawn => view.WithdrawnBy == view.RecordedBy
                && view.WithdrawnAtUtc is { Offset: var withdrawnOffset } && withdrawnOffset == TimeSpan.Zero
                && view.ApprovedBy is null && view.ApprovedAtUtc is null && view.RejectedBy is null && view.RejectedAtUtc is null && view.RejectionReason is null,
            _ => false,
        };
    }

    public async Task<ReadResult<FinanceOfficialSalesInvoiceViewV1>> ReadDecisionAsync(FinanceContext context,
        Guid invoiceId, Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(freshConsumerFence);
        cancellationToken.ThrowIfCancellationRequested();
        if (await Fence() is { } before) return before;
        if (invoiceId == Guid.Empty) return new(ReadOutcome.NotFound, null);
        // Reuse the actual owner projection, source-difference facts and tax warning; no second calculator.
        var answer = await GetAsync(context, invoiceId, cancellationToken);
        if (await Fence() is { } after) return after;
        if (answer.Outcome != ReadOutcome.Found || answer.Resource is not { } view) return answer;
        if (view.InvoiceId != invoiceId || view.CompanyId != context.CompanyId
            || string.IsNullOrWhiteSpace(view.RecordedBy)) return new(ReadOutcome.Unavailable, null);
        return view.RecordedBy == context.ActorId ? new(ReadOutcome.Denied, null) : answer;

        async Task<ReadResult<FinanceOfficialSalesInvoiceViewV1>?> Fence()
        {
            await freshConsumerFence(cancellationToken);
            if (!SameContext(context)) return new(ReadOutcome.Denied, null);
            var refusal = await FenceAsync(context, FinanceOfficialSalesInvoiceAuthorityActionV1.Approve, invoiceId, cancellationToken);
            await freshConsumerFence(cancellationToken);
            if (!SameContext(context)) return new(ReadOutcome.Denied, null);
            return refusal is null ? null : new(refusal.Outcome == CommandOutcome.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }
    }
}
