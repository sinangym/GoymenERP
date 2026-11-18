using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>The recorded voucher's two party-ledger sides; future original-document corrections are not redirected.</summary>
internal static class FinancePartyBalanceTransferLedger
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    public static async Task<IReadOnlyList<PartyLedgerFact>?> ReadAsync(FinanceDbContext database,
        Guid partyId, Guid accountId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var query = database.PartyBalanceTransfers.AsNoTracking().Where(x =>
            x.PredecessorPartyId == partyId && x.PredecessorAccountId == accountId
            || x.SuccessorPartyId == partyId && x.SuccessorAccountId == accountId);
        if (from.HasValue) query = query.Where(x => x.OccurredOn >= from.Value);
        if (to.HasValue) query = query.Where(x => x.OccurredOn <= to.Value);
        var rows = await query.OrderBy(x => x.OccurredOn).ThenBy(x => x.Id)
            .Take(PartyBalanceRules.MaximumFacts + 1).ToListAsync(cancellationToken);
        if (rows.Count > PartyBalanceRules.MaximumFacts) return null;
        var facts = new List<PartyLedgerFact>();
        try
        {
            foreach (var row in rows)
            {
                var currencies = JsonSerializer.Deserialize<List<FinancePartyBalanceTransferCurrencyV1>>(row.CurrenciesJson, Json);
                if (currencies is null || currencies.Select(x => x.CurrencyId).Distinct().Count() != currencies.Count) return null;
                foreach (var currency in currencies)
                {
                    if (currency.CurrencyId == Guid.Empty || !Enum.IsDefined(currency.Side)
                        || !FinanceExactDecimalText.TryParseNonNegative(currency.Amount, out var amount)
                        || (currency.Side == PartyLedgerBalanceSide.Zero) != amount.IsZero) return null;
                    if (amount.IsZero) continue;
                    var sourceSide = row.PredecessorAccountId == accountId;
                    var debit = (currency.Side == PartyLedgerBalanceSide.Debit) != sourceSide;
                    facts.Add(new PartyLedgerFact(debit ? PartyLedgerEntryKind.BalanceTransferDebit : PartyLedgerEntryKind.BalanceTransferCredit,
                        row.Id, row.OccurredOn, row.RecordedAtUtc, currency.CurrencyId, currency.Amount, row.Id, null, null, null, null));
                }
            }
        }
        catch (JsonException) { return null; }
        return facts;
    }
}
