using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Api.IntegrationTests;

public sealed class InventorySalesMarginCostReadPortV1Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Margin_adapter_preserves_real_currency_proof_and_never_defaults_legacy_cost_to_TL(bool proven)
    {
        var company = Guid.NewGuid(); var product = Guid.NewGuid(); var unit = Guid.NewGuid();
        var context = new CompanyContext(); context.Set(company);
        var owner = new CostOwner(InventoryAverageCostUnitReadAnswerV1.ForKilogram(
            InventoryCompanyWideAverageCostReadAnswerV1.ForValue(company, product, 12.3456m,
                [new(Guid.NewGuid(), 40m, 17)], proven ? SettingsCurrencyIdentitiesV1.TurkishLira : null), unit));
        var answer = await new InventorySalesMarginCostReadPortV1(context, owner).ReadAsync(company, product, unit, new(2026, 11, 17));
        Assert.Equal(proven ? SettingsCurrencyIdentitiesV1.TurkishLira : null, answer.CurrencyId);
        Assert.Equal(proven ? 12.3456m : null, answer.CostPerUnit);
        Assert.Equal(proven ? null : SalesLineMarginUnavailableReason.CurrencyConversionUndecided, answer.UnavailableReason);
        Assert.Contains("ledgerVersion", answer.SourceProof, StringComparison.Ordinal);
        Assert.Contains("17", answer.SourceProof, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Margin_adapter_refuses_an_owner_answer_from_another_company()
    {
        var company = Guid.NewGuid(); var product = Guid.NewGuid(); var unit = Guid.NewGuid();
        var context = new CompanyContext(); context.Set(company);
        var owner = new CostOwner(InventoryAverageCostUnitReadAnswerV1.ForUnconvertible(
            InventoryCompanyWideAverageCostReadAnswerV1.ForNoSource(Guid.NewGuid(), product), unit));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new InventorySalesMarginCostReadPortV1(context, owner)
            .ReadAsync(company, product, unit, new(2026, 11, 17)));
    }

    private sealed class CostOwner(InventoryAverageCostUnitReadAnswerV1 answer) : IInventoryAverageCostUnitReadPortV1
    {
        public Task<InventoryAverageCostUnitReadAnswerV1> ReadCompanyWideInUnitAsync(Guid productId, Guid unitId,
            DateOnly policyDate, CancellationToken cancellationToken = default) => Task.FromResult(answer);
    }
}
