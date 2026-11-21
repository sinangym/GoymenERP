using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// F-017 STOK F017-b, without a database: the pinned contract values (command and event names, the sales order line lock), the
/// canonical quantity text, the identity-only unit converter, the Sales demand source over fake Sales ports (the open quantity
/// reading is the author's, NEEDS-SINAN) and the fail-closed refusals of the read and release ports before any statement. What
/// SQL Server enforces is proven by <c>InventorySqlServerRelationalAcceptanceTests.FutureSupply</c>. SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryFutureAllocationContractTests
{
    private static readonly Guid Company = Guid.Parse("8a170000-0000-4000-8000-0000000000a1");
    private static readonly Guid OtherCompany = Guid.Parse("8a170000-0000-4000-8000-0000000000b2");
    private static readonly Guid OrderId = Guid.Parse("8a170000-0000-4000-8000-000000000011");
    private static readonly Guid LineId = Guid.Parse("8a170000-0000-4000-8000-000000000012");
    private static readonly Guid OtherLineId = Guid.Parse("8a170000-0000-4000-8000-000000000013");
    private static readonly Guid ProductId = Guid.Parse("8a170000-0000-4000-8000-000000000021");
    private static readonly Guid UnitId = Guid.Parse("8a170000-0000-4000-8000-000000000031");
    private static readonly Guid OtherUnitId = Guid.Parse("8a170000-0000-4000-8000-000000000032");

    [Fact]
    public void The_command_event_and_kind_names_are_the_pinned_contract_values()
    {
        Assert.Equal(("inventory.future-allocation.create.v1", "inventory.future-allocation.release.v1"),
            (InventoryFutureAllocationCommandsV1.Create, InventoryFutureAllocationCommandsV1.Release));
        Assert.Equal(("inventory.future-allocation.created.v1", "inventory.future-allocation.released.v1", 1, 1),
            (InventoryFutureAllocationCreatedOutboxV1.EventType, InventoryFutureAllocationReleasedOutboxV1.EventType,
                InventoryFutureAllocationCreatedOutboxV1.SchemaVersion, InventoryFutureAllocationReleasedOutboxV1.SchemaVersion));
        Assert.Equal("purchasing.order-line", InventoryFutureAllocationKindsV1.PurchaseOrderLine);
        Assert.Equal(InventoryAllocationDemandKindsV1.SalesOrderLine, InventoryFutureAllocationKindsV1.SalesOrderLine);
        Assert.Equal(("inventory", "purchasing", "sales"), (InventoryFutureAllocationOriginsV1.Inventory,
            InventoryFutureAllocationOriginsV1.Purchasing, InventoryFutureAllocationOriginsV1.Sales));
    }

    [Fact]
    public void The_sales_order_line_lock_resource_is_the_pinned_prefix_with_both_identities_in_n_format()
    {
        var name = SalesOrderLineSerializationLockContractV1.ResourceName(Company, LineId);

        Assert.Equal("goymen:sales-order-line:8a1700000000400080000000000000a1:8a170000000040008000000000000012", name);
        Assert.Equal(89, name.Length);
        Assert.StartsWith(SalesOrderLineSerializationLockContractV1.ResourcePrefix, name, StringComparison.Ordinal);
        Assert.Equal("goymen:sales-order-line:", SalesOrderLineSerializationLockContractV1.ResourcePrefix);
        Assert.True(name.Length <= 255);
        Assert.Throws<ArgumentException>(() => SalesOrderLineSerializationLockContractV1.ResourceName(Guid.Empty, LineId));
        Assert.Throws<ArgumentException>(() => SalesOrderLineSerializationLockContractV1.ResourceName(Company, Guid.Empty));
    }

    [Fact]
    public void The_sales_order_line_busy_code_and_the_short_wait_are_the_contract_values()
    {
        Assert.Equal("sales_order_line_busy", SalesOrderLineSerializationLockContractV1.BusyCode);
        Assert.Equal(TimeSpan.FromSeconds(5), SalesOrderLineSerializationLockContractV1.LockTimeout);
        var busy = new SalesOrderLineBusyException(LineId, -1);
        Assert.Equal((SalesOrderLineSerializationLockContractV1.BusyCode, LineId, -1), (busy.Code, busy.LineId, busy.LockResult));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("12.125", true)]
    [InlineData("0", false)]
    [InlineData("0.000", false)]
    [InlineData("1.0005", false)]
    [InlineData("1.1250", false)]
    [InlineData("-1", false)]
    [InlineData("1,5", false)]
    [InlineData(" 1", false)]
    [InlineData("1e3", false)]
    [InlineData("1.2.3", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_quantity_text_is_a_canonical_positive_decimal_with_at_most_three_decimals(string? text, bool accepted)
    {
        Assert.Equal(accepted, InventoryFutureAllocationTextV1.TryPositiveQuantity(text, out _));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("10", true)]
    [InlineData("30", true)]
    [InlineData("9223372036854775807", true)]
    [InlineData("0", false)]
    [InlineData("03", false)]
    [InlineData("01", false)]
    [InlineData("0001", false)]
    [InlineData("-1", false)]
    [InlineData("+1", false)]
    [InlineData("01a", false)]
    [InlineData("\u0661", false)]
    [InlineData("\uFF11", false)]
    [InlineData(" 1", false)]
    [InlineData("1 ", false)]
    [InlineData("1\t", false)]
    [InlineData("9223372036854775808", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void An_expected_version_is_a_canonical_positive_int64(string? text, bool accepted)
    {
        Assert.Equal(accepted, InventoryFutureAllocationTextV1.TryVersion(text, out _));
    }

    [Fact]
    public async Task The_default_converter_answers_only_the_identity_and_no_kilogram_source()
    {
        var converter = new InventoryIdentityFutureUnitConverterV1();

        var same = await converter.ConvertAsync(ProductId, "1", 12.5m, UnitId, UnitId);
        var other = await converter.ConvertAsync(ProductId, "1", 12.5m, UnitId, OtherUnitId);
        var empty = await converter.ConvertAsync(ProductId, "1", 12.5m, Guid.Empty, Guid.Empty);
        var kilograms = await converter.ConvertToKilogramsAsync(ProductId, "1", 12.5m, UnitId);

        Assert.Equal((InventoryFutureUnitConversionOutcomeV1.Converted, 12.5m), (same.Outcome, same.Quantity!.Value));
        Assert.Equal(
            (InventoryFutureUnitConversionOutcomeV1.NoSource, InventoryFutureUnitConversionOutcomeV1.NoSource,
                InventoryFutureUnitConversionOutcomeV1.NoSource),
            (other.Outcome, empty.Outcome, kilograms.Outcome));
        Assert.Null(other.Quantity);
    }

    [Fact]
    public async Task The_default_purchase_source_is_unavailable_until_the_host_replaces_it()
    {
        var answer = await new InventoryUnavailableFutureSupplySourceV1().GetAllocatableLineAsync(OrderId, LineId);

        Assert.Equal((InventoryFutureSupplySourceOutcomeV1.Unavailable, null), (answer.Outcome, answer.Line));
    }

    [Fact]
    public async Task The_demand_source_without_the_Sales_ports_is_unavailable_never_an_assumed_zero()
    {
        var withoutPorts = await new InventoryFutureDemandSource().GetDemandLineAsync(OrderId, LineId);
        var withoutDeliveries = await new InventoryFutureDemandSource(new FakeSalesLines(Approved(Line()))).GetDemandLineAsync(OrderId, LineId);

        Assert.Equal(InventoryFutureDemandSourceOutcomeV1.Unavailable, withoutPorts.Outcome);
        Assert.Equal(InventoryFutureDemandSourceOutcomeV1.Unavailable, withoutDeliveries.Outcome);
        Assert.Null(withoutPorts.Line);
    }

    [Fact]
    public async Task The_open_quantity_is_the_shippable_quantity_minus_what_the_delivery_owner_recorded_for_the_line()
    {
        var source = Source(Line(quantity: "100.5"), Shipped((LineId, UnitId, 30m), (LineId, UnitId, 20.25m), (OtherLineId, UnitId, 99m)));

        var answer = await source.GetDemandLineAsync(OrderId, LineId);

        Assert.Equal(InventoryFutureDemandSourceOutcomeV1.Open, answer.Outcome);
        Assert.Equal((OrderId, LineId, ProductId, "3", UnitId, "50.25"),
            (answer.Line!.OrderId, answer.Line.LineId, answer.Line.ProductId, answer.Line.ProductVersion, answer.Line.UnitId,
                answer.Line.OpenQuantityText));
    }

    [Fact]
    public async Task A_smaller_reapproval_shipping_cap_bounds_the_open_quantity_and_it_never_goes_below_zero()
    {
        var capped = await Source(Line(quantity: "100", cap: "60"), Shipped((LineId, UnitId, 10m))).GetDemandLineAsync(OrderId, LineId);
        var overShipped = await Source(Line(quantity: "100"), Shipped((LineId, UnitId, 120m))).GetDemandLineAsync(OrderId, LineId);

        Assert.Equal("50", capped.Line!.OpenQuantityText);
        Assert.Equal("0", overShipped.Line!.OpenQuantityText);
    }

    [Fact]
    public async Task A_cancelled_remainder_is_open_zero_and_an_unknown_remainder_state_is_unavailable()
    {
        var cancelled = await Source(Line(state: SalesRemainderChangeNamesV1.Cancelled), Shipped()).GetDemandLineAsync(OrderId, LineId);
        var unknown = await Source(Line(state: "Something"), Shipped()).GetDemandLineAsync(OrderId, LineId);

        Assert.Equal((InventoryFutureDemandSourceOutcomeV1.Open, "0"), (cancelled.Outcome, cancelled.Line!.OpenQuantityText));
        Assert.Equal(InventoryFutureDemandSourceOutcomeV1.Unavailable, unknown.Outcome);
    }

    [Fact]
    public async Task A_shipment_in_another_unit_a_foreign_delivery_answer_or_an_unreadable_line_is_unavailable()
    {
        var otherUnit = await Source(Line(), Shipped((LineId, OtherUnitId, 1m))).GetDemandLineAsync(OrderId, LineId);
        var foreign = await Source(Line(), new SalesOrderDeliveryReadAnswerV1(OtherCompany, OrderId, []))
            .GetDemandLineAsync(OrderId, LineId);
        var otherOrder = await Source(Line(), new SalesOrderDeliveryReadAnswerV1(Company, Guid.NewGuid(), []))
            .GetDemandLineAsync(OrderId, LineId);
        var badQuantity = await Source(Line(quantity: "1,5"), Shipped()).GetDemandLineAsync(OrderId, LineId);
        var wrongLine = await Source(Line() with { LineId = OtherLineId }, Shipped()).GetDemandLineAsync(OrderId, LineId);

        Assert.All(new[] { otherUnit, foreign, otherOrder, badQuantity, wrongLine },
            answer => Assert.Equal((InventoryFutureDemandSourceOutcomeV1.Unavailable, null), (answer.Outcome, answer.Line)));
    }

    [Fact]
    public async Task Not_found_and_not_approved_pass_through_and_an_owner_failure_is_unavailable()
    {
        var notFound = await new InventoryFutureDemandSource(
                new FakeSalesLines(new SalesOrderLineReferenceAnswerV1(SalesOrderLineReferenceOutcomeV1.NotFound, null, null)),
                new FakeDeliveries(Shipped()))
            .GetDemandLineAsync(OrderId, LineId);
        var notApproved = await new InventoryFutureDemandSource(
                new FakeSalesLines(new SalesOrderLineReferenceAnswerV1(SalesOrderLineReferenceOutcomeV1.NotApproved, null, "Draft")),
                new FakeDeliveries(Shipped()))
            .GetDemandLineAsync(OrderId, LineId);
        var failing = await new InventoryFutureDemandSource(new FakeSalesLines(Approved(Line())), new FakeDeliveries(null))
            .GetDemandLineAsync(OrderId, LineId);

        Assert.Equal(
            (InventoryFutureDemandSourceOutcomeV1.NotFound, InventoryFutureDemandSourceOutcomeV1.NotApproved,
                InventoryFutureDemandSourceOutcomeV1.Unavailable),
            (notFound.Outcome, notApproved.Outcome, failing.Outcome));
    }

    [Fact]
    public async Task The_read_port_without_a_resolved_company_or_with_an_empty_line_is_unreadable_before_any_statement()
    {
        await using var unresolvedContext = Context(resolved: false);
        var unresolved = new InventoryFutureAllocationReadPort(unresolvedContext, new CompanyContext());
        var company = new CompanyContext();
        company.Set(Company);
        await using var resolvedContext = Context(resolved: true);
        var resolved = new InventoryFutureAllocationReadPort(resolvedContext, company);

        Assert.Equal(InventoryFutureAllocationReadOutcomeV1.Unreadable, (await unresolved.GetAllocatedOnPurchaseLineAsync(LineId)).Outcome);
        Assert.Equal(InventoryFutureAllocationReadOutcomeV1.Unreadable,
            (await unresolved.GetConsumedPresenceOnPurchaseLineAsync(LineId)).Outcome);
        Assert.Equal(InventoryFutureAllocationReadOutcomeV1.Unreadable, (await resolved.GetAllocatedOnPurchaseLineAsync(Guid.Empty)).Outcome);
        Assert.Equal(InventoryFutureAllocationReadOutcomeV1.Unreadable,
            (await resolved.GetConsumedPresenceOnPurchaseLineAsync(Guid.Empty)).Outcome);
    }

    [Fact]
    public async Task The_release_port_refuses_an_unresolved_company_and_a_closed_connection_before_any_statement()
    {
        var company = new CompanyContext();
        company.Set(Company);
        var port = new InventoryFutureAllocationReleasePort(company, new RefusingPurchaseLocks(), TimeProvider.System);
        var unresolvedPort = new InventoryFutureAllocationReleasePort(new CompanyContext(), new RefusingPurchaseLocks(), TimeProvider.System);
        await using var closed = new SqlConnection();

        await Assert.ThrowsAsync<InvalidOperationException>(() => unresolvedPort.EnlistAsync(closed, new FakeTransaction(closed)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => port.EnlistAsync(closed, new FakeTransaction(closed)));
        Assert.Equal(ConnectionState.Closed, closed.State);
    }

    private static InventoryFutureDemandSource Source(SalesOrderLineReferenceV1 line, SalesOrderDeliveryReadAnswerV1 deliveries) =>
        new(new FakeSalesLines(Approved(line)), new FakeDeliveries(deliveries));

    private static SalesOrderLineReferenceAnswerV1 Approved(SalesOrderLineReferenceV1 line) =>
        new(SalesOrderLineReferenceOutcomeV1.Approved, line, null);

    private static SalesOrderLineReferenceV1 Line(string quantity = "100", string? cap = null,
        string state = SalesRemainderChangeNamesV1.Open) =>
        new(Company, OrderId, LineId, 1, Guid.Parse("8a170000-0000-4000-8000-000000000041"),
            Guid.Parse("8a170000-0000-4000-8000-000000000042"), ProductId, "3", UnitId, quantity, 1, 7, state,
            ShippingCapQuantityText: cap);

    private static SalesOrderDeliveryReadAnswerV1 Shipped(params (Guid Line, Guid Unit, decimal Quantity)[] entries) =>
        new(Company, OrderId, entries.Select(entry => new SalesOrderLineDeliveryV1(entry.Line, entry.Unit, entry.Quantity, 0m)).ToList());

    private static InventoryDbContext Context(bool resolved)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(Company);
        }

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;")
            .Options;
        return new InventoryDbContext(options, company);
    }

    private sealed class FakeSalesLines(SalesOrderLineReferenceAnswerV1 answer) : ISalesOrderLineReferencePortV1
    {
        public Task<SalesOrderLineReferenceAnswerV1> GetApprovedLineAsync(Guid orderId, Guid lineId,
            CancellationToken cancellationToken = default) => Task.FromResult(answer);

        public Task<SalesApprovedLinePageV1> ListApprovedLinesAsync(int pageSize, string? cursor,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>A null answer stands for an owner that fails: the read throws.</summary>
    private sealed class FakeDeliveries(SalesOrderDeliveryReadAnswerV1? answer) : ISalesOrderDeliveryReadPortV1
    {
        public Task<SalesOrderDeliveryReadAnswerV1> ReadOrderDeliveriesAsync(Guid orderId, CancellationToken cancellationToken = default) =>
            answer is null ? throw new InvalidOperationException("SENTETIK owner failure") : Task.FromResult(answer);
    }

    private sealed class RefusingPurchaseLocks : IPurchaseLineSerializationLockBinderV1
    {
        public IPurchaseLineSerializationLockV1 Bind(DbConnection connection, DbTransaction transaction) =>
            throw new InvalidOperationException("SENTETIK: no lock is bound in a refusal test.");
    }

    private sealed class FakeTransaction(DbConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        protected override DbConnection DbConnection => connection;

        public override void Commit() => throw new NotSupportedException();

        public override void Rollback() => throw new NotSupportedException();
    }
}
