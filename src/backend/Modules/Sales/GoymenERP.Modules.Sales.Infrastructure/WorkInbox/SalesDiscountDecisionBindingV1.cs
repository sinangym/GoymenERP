using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>Native formatter reuse. Scalar precommand checks do not certify monetary rules; full owner/receipt matching does.</summary>
public static class SalesDiscountDecisionBindingV1
{
    public const string KindName = SalesDiscountExceptionWorkInboxSourceV1.KindName;

    public static WorkSalesDiscountProofV1 Proof(SalesDiscountDecisionDetailV1 detail)
    {
        if (!SalesDraftService.MatchesDiscountDecisionDetail(detail)) throw new InvalidOperationException("İskonto sahibi olguları uyuşmuyor.");
        var source = detail.Source!; var below = Below(source);
        return new(source.CompanyId, source.DraftId, source.DocumentNo, detail.Version, detail.ApprovalSupported, false,
            detail.PriceDisclosure == SalesPriceDisclosure.Visible, Array.AsReadOnly(below.Select(SalesDiscountExceptionWorkInboxSourceV1.LineProof).ToArray()),
            detail.SourceFactsDigest, detail.PreviewDigest, null, null);
    }

    public static WorkSalesDiscountProofV1 Proof(SalesDiscountOriginalReceiptV1 receipt)
    {
        var source = OriginalSource(receipt);
        return new(source.CompanyId, source.DraftId, source.DocumentNo, source.Version.ToString(CultureInfo.InvariantCulture), false, true, false,
            Array.AsReadOnly(Below(source).Select(SalesDiscountExceptionWorkInboxSourceV1.LineProof).ToArray()),
            SalesDiscountDecisionDigestsV1.SourceFactsDigest(source), receipt.OriginalDecisionDigest, receipt.IdempotencyKeyHash, receipt.PayloadHash);
    }

    public static WorkInboxItemV1? Item(SalesDiscountDecisionDetailV1? detail) =>
        detail is not null && SalesDraftService.MatchesDiscountDecisionDetail(detail) && Below(detail.Source!).Length > 0
            ? SalesDiscountExceptionWorkInboxSourceV1.Item(detail.DraftId, detail.DocumentNo, Below(detail.Source!), detail.PriceDisclosure == SalesPriceDisclosure.Visible) : null;
    public static WorkInboxItemV1? Item(SalesDiscountOriginalReceiptV1 receipt)
    {
        var source = OriginalSource(receipt); var below = Below(source);
        return below.Length == 0 ? null : SalesDiscountExceptionWorkInboxSourceV1.Item(source.DraftId, source.DocumentNo, below, false);
    }

