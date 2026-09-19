using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using static GoymenERP.Product.UnitTests.ProductSalesEligibilityServiceTests;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// T07: the stock port asks only what stock needs (DEC-PRD-010 Active, DEC-PRD-007 kg stock unit,
/// the family policy) behind the same fenced authority as the sales port. It reuses the sales tests'
/// synthetic world; every identity is synthetic.
/// </summary>
public sealed class ProductStockEligibilityServiceTests
{
    private const string StoredVersion = ProductSalesEligibilityServiceTests.Version;

    private static ProductStockSelectionV1 StockSelection => new(ProductId, StoredVersion, On);

    private static ProductStockEligibilityService StockService(World world,
        IProductFamilyRequirementPolicyV1? policy = null, IErpAuthorityRevalidationService? authority = null) =>
        new(world.Company, world.User, authority ?? world.Authorization, world.Records, policy ?? world.Family,
            new ProductUnitProfileAdapter(world.Settings, world.Company));

    [Fact]
    public async Task Without_the_narrow_selection_permission_nothing_is_read()
    {
        var world = new World();
        world.Authorization.Granted = false;

        var error = await Assert.ThrowsAsync<ProductOperationException>(
            () => StockService(world).EvaluateAsync(StockSelection with { ProductId = Guid.Empty }, default));

        Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode), (error.StatusCode, error.Code));
        Assert.Equal((0, 0, 0), (world.Records.Reads, world.Family.Calls, world.Settings.Calls));
        Assert.Equal([ProductPermissionManifestV1.OperationalSelectionRead], world.Authorization.AskedPermissions);
    }

    [Fact]
    public async Task An_active_card_with_a_kilogram_stock_profile_gives_the_stock_snapshot_and_reads_only_the_stock_unit()
    {
        var world = new World();

        var result = await StockService(world).EvaluateAsync(StockSelection, default);

        Assert.Empty(result.Errors);
        var snapshot = Assert.IsType<ProductStockSnapshotV1>(result.Snapshot);
        Assert.Equal((ProductId, StoredVersion, "PSQL-1", "Galvaniz bant", On),
            (snapshot.ProductId, snapshot.ProductVersion, snapshot.Code, snapshot.Name, snapshot.EffectiveOn));
        Assert.Equal(new ProductSalesUnitProfileV1(CompanyId, StockUnitId, world.Settings.ProfileIdFor(StockUnitId), "9007199254740993"),
            snapshot.StockUnit);
        // The sales unit is sales' question: not read for stock.
        Assert.Equal([(StockUnitId, On)], world.Settings.Requests);
        // One measurement before the record read and one after each of the three awaited reads.
        Assert.Equal(4, world.Authorization.Calls);
        Assert.All(world.Authorization.AskedFor, company => Assert.Equal(CompanyId, company));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(false, 1)]
    public async Task No_role_is_required_to_stock_so_a_card_that_is_not_sellable_is_still_stockable(bool? sellable, int provenance)
    {
        var world = new World { Record = StoredProduct(sellable: new(sellable, provenance)) };

        var result = await StockService(world).EvaluateAsync(StockSelection, default);

        Assert.Empty(result.Errors);
        Assert.NotNull(result.Snapshot);
    }

    [Fact]
    public async Task A_missing_sales_binding_is_not_a_stock_precondition_the_family_policy_decides_the_card()
    {
        var world = new World { Record = StoredProduct(units: new(Stock: StockUnitId)) };

        Assert.NotNull((await StockService(world).EvaluateAsync(StockSelection, default)).Snapshot);

        // The same card under a family whose accepted rules require the sales binding is refused by that rule.
        world.Family.Result = [new("references.units.sales", "required")];
        var refused = await StockService(world).EvaluateAsync(StockSelection, default);
        Assert.Null(refused.Snapshot);
        Assert.Equal(("references.units.sales", "required"), (refused.Errors.Single().Field, refused.Errors.Single().Code));
    }

    [Theory]
    [InlineData(0)] // Draft: DEC-PRD-010, never in a definitive transaction.
    [InlineData(2)] // Inactive
    public async Task Draft_and_inactive_cards_are_not_stockable_and_nothing_further_is_read(int lifecycle)
    {
        var world = new World { Record = StoredProduct(lifecycle: lifecycle) };

        var result = await StockService(world).EvaluateAsync(StockSelection, default);

        Assert.Null(result.Snapshot);
        Assert.Contains(result.Errors, error => (error.Field, error.Code) == ("productId", ProductApiV1.LifecycleConflict));
        Assert.Equal((0, 0), (world.Family.Calls, world.Settings.Calls));
    }

    [Fact]
    public async Task A_missing_stock_binding_or_a_stale_version_is_refused_before_family_or_settings()
    {
        var world = new World { Record = StoredProduct(units: new(Sales: SalesUnitId)) };
        var missing = await StockService(world).EvaluateAsync(StockSelection with { ExpectedProductVersion = "9007199254740992" }, default);

        Assert.Null(missing.Snapshot);
        Assert.Contains(missing.Errors, error => (error.Field, error.Code) == ("references.units.stock", "invalid_reference"));
        Assert.Contains(missing.Errors, error => (error.Field, error.Code) == ("expectedProductVersion", ProductApiV1.VersionConflict));
        Assert.Equal((0, 0), (world.Family.Calls, world.Settings.Calls));
    }

    [Theory]
    [InlineData("not-kilogram")]
    [InlineData("expired")]
    [InlineData("inactive")]
    public async Task A_stock_profile_that_is_not_kilogram_or_not_in_effect_rejects_the_stock_reference(string fault)
    {
        var world = new World();
        switch (fault)
        {
            case "not-kilogram":
                world.Settings.KilogramUnits.Clear();
                break;
            case "expired":
                world.Settings.Shape = (_, profile) => profile with { EffectiveFrom = On.AddDays(-10), EffectiveThrough = On.AddDays(-1) };
                break;
            default:
                world.Settings.Shape = (_, profile) => profile with { IsActive = false };
                break;
        }

        var result = await StockService(world).EvaluateAsync(StockSelection, default);

        Assert.Null(result.Snapshot);
        Assert.Equal(("references.units.stock", "invalid_reference"), (result.Errors.Single().Field, result.Errors.Single().Code));
    }

    /// <summary>
    /// #335 P3-1: a company WITHOUT a usable kg profile is not <c>invalid_reference</c>. The real approved Settings
    /// provider's own answer passes through as a Settings exception — no snapshot, no field error:
    /// <c>settings_provider_unavailable</c> for a company with no approved source (company B), and
    /// <c>settings_unit_profile_not_found</c> for an approved source without the card's stock unit.
    /// (<c>invalid_reference</c> is only a well-formed profile that cannot be used on the date, above.)
    /// </summary>
    [Fact]
    public async Task A_company_without_an_approved_kg_profile_gets_the_settings_answer_not_invalid_reference()
    {
        var companyB = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var world = new World();
        world.Company.Set(companyB);
        world.User.Allowed.Add(companyB);
        var inB = new ProductStockEligibilityService(world.Company, world.User, new GrantedIn(companyB), world.Records, world.Family,
            new ProductUnitProfileAdapter(new ApprovedSettingsPrerequisiteProvider(world.Company, world.User), world.Company));

        var noSource = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => inB.EvaluateAsync(StockSelection, default));
        Assert.Equal("settings_provider_unavailable", noSource.Code);

        // GDC has an approved source, but not for this card's (synthetic) stock unit.
        var gdc = new World();
        var inGdc = new ProductStockEligibilityService(gdc.Company, gdc.User, gdc.Authorization, gdc.Records, gdc.Family,
            new ProductUnitProfileAdapter(new ApprovedSettingsPrerequisiteProvider(gdc.Company, gdc.User), gdc.Company));
        var noUnit = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => inGdc.EvaluateAsync(StockSelection, default));
        Assert.Equal(SettingsPrerequisiteCodesV1.UnitProfileNotFound, noUnit.Code);
    }

    [Fact]
    public async Task The_shipped_family_policy_keeps_every_otherwise_valid_selection_unavailable()
    {
        var world = new World();

        var error = await Assert.ThrowsAsync<ProductOperationException>(
            () => StockService(world, new UnacceptedProductFamilyRequirementPolicyV1()).EvaluateAsync(StockSelection, default));

        Assert.Equal((503, UnacceptedProductFamilyRequirementPolicyV1.UnavailableCode), (error.StatusCode, error.Code));
        Assert.Equal(0, world.Settings.Calls);
    }

    [Fact]
    public async Task A_family_policy_answering_nothing_is_unavailable()
    {
        var world = new World();
        world.Family.Result = null;

        var error = await Assert.ThrowsAsync<ProductOperationException>(() => StockService(world).EvaluateAsync(StockSelection, default));

        Assert.Equal((503, ProductSalesEligibilityService.FamilyPolicyInvalidCode), (error.StatusCode, error.Code));
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("company")]
    [InlineData("actor")]
    public async Task A_change_during_the_record_read_hides_the_result_behind_the_same_403(string change)
    {
        var world = new World();
        world.Records.During = () =>
        {
            if (change == "revoked")
            {
                world.Authorization.Granted = false;
            }
            else
            {
                world.Change(change);
            }
        };

        var error = await Assert.ThrowsAsync<ProductOperationException>(() => StockService(world).EvaluateAsync(StockSelection, default));

        Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode), (error.StatusCode, error.Code));
        Assert.Equal((0, 0), (world.Family.Calls, world.Settings.Calls));
    }

    [Fact]
    public async Task A_revocation_during_the_stock_profile_read_hides_its_answer()
    {
        var world = new World();
        world.Settings.During = () => world.Authorization.Granted = false;

        var error = await Assert.ThrowsAsync<ProductOperationException>(() => StockService(world).EvaluateAsync(StockSelection, default));

        Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode), (error.StatusCode, error.Code));
    }

    /// <summary>The narrow selection permission, granted company-wide in exactly one company.</summary>
    private sealed class GrantedIn(Guid company) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(companyId == company && permission == ProductPermissionManifestV1.OperationalSelectionRead
                ? GrantedScope(companyWide: true)
                : PermissionScope.Denied);
    }
}
