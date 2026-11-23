using GoymenERP.Api.ReportingRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Reporting.Domain;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>SENTETIK. WP14 (YOL-D29): the host join of Sales promised lines with Logistics dated shipment shares.</summary>
public sealed class ReportingOtifSourceTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Order = Guid.Parse("50000000-0000-4000-8000-000000000001");
    private static readonly Guid Line = Guid.Parse("51000000-0000-4000-8000-000000000001");
    private static readonly Guid Unit = Guid.Parse("52000000-0000-4000-8000-000000000001");
    private static readonly Guid Party = Guid.Parse("53000000-0000-4000-8000-000000000001");
    private static readonly DateOnly Promised = new(2026, 11, 10);

    [Fact]
    public async Task Shares_are_dated_by_the_Istanbul_business_day_and_attached_to_their_line()
    {
        // 21:30 UTC on the 10th is the 11th in Istanbul: one day late.
        var shipments = new FakeShipments(Share(new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero), 60, Unit),
            Share(new DateTimeOffset(2026, 11, 10, 21, 30, 0, TimeSpan.Zero), 40, Unit));
        var source = Source(new FakePromises(Promise()), shipments);
        var page = await source.ReadAsync(Company, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 1, 50);
        Assert.Equal(ReportingSourceOutcomeV1.Found, page.Outcome);
        var line = Assert.Single(page.Lines);
        Assert.Equal((Order, "SIP-1", 1, Party, "KG", Promised, 100m), (line.OrderId, line.OrderNumber, line.LineNumber, line.CustomerId, line.UnitCode,
            line.PromisedDate, line.OrderedQuantity));
        Assert.Equal(new[] { new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 11) }, line.Shipments.Select(share => share.ShippedOn).ToArray());
        Assert.Equal(new[] { Order }, Assert.Single(shipments.Queries).OrderIds!.ToArray());
        Assert.False(ReportingOtifCalculatorV1.Evaluate(line.PromisedDate, line.OrderedQuantity, line.Shipments).Otif);
        Assert.Equal(2, page.UnmeasuredLines);
    }

    [Fact]
    public async Task A_share_in_another_unit_a_cut_window_or_another_company_is_fail_closed()
    {
        var otherUnit = await Source(new FakePromises(Promise()), new FakeShipments(Share(DateTimeOffset.UtcNow, 1, Guid.NewGuid())))
            .ReadAsync(Company, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 1, 50);
        Assert.Equal(ReportingSourceOutcomeV1.Unavailable, otherUnit.Outcome);
        var cut = await Source(new FakePromises(Promise()), new FakeShipments { HasMore = true })
            .ReadAsync(Company, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 1, 50);
        Assert.Equal(ReportingSourceOutcomeV1.Unavailable, cut.Outcome);
        var foreign = await Source(new FakePromises(Promise() with { CompanyId = Guid.NewGuid() }), new FakeShipments())
            .ReadAsync(Company, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 1, 50);
        Assert.Equal(ReportingSourceOutcomeV1.Unavailable, foreign.Outcome);
        var denied = await Source(new FakePromises(Promise()), new FakeShipments())
            .ReadAsync(Guid.NewGuid(), new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 1, 50);
        Assert.Equal(ReportingSourceOutcomeV1.Denied, denied.Outcome);
        var noLogistics = new ReportingSalesLogisticsOtifSourceV1(new FakePromises(Promise()), new ServiceCollection().BuildServiceProvider(), Context());
        Assert.Equal(ReportingSourceOutcomeV1.NotConnected,
            (await noLogistics.ReadAsync(Company, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 1, 50)).Outcome);
    }

    private static SalesOrderPromisedLineV1 Promise() => new(Company, Order, "SIP-1", Line, 1, Party, Guid.NewGuid(), Unit, "KG", Promised, "100");

    private static SalesInvoiceableShipmentV1 Share(DateTimeOffset shippedAt, decimal quantity, Guid unit) => new(Guid.NewGuid(), "IRS-1", null,
        shippedAt, Party, Guid.NewGuid(), 1, Guid.NewGuid(), shippedAt, Order, Line, Guid.NewGuid(), unit, quantity);

    private static CompanyContext Context()
    {
        var context = new CompanyContext();
        context.Set(Company);
        return context;
    }

    private static ReportingSalesLogisticsOtifSourceV1 Source(ISalesOrderPromiseReadPortV1 promises, ISalesInvoiceableShipmentReadPortV1 shipments) =>
        new(promises, new ServiceCollection().AddSingleton(shipments).BuildServiceProvider(), Context());

    private sealed class FakePromises(params SalesOrderPromisedLineV1[] lines) : ISalesOrderPromiseReadPortV1
    {
        public Task<SalesOrderPromisePageV1> ListPromisedLinesAsync(DateOnly from, DateOnly to, int pageSize, string? cursor,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SalesOrderPromisePageV1(Company, lines, 2, null));
    }

    private sealed class FakeShipments(params SalesInvoiceableShipmentV1[] shares) : ISalesInvoiceableShipmentReadPortV1
    {
        public bool HasMore { get; init; }
        public List<SalesInvoiceableShipmentQueryV1> Queries { get; } = [];

        public Task<SalesInvoiceableShipmentAnswerV1> ReadInvoiceableShipmentsAsync(SalesInvoiceableShipmentQueryV1 query,
            CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            return Task.FromResult(new SalesInvoiceableShipmentAnswerV1(SalesDispatchReadOutcomeV1.Found, Company, shares, HasMore,
                SalesInvoicedStateSourceV1.NoSource));
        }
    }
}
