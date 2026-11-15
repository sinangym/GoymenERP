using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Costing;
using GoymenERP.Modules.Manufacturing.Domain.Orders;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// SAT-D307 production cost port, version 1: the port reads the given company's order once, calls the single
/// distribution calculator and fails closed on anything it cannot trust (empty company, a source that throws, answers
/// for another company or order, or answers without input). It computes no figure itself.
/// </summary>
public sealed class ManufacturingScrapCostDistributionPortTests
{
    private static readonly Guid CompanyA = Guid.Parse("10000000-0000-0000-0000-00000000000a");
    private static readonly Guid CompanyB = Guid.Parse("10000000-0000-0000-0000-00000000000b");
    private static readonly Guid OrderA = Guid.Parse("20000000-0000-0000-0000-00000000000a");
    private static readonly Guid OrderB = Guid.Parse("20000000-0000-0000-0000-00000000000b");
    private static readonly Guid GoodA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid SecondA = Guid.Parse("b0000000-0000-0000-0000-00000000000a");
    private static readonly Guid ScrapA = Guid.Parse("e0000000-0000-0000-0000-00000000000a");

    private static ProductionScrapCostDistributionInput GoldenInput(
        bool closed = true, NonKgInputUse nonKg = NonKgInputUse.None) =>
        new(new ProductionReceiptCostInput(
                closed,
                [new IssueCostInput(Guid.Parse("c0000000-0000-0000-0000-00000000000a"), new Kilograms(1000m),
                    IssueCostReadStatus.Value, 40m, IssueCostCurrencyInput.FunctionalCurrency)],
                [],
                new Kilograms(900m),
                new Kilograms(50m),
                Kilograms.Zero,
                [new ScrapWeighingInput(ScrapA, new Kilograms(50m), new ScrapPriceInput(ScrapPriceReadStatus.Value, 10m))],
                Kilograms.Zero,
                ScrapCostSettingStatus.Off,
                nonKg),
            [new ProductionScrapCostLabelInput(GoodA, "ETK-1", OutputClass.GoodProduct, new Kilograms(900m)),
                new ProductionScrapCostLabelInput(SecondA, "ETK-2", OutputClass.SecondQuality, new Kilograms(50m))]);

    private static ManufacturingScrapCostDistributionSourceReadV1 Found(ProductionScrapCostDistributionInput? input) =>
        new(ManufacturingScrapCostDistributionSourceStatusV1.Found, CompanyA, OrderA, input);

    [Fact]
    public async Task A_found_order_answers_the_calculator_distribution_with_the_golden_figures()
    {
        var source = new FakeSource(Found(GoldenInput(nonKg: NonKgInputUse.PaintLitres)));
        var port = new ManufacturingScrapCostDistributionPort(source);

        var result = await port.DistributeAsync(CompanyA, OrderA, CancellationToken.None);

        Assert.Equal(1, result.ContractVersion);
        Assert.Equal(ManufacturingScrapCostDistributionStatusV1.Available, result.Status);
        Assert.Equal(CompanyA, result.CompanyId);
        Assert.Equal(OrderA, result.OrderId);
        Assert.Null(result.ReasonCode);
        Assert.Equal(39500m / 950m, result.CostPerKgTl);
        Assert.Equal(40m, result.RawMaterialCostPerKgTl);
        Assert.Equal(500m, result.WeighedScrapExpenseTl);
        Assert.Equal(ProductionReceiptCostCodesV1.BasisWithoutScrap, result.CostBasis);
        Assert.Equal(new[] { ProductionReceiptCostCodesV1.PaintCostNotIncluded }, result.NoteCodes);
        Assert.Collection(result.Lines,
            line =>
            {
                Assert.Equal(GoodA, line.OutputLabelId);
                Assert.Equal(900m * (39500m / 950m), line.AllocatedCostTl);
            },
            line =>
            {
                Assert.Equal(SecondA, line.OutputLabelId);
                Assert.Equal(50m * (39500m / 950m), line.AllocatedCostTl);
            },
            line =>
            {
                Assert.Equal(ScrapA, line.OutputLabelId);
                Assert.Null(line.AllocatedCostTl);
                Assert.Equal(500m, line.SeparateScrapExpenseTl);
                Assert.Equal(ProductionScrapCostDistributionCodesV1.ScrapLabelCostNotDecided, line.NotAllocatedCode);
            });
        Assert.Equal(39500m, decimal.Round(result.AllocatedTotalTl!.Value, 10));
        Assert.Equal(new[] { (CompanyA, OrderA) }, source.Calls);
    }

    [Fact]
    public async Task An_empty_company_is_unavailable_and_the_source_is_not_read()
    {
        var source = new FakeSource(Found(GoldenInput()));

        var result = await new ManufacturingScrapCostDistributionPort(source)
            .DistributeAsync(Guid.Empty, OrderA, CancellationToken.None);

        AssertEmpty(result, ManufacturingScrapCostDistributionStatusV1.Unavailable,
            ManufacturingScrapCostDistributionCodesV1.CompanyRequired);
        Assert.Empty(source.Calls);
    }

