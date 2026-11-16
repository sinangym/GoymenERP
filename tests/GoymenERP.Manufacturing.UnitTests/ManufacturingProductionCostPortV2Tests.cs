using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// Production cost port, version 2 (URT-D54, URT-D55, URT-D58, URT-D78…URT-D80): the port reads the given company's
/// order once, calls the single rounding calculator and fails closed on anything it cannot trust. A half is rounded away
/// from zero, the one fixed rule (URT-D78): there is no rounding setting to read, and a remainder with no good product
/// line leaves the cost pending (URT-D80).
/// </summary>
public sealed class ManufacturingProductionCostPortV2Tests
{
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("10000000-0000-0000-0000-00000000000b");
    private static readonly Guid OrderA = Guid.Parse("20000000-0000-0000-0000-00000000000a");
    private static readonly Guid OrderB = Guid.Parse("20000000-0000-0000-0000-00000000000b");
    private static readonly Guid IssueA = Guid.Parse("c0000000-0000-0000-0000-00000000000a");
    private static readonly Guid GoodA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid SecondA = Guid.Parse("b0000000-0000-0000-0000-00000000000a");
    private static readonly Guid SecondB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid SecondC = Guid.Parse("b0000000-0000-0000-0000-00000000000c");
    private static readonly Guid ScrapA = Guid.Parse("e0000000-0000-0000-0000-00000000000a");
    private static readonly Guid CurrencyUsd = Guid.Parse("70000000-0000-0000-0000-00000000000a");

    private static ProductionScrapCostDistributionInput Input(
        decimal issueCost = 40m, IssueCostCurrencyInput? currency = null, bool closed = true) =>
        new(new ProductionReceiptCostInput(
                closed,
                [new IssueCostInput(IssueA, new Kilograms(1000m), IssueCostReadStatus.Value, issueCost,
                    currency ?? IssueCostCurrencyInput.FunctionalCurrency)],
                [],
                new Kilograms(900m),
                new Kilograms(50m),
                Kilograms.Zero,
                [new ScrapWeighingInput(ScrapA, new Kilograms(50m), new ScrapPriceInput(ScrapPriceReadStatus.Value, 10m))],
                Kilograms.Zero,
                ScrapCostSettingStatus.Off,
                NonKgInputUse.PaintLitres),
            [new ProductionScrapCostLabelInput(GoodA, "ETK-1", OutputClass.GoodProduct, new Kilograms(900m)),
                new ProductionScrapCostLabelInput(SecondA, "ETK-2", OutputClass.SecondQuality, new Kilograms(50m))]);

    private static ManufacturingScrapCostDistributionSourceReadV1 Found(ProductionScrapCostDistributionInput? input) =>
        new(ManufacturingScrapCostDistributionSourceStatusV1.Found, CompanyA, OrderA, input);

    // Synthetic: scrap 100 kg x 9.9999 TL; 900 kg second quality in three 13000.00333... TL labels leaves 0.01 TL
    // and there is no good product line to carry it.
    private static ProductionScrapCostDistributionInput SecondQualityThirdsInput() =>
        new(new ProductionReceiptCostInput(
                true,
                [new IssueCostInput(IssueA, new Kilograms(1000m), IssueCostReadStatus.Value, 40m,
                    IssueCostCurrencyInput.FunctionalCurrency)],
                [],
                Kilograms.Zero,
                new Kilograms(900m),
                Kilograms.Zero,
                [new ScrapWeighingInput(ScrapA, new Kilograms(100m), new ScrapPriceInput(ScrapPriceReadStatus.Value, 9.9999m))],
                Kilograms.Zero,
                ScrapCostSettingStatus.On,
                NonKgInputUse.None),
            [new ProductionScrapCostLabelInput(SecondA, "ETK-1", OutputClass.SecondQuality, new Kilograms(300m)),
                new ProductionScrapCostLabelInput(SecondB, "ETK-2", OutputClass.SecondQuality, new Kilograms(300m)),
                new ProductionScrapCostLabelInput(SecondC, "ETK-3", OutputClass.SecondQuality, new Kilograms(300m))]);

