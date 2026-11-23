using GoymenERP.Api.InventoryRuntime;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Product.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

/// <summary>
/// AK-1: the host adapter between Inventory's kg/unit seam and the Product owner's theoretical kg/unit read. Synthetic
/// owner replies only; it adapts DTOs, computes nothing and refuses whatever does not belong to the request.
/// </summary>
public sealed class ProductOwnerInventoryKilogramsPerUnitSourceTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Product = Guid.Parse("6f1d2c3b-4a59-4e68-8d7c-1b2a3c4d5e6f");
    private static readonly Guid Piece = Guid.Parse("5e0cbe45-61cc-541a-834a-366355de36e5");
    private static readonly Guid Policy = Guid.Parse("8d4f7a51-62a9-4f1e-8f0a-3c55a4f0b2d1");
    private static readonly Guid Profile = Guid.Parse("a2d30cad-e3ac-5484-adbc-d56daa5304f6");
    private static readonly DateOnly PolicyDate = new(2026, 11, 21);
    private const decimal Density = 7.85m;

    [Fact]
    public async Task A_found_owner_figure_is_handed_over_with_its_source_versions_for_the_same_policy_date()
    {
        var owner = new Owner(Found(31.4m, Evidence()));

        var answer = await Source(owner).ReadAsync(Product, Piece, PolicyDate, Density);

        Assert.Equal((InventoryKilogramsPerUnitOutcomeV1.Found, (decimal?)31.4m), (answer.Outcome, answer.KilogramsPerUnit));
        Assert.Equal("product:4;formula:GDC-UC/1 (DEC-PRD-006);dimension:9;weight-table:-;policy:" + Policy.ToString("D")
            + "/3;unit-profile:" + Profile.ToString("D") + "/1;unit-semantics:settings.standard-unit-semantics.v1", answer.SourceVersion);
        Assert.Equal([(Product, Piece, PolicyDate)], owner.Calls);
    }

    [Fact]
    public async Task A_card_without_a_kilogram_source_is_no_source_fail_closed()
    {
        var answer = await Source(new Owner(ProductTheoreticalKilogramsPerUnitAnswerV1.Without(
            ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource, ProductTheoreticalKilogramsPerUnitCodesV1.UnitNotOnCard)))
            .ReadAsync(Product, Piece, PolicyDate, Density);

        Assert.Equal(new InventoryKilogramsPerUnitV1(InventoryKilogramsPerUnitOutcomeV1.NoSource, null, null), answer);
    }

    public static TheoryData<string> Unprovable => ["refused", "not-found", "denied", "unavailable", "other-density",
        "other-product", "other-unit", "other-company"];

    [Theory]
    [MemberData(nameof(Unprovable))]
    public async Task Any_owner_answer_that_is_not_a_provable_figure_for_this_request_is_unavailable(string kind)
    {
        var answer = kind switch
        {
            "refused" => ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.Refused,
                ProductTheoreticalKilogramsPerUnitCodesV1.WeightConflict),
            "not-found" => ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.NotFound),
            "denied" => ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.Denied),
            "unavailable" => ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.Unavailable,
                ProductTheoreticalKilogramsPerUnitCodesV1.Unavailable),
            // The owner computed with another policy version's density than Inventory read: never mix two versions.
            "other-density" => Found(31.4m, Evidence() with { DensityGramsPerCubicCentimetre = 7.86m }),
            "other-product" => Found(31.4m, Evidence() with { ProductId = Guid.NewGuid() }),
            "other-unit" => Found(31.4m, Evidence() with { UnitId = Guid.NewGuid() }),
            _ => Found(31.4m, Evidence() with { CompanyId = Guid.NewGuid() }),
        };

        var result = await Source(new Owner(answer)).ReadAsync(Product, Piece, PolicyDate, Density);

        Assert.Equal(new InventoryKilogramsPerUnitV1(InventoryKilogramsPerUnitOutcomeV1.Unavailable, null, null), result);
    }

    [Fact]
    public async Task A_fixed_mass_unit_without_a_policy_needs_no_density_match()
    {
        var answer = await Source(new Owner(Found(1000m, Evidence() with
        {
            UnitKind = ProductPhysicalUnitKindV1.Tonne, PolicyId = null, PolicyVersion = null, DensityGramsPerCubicCentimetre = null,
        }))).ReadAsync(Product, Piece, PolicyDate, Density);

        Assert.Equal((InventoryKilogramsPerUnitOutcomeV1.Found, (decimal?)1000m), (answer.Outcome, answer.KilogramsPerUnit));
        Assert.Contains("policy:-", answer.SourceVersion, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unresolved_company_or_a_company_switch_during_the_read_is_unavailable()
    {
        var unresolved = new Owner(Found(31.4m, Evidence()));
        var context = new MutableCompany();

        Assert.Equal(InventoryKilogramsPerUnitOutcomeV1.Unavailable,
            (await new ProductOwnerInventoryKilogramsPerUnitSource(unresolved, context)
                .ReadAsync(Product, Piece, PolicyDate, Density)).Outcome);
        Assert.Empty(unresolved.Calls);

        context.Set(Company);
        var switching = new Owner(Found(31.4m, Evidence())) { During = () => context.Set(Guid.NewGuid()) };
        Assert.Equal(InventoryKilogramsPerUnitOutcomeV1.Unavailable,
            (await new ProductOwnerInventoryKilogramsPerUnitSource(switching, context)
                .ReadAsync(Product, Piece, PolicyDate, Density)).Outcome);
    }

    private static ProductOwnerInventoryKilogramsPerUnitSource Source(Owner owner)
    {
        var context = new CompanyContext();
        context.Set(Company);
        return new(owner, context);
    }

    private static ProductTheoreticalKilogramsPerUnitAnswerV1 Found(decimal kilogramsPerUnit,
        ProductTheoreticalKilogramsPerUnitEvidenceV1 evidence) =>
        ProductTheoreticalKilogramsPerUnitAnswerV1.Found(kilogramsPerUnit, evidence);

    private static ProductTheoreticalKilogramsPerUnitEvidenceV1 Evidence() => new(Company, Product, Piece,
        ProductPhysicalUnitKindV1.Piece, "4", "GDC-UC/1 (DEC-PRD-006)", 9, null, null, Policy, 3, Density, Profile, 1,
        "settings.standard-unit-semantics.v1");

    /// <summary>A context whose company can change mid-read (the real one refuses that), to prove the fence.</summary>
    private sealed class MutableCompany : ICompanyContext
    {
        public bool IsResolved { get; private set; }

        public Guid CompanyId { get; private set; }

        public void Set(Guid companyId)
        {
            CompanyId = companyId;
            IsResolved = true;
        }
    }

    private sealed class Owner(ProductTheoreticalKilogramsPerUnitAnswerV1 answer) : IProductTheoreticalKilogramsPerUnitReadPortV1
    {
        public List<(Guid ProductId, Guid UnitId, DateOnly EffectiveOn)> Calls { get; } = [];

        public Action? During { get; init; }

        public Task<ProductTheoreticalKilogramsPerUnitAnswerV1> ReadAsync(Guid productId, Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((productId, unitId, effectiveOn));
            During?.Invoke();
            return Task.FromResult(answer);
        }
    }
}
