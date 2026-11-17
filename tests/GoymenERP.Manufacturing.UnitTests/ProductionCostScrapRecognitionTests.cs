using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed class ProductionCostScrapRecognitionTests
{
    [Fact]
    public void The_approved_scrap_in_cost_golden_records_scrap_stock_value_instead_of_hiding_it_in_the_good_product_rounding_remainder()
    {
        var good = Guid.NewGuid(); var remnant = Guid.NewGuid(); var scrap = Guid.NewGuid();
        var input = new ProductionReceiptCostInput(true,
            [new(Guid.NewGuid(), new Kilograms(1000m), IssueCostReadStatus.Value, 40m, IssueCostCurrencyInput.FunctionalCurrency)], [],
            new Kilograms(900m), Kilograms.Zero, new Kilograms(50m),
            [new(scrap, new Kilograms(50m), new(ScrapPriceReadStatus.Value, 10m))], Kilograms.Zero, ScrapCostSettingStatus.On, NonKgInputUse.None);
        var result = ProductionCostRoundingCalculatorV1.Round(new(input,
            [new(good, "GOOD-1", OutputClass.GoodProduct, new Kilograms(900m)), new(remnant, "REMNANT-1", OutputClass.UsableRemnant, new Kilograms(50m))]));
        Assert.Equal(ProductionReceiptCostStatus.Value, result.Status);
        Assert.Equal(41.6667m, result.RecordedCostPerKg);
        Assert.Equal(37500m, Assert.Single(result.Lines, row => row.OutputLabelId == good).RecordedAmountTl);
        Assert.Equal(2000m, Assert.Single(result.Lines, row => row.OutputLabelId == remnant).RecordedAmountTl);
        var scrapLine = Assert.Single(result.Lines, row => row.OutputLabelId == scrap);
        Assert.Equal((10m, 500m, 0m), (scrapLine.RecordedUnitCostPerKg!.Value, scrapLine.RecordedAmountTl!.Value, scrapLine.RemainderTl));
        Assert.Equal(40000m, result.DistributedTotalTl);
        Assert.Equal(0m, result.RemainderTl);
    }
}
