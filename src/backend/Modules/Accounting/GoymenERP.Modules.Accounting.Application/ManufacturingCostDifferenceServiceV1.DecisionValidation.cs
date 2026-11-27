using System.Text.Json;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class ManufacturingCostDifferenceServiceV1
{
    // Exactly the existing source preview envelope; no alternative hash or source-facts reconstruction.
    public static string CostDifferencePreviewDigest(ManufacturingCostDifferenceViewV1 proposal, DateTimeOffset? preparedAtUtc) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(new { Proposal = proposal, PreparedAtUtc = preparedAtUtc }, Json));

    public static bool ValidCostDifferenceDecisionDetail(ManufacturingCostDifferenceDecisionDetailV1? detail)
    {
        if (detail?.Proposal is not { } p || p.CompanyId == Guid.Empty || p.ProposalId == Guid.Empty || p.ManufacturingOrderId == Guid.Empty
            || p.GenerationId == Guid.Empty || p.Version < 2 || string.IsNullOrWhiteSpace(p.GenerationVersion)
            || p.Outputs is not { Count: > 0 } || p.Outputs.Any(x => x is null || x.OutputId == Guid.Empty || x.ReceiptMovementId == Guid.Empty || x.LabelId == Guid.Empty)
            || p.PreparedBy is null || p.PreparedBy == Guid.Empty || detail.PreparedAtUtc.Offset != TimeSpan.Zero
            || p.PeriodId is null || p.PeriodId == Guid.Empty || p.DebitAccountId is null || p.DebitAccountId == Guid.Empty
            || p.CreditAccountId is null || p.CreditAccountId == Guid.Empty || p.DebitAccountId == p.CreditAccountId
            || p.AccountingDate is null || p.AccountingDate == default(DateOnly) || p.TransactionDate is null || p.TransactionDate == default(DateOnly)
            || string.IsNullOrWhiteSpace(p.Reason) || p.Reason != p.Reason.Trim() || p.Reason.Length > 1000 || p.SourceOccurredAtUtc.Offset != TimeSpan.Zero
            || p.AccountPlan is not { Debit: { } debit, Credit: { } credit } plan || plan.PackageId == Guid.Empty || plan.PackageVersion < 1
            || debit.AccountId != p.DebitAccountId || credit.AccountId != p.CreditAccountId
            || !CostDecisionDigest(detail.SourceFactsDigest) || detail.PreviewDigest != CostDifferencePreviewDigest(p, detail.PreparedAtUtc)) return false;
        // The private raw source/currency facts are established by the owner port, never invented from this human view.
        return p.Status == "Prepared" && p.ApprovedBy is null && p.JournalId is null
            || p.Status == "Posted" && p.ApprovedBy is { } actor && actor != Guid.Empty && actor != p.PreparedBy && p.JournalId is { } journal && journal != Guid.Empty;
    }
    private static bool CostDecisionDigest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
}
