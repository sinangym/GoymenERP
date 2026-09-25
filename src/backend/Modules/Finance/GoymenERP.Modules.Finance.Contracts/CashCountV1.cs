namespace GoymenERP.Modules.Finance.Contracts;

public enum CashCountDifferenceDirectionV1
{
    Balanced = 1,
    Over = 2,
    Short = 3
}

public sealed record PrepareCashCountV1(
    Guid CountId,
    Guid TreasuryAccountId,
    MoneyText BookBalance,
    MoneyText CountedBalance,
    string? DifferenceReason);

public sealed record CashCountDraftV1(
    Guid CountId,
    Guid CompanyId,
    Guid TreasuryAccountId,
    Guid CurrencyId,
    MoneyText BookBalance,
    MoneyText CountedBalance,
    MoneyText Difference,
    CashCountDifferenceDirectionV1 Direction,
    string? DifferenceReason,
    Guid CountedBy,
    DateTimeOffset CountedAtUtc,
    int Version,
    bool RequiresApproval,
    bool CreatesMoneyEvent);
