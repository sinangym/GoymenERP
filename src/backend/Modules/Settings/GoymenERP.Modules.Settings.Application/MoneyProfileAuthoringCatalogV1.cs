using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Modules.Settings.Application;

/// <summary>URN-D2/D3/D4 authoring targets, not active monetary values. A first profile needs
/// an authorized human's scales, effective date, active-state choice and reason. No seed is written.
/// Finance's independent company bindings/STOP and grants are not changed by these Settings targets.</summary>
public static class MoneyProfileAuthoringCatalogV1
{
    public const string CreditPlanOperation = "finance.credit-plan";
    public const string CashTransferOperation = "finance.cash-transfer.v1";
    // FNS-D48/FNS-D55: the expense payment's own profile row, authored apart from the manual bank payment's.
    public const string ExpensePaymentOperation = "finance.expense-payment.v1";
    public static IReadOnlyList<MoneyCurrencyCandidateV1> Currencies { get; } = Array.AsReadOnly(new[]
    {
        new MoneyCurrencyCandidateV1(SettingsCurrencyIdentitiesV1.TurkishLira, "TRY", "Türk lirası"),
        new MoneyCurrencyCandidateV1(SettingsCurrencyIdentitiesV1.UnitedStatesDollar, "USD", "ABD doları"),
        new MoneyCurrencyCandidateV1(SettingsCurrencyIdentitiesV1.Euro, "EUR", "Avro"),
    });

    public static string? CurrencyCode(Guid currency) => Currencies.SingleOrDefault(row => row.CurrencyId == currency)?.Code;

    public static IReadOnlyList<(Guid CurrencyId, string Operation)> Targets(Guid company) =>
        Operations(company).SelectMany(operation => Currencies.Select(currency => (currency.CurrencyId, operation))).ToArray();

    public static bool Allows(Guid company, Guid currency, string operation) => company != Guid.Empty
        && CurrencyCode(currency) is not null && Operations(company).Contains(operation, StringComparer.Ordinal);

    private static string[] Operations(Guid company)
    {
        var entries = ApprovedMoneyProfileCatalog.Approved.Entries.Where(entry => entry.CompanyId == company).ToArray();
        if (entries.Length == 0) return [];
        // Previously registered operations remain company-specific (including the SAT-D18 boundary).
        // Credit is a new source contract; only a company already approved for Finance monetary operations
        // gets its empty authoring target. A profile still has no value until a human records it.
        var operations = entries.SelectMany(entry => entry.Operations).Distinct(StringComparer.Ordinal);
        if (operations.Any(operation => operation.StartsWith("finance.", StringComparison.Ordinal)))
            operations = operations.Append(CreditPlanOperation).Append(CashTransferOperation).Append(ExpensePaymentOperation);
        return operations.Order(StringComparer.Ordinal).ToArray();
    }
}
