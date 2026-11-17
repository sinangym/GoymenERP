using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

internal sealed record FinancePartyBalanceTransferSnapshot(FinancePartyBalanceTransferPreviewV1 Preview,
    IReadOnlyList<FinanceBalanceTransferSourceFact> Sources);

internal static class FinancePartyBalanceTransferSnapshotReader
{
    public static async Task<FinancePartyBalanceTransferSnapshot?> ReadAsync(FinanceDbContext database,
        IPartyLedgerService ledger, FinanceContext context, FinancePartyBalanceTransferOwnerProofV1 owner,
        CancellationToken cancellationToken)
    {
        var balance = await ledger.SummarizeAuthorizedAsync(context,
            new(owner.PredecessorPartyId, owner.PredecessorAccountId), cancellationToken);
        if (balance.Outcome != PartyBalanceOutcome.Found || balance.Balance is null) return null;
        var currencies = FinancePartyBalanceTransferPolicy.Currencies(balance.Balance);
        var party = owner.PredecessorPartyId;
        var account = owner.PredecessorAccountId;
        var held = database.PartyBalanceAssignments.Where(x => x.CurrentPartyId == party && x.CurrentAccountId == account);
        var take = PartyBalanceRules.MaximumFacts + 1;
        var items = await FinancePartyBalanceAssignment.OpenItemsOf(database, party, account).AsNoTracking()
            .Where(x => x.Status == FinanceOpenItemStatuses.Open).OrderBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
        var receipts = await database.ReceiptDrafts.AsNoTracking().Where(x => x.Stage == ReceiptStages.Recorded
                && (x.PartyId == party && x.CompanyAccountId == account
                    || held.Any(a => a.Kind == FinanceBalanceAssignmentKinds.Receipt && a.SourceId == x.Id))
                && !database.ReversalProposals.Any(r => (r.OriginalSourceId == x.Id || r.ReversalSourceId == x.Id) && r.Status == ReversalProposalStatuses.Approved))
            .OrderBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
        var payments = await database.ManualBankPayments.AsNoTracking().Where(x => x.Status == ManualBankPaymentStatuses.Recorded
                && (x.PartyId == party && x.CompanyAccountId == account
                    || held.Any(a => a.Kind == FinanceBalanceAssignmentKinds.Payment && a.SourceId == x.Id))
                && !database.PaymentReversalProposals.Any(r => r.PaymentId == x.Id && r.Status == ReversalProposalStatuses.Approved))
            .OrderBy(x => x.Id).Take(take).ToListAsync(cancellationToken);
        if (items.Count + receipts.Count + payments.Count > PartyBalanceRules.MaximumFacts) return null;
        var receiptIds = receipts.Select(x => x.Id).ToArray();
        var paymentIds = payments.Select(x => x.Id).ToArray();
        var states = await database.SourceAllocationStates.AsNoTracking().Where(x => receiptIds.Contains(x.SourceId))
            .ToDictionaryAsync(x => x.SourceId, cancellationToken);
        var paymentStates = await database.PaymentAllocationStates.AsNoTracking().Where(x => paymentIds.Contains(x.PaymentId))
            .ToDictionaryAsync(x => x.PaymentId, cancellationToken);
        var allIds = items.Select(x => x.Id).Concat(receiptIds).Concat(paymentIds).ToArray();
        var assignments = await database.PartyBalanceAssignments.AsNoTracking().Where(x => allIds.Contains(x.SourceId))
            .OrderBy(x => x.Kind).ThenBy(x => x.SourceId).ThenBy(x => x.CurrentAccountId).ToListAsync(cancellationToken);
        var bySource = assignments.ToLookup(x => (x.Kind, x.SourceId));
        var sources = new List<FinanceBalanceTransferSourceFact>();
        foreach (var item in items)
            Add(FinanceBalanceAssignmentKinds.OpenItem, item.Id, item.PartyId, item.CompanyAccountId, item.CurrencyId,
                item.Version, null, item.OriginalAmountText, item.OutstandingAmountText);
        foreach (var source in receipts)
        {
            states.TryGetValue(source.Id, out var state);
            if (state is not null && (state.CurrencyId != source.CurrencyId
                || !FinanceExactDecimalText.Equal(state.OriginalAmountText, source.AmountText))) return null;
            Add(FinanceBalanceAssignmentKinds.Receipt, source.Id, source.PartyId, source.CompanyAccountId, source.CurrencyId,
                source.Version, state?.Version, source.AmountText, state?.UnallocatedAmountText ?? source.AmountText);
        }
        foreach (var payment in payments)
        {
            paymentStates.TryGetValue(payment.Id, out var state);
            if (state is not null && (state.CurrencyId != payment.CurrencyId
                || !FinanceExactDecimalText.Equal(state.OriginalAmountText, payment.AmountText))) return null;
            Add(FinanceBalanceAssignmentKinds.Payment, payment.Id, payment.PartyId, payment.CompanyAccountId, payment.CurrencyId,
                payment.Version, state?.Version, payment.AmountText, state?.UnallocatedAmountText ?? payment.AmountText);
        }
        var version = FinancePartyBalanceTransferPolicy.SnapshotVersion(owner, currencies, sources);
        return new(new(context.CompanyId, owner, version, currencies,
            sources.Count(x => x.Kind == FinanceBalanceAssignmentKinds.OpenItem),
            sources.Count(x => x.Kind == FinanceBalanceAssignmentKinds.Receipt),
            sources.Count(x => x.Kind == FinanceBalanceAssignmentKinds.Payment),
            sources.Count > 0 || currencies.Any(x => x.Side != PartyLedgerBalanceSide.Zero)), sources);

        void Add(int kind, Guid id, Guid originalParty, Guid originalAccount, Guid currency, long version,
            long? allocationVersion, string originalAmount, string available)
        {
            var rows = bySource[(kind, id)].ToArray();
            var position = new FinanceAllocationPositions(originalParty, originalAccount, currency, available, rows).All()
                .SingleOrDefault(x => x.PartyId == party && x.CompanyAccountId == account);
            if (position is null || FinanceAllocationPositions.Amount(position.AvailableAmount).IsZero) return;
            sources.Add(new(kind, id, originalParty, originalAccount, currency, version, allocationVersion,
                originalAmount, position.AvailableAmount, party, account, position.AssignmentVersion,
                FinancePartyBalanceTransferPolicy.Hash(rows.Select(x => new { x.CurrentPartyId, x.CurrentAccountId,
                    x.AvailableAmountText, x.LastTransferId, x.Version }))));
        }
    }
}
