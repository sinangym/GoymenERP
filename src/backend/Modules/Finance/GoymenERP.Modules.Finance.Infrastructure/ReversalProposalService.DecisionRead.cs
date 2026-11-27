using System.Globalization;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Finance.Infrastructure.WorkInbox;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed partial class ReversalProposalService : IReceiptReversalDecisionReadV1
{
    public static string DecisionPreviewDigest(ReceiptReversalDecisionDetailV1 detail) =>
        PayloadHash("finance.receipt-reversal.decision-preview.v1", detail);

    public static string RecordedFactsDigest(ReceiptReversalDecisionDetailV1 detail) => RecordedFactsDigest(detail.CompanyId, detail.OriginalReceipt);
    private static string RecordedFactsDigest(Guid companyId, ReceiptReversalOriginalReceiptV1 original) =>
        PayloadHash("finance.receipt-reversal.recorded-facts.v1", new { CompanyId = companyId, OriginalReceipt = original });
    public static bool MatchesDecisionRule(ReceiptReversalDecisionDetailV1? detail, IReadOnlyList<WorkApprovalRuleResultV1> rules) =>
        detail?.OriginalReceipt is { } original && rules is { Count: 1 } && FinanceWorkInboxRead.Amount(original.AmountText) is { } amount
        && rules[0] == new WorkApprovalRuleResultV1(FinanceManualReceiptReversalWorkInboxSourceV1.ApprovalRequiredRule, null, amount, null, null);

    public static bool MatchesDecisionItem(WorkInboxItemV1? item, ReceiptReversalDecisionDetailV1? detail)
    {
        var expected = DecisionItem(detail);
        return expected is not null && item is not null && item.Module == expected.Module && item.Kind == expected.Kind && item.ItemId == expected.ItemId
            && item.DocumentLabel == expected.DocumentLabel && item.WaitingSinceUtc == expected.WaitingSinceUtc && item.Link == expected.Link
            && item.PreparedBy == expected.PreparedBy && item.Scope == expected.Scope && item.ExcludedDeciders is null
            && item.Rules is not null && item.References is not null && item.Rules.SequenceEqual(expected.Rules) && item.References.SequenceEqual(expected.References);
    }

    internal static WorkInboxItemV1? DecisionItem(ReceiptReversalDecisionDetailV1? detail)
    {
        if (detail?.Proposal is not { } proposal || detail.OriginalReceipt is not { } original || detail.CompanyId == Guid.Empty
            || proposal.Id == Guid.Empty || proposal.OriginalSourceId != original.SourceId || original.SourceId == Guid.Empty
            || original.PartyId == Guid.Empty || original.TreasuryAccountId == Guid.Empty || original.CurrencyId == Guid.Empty
            || !ReceiptIdentity(proposal.ProposedBy) || !Version(proposal.Version, out _) || !Enum.IsDefined(proposal.Status)
            || FinanceWorkInboxRead.Amount(original.AmountText) is not { } amount) return null;
        return new(FinanceManualReceiptReversalWorkInboxSourceV1.ModuleName, FinanceManualReceiptReversalWorkInboxSourceV1.KindName,
            proposal.Id, FinanceWorkInboxRead.Label(original.DocumentNumber, proposal.Id), proposal.ProposedAtUtc,
            FinanceManualReceiptReversalWorkInboxSourceV1.ScreenPath, proposal.ProposedBy,
            [new(FinanceManualReceiptReversalWorkInboxSourceV1.ApprovalRequiredRule, null, amount, null, null)],
            [FinanceWorkInboxRead.Reference("manual-receipt", original.SourceId, FinanceWorkInboxRead.Label(original.DocumentNumber, original.SourceId),
                FinanceWorkInboxFence.Link(FinanceManualReceiptReversalWorkInboxSourceV1.ReceiptScreenPath, original.SourceId)),
             FinanceWorkInboxRead.Reference("party", original.PartyId), FinanceWorkInboxRead.Reference("bank-account", original.TreasuryAccountId),
             FinanceWorkInboxRead.Reference("currency", original.CurrencyId)]);
    }

    public static bool MatchesDecisionSnapshot(ReceiptReversalDecisionDetailV1? detail, WorkApprovalDecisionSnapshotV1? snapshot)
    {
        if (detail?.Proposal is not { } proposal || detail.OriginalReceipt is not { } original || snapshot is null || snapshot.Item is null
            || snapshot.ManualReceiptReversalProof is not { } proof || detail.CompanyId != snapshot.CompanyId
            || proposal.Id != snapshot.Item.ItemId || proposal.OriginalSourceId != original.SourceId
            || snapshot.Item.Module != FinanceManualReceiptReversalWorkInboxSourceV1.ModuleName
            || snapshot.Item.Kind != FinanceManualReceiptReversalWorkInboxSourceV1.KindName
            || snapshot.SourceVersion != proposal.Version || snapshot.Pending != (proposal.Status == ReversalProposalStatus.Pending)
            || snapshot.Item.PreparedBy != proposal.ProposedBy || snapshot.Item.WaitingSinceUtc != proposal.ProposedAtUtc
            || !MatchesDecisionItem(snapshot.Item, detail) || snapshot.Item.References is not { Count: 4 }) return false;
        var expectedProof = new WorkManualReceiptReversalProofV1(detail.CompanyId, proposal.Id, original.SourceId,
            proposal.ProposedBy, proposal.ProposedAtUtc, proposal.Status.ToString(), RecordedFactsDigest(detail), DecisionPreviewDigest(detail));
        return proof == expectedProof && Ref("manual-receipt", original.SourceId) && Ref("party", original.PartyId)
            && Ref("bank-account", original.TreasuryAccountId) && Ref("currency", original.CurrencyId);
        bool Ref(string role, Guid id) => id != Guid.Empty && snapshot.Item.References.Count(reference => reference.Role == role && reference.RecordId == id) == 1;
    }

    public async Task<ReadResult<ReceiptReversalDecisionDetailV1>> ReadDecisionAsync(FinanceContext context, Guid proposalId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(freshConsumerFence);
        cancellationToken.ThrowIfCancellationRequested();
        if (await Fence() is { } before) return before;
        if (proposalId == Guid.Empty) return new(ReadOutcome.NotFound, null);
        var proposal = await dbContext.ReversalProposals.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CompanyId == context.CompanyId && row.Id == proposalId, cancellationToken);
        if (await Fence() is { } afterProposal) return afterProposal;
        if (proposal is null) return new(ReadOutcome.NotFound, null);
        var capturedVersion = proposal.Version; var capturedStatus = proposal.Status; var view = View(proposal);
        if (!ValidDecisionProposal(context.CompanyId, proposalId, proposal)) return new(ReadOutcome.Unavailable, null);
        if (proposal.ProposedBy == context.ActorId) return new(ReadOutcome.Denied, null);
        var receipt = await ReadOriginalReceiptAsync(context.CompanyId, proposal.OriginalSourceId, cancellationToken);
        if (await Fence() is { } afterOriginal) return afterOriginal;
        if (receipt is null) return new(ReadOutcome.Unavailable, null);
        // One current proposal revision only; a concurrent native decision cannot mix pending/terminal human facts.
        var final = await dbContext.ReversalProposals.AsNoTracking().Where(row => row.CompanyId == context.CompanyId && row.Id == proposalId)
            .Select(row => new { row.Version, row.Status, row.OriginalSourceId }).SingleOrDefaultAsync(cancellationToken);
        if (await Fence() is { } afterFinal) return afterFinal;
        return final is null || final.Version != capturedVersion || final.Status != capturedStatus || final.OriginalSourceId != proposal.OriginalSourceId
            ? new(ReadOutcome.Unavailable, null) : new(ReadOutcome.Found, new(context.CompanyId, view, receipt));

        async Task<ReadResult<ReceiptReversalDecisionDetailV1>?> Fence()
        {
            await freshConsumerFence(cancellationToken);
            if (!SameContext(context)) return new(ReadOutcome.Denied, null);
            var refusal = await FenceAsync(context, FinanceAction.ApproveReversal, proposalId, cancellationToken);
            await freshConsumerFence(cancellationToken);
            if (!SameContext(context)) return new(ReadOutcome.Denied, null);
            return refusal is null ? null : new(refusal.Outcome == CommandOutcome.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }
    }

    private static bool ValidDecisionProposal(Guid companyId, Guid proposalId, ReversalProposalRow row) =>
        row.CompanyId == companyId && row.Id == proposalId && row.Id != Guid.Empty && row.OriginalSourceId != Guid.Empty
        && row.Version > 0 && ReceiptIdentity(row.ProposedBy) && CanonicalReason(row.Reason) && row.ProposedAtUtc.Offset == TimeSpan.Zero
        && (row.Status switch
        {
            ReversalProposalStatuses.Pending => row.DecidedBy is null && row.DecidedAtUtc is null && row.DecisionReason is null && row.ReversalSourceId is null,
            ReversalProposalStatuses.Approved => ReceiptIdentity(row.DecidedBy) && row.DecidedBy != row.ProposedBy
                && row.DecidedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero && row.DecidedAtUtc >= row.ProposedAtUtc
                && row.DecisionReason is null && row.ReversalSourceId is not null && row.ReversalSourceId != Guid.Empty && row.ReversalSourceId != row.OriginalSourceId,
            ReversalProposalStatuses.Rejected => ReceiptIdentity(row.DecidedBy) && row.DecidedBy != row.ProposedBy
                && row.DecidedAtUtc is { Offset: var offset } && offset == TimeSpan.Zero && row.DecidedAtUtc >= row.ProposedAtUtc
                && CanonicalReason(row.DecisionReason) && row.ReversalSourceId is null,
            _ => false,
        });

    private async Task<ReceiptReversalOriginalReceiptV1?> ReadOriginalReceiptAsync(Guid companyId, Guid sourceId, CancellationToken token)
    {
        var source = await dbContext.ReceiptDrafts.AsNoTracking().SingleOrDefaultAsync(row => row.CompanyId == companyId && row.Id == sourceId, token);
        var fact = await dbContext.MoneyEvents.AsNoTracking().SingleOrDefaultAsync(row => row.CompanyId == companyId && row.SourceId == sourceId
            && row.Kind == MoneyEventKinds.ManualBankReceiptRecorded, token);
        return OriginalReceipt(companyId, sourceId, source, fact);
    }

    internal static ReceiptReversalOriginalReceiptV1? OriginalReceipt(Guid companyId, Guid sourceId, ReceiptDraftRow? source, MoneyEventRow? fact)
    {
        if (source is null || fact is null || companyId == Guid.Empty || sourceId == Guid.Empty
            || source.CompanyId != companyId || fact.CompanyId != companyId || source.Id != sourceId || fact.SourceId != sourceId
            || fact.Id == Guid.Empty || fact.Kind != MoneyEventKinds.ManualBankReceiptRecorded || source.Stage != ReceiptStages.Recorded
            || source.Version < 1 || source.PartyId == Guid.Empty || source.CompanyAccountId == Guid.Empty
            || source.TreasuryAccountId == Guid.Empty || source.CurrencyId == Guid.Empty
            || source.PartyId != fact.PartyId || source.CompanyAccountId != fact.CompanyAccountId
            || source.TreasuryAccountId != fact.TreasuryAccountId || source.CurrencyId != fact.CurrencyId
            || !FinanceExactDecimalText.TryParsePositive(source.AmountText, out _) || source.AmountText != fact.AmountText
            || !ReceiptIdentity(source.ProfileId) || !ReceiptIdentity(source.ProfileVersion)
            || source.ProfileId != fact.ProfileId || source.ProfileVersion != fact.ProfileVersion
            || source.OccurredOn != fact.OccurredOn || source.ValueOn != fact.ValueOn
            || source.RecordedBy != fact.RecordedBy || !ReceiptIdentity(fact.RecordedBy) || !ReceiptIdentity(fact.CorrelationId)
            || source.RecordedAtUtc != fact.RecordedAtUtc || fact.RecordedAtUtc.Offset != TimeSpan.Zero
            || source.PosCommissionAmountText != fact.PosCommissionAmountText || source.PosBankNetAmountText != fact.PosBankNetAmountText
            || source.PosCommissionBearer != fact.PosCommissionBearer) return null;
        var noPos = source.PosCommissionAmountText is null && source.PosBankNetAmountText is null && source.PosCommissionBearer is null;
        if (!noPos && (source.PosCommissionBearer is not (PosCommissionBearers.Company or PosCommissionBearers.Customer)
            || !PosCollectionRules.TryBankNet(source.AmountText, source.PosCommissionAmountText, out var net)
            || net != source.PosBankNetAmountText)) return null;
        return new(source.Id, source.Version.ToString(CultureInfo.InvariantCulture), source.DocumentNumber, source.PartyId,
            source.CompanyAccountId, source.TreasuryAccountId, source.CurrencyId, source.AmountText, source.OccurredOn, source.ValueOn,
            source.PosCommissionAmountText, source.PosBankNetAmountText, source.PosCommissionBearer is { } bearer ? (PosCommissionBearer)bearer : null,
            fact.Id, fact.RecordedBy, fact.RecordedAtUtc);
    }
}