    [Fact]
    public async Task An_empty_order_is_not_found_and_the_source_is_not_read()
    {
        var source = new FakeSource(Found(GoldenInput()));

        var result = await new ManufacturingScrapCostDistributionPort(source)
            .DistributeAsync(CompanyA, Guid.Empty, CancellationToken.None);

        AssertEmpty(result, ManufacturingScrapCostDistributionStatusV1.NotFound,
            ManufacturingScrapCostDistributionCodesV1.OrderRequired);
        Assert.Empty(source.Calls);
    }

    [Fact]
    public async Task A_source_answer_for_another_company_or_order_is_unavailable()
    {
        var otherCompany = new ManufacturingScrapCostDistributionSourceReadV1(
            ManufacturingScrapCostDistributionSourceStatusV1.Found, CompanyB, OrderA, GoldenInput());
        var otherOrder = new ManufacturingScrapCostDistributionSourceReadV1(
            ManufacturingScrapCostDistributionSourceStatusV1.Found, CompanyA, OrderB, GoldenInput());

        foreach (var read in new[] { otherCompany, otherOrder })
        {
            var result = await new ManufacturingScrapCostDistributionPort(new FakeSource(read))
                .DistributeAsync(CompanyA, OrderA, CancellationToken.None);

            AssertEmpty(result, ManufacturingScrapCostDistributionStatusV1.Unavailable,
                ManufacturingScrapCostDistributionCodesV1.SourceScopeMismatch);
        }
    }

    [Fact]
    public async Task A_source_that_throws_answers_null_or_an_inconsistent_status_is_unavailable()
    {
        ManufacturingScrapCostDistributionSourceReadV1?[] reads =
        [
            null,
            Found(null),
            new(ManufacturingScrapCostDistributionSourceStatusV1.Unavailable, CompanyA, OrderA, null),
            new(ManufacturingScrapCostDistributionSourceStatusV1.NotFound, CompanyA, OrderA, GoldenInput()),
            new((ManufacturingScrapCostDistributionSourceStatusV1)99, CompanyA, OrderA, GoldenInput()),
        ];

        foreach (var read in reads)
        {
            var result = await new ManufacturingScrapCostDistributionPort(new FakeSource(read))
                .DistributeAsync(CompanyA, OrderA, CancellationToken.None);

            AssertEmpty(result, ManufacturingScrapCostDistributionStatusV1.Unavailable,
                ManufacturingScrapCostDistributionCodesV1.SourceUnavailable);
        }

        var throwing = await new ManufacturingScrapCostDistributionPort(
                new FakeSource(null, new InvalidOperationException("SENTETIK kaynak hatası")))
            .DistributeAsync(CompanyA, OrderA, CancellationToken.None);
        AssertEmpty(throwing, ManufacturingScrapCostDistributionStatusV1.Unavailable,
            ManufacturingScrapCostDistributionCodesV1.SourceUnavailable);
    }

    [Fact]
    public async Task An_order_the_company_does_not_have_is_not_found()
    {
        var source = new FakeSource(new ManufacturingScrapCostDistributionSourceReadV1(
            ManufacturingScrapCostDistributionSourceStatusV1.NotFound, CompanyA, OrderA, null));

        var result = await new ManufacturingScrapCostDistributionPort(source)
            .DistributeAsync(CompanyA, OrderA, CancellationToken.None);

        AssertEmpty(result, ManufacturingScrapCostDistributionStatusV1.NotFound,
            ManufacturingScrapCostDistributionCodesV1.OrderNotFound);
    }

    [Fact]
    public async Task A_pending_or_unreadable_distribution_carries_its_code_and_no_figure()
    {
        var pending = await new ManufacturingScrapCostDistributionPort(new FakeSource(Found(GoldenInput(closed: false))))
            .DistributeAsync(CompanyA, OrderA, CancellationToken.None);
        AssertEmpty(pending, ManufacturingScrapCostDistributionStatusV1.Pending,
            ProductionReceiptCostCodesV1.OrderNotClosed);

        var golden = GoldenInput();
        var mismatched = golden with { Labels = [golden.Labels[0]] };
        var unreadable = await new ManufacturingScrapCostDistributionPort(new FakeSource(Found(mismatched)))
            .DistributeAsync(CompanyA, OrderA, CancellationToken.None);
        AssertEmpty(unreadable, ManufacturingScrapCostDistributionStatusV1.Unreadable,
            ProductionScrapCostDistributionCodesV1.LabelMassMismatch);
    }

    [Fact]
    public async Task Cancellation_is_not_turned_into_an_unavailable_answer()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var source = new FakeSource(null, new OperationCanceledException(cancellation.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ManufacturingScrapCostDistributionPort(source).DistributeAsync(CompanyA, OrderA, cancellation.Token));
    }

    private static void AssertEmpty(ManufacturingScrapCostDistributionResultV1 result,
        ManufacturingScrapCostDistributionStatusV1 status, string reasonCode)
    {
        Assert.Equal(1, result.ContractVersion);
        Assert.Equal(status, result.Status);
        Assert.Equal(reasonCode, result.ReasonCode);
        Assert.Empty(result.Lines);
        Assert.Empty(result.NoteCodes);
        Assert.Null(result.CostBasis);
        Assert.Null(result.CostPerKgTl);
        Assert.Null(result.RawMaterialCostPerKgTl);
        Assert.Null(result.WeighedScrapExpenseTl);
        Assert.Null(result.AllocatedTotalTl);
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
