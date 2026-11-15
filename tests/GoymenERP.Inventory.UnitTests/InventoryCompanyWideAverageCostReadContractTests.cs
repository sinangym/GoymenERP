using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// SAT-D272 / SAT-D347 / SAT-D350 (STK-S35): the company-wide average-cost read port V1 is a contract Sales builds on
/// (SAT-S12). Its version, basis, answer shape, invariants and registration are pinned here so a change is a deliberate V2;
/// the ledger row mapping both the read and the writer share is pinned with it. What the port and the writer do with real
/// rows (weighting, fail-closed, company isolation, single save) is proven on SQL Server
/// (<c>InventorySqlServerRelationalAcceptanceTests</c>). SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryCompanyWideAverageCostReadContractTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid FirstLocation = Guid.Parse("22222222-2222-4222-8222-222222222221");
    private static readonly Guid SecondLocation = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Product = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void Contract_version_one_pins_its_unit_basis_aggregation_currency_basis_and_scales()
    {
        Assert.Equal((1, "kg", "purchase-moving-average", "all-locations-on-hand-kg-weighted", "company-base-currency", 4, 3),
            (InventoryCompanyWideAverageCostReadContractV1.Version, InventoryCompanyWideAverageCostReadContractV1.CostUnit,
                InventoryCompanyWideAverageCostReadContractV1.CostBasis, InventoryCompanyWideAverageCostReadContractV1.Aggregation,
                InventoryCompanyWideAverageCostReadContractV1.CostCurrencyBasis,
                InventoryCompanyWideAverageCostReadContractV1.InternalScale,
                InventoryCompanyWideAverageCostReadContractV1.QuantityScale));
        Assert.Equal(AverageCostPrecision.InternalScale, InventoryCompanyWideAverageCostReadContractV1.InternalScale);
    }

    [Fact]
    public void The_answer_and_source_shapes_are_exactly_the_published_properties()
    {
        Assert.Equal(
            [
                "AverageCostPerKg", "CompanyId", "ContractVersion", "CostCurrencyBasis", "OnHandKg", "ProductId", "Sources",
                "Status", "UnreadableCode", "UnreadableLocationId",
            ],
            typeof(InventoryCompanyWideAverageCostReadAnswerV1).GetProperties().Select(property => property.Name)
                .Where(name => name != "EqualityContract").Order(StringComparer.Ordinal));
        Assert.Equal(
            ["LedgerVersion", "LocationId", "OnHandKg"],
            typeof(InventoryCompanyWideAverageCostSourceV1).GetProperties().Select(property => property.Name)
                .Where(name => name != "EqualityContract").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_value_carries_the_average_and_its_sources_ordered_by_location_with_their_total()
    {
        var answer = InventoryCompanyWideAverageCostReadAnswerV1.ForValue(CompanyA, Product, 12m,
            [new(SecondLocation, 20m, 4), new(FirstLocation, 10.5m, 1)]);

        Assert.Equal((1, CompanyA, Product, InventoryAverageCostReadStatusV1.Value, 12m, "company-base-currency", null, null, 30.5m),
            (answer.ContractVersion, answer.CompanyId, answer.ProductId, answer.Status, answer.AverageCostPerKg,
                answer.CostCurrencyBasis, answer.UnreadableCode, answer.UnreadableLocationId, answer.OnHandKg));
        Assert.Equal([(FirstLocation, 10.5m, (long?)1), (SecondLocation, 20m, (long?)4)],
            answer.Sources.Select(source => (source.LocationId, source.OnHandKg, source.LedgerVersion)));
    }

    [Fact]
    public void An_unreadable_carries_a_code_and_the_location_and_never_a_number()
    {
        var answer = InventoryCompanyWideAverageCostReadAnswerV1.ForUnreadable(CompanyA, Product,
            InventoryAverageCostUnreadableCodesV1.LedgerStockMismatch, SecondLocation,
            [new(FirstLocation, 10m, 2), new(SecondLocation, 5m, null)]);

        Assert.Equal((InventoryAverageCostReadStatusV1.Unreadable, null, "uncosted_stock_on_hand", SecondLocation, 15m),
            (answer.Status, answer.AverageCostPerKg, answer.UnreadableCode, answer.UnreadableLocationId, answer.OnHandKg));
        Assert.Equal(2, answer.Sources.Count);
    }

    [Fact]
    public void A_no_source_carries_no_number_no_code_no_location_and_no_sources()
    {
        var answer = InventoryCompanyWideAverageCostReadAnswerV1.ForNoSource(CompanyA, Product);

        Assert.Equal((InventoryAverageCostReadStatusV1.NoSource, null, null, null, 0m),
            (answer.Status, answer.AverageCostPerKg, answer.UnreadableCode, answer.UnreadableLocationId, answer.OnHandKg));
        Assert.Empty(answer.Sources);
    }

    [Fact]
    public void Answers_refuse_empty_ids_bad_averages_unknown_codes_and_inconsistent_sources()
    {
        InventoryCompanyWideAverageCostSourceV1[] one = [new(FirstLocation, 1m, 1)];
        Assert.Throws<ArgumentException>(() => InventoryCompanyWideAverageCostReadAnswerV1.ForNoSource(Guid.Empty, Product));
        Assert.Throws<ArgumentException>(() => InventoryCompanyWideAverageCostReadAnswerV1.ForNoSource(CompanyA, Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryCompanyWideAverageCostReadAnswerV1.ForValue(CompanyA, Product, -0.0001m, one));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryCompanyWideAverageCostReadAnswerV1.ForValue(CompanyA, Product, 1.00001m, one));
        Assert.Throws<ArgumentException>(() => InventoryCompanyWideAverageCostReadAnswerV1.ForValue(CompanyA, Product, 1m, []));
        Assert.Throws<ArgumentException>(() => InventoryCompanyWideAverageCostReadAnswerV1.ForValue(
            CompanyA, Product, 1m, [new(FirstLocation, 1m, 1), new(FirstLocation, 2m, 2)]));
        Assert.Throws<ArgumentException>(() => InventoryCompanyWideAverageCostReadAnswerV1.ForValue(
            CompanyA, Product, 1m, [new(FirstLocation, 1m, null)]));
        Assert.Throws<ArgumentNullException>(() => InventoryCompanyWideAverageCostReadAnswerV1.ForValue(CompanyA, Product, 1m, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryCompanyWideAverageCostReadAnswerV1.ForUnreadable(
            CompanyA, Product, "made_up", FirstLocation, one));
        Assert.Throws<ArgumentException>(() => InventoryCompanyWideAverageCostReadAnswerV1.ForUnreadable(
            CompanyA, Product, InventoryAverageCostUnreadableCodesV1.NegativeStock, SecondLocation, one));
        Assert.Throws<ArgumentException>(() => InventoryCompanyWideAverageCostReadAnswerV1.ForUnreadable(
            CompanyA, Product, InventoryAverageCostUnreadableCodesV1.NegativeStock, FirstLocation, []));
    }

    [Fact]
    public void A_source_holds_positive_stock_at_the_quantity_scale_and_a_real_ledger_version()
    {
        Assert.Throws<ArgumentException>(() => new InventoryCompanyWideAverageCostSourceV1(Guid.Empty, 1m, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryCompanyWideAverageCostSourceV1(FirstLocation, 0m, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryCompanyWideAverageCostSourceV1(FirstLocation, -1m, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryCompanyWideAverageCostSourceV1(FirstLocation, 1.0001m, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InventoryCompanyWideAverageCostSourceV1(FirstLocation, 1m, 0));
        var source = new InventoryCompanyWideAverageCostSourceV1(FirstLocation, 0.001m, null);
        Assert.Equal((FirstLocation, 0.001m, (long?)null), (source.LocationId, source.OnHandKg, source.LedgerVersion));
    }

    [Fact]
    public void The_port_has_one_read_method_with_the_product_only_and_no_company_or_location_parameter()
    {
        var method = Assert.Single(typeof(IInventoryCompanyWideAverageCostReadPortV1).GetMethods());
        Assert.Equal("ReadCompanyWideAsync", method.Name);
        Assert.Equal([typeof(Guid), typeof(CancellationToken)], method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(typeof(Task<InventoryCompanyWideAverageCostReadAnswerV1>), method.ReturnType);
    }

    [Fact]
    public void The_inventory_module_registers_the_company_wide_port_and_the_ledger_writer_as_scoped_services()
    {
        var services = new ServiceCollection().AddInventoryModule(new Settings("Server=unused.invalid;Database=unused;"));

        var port = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IInventoryCompanyWideAverageCostReadPortV1));
        Assert.Equal((ServiceLifetime.Scoped, typeof(InventoryCompanyWideAverageCostReadPort)), (port.Lifetime, port.ImplementationType));
        var writer = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(InventoryAverageCostLedgerWriter));
        Assert.Equal((ServiceLifetime.Scoped, typeof(InventoryAverageCostLedgerWriter)), (writer.Lifetime, writer.ImplementationType));
    }

    [Fact]
    public async Task Without_a_resolved_company_the_port_refuses_before_any_connection_and_never_reads_unscoped()
    {
        await using var context = Context(resolved: false);
        var port = new InventoryCompanyWideAverageCostReadPort(context, new CompanyContext());

        await Assert.ThrowsAsync<InvalidOperationException>(() => port.ReadCompanyWideAsync(Product));
    }

    [Fact]
    public async Task An_empty_product_is_refused_as_a_caller_fault_without_a_query()
    {
        await using var context = Context(resolved: true);
        var port = new InventoryCompanyWideAverageCostReadPort(context, Resolved());

        // The connection string points nowhere: a query would be a SqlException, not this refusal.
        await Assert.ThrowsAsync<ArgumentException>(() => port.ReadCompanyWideAsync(Guid.Empty));
    }

    [Fact]
    public async Task Without_a_resolved_company_the_ledger_writer_refuses_before_any_connection()
    {
        await using var context = Context(resolved: false);
        var writer = new InventoryAverageCostLedgerWriter(context, new CompanyContext(), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StageAsync(FirstLocation, Product,
            new AverageCostMovement(AverageCostMovementKind.PurchaseReceipt, 10m, 0m, 10m)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.StageTransferAsync(
            FirstLocation, 10m, SecondLocation, 0m, Product, 5m));
    }

    [Fact]
    public async Task The_ledger_writer_refuses_a_missing_movement_empty_ids_and_a_transfer_onto_its_own_location_without_a_query()
    {
        await using var context = Context(resolved: true);
        var writer = new InventoryAverageCostLedgerWriter(context, Resolved(), TimeProvider.System);
        var receipt = new AverageCostMovement(AverageCostMovementKind.PurchaseReceipt, 10m, 0m, 10m);

        await Assert.ThrowsAsync<ArgumentNullException>(() => writer.StageAsync(FirstLocation, Product, null!));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.StageAsync(Guid.Empty, Product, receipt));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.StageAsync(FirstLocation, Guid.Empty, receipt));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.StageTransferAsync(
            FirstLocation, 10m, FirstLocation, 0m, Product, 5m));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void A_ledger_row_reads_back_exactly_the_position_written_onto_it()
    {
        var known = AverageCostPosition.Known(12.5m, 10.5714m);
        var unreadable = AverageCostPosition.Unreadable(AverageCostUnreadableReason.TransferSourceUnreadable);
        var row = new AverageCostLedgerRow();

        InventoryAverageCostLedgerRows.Write(row, known);
        Assert.Equal((1, null, 12.5m, 10.5714m), (row.Status, row.UnreadableReason, row.QuantityKg, row.AverageCostPerKg));
        Assert.Equal(known, InventoryAverageCostLedgerRows.PositionOf(row));

        InventoryAverageCostLedgerRows.Write(row, unreadable);
        Assert.Equal((2, (int?)7, null, null), (row.Status, row.UnreadableReason, row.QuantityKg, row.AverageCostPerKg));
        Assert.Equal(unreadable, InventoryAverageCostLedgerRows.PositionOf(row));
    }

    [Theory]
    [InlineData(1, 8, 1, 1)]
    [InlineData(1, null, null, 1)]
    [InlineData(1, null, 1, null)]
    [InlineData(2, null, null, null)]
    [InlineData(2, 99, null, null)]
    [InlineData(2, 8, 1, null)]
    [InlineData(3, null, 1, 1)]
    public void A_malformed_ledger_row_fails_closed_and_never_reads_as_a_figure(
        int status, int? reason, int? quantityKg, int? average)
    {
        var row = new AverageCostLedgerRow
        {
            Status = status, UnreadableReason = reason, QuantityKg = quantityKg, AverageCostPerKg = average,
        };

        Assert.Throws<InvalidOperationException>(() => InventoryAverageCostLedgerRows.PositionOf(row));
    }

    private static CompanyContext Resolved()
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        return company;
    }

    private sealed class Settings(string connectionString) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "DB_CONNECTION_STRING" ? connectionString : null;
            set => throw new NotSupportedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();

        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }

    private static InventoryDbContext Context(bool resolved) =>
        new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, resolved ? Resolved() : new CompanyContext());
}