    public static bool MatchesDecisionItem(WorkInboxItemV1? item, SalesDiscountDecisionDetailV1? detail) =>
        Item(detail) is { } expected && SameItem(item, expected);
    public static bool MatchesDecisionSnapshot(WorkApprovalDecisionSnapshotV1? snapshot, SalesDiscountDecisionDetailV1? detail) =>
        snapshot?.SalesDiscountProof is { } p && SalesDraftService.MatchesDiscountDecisionDetail(detail) && !p.Historical && SameProof(p, Proof(detail!))
        && snapshot.CompanyId == p.CompanyId && snapshot.SourceVersion == p.Version && snapshot.Pending == p.Pending && MatchesDecisionItem(snapshot.Item, detail);

    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkSalesDiscountProofV1? p)
    {
        if (p is null || item is null || p.CompanyId == Guid.Empty || p.DraftId == Guid.Empty || !TryVersion(p.Version, out _)
            || string.IsNullOrWhiteSpace(p.DocumentNo) || p.DocumentNo != p.DocumentNo.Trim() || p.DocumentNo.Length > WorkInboxRulesV1.MaxDocumentLabelLength
            || !Digest(p.SourceFactsDigest) || !Digest(p.PreviewDigest) || p.Lines is not { Count: > 0 and <= SalesOrderDraft.MaximumLines }
            || p.Lines.Any(x => x is null || x.LineId == Guid.Empty || x.LineNo < 1 || x.ProductId == Guid.Empty || x.PriceListId == Guid.Empty || x.PriceListId is null && x.PriceListName is not null || x.PriceListId is not null && string.IsNullOrWhiteSpace(x.PriceListName)
                || string.IsNullOrWhiteSpace(x.CheckedBy) || x.CheckedBy.Length > SalesOrderDraft.MaximumActorLength || x.CheckedBy != x.CheckedBy.Trim() || x.CheckedAtUtc.Offset != TimeSpan.Zero
                || string.IsNullOrWhiteSpace(x.ProductCode) || string.IsNullOrWhiteSpace(x.ProductName))
            || p.Lines.Select(x => x.LineId).Distinct().Count() != p.Lines.Count || p.Lines.Select(x => x.LineNo).Distinct().Count() != p.Lines.Count
            || !p.Lines.Select(x => x.LineNo).SequenceEqual(p.Lines.Select(x => x.LineNo).Order())
            || p.Historical && (p.Pending || p.PriceDisclosed || !UpperDigest(p.OriginalKeyHash) || !Digest(p.OriginalPayloadHash))
            || !p.Historical && (p.OriginalKeyHash is not null || p.OriginalPayloadHash is not null)
            || item.Rules is null || item.Rules.Count != Math.Min(p.Lines.Count, WorkInboxRulesV1.MaxRulesPerItem)
            || item.Rules.Where((rule, i) => !ValidRuleShape(rule, p.Lines[i], p.PriceDisclosed)).Any()) return false;
        // Exact caption/references/all requesters; prices are bound only by MatchesDecisionItem/full original receipt below.
        return SameItem(item, SalesDiscountExceptionWorkInboxSourceV1.Item(p.DraftId, p.DocumentNo, p.Lines, item.Rules));
    }

    public static bool MatchesOriginalDecisionReceipt(WorkApprovalDecisionSnapshotV1? snapshot, SalesOrderDraftResponse? response,
        SalesDraftDiscountExceptionApprovalRequest request, string key, string actorId)
    {
        try { return MatchesOriginal(snapshot, response, request, key, actorId); }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException or FormatException or OverflowException or JsonException)
        { return false; }
    }
    private static bool MatchesOriginal(WorkApprovalDecisionSnapshotV1? snapshot, SalesOrderDraftResponse? response,
        SalesDraftDiscountExceptionApprovalRequest request, string key, string actorId)
    {
        if (snapshot?.SalesDiscountProof is not { } p || snapshot.CompanyId != p.CompanyId || snapshot.SourceVersion != p.Version
            || snapshot.Pending != p.Pending || !MatchesDecisionProof(snapshot.Item, p) || response?.OriginalDiscountApprovalReceipt is not { } receipt
            || request is null || request.ExpectedVersion != p.Version || !TryVersion(request.ExpectedVersion, out var expected) || expected == long.MaxValue
            || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(actorId) || receipt.ActorId != actorId || receipt.CompanyId != p.CompanyId
            || receipt.DraftId != p.DraftId || receipt.CommandName != SalesDraftCommands.ApproveDiscountExceptionsV1
            || receipt.ExpectedVersion != expected || receipt.ResultVersion != expected + 1
            || receipt.IdempotencyKeyHash != SalesStore.IdempotencyKeyHash(key)
            || receipt.PayloadHash != SalesDraftRequestHash.Compute(SalesDraftCommands.ApproveDiscountExceptionsV1, p.DraftId, request)
            || receipt.OriginalCorrelationId != response.OriginalCorrelationId || string.IsNullOrWhiteSpace(receipt.OriginalCorrelationId)
            || receipt.AuditEventId == Guid.Empty || !UpperDigest(receipt.AuditIntegrityHash)
            || receipt.OriginalResultDigest != SalesDiscountDecisionDigestsV1.OriginalResultDigest(receipt.OriginalResult)
            || receipt.OriginalDecisionDigest != SalesDiscountDecisionDigestsV1.OriginalDecisionDigest(receipt)
            || receipt.CurrentResponseDigest != SalesDiscountDecisionDigestsV1.CurrentResponseDigest(response)) return false;
        var source = OriginalSource(receipt); var detail = SalesDraftService.ProjectDiscountDecision(source,
            p.PriceDisclosed ? SalesPriceDisclosure.Visible : SalesPriceDisclosure.Withheld);
        var expectedProof = p.Historical ? Proof(receipt) : Proof(detail);
        var expectedItem = p.Historical ? Item(receipt) : Item(detail);
        return SameProof(p, expectedProof) && expectedItem is not null && SameItem(snapshot.Item, expectedItem);
    }

    private static SalesDiscountDecisionSourceV1 OriginalSource(SalesDiscountOriginalReceiptV1 receipt)
    {
        if (receipt is null || receipt.CompanyId == Guid.Empty || receipt.DraftId == Guid.Empty || receipt.ExpectedVersion < 1
            || receipt.ExpectedVersion == long.MaxValue || receipt.ResultVersion != receipt.ExpectedVersion + 1
            || receipt.Lines is not { Count: > 0 and <= SalesOrderDraft.MaximumLines } || receipt.Lines.Any(x => x is null)
            || receipt.OriginalResult?.Visible?.Draft is not { } header || receipt.OriginalResult.Visible.Lines is not { Count: > 0 } originals
            || header.Id != receipt.DraftId || header.CompanyId != receipt.CompanyId || originals.Count != receipt.Lines.Count
            || receipt.OriginalDecisionDigest != SalesDiscountDecisionDigestsV1.OriginalDecisionDigest(receipt)
            || receipt.OriginalResultDigest != SalesDiscountDecisionDigestsV1.OriginalResultDigest(receipt.OriginalResult))
            throw new InvalidOperationException("Özgün iskonto olguları doğrulanamadı.");
        var h = receipt.OriginalResult.Visible.Draft;
        if (!h.Revision.StartsWith("Rev.", StringComparison.Ordinal) || !int.TryParse(h.Revision.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            || revision < 0 || SalesDraftApiV1.RevisionText(revision) != h.Revision) throw new InvalidOperationException("Özgün revizyon geçersiz.");
        // This exact command changes only DiscountCheck approval fields/version. Its genuine original result retains the unchanged header/line identities.
        var lines = receipt.OriginalResult.Visible.Lines.Select(line => new SalesDiscountDecisionSourceLineV1(line.LineId, line.LineNo,
            line.ProductId, line.ProductVersion, line.ProductCode, line.ProductName, line.OrderUnitId, line.Quantity,
            receipt.Lines.Single(x => x.LineId == line.LineId && x.LineNo == line.LineNo).Before)).ToArray();
        return new(receipt.CompanyId, receipt.DraftId, h.DocumentNo, receipt.ExpectedVersion, revision, h.State, h.ApprovalState,
            h.PartyId, h.CompanyAccountId, h.CurrencyId, h.BusinessDate, h.SubcontractService == true, Array.AsReadOnly(lines));
    }
    private static SalesWorkInboxDraftLineV1[] Below(SalesDiscountDecisionSourceV1 source) => source.Lines
        .Where(x => !source.IsSubcontractService && SalesOrderDraft.NeedsDiscountExceptionApproval(x.Commercial)).OrderBy(x => x.LineNo)
        .Select(x => new SalesWorkInboxDraftLineV1(x.LineId, x.LineNo, x.ProductId, x.ProductCode, x.ProductName, x.Commercial)).ToArray();
    public static bool TryVersion(string? value, out long version) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out version)
        && version > 0 && version.ToString(CultureInfo.InvariantCulture) == value;
    public static string DecisionKeyHash(string key) => SalesStore.IdempotencyKeyHash(key);
    public static string DecisionPayloadHash(Guid id, SalesDraftDiscountExceptionApprovalRequest request) =>
        SalesDraftRequestHash.Compute(SalesDraftCommands.ApproveDiscountExceptionsV1, id, request);
    private static bool ValidRuleShape(WorkApprovalRuleResultV1 rule, WorkSalesDiscountLineProofV1 line, bool prices)
    {
        if (!WorkInboxRulesV1.IsValidRule("sales", rule) || !prices && (rule.Actual is not null || rule.Deviation is not null)) return false;
        if (line.PriceListId is null) return rule.RuleCode == SalesDiscountExceptionWorkInboxSourceV1.WithoutListPriceRule
            && rule.Threshold is null && rule.Actual is null && rule.Deviation is null && rule.Unit is null;
        if (line.LimitDefined) return rule.RuleCode == SalesDiscountExceptionWorkInboxSourceV1.OverLimitRule && rule.Threshold is not null && rule.Unit == "%";
        return rule.RuleCode == SalesDiscountExceptionWorkInboxSourceV1.LimitUndefinedRule && rule.Threshold is null && rule.Deviation is null
            && (rule.Actual is null ? rule.Unit is null : rule.Unit == "%");
    }
    private static bool Digest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool UpperDigest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool SameProof(WorkSalesDiscountProofV1 first, WorkSalesDiscountProofV1 second) =>
        JsonSerializer.Serialize(first, SalesDraftJson.Options) == JsonSerializer.Serialize(second, SalesDraftJson.Options);
    private static bool SameItem(WorkInboxItemV1? a, WorkInboxItemV1 b) => a is not null && a.Module == b.Module && a.Kind == b.Kind && a.ItemId == b.ItemId
        && a.DocumentLabel == b.DocumentLabel && a.WaitingSinceUtc == b.WaitingSinceUtc && a.Link == b.Link && a.PreparedBy == b.PreparedBy && a.Scope is null
        && a.Rules is not null && a.Rules.SequenceEqual(b.Rules) && a.References is not null && a.References.SequenceEqual(b.References)
        && (a.ExcludedDeciders is null ? b.ExcludedDeciders is null
            : b.ExcludedDeciders is not null && a.ExcludedDeciders.SequenceEqual(b.ExcludedDeciders));
}
