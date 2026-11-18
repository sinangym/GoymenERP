using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

internal sealed record FinanceAllocationPosition(Guid PartyId, Guid CompanyAccountId, string AvailableAmount,
    long AssignmentVersion, Guid? TransferId);

/// <summary>
/// Only the amount explicitly transferred is assigned. Later source increases stay with the original account.
/// Existing historical financial facts are never rewritten; there is no account-wide successor alias.
/// </summary>
internal sealed class FinanceAllocationPositions(Guid originalPartyId, Guid originalAccountId, Guid currencyId,
    string totalAvailable, IReadOnlyList<FinancePartyBalanceAssignmentRow> assignments)
{
    public IReadOnlyList<FinanceAllocationPosition> All()
    {
        var total = Amount(totalAvailable);
        var transferred = new FinanceExactDecimalText(0, total.Scale);
        if (assignments.Select(x => x.CurrentAccountId).Distinct().Count() != assignments.Count)
            throw new InvalidOperationException("Bakiye tahsis hesabı yineleniyor.");
        foreach (var row in assignments)
        {
            if (row.OriginalPartyId != originalPartyId || row.OriginalAccountId != originalAccountId
                || row.CurrentPartyId == Guid.Empty || row.CurrentAccountId == Guid.Empty
                || row.CurrentAccountId == originalAccountId || row.CurrencyId != currencyId || row.Version < 1)
                throw new InvalidOperationException("Bakiye tahsis kimliği kaynakla uyuşmuyor.");
            transferred = transferred.Add(Amount(row.AvailableAmountText));
        }
        if (transferred.CompareTo(total) > 0)
            throw new InvalidOperationException("Devredilen kullanılabilir tutar kaynak tutarını aşıyor.");
        return new[] { new FinanceAllocationPosition(originalPartyId, originalAccountId,
            total.Subtract(transferred).Text, 0, null) }.Concat(assignments.OrderBy(x => x.CurrentAccountId)
            .Select(x => new FinanceAllocationPosition(x.CurrentPartyId, x.CurrentAccountId,
                x.AvailableAmountText, x.Version, x.LastTransferId))).ToArray();
    }

    public FinanceAllocationPosition Select(Guid? partyId, Guid? accountId)
    {
        if (partyId.HasValue != accountId.HasValue || partyId == Guid.Empty || accountId == Guid.Empty)
            throw new FinanceAllocationPolicyException("allocationAccountId", "finance_allocation_reference_invalid");
        var selected = All().SingleOrDefault(x => x.PartyId == (partyId ?? originalPartyId)
            && x.CompanyAccountId == (accountId ?? originalAccountId));
        return selected ?? throw new FinanceAllocationPolicyException("allocationAccountId", "finance_allocation_reference_invalid");
    }

    public static void RequireAvailable(FinanceAllocationPosition position, string value)
    {
        var amount = Amount(value);
        if (amount.IsZero || amount.CompareTo(Amount(position.AvailableAmount)) > 0)
            throw new FinanceAllocationPolicyException("amount", "finance_allocation_exceeds_transferred_amount");
    }

    public void Spend(FinanceAllocationPosition position, string value)
    {
        RequireAvailable(position, value);
        var amount = Amount(value);
        var row = assignments.SingleOrDefault(x => x.CurrentAccountId == position.CompanyAccountId);
        if (row is null) return;
        row.AvailableAmountText = Amount(row.AvailableAmountText).Subtract(amount).Text;
        row.Version = checked(row.Version + 1);
    }

    internal static FinanceExactDecimalText Amount(string value) =>
        FinanceExactDecimalText.TryParseNonNegative(value, out var amount) ? amount
            : throw new InvalidOperationException("Kaynak tahsis tutarı okunamadı.");
}

internal sealed record FinanceOpenItemPosition(FinanceOpenItemRow Source, FinanceAllocationPosition Balance);

internal static class FinancePartyBalanceAssignment
{
    public static async Task<FinanceAllocationPositions> ReadAsync(FinanceDbContext database, int kind, Guid sourceId,
        Guid originalPartyId, Guid originalAccountId, Guid currencyId, string available,
        bool forWrite, CancellationToken cancellationToken)
    {
        var query = forWrite ? database.PartyBalanceAssignments.AsTracking() : database.PartyBalanceAssignments.AsNoTracking();
        var rows = await query.Where(x => x.Kind == kind && x.SourceId == sourceId).ToListAsync(cancellationToken);
        return new FinanceAllocationPositions(originalPartyId, originalAccountId, currencyId, available, rows);
    }

    public static async Task ClearAsync(FinanceDbContext database, int kind, Guid sourceId, CancellationToken cancellationToken)
    {
        var rows = await database.PartyBalanceAssignments.Where(x => x.Kind == kind && x.SourceId == sourceId)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.AvailableAmountText = FinanceAllocationPositions.Amount(row.AvailableAmountText)
                .Subtract(FinanceAllocationPositions.Amount(row.AvailableAmountText)).Text;
            Touch(database, row);
        }
    }

    // An explicit allocation release restores that allocation's captured beneficiary, not a newer successor.
    public static async Task RestoreAsync(FinanceDbContext database, int kind, Guid sourceId,
        Guid originalAccountId, Guid? beneficiaryAccountId, string value, CancellationToken cancellationToken)
    {
        if (beneficiaryAccountId is null || beneficiaryAccountId == originalAccountId) return;
        var row = await database.PartyBalanceAssignments.SingleOrDefaultAsync(x => x.Kind == kind
            && x.SourceId == sourceId && x.CurrentAccountId == beneficiaryAccountId, cancellationToken)
            ?? throw new InvalidOperationException("Tahsisin kayıtlı devir bağlantısı bulunamadı.");
        row.AvailableAmountText = FinanceAllocationPositions.Amount(row.AvailableAmountText)
            .Add(FinanceAllocationPositions.Amount(value)).Text;
        Touch(database, row);
    }

    private static void Touch(FinanceDbContext database, FinancePartyBalanceAssignmentRow row) =>
        row.Version = checked(database.Entry(row).Property(x => x.Version).OriginalValue + 1);

    public static async Task<IReadOnlyList<FinanceOpenItemPosition>> OpenPositionsAsync(FinanceDbContext database,
        IReadOnlyList<FinanceOpenItemRow> items, CancellationToken cancellationToken)
    {
        var ids = items.Select(x => x.Id).ToArray();
        var assignments = await database.PartyBalanceAssignments.AsNoTracking()
            .Where(x => x.Kind == FinanceBalanceAssignmentKinds.OpenItem && ids.Contains(x.SourceId))
            .ToListAsync(cancellationToken);
        var bySource = assignments.ToLookup(x => x.SourceId);
        return items.SelectMany(item => new FinanceAllocationPositions(item.PartyId, item.CompanyAccountId,
                item.CurrencyId, item.OutstandingAmountText, bySource[item.Id].ToArray()).All()
            .Where(position => !FinanceAllocationPositions.Amount(position.AvailableAmount).IsZero)
            .Select(position => new FinanceOpenItemPosition(item, position))).ToArray();
    }

    public static IQueryable<FinanceOpenItemRow> OpenItemsOf(FinanceDbContext database, Guid partyId, Guid accountId) =>
        database.OpenItems.Where(row => row.PartyId == partyId && row.CompanyAccountId == accountId
            || database.PartyBalanceAssignments.Any(x => x.Kind == FinanceBalanceAssignmentKinds.OpenItem
                && x.SourceId == row.Id && x.CurrentPartyId == partyId && x.CurrentAccountId == accountId));
}
