using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Finance.Contracts;

namespace GoymenERP.Modules.Finance.Infrastructure;

internal sealed record FinanceBalanceTransferSourceFact(
    int Kind, Guid SourceId, Guid OriginalPartyId, Guid OriginalAccountId, Guid CurrencyId,
    long SourceVersion, long? AllocationVersion, string OriginalAmount, string AvailableAmount,
    Guid CurrentPartyId, Guid CurrentAccountId, long AssignmentVersion, string? PositionFingerprint = null);

/// <summary>Exact existing ledger amounts only; the transfer neither values money nor calculates an exchange difference.</summary>
internal static class FinancePartyBalanceTransferPolicy
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<FinancePartyBalanceTransferCurrencyV1> Currencies(PartyBalanceView balance)
    {
        if (balance.Currencies.Select(x => x.CurrencyId).Distinct().Count() != balance.Currencies.Count)
            throw new InvalidOperationException("Bakiye para birimi yineleniyor.");
        return balance.Currencies.OrderBy(x => x.CurrencyId).Select(currency =>
        {
            var amount = Visible(currency.Balance.Amount, currency.CurrencyId);
            var receivable = Visible(currency.OpenReceivable, currency.CurrencyId);
            var payable = Visible(currency.OpenPayable, currency.CurrencyId);
            if (currency.CurrencyId == Guid.Empty || currency.Balance.Side is not { } side || !Enum.IsDefined(side)
                || (side == PartyLedgerBalanceSide.Zero) != Parse(amount).IsZero)
                throw new InvalidOperationException("Bakiye yönü ve kaynak tutarı uyuşmuyor.");
            return new FinancePartyBalanceTransferCurrencyV1(currency.CurrencyId, side, amount, receivable, payable);
        }).ToArray();
    }

    public static string SnapshotVersion(FinancePartyBalanceTransferOwnerProofV1 owner,
        IReadOnlyList<FinancePartyBalanceTransferCurrencyV1> currencies,
        IReadOnlyList<FinanceBalanceTransferSourceFact> sources)
    {
        if (sources.Select(x => (x.Kind, x.SourceId)).Distinct().Count() != sources.Count
            || sources.Any(x => x.SourceId == Guid.Empty || x.SourceVersion < 1 || x.AssignmentVersion < 0
                || x.CurrentPartyId != owner.PredecessorPartyId || x.CurrentAccountId != owner.PredecessorAccountId
                || x.Kind is < 1 or > 3))
            throw new InvalidOperationException("Bakiye devir kaynak kimliği okunamadı.");
        foreach (var source in sources) { Parse(source.OriginalAmount); Parse(source.AvailableAmount); }
        var json = JsonSerializer.Serialize(new
        {
            owner,
            currencies = currencies.OrderBy(x => x.CurrencyId),
            sources = sources.OrderBy(x => x.Kind).ThenBy(x => x.SourceId),
        }, Json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    internal static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json))));

    private static string Visible(AmountView amount, Guid currencyId) =>
        amount.Disclosure == AmountDisclosure.Visible && amount.Money is { } money && money.CurrencyId == currencyId
            ? Parse(money.Amount).Text : throw new InvalidOperationException("Bakiye tutarı görünür değil; devir yapılmaz.");

    private static FinanceExactDecimalText Parse(string value) =>
        FinanceExactDecimalText.TryParseNonNegative(value, out var amount)
            ? amount : throw new InvalidOperationException("Kaynak bakiye tutarı okunamadı.");
}