    [Fact]
    public async Task A_found_order_answers_the_recorded_figures_with_the_tl_in_equal_to_the_tl_distributed()
    {
        // SAT-D326 (1) golden: 41.5789 TL per kg; 900 kg 37421.05 TL, 50 kg 2078.95 TL, scrap expense 500.00 TL.
        var currency = new IssueCostCurrencyInput(IssueCostCurrencyStatus.Converted, CurrencyUsd, 2.5m, 16m);
        var source = new FakeSource(Found(Input(currency: currency)));
        var port = new ManufacturingProductionCostPort(source);

        var result = await port.ReadAsync(CompanyA, OrderA, CancellationToken.None);

        Assert.Equal(2, result.ContractVersion);
        Assert.Equal(ManufacturingScrapCostDistributionStatusV1.Available, result.Status);
        Assert.Equal(CompanyA, result.CompanyId);
        Assert.Equal(OrderA, result.OrderId);
        Assert.Null(result.ReasonCode);
        Assert.Equal(ProductionReceiptCostCodesV1.BasisWithoutScrap, result.CostBasis);
        Assert.Equal(41.5789m, result.RecordedCostPerKgTl);
        Assert.Equal(40m, result.RecordedRawMaterialCostPerKgTl);
        Assert.Equal(40000m, result.InputTotalTl);
        Assert.Equal(500m, result.RecordedWeighedScrapExpenseTl);
        Assert.Equal(40000m, result.DistributedTotalTl);
        Assert.Equal(0m, result.RemainderTl);
        Assert.Null(result.RemainderLabelId);
        Assert.Equal([ProductionReceiptCostCodesV1.PaintCostNotIncluded], result.NoteCodes);
        Assert.Empty(result.IssueReasons);
        Assert.Collection(result.Lines,
            line =>
            {
                Assert.Equal(GoodA, line.OutputLabelId);
                Assert.Equal(41.5789m, line.RecordedUnitCostPerKgTl);
                Assert.Equal(37421.05m, line.RecordedAmountTl);
            },
            line =>
            {
                Assert.Equal(SecondA, line.OutputLabelId);
                Assert.Equal(2078.95m, line.RecordedAmountTl);
            },
            line =>
            {
                Assert.Equal(ScrapA, line.OutputLabelId);
                Assert.Null(line.RecordedAmountTl);
                Assert.Equal(500m, line.RecordedSeparateScrapExpenseTl);
                Assert.Equal(ProductionScrapCostDistributionCodesV1.ScrapLabelCostNotDecided, line.NotAllocatedCode);
            });
        Assert.Equal(
            [new ManufacturingProductionCostCurrencyLineV2(IssueA, CurrencyUsd, 2.5m, 16m, 40m, 1000m, 2500m)],
            result.CurrencyLines);
        Assert.Equal(new[] { (CompanyA, OrderA) }, source.Calls);
    }

    [Fact]
    public async Task A_remainder_with_no_good_product_line_is_pending_with_no_figure()
    {
        // URT-D80: "Emirde iyi urun etiketi yoksa maliyet 'bekliyor' kalir".
        var port = new ManufacturingProductionCostPort(new FakeSource(Found(SecondQualityThirdsInput())));

        var result = await port.ReadAsync(CompanyA, OrderA, CancellationToken.None);

        AssertEmpty(result, ManufacturingScrapCostDistributionStatusV1.Pending,
            ProductionCostRoundingCodesV1.RemainderLineMissing);
    }

    [Fact]
    public async Task An_order_source_that_fails_answers_null_or_another_order_is_unavailable()
    {
        (FakeSource Source, string Code)[] cases =
        [
            (new FakeSource(null, new InvalidOperationException("SENTETIK")),
                ManufacturingScrapCostDistributionCodesV1.SourceUnavailable),
            (new FakeSource(null), ManufacturingScrapCostDistributionCodesV1.SourceUnavailable),
            (new FakeSource(new ManufacturingScrapCostDistributionSourceReadV1(
                    ManufacturingScrapCostDistributionSourceStatusV1.Found, CompanyA, OrderB, Input())),
                ManufacturingScrapCostDistributionCodesV1.SourceScopeMismatch),
            (new FakeSource(new ManufacturingScrapCostDistributionSourceReadV1(
                    ManufacturingScrapCostDistributionSourceStatusV1.Found, CompanyA, OrderA, null)),
                ManufacturingScrapCostDistributionCodesV1.SourceUnavailable),
        ];

        foreach (var (source, code) in cases)
        {
            var port = new ManufacturingProductionCostPort(source);

            AssertEmpty(await port.ReadAsync(CompanyA, OrderA, CancellationToken.None),
                ManufacturingScrapCostDistributionStatusV1.Unavailable, code);
        }
    }

    [Fact]
    public async Task An_order_that_does_not_exist_is_not_found()
    {
        var port = new ManufacturingProductionCostPort(new FakeSource(
            new ManufacturingScrapCostDistributionSourceReadV1(
                ManufacturingScrapCostDistributionSourceStatusV1.NotFound, CompanyA, OrderA, null)));

        AssertEmpty(await port.ReadAsync(CompanyA, OrderA, CancellationToken.None),
            ManufacturingScrapCostDistributionStatusV1.NotFound, ManufacturingScrapCostDistributionCodesV1.OrderNotFound);
    }

