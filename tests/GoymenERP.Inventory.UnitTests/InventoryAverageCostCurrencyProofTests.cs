using System.Text.Json;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Inventory.UnitTests;

public sealed class InventoryAverageCostCurrencyProofTests
{
    [Fact]
    public void A_legacy_base_currency_description_does_not_invent_a_currency_identity()
    {
        var answer = InventoryCompanyWideAverageCostReadAnswerV1.ForValue(Guid.NewGuid(), Guid.NewGuid(), 12m,
            [new(Guid.NewGuid(), 10m, 1)]);
        Assert.Equal("company-base-currency", answer.CostCurrencyBasis);
        Assert.Null(answer.CostCurrencyId);
        var unit = InventoryAverageCostUnitReadAnswerV1.ForKilogram(answer, Guid.NewGuid());
        Assert.Null(unit.CostCurrencyId);
        Assert.False(JsonSerializer.SerializeToElement(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .TryGetProperty("costCurrencyId", out _)); // Old unproven wire shape stays readable.
    }

    [Fact]
    public void A_proven_original_currency_survives_physical_unit_conversion_without_changing_money()
    {
        var answer = InventoryCompanyWideAverageCostReadAnswerV1.ForValue(Guid.NewGuid(), Guid.NewGuid(), 12m,
            [new(Guid.NewGuid(), 10m, 1)], SettingsCurrencyIdentitiesV1.TurkishLira);
        var unit = InventoryAverageCostUnitReadAnswerV1.ForKilogram(answer, Guid.NewGuid());
        Assert.Equal(SettingsCurrencyIdentitiesV1.TurkishLira, answer.CostCurrencyId);
        Assert.Equal(answer.CostCurrencyId, unit.CostCurrencyId);
        Assert.Equal(12m, unit.CostPerUnit);
        Assert.Throws<ArgumentException>(() => InventoryCompanyWideAverageCostReadAnswerV1.ForValue(Guid.NewGuid(), Guid.NewGuid(),
            12m, [new(Guid.NewGuid(), 10m, 1)], Guid.Empty));
    }
}
