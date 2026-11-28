using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Sales.Application;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.SalesContracts;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;

namespace GoymenERP.Modules.Sales.Infrastructure.WorkInbox;

/// <summary>Scalar identity/rule-shape precheck, followed by full native owner/immutable receipt binding. No price certificate is inferred from a scalar digest.</summary>
public static class SalesContractDiscountDecisionBindingV1
{
    public const string KindName = SalesContractDiscountWorkInboxSourceV1.KindName;
    public static WorkSalesContractDiscountProofV1 Proof(SalesContractDiscountDecisionDetailV1 detail)
    {
        if (!SalesContractService.MatchesContractDiscountDecisionDetail(detail)) throw new InvalidOperationException("Sözleşme sahibi olguları uyuşmuyor.");
        var s = detail.Source!; var c = s.Contract; var item = Item(detail)!;
        return new(s.CompanyId, c.Id, c.ContractNo, detail.Contract.Version, detail.Contract.Version, c.CreatedBy, c.CreatedAtUtc,
            c.State == SalesContractState.PendingDiscountApproval, false, detail.Contract.AmountDisclosure == SalesPriceDisclosure.Visible,
            c.DiscountLimitPercent is not null, item.Rules.Count, detail.SourceFactsDigest, detail.PreviewDigest, null, null, null);
    }
    public static WorkSalesContractDiscountProofV1 Proof(SalesContractDiscountOriginalReceiptV1 receipt)
    {
        var source = OriginalSource(receipt); var c = source.Contract; var item = Item(receipt);
        return new(source.CompanyId, c.Id, c.ContractNo, receipt.ExpectedVersion, receipt.CurrentSource!.Contract.Version.ToString(CultureInfo.InvariantCulture),
            c.CreatedBy, c.CreatedAtUtc, false, true, false, c.DiscountLimitPercent is not null, item.Rules.Count,
            SalesContractDiscountDecisionDigestsV1.SourceFactsDigest(source), receipt.OriginalDecisionDigest, receipt.OriginalDecisionDigest, receipt.IdempotencyKeyHash, receipt.PayloadHash);
    }
    public static WorkInboxItemV1? Item(SalesContractDiscountDecisionDetailV1? detail) => detail is not null && SalesContractService.MatchesContractDiscountDecisionDetail(detail)
        ? Item(detail.Source!, detail.Contract.AmountDisclosure == SalesPriceDisclosure.Visible) : null;
    public static WorkInboxItemV1 Item(SalesContractDiscountOriginalReceiptV1 receipt) => Item(OriginalSource(receipt), false);
    private static WorkInboxItemV1 Item(SalesContractDiscountDecisionSourceV1 source, bool prices)
    {
        var c = source.Contract;
        return SalesContractDiscountWorkInboxSourceV1.Project(source.CompanyId,
            [new(c.Id, source.CompanyId, c.ContractNo, c.CreatedBy, c.CreatedAtUtc, c.DiscountLimitPercent, c.DiscountRules)], 1, prices).Items.Single();
    }
    public static bool MatchesDecisionProof(WorkInboxItemV1? item, WorkSalesContractDiscountProofV1? p)
    {
        if (item is null || p is null || p.CompanyId == Guid.Empty || p.ContractId == Guid.Empty || p.ContractNo < 1
            || !SalesContractDiscountDecisionDigestsV1.TryVersion(p.Version, out var version) || !SalesContractDiscountDecisionDigestsV1.TryVersion(p.CurrentVersion, out var current)
            || current < version || string.IsNullOrWhiteSpace(p.CreatedBy) || p.CreatedBy != p.CreatedBy.Trim() || p.CreatedBy.Length > SalesContract.MaximumActorLength
            || p.CreatedAtUtc.Offset != TimeSpan.Zero || !Digest(p.SourceFactsDigest) || !Digest(p.PreviewDigest) || p.RuleCount < 1 || p.RuleCount > WorkInboxRulesV1.MaxRulesPerItem
            || p.Historical && (p.Pending || p.PriceDisclosed || current <= version || !Digest(p.OriginalDecisionDigest) || !UpperDigest(p.OriginalKeyHash) || !Digest(p.OriginalPayloadHash))
            || !p.Historical && (current != version || p.OriginalDecisionDigest is not null || p.OriginalKeyHash is not null || p.OriginalPayloadHash is not null)
            || item.Rules is null || item.Rules.Count != p.RuleCount || !p.LimitDefined && p.RuleCount != 1
            || item.Rules.Any(r => r is null || !WorkInboxRulesV1.IsValidRule("sales", r)
                || r.RuleCode != (p.LimitDefined ? SalesContractDiscountWorkInboxSourceV1.OverLimitRule : SalesContractDiscountWorkInboxSourceV1.LimitUndefinedRule)
                || (!p.PriceDisclosed || !p.LimitDefined ? r.Threshold is not null || r.Actual is not null || r.Deviation is not null || r.Unit is not null
                    : r.Threshold is null || r.Actual is null || r.Deviation is null || r.Unit != "%"))) return false;
        return SameItem(item, SalesContractDiscountWorkInboxSourceV1.Item(p.ContractId, p.ContractNo, p.CreatedBy, p.CreatedAtUtc, item.Rules));
    }
    public static bool MatchesDecisionSnapshot(WorkApprovalDecisionSnapshotV1? snapshot, SalesContractDiscountDecisionDetailV1? detail) => snapshot is not null
        && detail is not null && SalesContractService.MatchesContractDiscountDecisionDetail(detail) && snapshot.SalesContractDiscountProof == Proof(detail)
        && snapshot.CompanyId == detail.CompanyId && snapshot.SourceVersion == detail.Contract.Version && snapshot.Pending == (detail.Contract.State == "PendingDiscountApproval")
        && MatchesDecisionProof(snapshot.Item, snapshot.SalesContractDiscountProof) && SameItem(snapshot.Item, Item(detail)!);
    public static bool MatchesOriginalDecisionReceipt(WorkApprovalDecisionSnapshotV1? snapshot, SalesContractCommandResult? response,
        string command, SalesDraftStateCommandRequest request, string key, string actor)
    {
        try { return Original(snapshot, response, command, request, key, actor); }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException or OverflowException or System.Text.Json.JsonException) { return false; }
    }
    private static bool Original(WorkApprovalDecisionSnapshotV1? snapshot, SalesContractCommandResult? response, string command, SalesDraftStateCommandRequest request, string key, string actor)
    {
        if (snapshot?.SalesContractDiscountProof is not { } p || response?.OriginalDecisionReceipt is not { CurrentSource: { } current } r
            || request is null || snapshot.CompanyId != p.CompanyId || snapshot.SourceVersion != p.Version || snapshot.Pending != p.Pending || !MatchesDecisionProof(snapshot.Item, p)
            || request.ExpectedVersion != p.Version || !SalesContractDiscountDecisionDigestsV1.TryVersion(request.ExpectedVersion, out var expected) || expected == long.MaxValue
            || command is not (SalesContractCommands.ApproveDiscountV1 or SalesContractCommands.RejectDiscountV1) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(actor)
            || actor == p.CreatedBy || r.CompanyId != p.CompanyId || r.ContractId != p.ContractId || r.CommandName != command || r.ActorId != actor
            || r.ExpectedVersion != p.Version || r.ResultVersion != (expected + 1).ToString(CultureInfo.InvariantCulture)
            || r.IdempotencyKeyHash != DecisionKeyHash(key) || r.PayloadHash != DecisionPayloadHash(command, p.ContractId, request)
            || r.OriginalCorrelationId != response.OriginalCorrelationId || string.IsNullOrWhiteSpace(r.OriginalCorrelationId) || r.AuditEventId == Guid.Empty || !UpperDigest(r.AuditIntegrityHash)
            || r.OriginalDecisionDigest != SalesContractDiscountDecisionDigestsV1.OriginalDecisionDigest(r) || r.CurrentResponseDigest != SalesContractDiscountDecisionDigestsV1.CurrentResponseDigest(response)
            || current.CompanyId != p.CompanyId || !SalesContractService.MatchesContractDecisionResponse(current, response.Contract)
            || SalesContractDiscountDecisionDigestsV1.SourceFactsDigest(current) != SalesContractDiscountDecisionDigestsV1.SourceFactsDigest(r.After)) return false;
        var before = OriginalSource(r); var b = before.Contract; var a = r.After.Contract;
        var approve = command == SalesContractCommands.ApproveDiscountV1;
        if (b.Version != expected || b.CreatedBy != p.CreatedBy || b.CreatedAtUtc != p.CreatedAtUtc || b.ContractNo != p.ContractNo || b.State != SalesContractState.PendingDiscountApproval
            || b.Decision is not null || a.Version != expected + 1 || a.Decision is not { } decision || decision.DecidedBy != actor || decision.DecidedAtUtc != r.RecordedAtUtc
            || (approve ? request.Reason is not null : string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > SalesContract.MaximumReasonLength)
            || decision.RejectionReason != (approve ? null : request.Reason!.Trim())) return false;
        var native = SalesContractDiscountDecisionDigestsV1.Freeze(before).Contract;
        if (approve) native.ApproveDiscount(expected, actor, r.RecordedAtUtc); else native.RejectDiscount(expected, actor, request.Reason, r.RecordedAtUtc);
        if (SalesContractDiscountDecisionDigestsV1.SourceFactsDigest(new(p.CompanyId, native)) != SalesContractDiscountDecisionDigestsV1.SourceFactsDigest(r.After)
            || SalesContractDiscountDecisionDigestsV1.SourceFactsDigest(before) != p.SourceFactsDigest) return false;
        return p.Historical ? p == Proof(r) && SameItem(snapshot.Item, Item(r))
            : p.Pending && SameItem(snapshot.Item, Item(before, p.PriceDisclosed));
    }
    private static SalesContractDiscountDecisionSourceV1 OriginalSource(SalesContractDiscountOriginalReceiptV1 r)
    {
        if (r?.Before?.Contract is not { } b || r.After?.Contract is not { } a || r.CurrentSource?.Contract is null
            || r.CompanyId != r.Before.CompanyId || r.CompanyId != r.After.CompanyId || r.ContractId != b.Id || r.ContractId != a.Id
            || r.CompanyId == Guid.Empty || r.ContractId == Guid.Empty || r.CurrentSource.CompanyId != r.CompanyId || r.CurrentSource.Contract.Id != r.ContractId
            || !SalesContractDiscountDecisionDigestsV1.TryVersion(r.ExpectedVersion, out var expected) || expected == long.MaxValue
            || r.ResultVersion != (expected + 1).ToString(CultureInfo.InvariantCulture) || b.Version != expected || a.Version != expected + 1
            || b.State != SalesContractState.PendingDiscountApproval || b.Decision is not null || !b.DiscountApprovalRequired
            || a.Decision is not { } decision || decision.DecidedBy != r.ActorId || decision.DecidedAtUtc != r.RecordedAtUtc || r.ActorId == b.CreatedBy
            || r.CommandName is not (SalesContractCommands.ApproveDiscountV1 or SalesContractCommands.RejectDiscountV1)
            || r.AuditEventId == Guid.Empty || !UpperDigest(r.AuditIntegrityHash) || !UpperDigest(r.IdempotencyKeyHash) || !Digest(r.PayloadHash)
            || string.IsNullOrWhiteSpace(r.OriginalCorrelationId) || r.OriginalDecisionDigest != SalesContractDiscountDecisionDigestsV1.OriginalDecisionDigest(r))
            throw new InvalidOperationException("Özgün sözleşme karar bağı eksik.");
        var before = SalesContractDiscountDecisionDigestsV1.Freeze(r.Before);
        var after = SalesContractDiscountDecisionDigestsV1.Freeze(r.After);
        var current = SalesContractDiscountDecisionDigestsV1.Freeze(r.CurrentSource);
        var restored = SalesContractDiscountDecisionDigestsV1.Freeze(before).Contract;
        if (r.CommandName == SalesContractCommands.ApproveDiscountV1) restored.ApproveDiscount(expected, r.ActorId, r.RecordedAtUtc);
        else restored.RejectDiscount(expected, r.ActorId, decision.RejectionReason, r.RecordedAtUtc);
        if (SalesContractDiscountDecisionDigestsV1.SourceFactsDigest(new(r.CompanyId, restored)) != SalesContractDiscountDecisionDigestsV1.SourceFactsDigest(after)
            || SalesContractDiscountDecisionDigestsV1.SourceFactsDigest(current) != SalesContractDiscountDecisionDigestsV1.SourceFactsDigest(after)
            || DecisionPayloadHash(r.CommandName, r.ContractId, new(r.ExpectedVersion, decision.RejectionReason)) != r.PayloadHash)
            throw new InvalidOperationException("Özgün sözleşme komutu olgularıyla uyuşmuyor.");
        return before;
    }
    public static string DecisionKeyHash(string key) => SalesStore.IdempotencyKeyHash(key);
    public static string DecisionPayloadHash(string command, Guid id, SalesDraftStateCommandRequest request) => SalesContractHash.Compute(command, id, request);
    private static bool Digest(string? s) => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool UpperDigest(string? s) => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool SameItem(WorkInboxItemV1? a, WorkInboxItemV1 b) => a is not null && a.Module == b.Module && a.Kind == b.Kind && a.ItemId == b.ItemId
        && a.DocumentLabel == b.DocumentLabel && a.WaitingSinceUtc == b.WaitingSinceUtc && a.Link == b.Link && a.PreparedBy == b.PreparedBy && a.Scope is null && a.ExcludedDeciders is null
        && a.Rules is not null && a.Rules.SequenceEqual(b.Rules) && a.References is not null && a.References.SequenceEqual(b.References);
}
