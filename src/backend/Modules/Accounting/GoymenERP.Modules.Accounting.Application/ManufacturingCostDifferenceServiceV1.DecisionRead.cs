using System.Text.Json;
using System.Globalization;

namespace GoymenERP.Modules.Accounting.Application;

public sealed partial class ManufacturingCostDifferenceServiceV1 : IManufacturingCostDifferenceDecisionReadV1
{
    public async Task<AccountingApiResult<ManufacturingCostDifferenceDecisionDetailV1>> ReadDecisionAsync(Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(freshConsumerFence); cancellationToken.ThrowIfCancellationRequested();
        if (proposalId == Guid.Empty) return new(AccountingApiStatus.Invalid, null, false, AccountingApiCodes.RequestInvalid, "proposalId");
        if (!Capture(out var companyId, out var actor, out var actorText)) return Denied<ManufacturingCostDifferenceDecisionDetailV1>();
        var captured = new ApprovalCapture(companyId, actor, actorText);
        try
        {
            await ReadFence(cancellationToken);
            if (store is not IManufacturingCostDifferenceDecisionStoreV1 reader) return new(AccountingApiStatus.Unavailable, null, false, null);
            var source = await reader.ReadCostDifferenceDecisionAsync(proposalId, ReadFence, cancellationToken);
            await ReadFence(cancellationToken);
            if (source is null) return new(AccountingApiStatus.NotFound, null, false, null);
            if (!ValidCostDifferenceSource(source, companyId, proposalId)) return new(AccountingApiStatus.Unavailable, null, false, null);
            if (source.Proposal.PreparedBy == actor) return Denied<ManufacturingCostDifferenceDecisionDetailV1>();
            return new(AccountingApiStatus.Ok, new(source.Proposal, source.PreparedAtUtc!.Value,
                CostDifferenceSourceFactsDigest(source), CostDifferencePreviewDigest(source)), false, null);
        }
        catch (NormalJournalAuthorityDeniedException) { return Denied<ManufacturingCostDifferenceDecisionDetailV1>(); }
        async Task ReadFence(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); await freshConsumerFence(token); token.ThrowIfCancellationRequested();
            // The final native permission check follows the last external await, including normal-completing revocation.
            await ApprovalFenceAsync(captured, token); token.ThrowIfCancellationRequested();
        }
    }

    public static string CostDifferenceSourceFactsDigest(ManufacturingCostDifferenceDecisionSourceV1 source) =>
        AccountingApiService.Sha256(JsonSerializer.Serialize(new
        {
            source.SourceHash, source.SourceCorrelationId, source.CurrencyId, source.PreparedAtUtc,
            source.Proposal.ProposalId, source.Proposal.CompanyId, source.Proposal.ManufacturingOrderId,
            source.Proposal.GenerationId, source.Proposal.GenerationVersion, source.Proposal.SignedTlDifference,
            source.Proposal.Outputs, source.Proposal.SourceOccurredAtUtc, source.Proposal.PeriodId,
            source.Proposal.AccountingDate, source.Proposal.TransactionDate, source.Proposal.DebitAccountId,
            source.Proposal.CreditAccountId, source.Proposal.PreparedBy, source.Proposal.Reason, source.Proposal.AccountPlan,
        }, Json));
    public static string CostDifferencePreviewDigest(ManufacturingCostDifferenceDecisionSourceV1 source) =>
        CostDifferencePreviewDigest(source.Proposal, source.PreparedAtUtc);

    // The native View's existing literal projection, shared with the purpose proof. No amounts are recalculated.
    public static IReadOnlyList<ManufacturingCostDifferenceOutputViewV1> CostDifferenceOutputs(ManufacturingCostDifferenceEventV1 source) =>
        source.Outputs.Select(output => new ManufacturingCostDifferenceOutputViewV1(
            output.OutputId, output.ReceiptMovementId, output.LabelId, Text(output.NetQuantityKg),
            ManufacturingCostDifferenceQuantityV1.Display(output.RemainingQuantityKg, output.RemainingQuantityKgRatio),
            ManufacturingCostDifferenceQuantityV1.Display(output.ConsumedQuantityKg, output.ConsumedQuantityKgRatio),
            Text(output.TlUnitCostPerKg!.Value), Text(output.RemainingTlValue!.Value), Text(output.ConsumedTlDifference!.Value),
            output.UsdUnitCostPerKg is { } usd ? Text(usd) : null, Text(output.OriginalTlAmount!.Value))).ToArray();
    private static string Text(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>The exact existing native admission aggregate and text; no new cost or rounding policy.</summary>
    public static bool TryCostDifferenceSignedTotal(ManufacturingCostDifferenceEventV1 source, out decimal amount, out string text)
    {
        amount = default; text = "";
        if (source.Outputs is null || source.Outputs.Any(x => x is null || !x.ConsumedTlDifference.HasValue)) return false;
        var exact = source.Outputs.Aggregate(AccountingInventoryCostAmountsV1.FromDecimal(0), (sum, output) =>
            AccountingInventoryCostAmountsV1.Add(sum, AccountingInventoryCostAmountsV1.FromDecimal(output.ConsumedTlDifference!.Value)));
        if (!AccountingInventoryCostAmountsV1.TryExactDecimal(exact, 28, out amount)) return false;
        text = Text(amount); return true;
    }

    private static bool ValidCostDifferenceSource(ManufacturingCostDifferenceDecisionSourceV1 source, Guid companyId, Guid id)
    {
        if (source.Proposal is not { } p || p.CompanyId != companyId || p.ProposalId != id || companyId == Guid.Empty || id == Guid.Empty
            || p.ManufacturingOrderId == Guid.Empty || p.GenerationId == Guid.Empty || p.Version < 2
            || string.IsNullOrWhiteSpace(p.GenerationVersion) || p.Outputs is not { Count: > 0 }
            || p.Outputs.Any(x => x is null || x.OutputId == Guid.Empty || x.ReceiptMovementId == Guid.Empty || x.LabelId == Guid.Empty)
            || p.PeriodId is null || p.PeriodId == Guid.Empty || p.AccountingDate is null || p.AccountingDate == default(DateOnly)
            || p.TransactionDate is null || p.TransactionDate == default(DateOnly) || p.DebitAccountId is null || p.DebitAccountId == Guid.Empty
            || p.CreditAccountId is null || p.CreditAccountId == Guid.Empty || p.CreditAccountId == p.DebitAccountId
            || p.PreparedBy is null || p.PreparedBy == Guid.Empty || source.PreparedAtUtc is not { Offset: var preparedOffset } || preparedOffset != TimeSpan.Zero
            || source.CurrencyId is null || source.CurrencyId == Guid.Empty || p.SourceOccurredAtUtc.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(p.Reason) || p.Reason != p.Reason.Trim() || p.Reason.Length > 1000
            || string.IsNullOrWhiteSpace(source.SourceCorrelationId) || string.IsNullOrWhiteSpace(source.SourceJson)
            || AccountingApiService.Sha256(source.SourceJson) != source.SourceHash
            || p.AccountPlan is not { } plan || plan.PackageId == Guid.Empty || plan.PackageVersion < 1
            || plan.Debit is null || plan.Credit is null || plan.Debit.AccountId != p.DebitAccountId || plan.Credit.AccountId != p.CreditAccountId
            || string.IsNullOrWhiteSpace(plan.SourceVersion) || string.IsNullOrWhiteSpace(plan.Sha256)) return false;
        ManufacturingCostDifferenceEventV1? frozen;
        try { frozen = JsonSerializer.Deserialize<ManufacturingCostDifferenceEventV1>(source.SourceJson, Json); }
        catch (JsonException) { return false; }
        if (frozen is null || frozen.CompanyId != companyId || frozen.CostDifferenceEventId != id
            || frozen.ManufacturingOrderId != p.ManufacturingOrderId || frozen.GenerationId != p.GenerationId
            || frozen.GenerationVersion != p.GenerationVersion || !ReadableCostDifferenceOutputs(frozen)
            || !TryCostDifferenceSignedTotal(frozen, out _, out var signedTotal) || p.SignedTlDifference != signedTotal
            || JsonSerializer.Serialize(CostDifferenceOutputs(frozen), Json) != JsonSerializer.Serialize(p.Outputs, Json)) return false;
        return p.Status == "Prepared" && p.ApprovedBy is null && p.JournalId is null && source.ApprovedAtUtc is null
            || p.Status == "Posted" && p.ApprovedBy is { } checker && checker != Guid.Empty && checker != p.PreparedBy
                && p.JournalId is { } journal && journal != Guid.Empty && source.ApprovedAtUtc is { Offset: var offset }
                && offset == TimeSpan.Zero && source.JournalBound;
    }
    public static bool ReadableCostDifferenceOutputs(ManufacturingCostDifferenceEventV1 source) =>
        source.Outputs is { Count: > 0 } && source.Outputs.All(x => x is not null && x.TlUnitCostPerKg.HasValue
            && x.RemainingTlValue.HasValue && x.ConsumedTlDifference.HasValue && x.OriginalTlAmount.HasValue
            && ManufacturingCostDifferenceQuantityV1.TryResolve(x.RemainingQuantityKg, x.RemainingQuantityKgRatio, out _)
            && ManufacturingCostDifferenceQuantityV1.TryResolve(x.ConsumedQuantityKg, x.ConsumedQuantityKgRatio, out _));
}
