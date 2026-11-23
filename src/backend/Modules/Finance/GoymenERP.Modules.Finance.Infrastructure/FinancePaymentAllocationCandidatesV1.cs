using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

public sealed class FinancePaymentAllocationCandidatesV1(FinanceDbContext database, ICompanyContext company,
    ICurrentUser user, IFinancePermissionPort permissions) : IFinancePaymentAllocationCandidatesV1
{
    private const int PageSize = 50;
    public async Task<ReadResult<FinancePaymentAllocationCandidatesViewV1>> ReadAsync(FinanceContext context,
        Guid paymentId, int pageIndex, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, cancellationToken) is { } denied) return new ReadResult<FinancePaymentAllocationCandidatesViewV1>(denied, null);
        if (paymentId == Guid.Empty || pageIndex is < 0 or > 100_000) return new ReadResult<FinancePaymentAllocationCandidatesViewV1>(ReadOutcome.NotFound, null);
        var payment = await database.ManualBankPayments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == paymentId, cancellationToken);
        if (await FenceAsync(context, cancellationToken) is { } first) return new ReadResult<FinancePaymentAllocationCandidatesViewV1>(first, null);
        if (payment is null || payment.Status != ManualBankPaymentStatuses.Recorded) return new ReadResult<FinancePaymentAllocationCandidatesViewV1>(ReadOutcome.NotFound, null);
        var reversed = await database.OutgoingMoneyEventReversals.AsNoTracking().AnyAsync(x => x.PaymentId == paymentId, cancellationToken);
        if (await FenceAsync(context, cancellationToken) is { } afterReversal) return new ReadResult<FinancePaymentAllocationCandidatesViewV1>(afterReversal, null);
        if (reversed) return new ReadResult<FinancePaymentAllocationCandidatesViewV1>(ReadOutcome.NotFound, null);
        var state = await database.PaymentAllocationStates.AsNoTracking().SingleOrDefaultAsync(x => x.PaymentId == paymentId, cancellationToken);
        if (await FenceAsync(context, cancellationToken) is { } afterState) return new ReadResult<FinancePaymentAllocationCandidatesViewV1>(afterState, null);
        try
        {
            if (state is not null && (state.CurrencyId != payment.CurrencyId
                || !FinanceExactDecimalText.Equal(state.OriginalAmountText, payment.AmountText))) return new ReadResult<FinancePaymentAllocationCandidatesViewV1>(ReadOutcome.Unavailable, null);
            var available = await FinancePartyBalanceAssignment.ReadAsync(database, FinanceBalanceAssignmentKinds.Payment,
                paymentId, payment.PartyId, payment.CompanyAccountId, payment.CurrencyId,
                state?.UnallocatedAmountText ?? payment.AmountText, false, cancellationToken);
            var positions = available.All();
            var accounts = positions.Where(x => !FinanceAllocationPositions.Amount(x.AvailableAmount).IsZero)
                .Select(x => x.CompanyAccountId).ToArray();
            var items = await database.OpenItems.AsNoTracking().Where(x => x.Direction == FinanceOpenItemDirectionCodes.Payable
                    && x.Status == FinanceOpenItemStatuses.Open && x.CurrencyId == payment.CurrencyId
                    && (accounts.Contains(x.CompanyAccountId) || database.PartyBalanceAssignments.Any(a =>
                        a.Kind == FinanceBalanceAssignmentKinds.OpenItem && a.SourceId == x.Id && accounts.Contains(a.CurrentAccountId))))
                .OrderBy(x => x.Id).Skip(checked(pageIndex * PageSize)).Take(PageSize + 1).ToArrayAsync(cancellationToken);
            var expanded = await FinancePartyBalanceAssignment.OpenPositionsAsync(database, items.Take(PageSize).ToArray(), cancellationToken);
            var candidates = expanded.Where(x => positions.Any(position => position.PartyId == x.Balance.PartyId
                    && position.CompanyAccountId == x.Balance.CompanyAccountId && !FinanceAllocationPositions.Amount(position.AvailableAmount).IsZero))
                .GroupBy(x => x.Source.Id).Select(group =>
                {
                    var item = group.First().Source;
                    return new FinancePaymentAllocationCandidateItemV1(item.Id, item.Version.ToString(CultureInfo.InvariantCulture),
                        item.SourceKind, item.SourceId, item.DueOn, item.PartyId, item.CompanyAccountId,
                        group.Select(x => View(x.Balance)).ToArray());
                }).ToArray();
            var allocations = await database.PaymentAllocations.AsNoTracking().Where(x => x.PaymentId == paymentId)
                .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip(checked(pageIndex * PageSize))
                .Take(PageSize + 1).ToArrayAsync(cancellationToken);
            if (await FenceAsync(context, cancellationToken) is { } late) return new ReadResult<FinancePaymentAllocationCandidatesViewV1>(late, null);
            return new ReadResult<FinancePaymentAllocationCandidatesViewV1>(ReadOutcome.Found, new FinancePaymentAllocationCandidatesViewV1(context.CompanyId, paymentId, payment.Version.ToString(CultureInfo.InvariantCulture),
                payment.CurrencyId, payment.PartyId, payment.CompanyAccountId, positions.Select(View).ToArray(), candidates,
                pageIndex, items.Length > PageSize, allocations.Take(PageSize).Select(FinancePaymentAllocationService.View).ToArray(),
                allocations.Length > PageSize));
        }
        catch (InvalidOperationException) { return new ReadResult<FinancePaymentAllocationCandidatesViewV1>(ReadOutcome.Unavailable, null); }
    }
    private async Task<ReadOutcome?> FenceAsync(FinanceContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Same()) return ReadOutcome.Denied;
        foreach (var action in new[] { FinanceAction.ReadAllocation, FinanceAction.ReadManualBankPayments, FinanceAction.ReadPartyLedgerAmount })
        {
            var answer = await permissions.CheckAsync(context, action, null, token);
            if (!Same()) return ReadOutcome.Denied;
            if (answer != PermissionDecision.Allowed) return answer == PermissionDecision.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable;
        }
        return null;
        bool Same() => company.IsResolved && company.CompanyId == context.CompanyId && user.IsAuthenticated
            && user.UserId == context.ActorId && user.CanAccessCompany(context.CompanyId);
    }
    private static FinanceAllocationPositionV1 View(FinanceAllocationPosition position) => new FinanceAllocationPositionV1(position.PartyId,
        position.CompanyAccountId, position.AvailableAmount, position.AssignmentVersion.ToString(CultureInfo.InvariantCulture), position.TransferId);
}
