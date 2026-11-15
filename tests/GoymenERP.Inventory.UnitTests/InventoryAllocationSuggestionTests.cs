using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Inventory.UnitTests;

/// <summary>
/// KLN-D34/D39/D40/D70 (STK-S30) without a database: the module's fail-closed defaults, the server-side substitute check
/// and the suggestion read's refusals before any connection. What the read answers over real rows is proven on SQL
/// Server (<c>InventoryAllocationSuggestionSqlServerRelationalAcceptanceTests</c>). SENTETIK / NOT_ENV.
/// </summary>
public sealed class InventoryAllocationSuggestionTests
{
    private static readonly Guid Company = Guid.Parse("12000000-0000-0000-0000-000000000001");
    private static readonly InventoryProductVersionV1 Demand =
        new(Guid.Parse("22000000-0000-0000-0000-000000000001"), "3");
    private static readonly InventoryProductVersionV1 Offered =
        new(Guid.Parse("22000000-0000-0000-0000-000000000002"), "5");

    [Fact]
    public void The_module_registers_fail_closed_defaults_the_host_can_replace()
    {
        var services = new ServiceCollection().AddInventoryModule(new Settings("Server=unused.invalid;Database=unused;"));

        Assert.Equal(typeof(UnconfiguredInventorySimilarProductToleranceSourceV1), Implementation<IInventorySimilarProductToleranceSourceV1>(services));
        Assert.Equal(typeof(UnavailableInventoryProductSimilarityFactsV1), Implementation<IInventoryProductSimilarityFactsV1>(services));
        Assert.Equal(typeof(DisabledInventoryAllocationAiSuggestionToolV1), Implementation<IInventoryAllocationAiSuggestionToolV1>(services));
        Assert.Equal(typeof(InventorySubstituteProductVerifier), Implementation<IInventorySubstituteProductVerifierV1>(services));
        Assert.Equal(typeof(InventoryAllocationSuggestionReader), Implementation<IInventoryAllocationSuggestionReaderV1>(services));
        Assert.False(new DisabledInventoryAllocationAiSuggestionToolV1().IsEnabled);
    }

