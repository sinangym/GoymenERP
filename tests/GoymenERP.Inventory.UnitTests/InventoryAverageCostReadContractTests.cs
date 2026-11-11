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
/// SAT-D262 (SINAN_KARARLARI_2026-11-10_SATIS_EK52): the versioned average-cost read port V1 is a contract Sales builds on.
/// Its version, status numbers, wire codes, answer shape and invariants are pinned here so a change is a deliberate V2.
/// What the port reads from real rows (company isolation, NoSource without a row) is proven on SQL Server
/// (<c>InventorySqlServerRelationalAcceptanceTests</c>). SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryAverageCostReadContractTests
{
    private static readonly Guid CompanyA = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Location = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Product = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void Contract_version_one_pins_its_basis_unit_and_scale()
    {
        Assert.Equal((1, "kg", "purchase-moving-average", 4),
            (InventoryAverageCostReadContractV1.Version, InventoryAverageCostReadContractV1.CostUnit,
                InventoryAverageCostReadContractV1.CostBasis, InventoryAverageCostReadContractV1.InternalScale));
        Assert.Equal(AverageCostPrecision.InternalScale, InventoryAverageCostReadContractV1.InternalScale);
    }

    [Fact]
    public void The_three_statuses_keep_their_numbers()
    {
        Assert.Equal(
            [("Value", 1), ("Unreadable", 2), ("NoSource", 3)],
            Enum.GetValues<InventoryAverageCostReadStatusV1>().Select(status => (status.ToString(), (int)status)));
    }

    [Fact]
    public void The_unreadable_wire_codes_are_pinned_and_every_domain_reason_maps_to_exactly_one()
    {
        Assert.Equal(
            [
                "negative_stock", "unit_conversion_undecided", "foreign_currency_undecided", "additional_cost_undecided",
                "return_effect_undecided", "mixed_source_undecided", "transfer_source_unreadable", "uncosted_stock_on_hand",
                "zero_cost_receipt",
            ],
            InventoryAverageCostUnreadableCodesV1.All);

        var mapped = Enum.GetValues<AverageCostUnreadableReason>()
            .Select(reason => InventoryAverageCostReadPort.CodeOf((int)reason)).ToArray();
        Assert.Equal(InventoryAverageCostUnreadableCodesV1.All.Order(StringComparer.Ordinal), mapped.Order(StringComparer.Ordinal));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryAverageCostReadPort.CodeOf(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryAverageCostReadPort.CodeOf(10));
    }

    [Fact]
    public void The_answer_shape_is_exactly_the_published_properties()
    {
        Assert.Equal(
            [
                "AverageCostPerKg", "CompanyId", "ContractVersion", "LedgerVersion", "LocationId", "ProductId", "Status",
                "UnreadableCode",
            ],
            typeof(InventoryAverageCostReadAnswerV1).GetProperties().Select(property => property.Name)
                .Where(name => name != "EqualityContract").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_value_carries_the_average_and_the_ledger_version_only()
    {
        var answer = InventoryAverageCostReadAnswerV1.ForValue(CompanyA, Location, Product, 10.5714m, 3);

        Assert.Equal((1, CompanyA, Location, Product, InventoryAverageCostReadStatusV1.Value, 10.5714m, null, 3L),
            (answer.ContractVersion, answer.CompanyId, answer.LocationId, answer.ProductId, answer.Status,
                answer.AverageCostPerKg, answer.UnreadableCode, answer.LedgerVersion));
    }

    [Fact]
    public void An_unreadable_carries_a_code_and_never_a_number()
    {
        var answer = InventoryAverageCostReadAnswerV1.ForUnreadable(
            CompanyA, Location, Product, InventoryAverageCostUnreadableCodesV1.NegativeStock, 2);

        Assert.Equal((InventoryAverageCostReadStatusV1.Unreadable, null, "negative_stock", 2L),
            (answer.Status, answer.AverageCostPerKg, answer.UnreadableCode, answer.LedgerVersion));
    }

    [Fact]
    public void A_no_source_carries_neither_number_nor_code_nor_version()
    {
        var answer = InventoryAverageCostReadAnswerV1.ForNoSource(CompanyA, Location, Product);

        Assert.Equal((InventoryAverageCostReadStatusV1.NoSource, null, null, null),
            (answer.Status, answer.AverageCostPerKg, answer.UnreadableCode, answer.LedgerVersion));
    }

    [Fact]
    public void Answers_refuse_empty_ids_overscaled_or_negative_averages_unknown_codes_and_bad_versions()
    {
        Assert.Throws<ArgumentException>(() => InventoryAverageCostReadAnswerV1.ForNoSource(Guid.Empty, Location, Product));
        Assert.Throws<ArgumentException>(() => InventoryAverageCostReadAnswerV1.ForNoSource(CompanyA, Guid.Empty, Product));
        Assert.Throws<ArgumentException>(() => InventoryAverageCostReadAnswerV1.ForNoSource(CompanyA, Location, Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryAverageCostReadAnswerV1.ForValue(CompanyA, Location, Product, -0.0001m, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryAverageCostReadAnswerV1.ForValue(CompanyA, Location, Product, 1.00001m, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryAverageCostReadAnswerV1.ForValue(CompanyA, Location, Product, 1m, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryAverageCostReadAnswerV1.ForUnreadable(CompanyA, Location, Product, "made_up", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => InventoryAverageCostReadAnswerV1.ForUnreadable(
            CompanyA, Location, Product, InventoryAverageCostUnreadableCodesV1.Return, 0));
    }

    [Fact]
    public void The_port_has_one_read_method_with_location_and_product_and_no_company_parameter()
    {
        var method = Assert.Single(typeof(IInventoryAverageCostReadPortV1).GetMethods());
        Assert.Equal("ReadAsync", method.Name);
        Assert.Equal([typeof(Guid), typeof(Guid), typeof(CancellationToken)], method.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.Equal(typeof(Task<InventoryAverageCostReadAnswerV1>), method.ReturnType);
    }

    [Fact]
    public void The_inventory_module_registers_the_port_over_its_own_ledger_as_a_scoped_service()
    {
        var services = new ServiceCollection().AddInventoryModule(new Settings("Server=unused.invalid;Database=unused;"));

        var registration = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IInventoryAverageCostReadPortV1));
        Assert.Equal((ServiceLifetime.Scoped, typeof(InventoryAverageCostReadPort)),
            (registration.Lifetime, registration.ImplementationType));
    }

    [Fact]
    public async Task Without_a_resolved_company_the_port_refuses_before_any_connection_and_never_reads_unscoped()
    {
        await using var context = Context(resolved: false);
        var port = new InventoryAverageCostReadPort(context, new CompanyContext());

        await Assert.ThrowsAsync<InvalidOperationException>(() => port.ReadAsync(Location, Product));
    }

    [Theory]
    [InlineData("empty-location")]
    [InlineData("empty-product")]
    public async Task An_empty_location_or_product_is_refused_as_a_caller_fault_without_a_query(string fault)
    {
        var company = new CompanyContext();
        company.Set(CompanyA);
        await using var context = Context(resolved: true);
        var port = new InventoryAverageCostReadPort(context, company);

        // The connection string points nowhere: a query would be a SqlException, not this refusal.
        await Assert.ThrowsAsync<ArgumentException>(() => port.ReadAsync(
            fault == "empty-location" ? Guid.Empty : Location, fault == "empty-product" ? Guid.Empty : Product));
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

    private static InventoryDbContext Context(bool resolved)
    {
        var company = new CompanyContext();
        if (resolved)
        {
            company.Set(CompanyA);
        }

        return new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, company);
    }
}
