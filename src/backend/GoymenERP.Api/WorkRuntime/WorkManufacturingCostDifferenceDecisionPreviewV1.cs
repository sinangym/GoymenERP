using System.Globalization;
using GoymenERP.Modules.Accounting.Application;

namespace GoymenERP.Api.WorkRuntime;

// Human-only lossless transport. Native command/result JSON, hashes and receipt remain unchanged.
public sealed record WorkManufacturingCostDifferencePreviewV1(WorkManufacturingCostDifferenceViewV1 Proposal,
    DateTimeOffset PreparedAtUtc, string SourceFactsDigest, string PreviewDigest)
{
    internal static WorkManufacturingCostDifferencePreviewV1 FromNative(ManufacturingCostDifferenceDecisionDetailV1 detail) =>
        new(WorkManufacturingCostDifferenceViewV1.FromNative(detail.Proposal), detail.PreparedAtUtc, detail.SourceFactsDigest, detail.PreviewDigest);
}

public sealed record WorkManufacturingCostDifferenceViewV1(Guid ProposalId, Guid CompanyId, Guid ManufacturingOrderId,
    Guid GenerationId, string GenerationVersion, string Status, string Version, string SignedTlDifference,
    IReadOnlyList<ManufacturingCostDifferenceOutputViewV1> Outputs, DateTimeOffset SourceOccurredAtUtc,
    Guid? PeriodId, DateOnly? AccountingDate, DateOnly? TransactionDate, Guid? DebitAccountId, Guid? CreditAccountId,
    Guid? PreparedBy, Guid? ApprovedBy, Guid? JournalId, string? Reason, WorkManufacturingCostDifferenceAccountPlanV1? AccountPlan)
{
    internal static WorkManufacturingCostDifferenceViewV1 FromNative(ManufacturingCostDifferenceViewV1 view) =>
        new(view.ProposalId, view.CompanyId, view.ManufacturingOrderId, view.GenerationId, view.GenerationVersion,
            view.Status, view.Version.ToString(CultureInfo.InvariantCulture), view.SignedTlDifference, view.Outputs,
            view.SourceOccurredAtUtc, view.PeriodId, view.AccountingDate, view.TransactionDate, view.DebitAccountId, view.CreditAccountId,
            view.PreparedBy, view.ApprovedBy, view.JournalId, view.Reason,
            view.AccountPlan is { } plan ? WorkManufacturingCostDifferenceAccountPlanV1.FromNative(plan) : null);
}

public sealed record WorkManufacturingCostDifferenceAccountPlanV1(Guid PackageId, string PackageVersion, string? CatalogVersion,
    string SourceVersion, string Sha256, ManufacturingCostDifferenceAccountV1 Debit, ManufacturingCostDifferenceAccountV1 Credit)
{
    internal static WorkManufacturingCostDifferenceAccountPlanV1 FromNative(ManufacturingCostDifferenceAccountPlanV1 plan) =>
        new(plan.PackageId, plan.PackageVersion.ToString(CultureInfo.InvariantCulture), plan.CatalogVersion?.ToString(CultureInfo.InvariantCulture),
            plan.SourceVersion, plan.Sha256, plan.Debit, plan.Credit);
}