    [Fact]
    public async Task The_unconfigured_defaults_answer_no_tolerance_and_refuse_facts()
    {
        Assert.Null(await new UnconfiguredInventorySimilarProductToleranceSourceV1().GetAsync(Company));
        var refused = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
            new UnavailableInventoryProductSimilarityFactsV1().ReadAsync([Demand]));
        Assert.Equal((503, InventoryAllocationSuggestionCodesV1.ProductFactsUnavailable), (refused.Status, refused.Code));
    }

    [Fact]
    public async Task Without_a_tolerance_no_substitute_is_accepted_and_Product_is_never_asked()
    {
        var products = new FakeFacts();
        var verifier = new InventorySubstituteProductVerifier(new FixedTolerance(null), products);

        Assert.False(await verifier.IsSimilarAsync(Company, Demand, Offered));
        Assert.Equal(0, products.Reads);
    }

    [Fact]
    public async Task A_substitute_within_the_tolerance_and_with_the_same_key_is_accepted()
    {
        var products = new FakeFacts
        {
            Answer = [Facts(Demand, "key", 1m, 100m), Facts(Offered, "key", 1.05m, 98m)],
        };
        var verifier = new InventorySubstituteProductVerifier(new FixedTolerance(new(10m, 10m, 10m)), products);

        Assert.True(await verifier.IsSimilarAsync(Company, Demand, Offered));
        Assert.Equal([Demand, Offered], products.Requested);
    }

    [Theory]
    [InlineData("key")]
    [InlineData("width")]
    [InlineData("nofacts")]
    public async Task A_substitute_outside_the_rule_is_refused(string breach)
    {
        var products = new FakeFacts
        {
            Answer =
            [
                Facts(Demand, "key", 1m, 100m),
                breach switch
                {
                    "key" => Facts(Offered, "other", 1m, 100m),
                    "width" => Facts(Offered, "key", 1m, 120m),
                    _ => Facts(Offered, null, null, null),
                },
            ],
        };
        var verifier = new InventorySubstituteProductVerifier(new FixedTolerance(new(10m, 10m, 10m)), products);

        Assert.False(await verifier.IsSimilarAsync(Company, Demand, Offered));
    }

    [Fact]
    public async Task The_same_product_is_never_a_substitute()
    {
        var products = new FakeFacts();
        var verifier = new InventorySubstituteProductVerifier(new FixedTolerance(new(10m, 10m, 10m)), products);

        Assert.False(await verifier.IsSimilarAsync(Company, Demand, Demand with { ProductVersion = "4" }));
        Assert.Equal(0, products.Reads);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    public async Task An_unreliable_owner_answer_is_503_never_a_guess(string defect)
    {
        var products = new FakeFacts
        {
            Answer = defect switch
            {
                "missing" => [Facts(Demand, "key", 1m, 100m)],
                "duplicate" => [Facts(Demand, "key", 1m, 100m), Facts(Demand, "key", 1m, 100m)],
                _ => [Facts(Demand, "key", 1m, 100m), Facts(Offered with { ProductVersion = "6" }, "key", 1m, 100m)],
            },
        };
        var verifier = new InventorySubstituteProductVerifier(new FixedTolerance(new(10m, 10m, 10m)), products);

        var refused = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
            verifier.IsSimilarAsync(Company, Demand, Offered));
        Assert.Equal((503, InventoryAllocationSuggestionCodesV1.ProductFactsUnavailable), (refused.Status, refused.Code));
    }

    [Fact]
    public async Task A_malformed_Settings_tolerance_is_503_never_no_tolerance()
    {
        var verifier = new InventorySubstituteProductVerifier(new FixedTolerance(new(150m, 10m, 10m)), new FakeFacts());

        var refused = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
            verifier.IsSimilarAsync(Company, Demand, Offered));
        Assert.Equal((503, InventoryAllocationSuggestionCodesV1.ToleranceUnavailable), (refused.Status, refused.Code));
    }

    [Fact]
    public async Task The_suggestion_read_refuses_without_a_resolved_company_or_with_an_incomplete_query()
    {
        await using var context = Context(resolved: false);
        var unresolved = new InventoryAllocationSuggestionReader(context, new CompanyContext(),
            new FixedTolerance(null), new FakeFacts(), new DisabledInventoryAllocationAiSuggestionToolV1());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            unresolved.ReadAsync(new(Guid.NewGuid(), Guid.NewGuid(), Demand.ProductId, Demand.ProductVersion)));

        var company = new CompanyContext();
        company.Set(Company);
        await using var resolvedContext = Context(resolved: true);
        var reader = new InventoryAllocationSuggestionReader(resolvedContext, company,
            new FixedTolerance(null), new FakeFacts(), new DisabledInventoryAllocationAiSuggestionToolV1());
        var refused = await Assert.ThrowsAsync<InventoryAllocationException>(() =>
            reader.ReadAsync(new(Guid.NewGuid(), Guid.NewGuid(), Demand.ProductId, " ")));
        Assert.Equal((400, InventoryAllocationCodesV1.ValidationFailed), (refused.Status, refused.Code));
    }

    private static Type? Implementation<TService>(IServiceCollection services) =>
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(TService)).ImplementationType;

    private static InventoryProductSimilarityFactsV1 Facts(
        InventoryProductVersionV1 product,
        string? key,
        decimal? thickness,
        decimal? width) =>
        new(product.ProductId, product.ProductVersion, "SYN-P", "Sentetik ürün", key, thickness, width, null);

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

    private sealed class FixedTolerance(InventorySimilarProductToleranceV1? tolerance) : IInventorySimilarProductToleranceSourceV1
    {
        public Task<InventorySimilarProductToleranceV1?> GetAsync(Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(tolerance);
    }

    private sealed class FakeFacts : IInventoryProductSimilarityFactsV1
    {
        public IReadOnlyList<InventoryProductSimilarityFactsV1> Answer { get; init; } = [];

        public IReadOnlyList<InventoryProductVersionV1> Requested { get; private set; } = [];

        public int Reads { get; private set; }

        public Task<IReadOnlyList<InventoryProductSimilarityFactsV1>> ReadAsync(
            IReadOnlyList<InventoryProductVersionV1> selections,
            CancellationToken cancellationToken = default)
        {
            Reads++;
            Requested = [.. selections];
            return Task.FromResult(Answer);
        }
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
}
