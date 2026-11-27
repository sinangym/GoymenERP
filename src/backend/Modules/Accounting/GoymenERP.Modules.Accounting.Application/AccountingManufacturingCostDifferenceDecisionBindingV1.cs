using System.Globalization;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>The native inbox formatter, shared by list, purpose GET and original receipt POST matching.</summary>
public static class AccountingManufacturingCostDifferenceDecisionBindingV1
{
    public const string ModuleName = "accounting";
    public const string KindName = "accounting.manufacturing-cost-difference.v1";
    public const string ScreenPath = "/muhasebe/uretim-maliyet-farki-onay";
    public const string PurposePath = "/onaylarim/accounting.manufacturing-cost-difference.v1";
    public const string ApprovalRequiredRule = "accounting.manufacturing-cost-difference-approval-required";
    public static WorkInboxItemV1? Item(Guid id, Guid order, Guid period, Guid debit, Guid credit,
        Guid maker, DateTimeOffset preparedAt, decimal signedDifference)
    {
        if (id == Guid.Empty || order == Guid.Empty || period == Guid.Empty || debit == Guid.Empty || credit == Guid.Empty || maker == Guid.Empty) return null;
        return new(ModuleName, KindName, id, Caption(id), preparedAt.ToUniversalTime(), ScreenPath, maker.ToString("D", CultureInfo.InvariantCulture),
            [new(ApprovalRequiredRule, null, WorkInboxRulesV1.Decimal(signedDifference), null, "TRY")],
            [new("manufacturing-order", Caption(order), order, null), new("period", Caption(period), period, null),
             new("debit-ledger-account", Caption(debit), debit, null), new("credit-ledger-account", Caption(credit), credit, null)]);
    }
    /// <summary>Scalar identity/shape check only: money and full source digest require genuine owner detail/receipt.</summary>
    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkManufacturingCostDifferenceProofV1? proof) =>
        proof is not null && proof.CompanyId != Guid.Empty && proof.Status is "Prepared" or "Posted"
        && proof.PreparedAtUtc.Offset == TimeSpan.Zero && Digest(proof.SourceFactsDigest) && Digest(proof.PreviewDigest)
        && item?.Rules is { Count: 1 } rules && rules[0] is { Threshold: null, Deviation: null, Unit: "TRY" } rule
        && rule.RuleCode == ApprovalRequiredRule && TryAmount(rule.Actual, out var amount) && WorkInboxRulesV1.Decimal(amount) == rule.Actual
        && Item(proof.ProposalId, proof.ManufacturingOrderId, proof.PeriodId, proof.DebitAccountId, proof.CreditAccountId,
            proof.PreparedBy, proof.PreparedAtUtc, amount) is { } expected && Same(item, expected);

    public static bool MatchesDecisionItem(WorkInboxItemV1? item, ManufacturingCostDifferenceDecisionDetailV1? detail) =>
        ManufacturingCostDifferenceServiceV1.ValidCostDifferenceDecisionDetail(detail)
        && FromView(detail!.Proposal, detail.PreparedAtUtc) is { } expected && Same(item, expected);

    public static bool MatchesDecisionItem(WorkInboxItemV1? item, ManufacturingCostDifferenceViewV1? original,
        ManufacturingCostDifferenceApprovalReceiptV1? receipt) =>
        original is not null && receipt is not null && original.CompanyId == receipt.CompanyId && receipt.CompanyId != Guid.Empty
        && original.ProposalId == receipt.ProposalId && original.Status == "Posted" && original.PreparedBy == receipt.PreparedBy
        && original.ApprovedBy == receipt.ActorId && receipt.ActorId != Guid.Empty && receipt.ActorId != receipt.PreparedBy
        && original.JournalId == receipt.JournalId && receipt.JournalId != Guid.Empty && receipt.ExpectedVersion > 0 && receipt.ExpectedVersion < long.MaxValue
        && receipt.ResultVersion == receipt.ExpectedVersion + 1 && original.Version == receipt.ResultVersion
        && receipt.PreparedAtUtc.Offset == TimeSpan.Zero && receipt.ApprovedAtUtc.Offset == TimeSpan.Zero
        && receipt.CommandName == ManufacturingCostDifferenceCommandNamesV1.Approve && Digest(receipt.IdempotencyKeyHash)
        && receipt.PayloadHash == ManufacturingCostDifferenceServiceV1.CostDifferenceApprovalPayloadHash(original.ProposalId, receipt.ExpectedVersion)
        && Digest(receipt.SourceFactsDigest) && receipt.OriginalResultDigest == ManufacturingCostDifferenceServiceV1.CostDifferenceOriginalResultDigest(new(AccountingApiStatus.Ok, original, false, null))
        && !string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
        && FromView(original, receipt.PreparedAtUtc) is { } expected && Same(item, expected);

    public static WorkInboxItemV1? FromView(ManufacturingCostDifferenceViewV1 view, DateTimeOffset preparedAt) =>
        view.PeriodId is { } period && view.DebitAccountId is { } debit && view.CreditAccountId is { } credit && view.PreparedBy is { } maker
        && TryAmount(view.SignedTlDifference, out var amount)
            ? Item(view.ProposalId, view.ManufacturingOrderId, period, debit, credit, maker, preparedAt, amount) : null;
    private static string Caption(Guid id) => id.ToString("D", CultureInfo.InvariantCulture)[..8];
    internal static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool TryAmount(string? value, out decimal amount) => decimal.TryParse(value,
        NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out amount);
    private static bool Same(WorkInboxItemV1? item, WorkInboxItemV1 expected) => item is not null
        && item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId
        && item.DocumentLabel == expected.DocumentLabel && item.WaitingSinceUtc == expected.WaitingSinceUtc && item.Link == expected.Link
        && item.PreparedBy == expected.PreparedBy && item.Scope is null && item.Rules is not null && item.References is not null
        && item.Rules.SequenceEqual(expected.Rules) && item.References.SequenceEqual(expected.References)
        && (item.ExcludedDeciders ?? []).SequenceEqual(expected.ExcludedDeciders ?? []);
}