    [Fact]
    public async Task An_order_source_answering_another_company_is_unavailable()
    {
        var port = new ManufacturingProductionCostPort(new FakeSource(
            new ManufacturingScrapCostDistributionSourceReadV1(
                ManufacturingScrapCostDistributionSourceStatusV1.Found, CompanyB, OrderA, Input())));

        AssertEmpty(await port.ReadAsync(CompanyA, OrderA, CancellationToken.None),
            ManufacturingScrapCostDistributionStatusV1.Unavailable,
            ManufacturingScrapCostDistributionCodesV1.SourceScopeMismatch);
    }

    [Fact]
    public async Task An_empty_company_or_order_reads_nothing()
    {
        var source = new FakeSource(Found(Input()));
        var port = new ManufacturingProductionCostPort(source);

        AssertEmpty(await port.ReadAsync(Guid.Empty, OrderA, CancellationToken.None),
            ManufacturingScrapCostDistributionStatusV1.Unavailable,
            ManufacturingScrapCostDistributionCodesV1.CompanyRequired);
        AssertEmpty(await port.ReadAsync(CompanyA, Guid.Empty, CancellationToken.None),
            ManufacturingScrapCostDistributionStatusV1.NotFound, ManufacturingScrapCostDistributionCodesV1.OrderRequired);
        Assert.Empty(source.Calls);
    }

    [Fact]
    public async Task A_zero_issue_cost_is_unreadable_and_names_the_issue()
    {
        // URT-D58 (1): the screen names the input whose cost is zero.
        var port = new ManufacturingProductionCostPort(new FakeSource(Found(Input(issueCost: 0m))));

        var result = await port.ReadAsync(CompanyA, OrderA, CancellationToken.None);

        Assert.Equal(ManufacturingScrapCostDistributionStatusV1.Unreadable, result.Status);
        Assert.Equal(ProductionReceiptCostCodesV1.InputCostZero, result.ReasonCode);
        Assert.Equal([new ManufacturingProductionCostIssueReasonV2(IssueA, ProductionReceiptCostCodesV1.InputCostZero)],
            result.IssueReasons);
        Assert.Null(result.InputTotalTl);
        Assert.Empty(result.Lines);
        Assert.Empty(result.CurrencyLines);
    }

    [Fact]
    public async Task An_open_order_is_pending()
    {
        var port = new ManufacturingProductionCostPort(new FakeSource(Found(Input(closed: false))));

        AssertEmpty(await port.ReadAsync(CompanyA, OrderA, CancellationToken.None),
            ManufacturingScrapCostDistributionStatusV1.Pending, ProductionReceiptCostCodesV1.OrderNotClosed);
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var port = new ManufacturingProductionCostPort(
            new FakeSource(null, new OperationCanceledException(cancellation.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => port.ReadAsync(CompanyA, OrderA, cancellation.Token));
    }

    private static void AssertEmpty(ManufacturingProductionCostResultV2 result,
        ManufacturingScrapCostDistributionStatusV1 status, string code)
    {
        Assert.Equal(2, result.ContractVersion);
        Assert.Equal(status, result.Status);
        Assert.Equal(code, result.ReasonCode);
        Assert.Null(result.CostBasis);
        Assert.Null(result.RecordedCostPerKgTl);
        Assert.Null(result.RecordedRawMaterialCostPerKgTl);
        Assert.Null(result.InputTotalTl);
        Assert.Null(result.RecordedWeighedScrapExpenseTl);
        Assert.Null(result.DistributedTotalTl);
        Assert.Null(result.RemainderTl);
        Assert.Null(result.RemainderLabelId);
        Assert.Empty(result.Lines);
        Assert.Empty(result.CurrencyLines);
        Assert.Empty(result.IssueReasons);
        Assert.Empty(result.NoteCodes);
    }

    private sealed class FakeSource(ManufacturingScrapCostDistributionSourceReadV1? read, Exception? failure = null)
        : IManufacturingScrapCostDistributionSourceV1
    {
        public List<(Guid CompanyId, Guid OrderId)> Calls { get; } = [];

        public Task<ManufacturingScrapCostDistributionSourceReadV1> ReadAsync(
            Guid companyId, Guid orderId, CancellationToken cancellationToken)
        {
            Calls.Add((companyId, orderId));
            if (failure is not null)
            {
                throw failure;
            }

            return Task.FromResult(read!);
        }
    }
}
