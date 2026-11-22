using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

/// <summary>The treasury account kind whose ledger balance is read (AI-D43).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FinanceTreasuryAccountKindV1>))]
public enum FinanceTreasuryAccountKindV1
{
    Bank = 1,
    Cash = 2,
}

/// <summary>
/// One treasury account's balance from the Finance internal treasury ledger (FIN-SC36 / FNS-D102; AI-D43: "bakiye finans
/// defterindeki hareketlerden gelir"). <see cref="Balance"/> is exact decimal text (<c>.</c> separator, a leading
/// <c>-</c> only if the ledger's outgoing exceeds its incoming), in the account's own currency; never rounded, converted
/// or summed across accounts. <see cref="ThroughDate"/> is null for the balance of every recorded fact, or the business
/// date the balance was read through (inclusive).
/// </summary>
public sealed record FinanceTreasuryBalanceViewV1(
    Guid CompanyId,
    FinanceTreasuryAccountKindV1 Kind,
    Guid AccountId,
    Guid CurrencyId,
    DateOnly? ThroughDate,
    string Balance);

/// <summary>
/// AI-D43: the authorized read of a treasury account's ledger balance. It measures
/// <c>finance.treasury-balance.read</c> (registered, granted to no role) for the server's actor and company before and
/// after the ledger read; another company's or an unknown account is NotFound; a ledger that cannot be told (a fact in
/// another currency, an unreadable amount) is Unavailable — never a zero. It writes nothing.
/// </summary>
public interface IFinanceTreasuryBalanceReadV1
{
    Task<ReadResult<FinanceTreasuryBalanceViewV1>> ReadAuthorizedAsync(
        FinanceContext context,
        FinanceTreasuryAccountKindV1 kind,
        Guid accountId,
        DateOnly? throughDate,
        CancellationToken cancellationToken);
}
