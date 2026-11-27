using System.Globalization;
using GoymenERP.BuildingBlocks.Work;

namespace GoymenERP.Modules.Accounting.Application;

/// <summary>The existing two inbox formatters. Source JSON and costs never become private scalar evidence.</summary>
public static class AccountingInventoryCostDecisionBindingV1
{
    public const string ShipmentKind = "accounting.invoiced-shipment-cost.v1";
    public const string PurchaseKind = "accounting.purchase-cost-difference.v1";
    public static string KindName(AccountingInventoryCostKindV1 kind) => kind switch
    { AccountingInventoryCostKindV1.SalesInvoicedShipment => ShipmentKind, AccountingInventoryCostKindV1.PurchaseInvoiceDifference => PurchaseKind, _ => "" };
    public static string ScreenPath(AccountingInventoryCostKindV1 kind) => kind switch
    { AccountingInventoryCostKindV1.SalesInvoicedShipment => "/muhasebe/sevk-smm-onay", AccountingInventoryCostKindV1.PurchaseInvoiceDifference => "/muhasebe/alis-maliyet-farki-onay", _ => "" };
    public static string RuleCode(AccountingInventoryCostKindV1 kind) => kind switch
    { AccountingInventoryCostKindV1.SalesInvoicedShipment => "accounting.invoiced-shipment-cost-approval-required", AccountingInventoryCostKindV1.PurchaseInvoiceDifference => "accounting.purchase-cost-difference-approval-required", _ => "" };
    public static WorkInboxItemV1? Item(AccountingInventoryCostKindV1 kind, Guid id, Guid invoiceId, string? invoiceNumber,
        Guid periodId, Guid maker, DateTimeOffset recordedAtUtc, decimal signedAmount) =>
        !Enum.IsDefined(kind) || id == Guid.Empty || invoiceId == Guid.Empty || periodId == Guid.Empty || maker == Guid.Empty ? null
        : new("accounting", KindName(kind), id, Label(invoiceNumber, id), recordedAtUtc.ToUniversalTime(), ScreenPath(kind), maker.ToString("D", CultureInfo.InvariantCulture),
            [new(RuleCode(kind), null, WorkInboxRulesV1.Decimal(signedAmount), null, "TRY")],
            [new("invoice", Label(invoiceNumber, invoiceId), invoiceId, null), new("period", Short(periodId), periodId, null)]);
    public static WorkInboxItemV1? FromView(AccountingInventoryCostProposalV1 p, DateTimeOffset recordedAtUtc) =>
        p.Preparation is { } preparation && TryAmount(preparation.SignedAmount, out var amount)
            ? Item(p.Kind, p.ProposalId, p.InvoiceId, p.OfficialInvoiceNumber, preparation.PeriodId, preparation.PreparedBy, recordedAtUtc, amount) : null;
    // Only the scalar identity/formatter shape is proven here; actual rule amount/full facts need owner detail or receipt.
    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkInventoryCostProofV1? proof) =>
        proof is not null && proof.CompanyId != Guid.Empty && proof.EventId != Guid.Empty
        && Enum.IsDefined((AccountingInventoryCostKindV1)proof.CostKind) && proof.Status is "Prepared" or "Posted" or "RoundedNoCost" or "Stale"
        && proof.PreparedRecordedAtUtc.Offset == TimeSpan.Zero && Digest(proof.SourceFactsDigest) && Digest(proof.PreviewDigest)
        && item?.Rules is { Count: 1 } rules && rules[0] is { Threshold: null, Deviation: null, Unit: "TRY" } rule
        && TryAmount(rule.Actual, out var amount) && WorkInboxRulesV1.Decimal(amount) == rule.Actual
        && Item((AccountingInventoryCostKindV1)proof.CostKind, proof.ProposalId, proof.InvoiceId, proof.OfficialInvoiceNumber,
            proof.PeriodId, proof.PreparedBy, proof.PreparedRecordedAtUtc, amount) is { } expected && Same(item, expected);
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, AccountingInventoryCostDecisionDetailV1? detail) =>
        AccountingInventoryCostServiceV1.ValidInventoryCostDecisionDetail(detail)
        && FromView(detail!.Proposal, detail.PreparedRecordedAtUtc) is { } expected && Same(item, expected);
    public static bool MatchesDecisionItem(WorkInboxItemV1? item, AccountingInventoryCostProposalV1? original,
        AccountingInventoryCostApprovalReceiptV1? receipt) =>
        original?.Preparation is { } p && receipt is not null && original.CompanyId == receipt.CompanyId && receipt.CompanyId != Guid.Empty
        && original.ProposalId == receipt.ProposalId && original.EventId == receipt.EventId && original.InvoiceId == receipt.InvoiceId && original.Kind == receipt.Kind
        && p.PreparedBy == receipt.PreparedBy && p.PreparedAtUtc == receipt.PreparedAtUtc && receipt.ActorId != Guid.Empty && receipt.ActorId != p.PreparedBy
        && receipt.ExpectedVersion >= 2 && receipt.ExpectedVersion < long.MaxValue && original.Version == receipt.ExpectedVersion + 1
        && receipt.ResultVersion == original.Version && receipt.ResultStatus == original.Status && receipt.CommandName == AccountingInventoryCostServiceV1.InventoryCostApproveCommand
        && Digest(receipt.IdempotencyKeyHash) && receipt.PayloadHash == AccountingInventoryCostServiceV1.InventoryCostApprovalPayloadHash(original.ProposalId, original.Kind, receipt.ExpectedVersion)
        && Digest(receipt.SourceFactsDigest) && Digest(receipt.OriginalResultDigest) && !string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
        && (original.Status == "Stale" ? original.ApprovedBy is null && original.StaleClosure?.ClosedAtUtc == receipt.DecidedAtUtc
            : original.Status is "Posted" or "RoundedNoCost" && original.ApprovedBy == receipt.ActorId && original.ApprovedAtUtc == receipt.DecidedAtUtc)
        && FromView(original, receipt.PreparedRecordedAtUtc) is { } expected && Same(item, expected);
    private static bool Same(WorkInboxItemV1? item, WorkInboxItemV1 expected) => item is not null
        && item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId && item.DocumentLabel == expected.DocumentLabel
        && item.WaitingSinceUtc == expected.WaitingSinceUtc && item.Link == expected.Link && item.PreparedBy == expected.PreparedBy
        && item.Scope is null && item.Rules is not null && item.References is not null
        && item.Rules.SequenceEqual(expected.Rules) && item.References.SequenceEqual(expected.References)
        && (item.ExcludedDeciders ?? []).SequenceEqual(expected.ExcludedDeciders ?? []);
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool TryAmount(string? value, out decimal amount) => decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount);
    private static string Label(string? value, Guid id) => string.IsNullOrWhiteSpace(value) || value.Length > WorkInboxRulesV1.MaxDocumentLabelLength ? Short(id) : value;
    private static string Short(Guid id) => id.ToString("D", CultureInfo.InvariantCulture)[..8];
}
