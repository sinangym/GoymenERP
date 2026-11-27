using System.Text.Json;
using System.Text.Json.Nodes;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class AccountingInventoryCostServiceV1 : IAccountingInventoryCostDecisionReadV1
{
    public async Task<AccountingApiResult<AccountingInventoryCostDecisionDetailV1>> ReadDecisionAsync(Guid proposalId,
        AccountingInventoryCostKindV1 kind, Func<CancellationToken, Task> freshConsumerFence, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); token.ThrowIfCancellationRequested();
        if (proposalId == Guid.Empty || !Enum.IsDefined(kind)) return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid);
        if (!Capture(out var companyId, out var actor, out var actorText)) return Denied<AccountingInventoryCostDecisionDetailV1>();
        var captured = new ApprovalCapture(companyId, actor, actorText, Permission(kind, true));
        try
        {
            await Fence(token);
            if (store is not IAccountingInventoryCostDecisionStoreV1 reader) return new(AccountingApiStatus.Unavailable, null, false, null);
            var source = await reader.ReadInventoryCostDecisionAsync(proposalId, kind, null, Fence, token);
            await Fence(token);
            if (source is null) return new(AccountingApiStatus.NotFound, null, false, null);
            if (!ValidInventoryCostDecisionSource(source, companyId, proposalId, kind)) return new(AccountingApiStatus.Unavailable, null, false, null);
            if (source.Prepared.Preparation!.PreparedBy == actor) return Denied<AccountingInventoryCostDecisionDetailV1>();
            return new(AccountingApiStatus.Ok, new(source.Proposal, source.PreparedRecordedAtUtc,
                InventoryCostSourceFactsDigest(source), InventoryCostPreviewDigest(source.Proposal, source.PreparedRecordedAtUtc))
                { SourceCancelled = source.SourceCancelled }, false, null);
        }
        catch (NormalJournalAuthorityDeniedException) { return Denied<AccountingInventoryCostDecisionDetailV1>(); }
        catch (AccountingStoreException)
        {
            try { await Fence(token); }
            catch (NormalJournalAuthorityDeniedException) { return Denied<AccountingInventoryCostDecisionDetailV1>(); }
            return new(AccountingApiStatus.Unavailable, null, false, null);
        }
        async Task Fence(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); await freshConsumerFence(ct); ct.ThrowIfCancellationRequested();
            await ApprovalFenceAsync(captured, ct); ct.ThrowIfCancellationRequested();
        }
    }
    public static string InventoryCostSourceFactsDigest(AccountingInventoryCostDecisionSourceV1 source) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(new { source.Prepared, source.PreparedRecordedAtUtc, source.SourceHash }, Json));
    public static string InventoryCostPreviewDigest(AccountingInventoryCostProposalV1 proposal, DateTimeOffset preparedRecordedAtUtc) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(new { Proposal = proposal, PreparedRecordedAtUtc = preparedRecordedAtUtc }, Json));
    public static bool ValidInventoryCostDecisionDetail(AccountingInventoryCostDecisionDetailV1? detail) =>
        detail is { SourceCancelled: not null, Proposal: { Preparation: not null } p }
        && ValidInventoryCostPreparedShape(p) && ValidInventoryCostFrozenAmount(p) && ValidInventoryCostTerminalShape(p)
        && detail.PreparedRecordedAtUtc.Offset == TimeSpan.Zero
        && DecisionDigest(detail.SourceFactsDigest) && detail.PreviewDigest == InventoryCostPreviewDigest(p, detail.PreparedRecordedAtUtc);
    private static bool DecisionDigest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    private static bool ValidInventoryCostPreparedShape(AccountingInventoryCostProposalV1 p) =>
        p.ProposalId != Guid.Empty && p.CompanyId != Guid.Empty && p.EventId != Guid.Empty && Enum.IsDefined(p.Kind)
        && p.InvoiceId != Guid.Empty && p.InvoiceVersion > 0 && p.AdmissionId != Guid.Empty && p.Version >= 2
        && p.Component is { } c && !string.IsNullOrWhiteSpace(c.ComponentKey) && c.ProductId != Guid.Empty
        && AccountingInventoryCostAmountsV1.TryRead(c.SignedTlAmount, out _, out _)
        && !string.IsNullOrWhiteSpace(c.SourceProofJson) && c.SourceProofSha256 == AccountingInventoryCostSourceValidationV1.Sha256(c.SourceProofJson)
        && p.Preparation is { } prep && prep.PreparedBy != Guid.Empty && prep.PreparedAtUtc.Offset == TimeSpan.Zero
        && prep.PeriodId != Guid.Empty && prep.CurrencyId != Guid.Empty && prep.AccountingDate != default && prep.TransactionDate != default
        && prep.Classification is { AccountId: var account } && account != Guid.Empty
        && prep.CounterAccount is { AccountId: var counter } && counter != Guid.Empty && counter != account
        && prep.AccountPlanPackageId != Guid.Empty && prep.AccountPlanPackageVersion > 0
        && !string.IsNullOrWhiteSpace(prep.AccountPlanSourceVersion) && !string.IsNullOrWhiteSpace(prep.AccountPlanSha256)
        && !string.IsNullOrWhiteSpace(prep.Reason) && prep.Reason == prep.Reason.Trim() && prep.Reason.Length <= 1000
        && p.SourceOccurredAtUtc.Offset == TimeSpan.Zero && p.Status is "Prepared" or "Posted" or "RoundedNoCost" or "Stale";
    private static bool ValidInventoryCostTerminalShape(AccountingInventoryCostProposalV1 p) => p.Status switch
    {
        "Prepared" => p.ApprovedBy is null && p.ApprovedAtUtc is null && p.JournalId is null && p.StaleClosure is null,
        "Stale" => p.ApprovedBy is null && p.ApprovedAtUtc is null && p.JournalId is null && p.StaleClosure is { } closed
            && p.Version > 2 && closed.PreparedVersion == p.Version - 1 && closed.PreparedBy == p.Preparation!.PreparedBy
            && closed.ReasonCode == AccountingCostProposalStaleEventV1.ReasonCode && closed.ReasonText == AccountingCostProposalStaleEventV1.ReasonText,
        "Posted" or "RoundedNoCost" => p.ApprovedBy is { } actor && actor != Guid.Empty && actor != p.Preparation!.PreparedBy
            && p.ApprovedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero && p.StaleClosure is null
            && (p.Status == "Posted" ? p.JournalId is { } journal && journal != Guid.Empty
                : p.JournalId is null && p.Preparation!.SignedAmount == "0" && p.Preparation.Rounding is not null),
        _ => false,
    };
    public static bool ValidInventoryCostDecisionSource(AccountingInventoryCostDecisionSourceV1? source, Guid companyId, Guid id, AccountingInventoryCostKindV1 kind)
    {
        if (source?.Proposal is not { } p || source.Prepared is not { } before || !ValidInventoryCostPreparedShape(p)
            || !ValidInventoryCostTerminalShape(p) || !ValidInventoryCostPreparedShape(before) || !ValidInventoryCostTerminalShape(before)
            || p.CompanyId != companyId || p.ProposalId != id || p.Kind != kind
            || before.Status != "Prepared" || before.ApprovedBy is not null || before.ApprovedAtUtc is not null || before.JournalId is not null
            || before.StaleClosure is not null || source.PreparedRecordedAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(source.SourceJson) || source.SourceHash != AccountingApiService.Sha256(source.SourceJson)
            || !ValidInventoryCostFrozenAmount(p)) return false;
        AccountingInventoryCostSourceV1? frozen;
        try
        {
            frozen = JsonSerializer.Deserialize<AccountingInventoryCostSourceV1>(source.SourceJson, Json);
            if (frozen is null || !JsonNode.DeepEquals(JsonNode.Parse(source.SourceJson), JsonSerializer.SerializeToNode(frozen, Json))) return false;
        }
        catch (JsonException) { return false; }
        if (frozen.CompanyId != companyId || frozen.EventId != p.EventId || frozen.Kind != kind || frozen.InvoiceId != p.InvoiceId
            || frozen.InvoiceVersion != p.InvoiceVersion || frozen.AdmissionId != p.AdmissionId || frozen.InvoiceDate != p.InvoiceDate
            || frozen.OfficialInvoiceNumber != p.OfficialInvoiceNumber || frozen.Components is null
            || frozen.Components.Count(c => c.ComponentKey == p.Component.ComponentKey) != 1
            || JsonSerializer.Serialize(frozen.Components.Single(c => c.ComponentKey == p.Component.ComponentKey), Json) != JsonSerializer.Serialize(p.Component, Json)) return false;
        // Whole retained preparation equality binds every source/component/profile/plan fact without a live calculator.
        var normalized = p with { Version = before.Version, Status = "Prepared", ApprovedBy = null, ApprovedAtUtc = null, JournalId = null, StaleClosure = null, ApprovalReceipt = null };
        if (JsonSerializer.Serialize(normalized, Json) != JsonSerializer.Serialize(before, Json)) return false;
        if (p.Status == "Prepared") return p.Version == before.Version && p.ApprovedBy is null && p.ApprovedAtUtc is null && p.JournalId is null;
        if (before.Version == long.MaxValue || p.Version != before.Version + 1) return false;
        if (p.Status == "Stale") return p.ApprovedBy is null && p.JournalId is null && p.ApprovedAtUtc is null
            && p.StaleClosure is { } stale && stale.PreparedVersion == before.Version && stale.PreparedBy == before.Preparation!.PreparedBy
            && stale.EventId != Guid.Empty && stale.ClosedAtUtc.Offset == TimeSpan.Zero
            && stale.ReasonCode == AccountingCostProposalStaleEventV1.ReasonCode && stale.ReasonText == AccountingCostProposalStaleEventV1.ReasonText;
        return p.ApprovedBy is { } checker && checker != Guid.Empty && checker != p.Preparation!.PreparedBy
            && p.ApprovedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero
            && (p.Status == "Posted" ? p.JournalId is { } journal && journal != Guid.Empty && source.JournalBound
                : p.Status == "RoundedNoCost" && p.JournalId is null && p.Preparation!.Rounding is not null && p.Preparation.SignedAmount == "0");
    }
    // Reuse the owner's existing frozen-preparation verifier, including its recorded rounding policy.
    private static bool ValidInventoryCostFrozenAmount(AccountingInventoryCostProposalV1 p)
    {
        var prep = p.Preparation!;
        if (prep.Rounding is not null)
            return AccountingInventoryCostRoundingV1Rules.TryResolve(p.Component, prep.CurrencyId, prep.MoneyProfileId,
                prep.MoneyProfileVersion, prep.MoneyScale, out _, out var rounding)
                && prep.Rounding == rounding && rounding?.RoundedSignedAmount == prep.SignedAmount;
        return p.Component.UnpostedTlAmount is null && AccountingInventoryCostAmountsV1.TryExactDecimal(p.Component.SignedTlAmount, 28, out var amount)
            && amount.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture) == prep.SignedAmount;
    }
}
