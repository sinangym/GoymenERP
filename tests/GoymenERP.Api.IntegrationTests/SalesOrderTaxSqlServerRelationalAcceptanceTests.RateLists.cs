using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// SAT-D181 (Satış modül bağlantıları 4d): the tax options route reads the VAT rate and withholding ratio lists through the REAL
/// Settings rate list port of the host composition. The reviewed catalog is empty until Sinan / the müşavir enter it (SAT-D148), so
/// the real port answers <c>Unresolved</c> with the Settings reason <c>rate_list_missing</c> — never the Sales fallback
/// <c>provider_unavailable</c> that a missing port would give — and offers no rate, in every company. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class SalesOrderTaxSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task The_tax_options_read_the_vat_rate_and_withholding_ratio_lists_through_the_real_settings_port_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var readerOfA = await run.GrantAsync(TestUsers.CompanyA, CompanyA, SalesPermissionManifestV1.DraftsRead);
        var readerOfB = await run.GrantAsync(TestUsers.CompanyB, CompanyB, SalesPermissionManifestV1.DraftsRead);

        foreach (var (subject, company) in new[] { (readerOfA, CompanyA), (readerOfB, CompanyB) })
        {
            using var client = run.Client(subject, company);
            using var response = await client.GetAsync($"{SalesDraftTaxApiV1.TaxOptionsPath}?on={Day:yyyy-MM-dd}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var options = await response.Content.ReadFromJsonAsync<JsonElement>();

            Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxRateListReasonsV1.RateListMissing),
                (options.GetProperty("vatRatesState").GetString(), options.GetProperty("vatRatesReason").GetString()));
            Assert.Equal(0, options.GetProperty("vatRates").GetArrayLength());
            Assert.Equal((TaxResolutionStatesV1.Unresolved, TaxRateListReasonsV1.RateListMissing),
                (options.GetProperty("withholdingRatiosState").GetString(), options.GetProperty("withholdingRatiosReason").GetString()));
            Assert.Equal(0, options.GetProperty("withholdingRatios").GetArrayLength());
            Assert.NotEqual(TaxMasterProblemCodesV1.ProviderUnavailable, options.GetProperty("vatRatesReason").GetString());
        }
    }
}
