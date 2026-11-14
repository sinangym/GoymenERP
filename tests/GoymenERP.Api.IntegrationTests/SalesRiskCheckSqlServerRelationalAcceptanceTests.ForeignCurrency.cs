using System.Globalization;
using System.Text.Json;
using GoymenERP.Api.Endpoints;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D47 / SAT-D200 / SAT-D203 (Satış modül bağlantıları 4a): a customer's receivable in a foreign currency reaches the risk check
/// through the real Finance port in its own currency, and the check asks the REAL Settings exchange rate resolver of the host
/// composition for the check day's TCMB rate into TL. No rate is entered here, so the resolver answers "kur bulunamadı" and the
/// foreign amount is never converted with an invented rate; the resolver answers the authenticated member (no 403) and the read
/// writes nothing in Settings or any other module.
/// <para>
/// Limit on main: "shipped but not invoiced" (S5) and the credit limit have no owner, so the local and limit-currency totals are
/// not computed in any case and a successful conversion is not observable through the route yet. SENTETIK / NOT_ENV.
/// </para>
/// </summary>
public sealed partial class SalesRiskCheckSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task A_foreign_currency_receivable_reaches_the_check_in_its_own_currency_and_asks_the_real_settings_rate_resolver_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var usd = ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD");
        var customer = new Customer(Guid.NewGuid(), Guid.NewGuid());
        await run.AdmitOpenItemsAsync(CompanyA, customer, "1000.00");
        await run.AdmitOpenItemsInAsync(CompanyA, customer, usd, "250.00");
        var draft = await run.ApprovedDraftAsync(CompanyA, customer, "fx", "4");

        var reader = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead,
            SalesPermissionManifestV1.OrdersPriceRead);
        using var client = run.Client(reader, CompanyA);
        var before = await run.EffectsAsync();
        var ratesBefore = await RateRowsAsync(run);

        var check = await run.RiskCheckAsync(client, $"{SalesEndpoints.DraftsPath}/{draft.Id:D}/risk-check");

        Assert.Equal(("Unavailable", "Visible"), (check.GetProperty("result").GetString(), check.GetProperty("amountDisclosure").GetString()));
        // The decision's reasons are the missing owners only: the local (TL) total is information only (SAT-D80), so the missing
        // rate never becomes a decision reason. Nothing is shipped, so "shipped but not invoiced" (plan S0) is an available zero.
        Assert.Equal(
            [("ApprovedUnshippedRemainder", "vat_inclusive_amount_unavailable"), ("CreditLimit", "finance_credit_limit_owner_missing")],
            check.GetProperty("reasons").EnumerateArray().Select(reason => (reason.GetProperty("component").GetString(), reason.GetProperty("code").GetString())));

        // The open receivables: one amount per currency, each in its own currency — the USD amount is not converted to TL.
        var receivables = check.GetProperty("components")[0];
        Assert.Equal(("OpenReceivables", "finance.open-items", "Available", "IncludingTax"),
            (receivables.GetProperty("kind").GetString(), receivables.GetProperty("source").GetString(),
                receivables.GetProperty("state").GetString(), receivables.GetProperty("taxBasis").GetString()));
        var amounts = receivables.GetProperty("amounts").EnumerateArray()
            .ToDictionary(amount => Guid.Parse(amount.GetProperty("currencyId").GetString()!),
                amount => decimal.Parse(amount.GetProperty("amount").GetString()!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture));
        Assert.Equal(2, amounts.Count);
        Assert.Equal(1000m, amounts[Currency]);
        Assert.Equal(250m, amounts[usd]);
        var unbilled = check.GetProperty("components")[1];
        Assert.Equal(("ShippedNotInvoiced", "sales.unbilled-shipments", "Available", 0),
            (unbilled.GetProperty("kind").GetString(), unbilled.GetProperty("source").GetString(),
                unbilled.GetProperty("state").GetString(), unbilled.GetProperty("amounts").GetArrayLength()));

        var local = check.GetProperty("inLocalCurrency");
        Assert.Equal((Currency.ToString("D"), JsonValueKind.Null, JsonValueKind.Null), (local.GetProperty("currencyId").GetString(),
            local.GetProperty("grossRisk").ValueKind, local.GetProperty("grossRiskWithThisOrder").ValueKind));
        Assert.Equal(JsonValueKind.Null, check.GetProperty("inLimitCurrency").ValueKind);

        // The rate read writes nothing: no rate record or rate day appears, and no module effect.
        Assert.Equal(ratesBefore, await RateRowsAsync(run));
        Assert.Equal(before, await run.EffectsAsync());
    }

    private static async Task<(int Records, int Days)> RateRowsAsync(Run run) => (
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ExchangeRateRecords]"),
        await run.ScalarAsync<int>("SELECT COUNT(*) FROM [settings].[ExchangeRateDays]"));
}
