using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class InventorySqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Company_average_currency_is_proven_by_the_actual_TRY_pools_and_never_backfilled_from_a_legacy_ledger()
    {
        await using var run = await Run.StartAsync();
        var location = await run.LocationOfAsync(CompanyA);
        await CostedLabelAsync(run, location, 100m, 15m, 2m); // Real receipt/stamp/pool; explicitly synthetic price proof input.
        await using var database = run.Context(CompanyA);
        var company = new CompanyContext(); company.Set(CompanyA);
        var reader = new InventoryCompanyWideAverageCostReadPort(database, company);
        var proven = await reader.ReadCompanyWideAsync(Product);
        Assert.Equal(InventoryAverageCostReadStatusV1.Value, proven.Status);
        Assert.Equal(SettingsCurrencyIdentitiesV1.TurkishLira, proven.CostCurrencyId);
        Assert.Equal(15m, proven.AverageCostPerKg);

        // Isolated legacy-data fault. No production writer deletes or fills historical currency evidence.
        await run.ExecuteAsync("DELETE FROM inventory.ExactCostPools WHERE CompanyId=@c AND ProductId=@p AND CurrencyId=@currency",
            ("@c", CompanyA), ("@p", Product), ("@currency", SettingsCurrencyIdentitiesV1.TurkishLira));
        var legacy = await reader.ReadCompanyWideAsync(Product);
        Assert.Equal(InventoryAverageCostReadStatusV1.Value, legacy.Status); // Existing numerical read semantics are preserved.
        Assert.Equal(15m, legacy.AverageCostPerKg);
        Assert.Null(legacy.CostCurrencyId); // Legacy description is insufficient for money/margin admission.
        Assert.Equal("company-base-currency", legacy.CostCurrencyBasis);
    }
}
