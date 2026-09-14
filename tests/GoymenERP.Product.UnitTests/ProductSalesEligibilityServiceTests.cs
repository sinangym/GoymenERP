using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductSalesEligibilityServiceTests
{
    internal static readonly Guid CompanyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    internal static readonly Guid ProductId = Guid.Parse("59c57d4c-9890-4dad-823c-b3b4030b42d1");
    internal static readonly Guid StockUnitId = Guid.Parse("7a1d3b8e-2b7c-4c55-9d61-0a6f2f3b1c01");
    internal static readonly Guid SalesUnitId = Guid.Parse("7a1d3b8e-2b7c-4c55-9d61-0a6f2f3b1c02");
    internal static readonly DateOnly On = new(2026, 9, 14);
    internal const string Version = "9007199254740993";

    internal static ProductSalesSelectionV1 Selection => new(ProductId, Version, SalesUnitId, On);

    [Theory]
    [InlineData("anonymous")]
    [InlineData("no-user-id")]
    [InlineData("unresolved-company")]
    [InlineData("foreign-company")]
    public async Task Missing_server_scope_denies_before_any_owner_read(string fault)
    {
        var world = new World();
        switch (fault)
        {
            case "anonymous": world.User.IsAuthenticated = false; break;
            case "no-user-id": world.User.UserId = " "; break;
            case "unresolved-company": world.Company.IsResolved = false; break;
            default: world.User.Allowed.Clear(); break;
        }

        var error = await Assert.ThrowsAsync<ProductOperationException>(() => world.Service().EvaluateAsync(Selection, default));
        Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode), (error.StatusCode, error.Code));
        Assert.Equal(0, world.Records.Reads);
        Assert.Equal(0, world.Family.Calls);
        Assert.Equal(0, world.Settings.Calls);
    }

    [Fact]
    public async Task Empty_product_id_is_rejected_without_reading_storage()
    {
        var world = new World();
        var result = await world.Service().EvaluateAsync(Selection with { ProductId = Guid.Empty }, default);
        AssertRejected(result, "productId", "invalid_reference");
        Assert.Equal(0, world.Records.Reads);
    }

    [Fact]
    public async Task Missing_product_is_an_owner_rejection_not_an_empty_success()
    {
        var world = new World { Record = null };
        var result = await world.Service().EvaluateAsync(Selection, default);
        AssertRejected(result, "productId", "invalid_reference");
        Assert.Equal(1, world.Records.Reads);
        Assert.Equal(0, world.Family.Calls);
    }

    [Theory]
    [InlineData(0, "lifecycle_conflict")] // Draft: the only lifecycle real Product rows can have at this base.
    [InlineData(2, "lifecycle_conflict")] // Inactive
    public async Task Draft_and_inactive_products_never_reach_family_or_settings(int lifecycle, string code)
    {
        var world = new World { Record = StoredProduct(lifecycle: lifecycle) };
        var result = await world.Service().EvaluateAsync(Selection, default);
        Assert.Null(result.Snapshot);
        Assert.Contains(result.Errors, error => error.Code == code);
        Assert.Equal(0, world.Family.Calls);
        Assert.Equal(0, world.Settings.Calls);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(false, 1)]
    public async Task Unknown_or_false_sales_role_is_not_sellable(bool? sellable, int provenance)
    {
        var world = new World { Record = StoredProduct(sellable: new(sellable, provenance)) };
        var result = await world.Service().EvaluateAsync(Selection, default);
        Assert.Contains(result.Errors, error => error.Code == "not_sellable");
        Assert.Equal(0, world.Family.Calls);
    }

    [Fact]
    public async Task Stale_expected_version_is_a_conflict_even_above_javascript_precision()
    {
        var world = new World();
        var result = await world.Service().EvaluateAsync(Selection with { ExpectedProductVersion = "9007199254740992" }, default);
        Assert.Contains(result.Errors, error => error.Code == ProductApiV1.VersionConflict);
        Assert.Equal(0, world.Family.Calls);
    }

    [Fact]
    public async Task Missing_unit_bindings_are_rejected_before_family_or_settings()
    {
        var world = new World { Record = StoredProduct(units: new()) };
        var result = await world.Service().EvaluateAsync(Selection, default);
        Assert.Contains(result.Errors, error => error.Field == "references.units.stock");
        Assert.Contains(result.Errors, error => error.Field == "references.units.sales");
        Assert.Equal(0, world.Settings.Calls);
    }

    [Theory]
    [InlineData("stock")]
    [InlineData("other")]
    public async Task Order_unit_other_than_the_products_sales_unit_is_not_offered(string orderUnit)
    {
        var world = new World();
        var id = orderUnit == "stock" ? StockUnitId : Guid.NewGuid();
        var result = await world.Service().EvaluateAsync(Selection with { OrderUnitId = id }, default);
        AssertRejected(result, "orderUnitId", ProductSalesEligibilityService.UnsupportedOrderUnitCode);
        Assert.Equal(0, world.Family.Calls);
        Assert.Equal(0, world.Settings.Calls);
    }

    [Fact]
    public async Task Shipped_family_policy_makes_an_otherwise_valid_selection_unavailable_not_usable()
    {
        var world = new World();
        var service = world.Service(new UnacceptedProductFamilyRequirementPolicyV1());
        var error = await Assert.ThrowsAsync<ProductOperationException>(() => service.EvaluateAsync(Selection, default));
        Assert.Equal((503, UnacceptedProductFamilyRequirementPolicyV1.UnavailableCode), (error.StatusCode, error.Code));
        Assert.Equal(0, world.Settings.Calls);
    }

    [Fact]
    public async Task Family_policy_errors_are_returned_without_reading_unit_profiles()
    {
        var world = new World();
        world.Family.Result = [new("attributes.thicknessMm", "required")];
        var result = await world.Service().EvaluateAsync(Selection, default);
        AssertRejected(result, "attributes.thicknessMm", "required");
        Assert.Equal(0, world.Settings.Calls);
    }

    [Fact]
    public async Task Family_policy_returning_null_is_unavailable()
    {
        var world = new World();
        world.Family.Result = null;
        var error = await Assert.ThrowsAsync<ProductOperationException>(() => world.Service().EvaluateAsync(Selection, default));
        Assert.Equal((503, ProductSalesEligibilityService.FamilyPolicyInvalidCode), (error.StatusCode, error.Code));
    }

    [Fact]
    public async Task Accepted_family_and_owner_profiles_produce_the_exact_snapshot()
    {
        var world = new World();
        var result = await world.Service().EvaluateAsync(Selection, default);

        Assert.Empty(result.Errors);
        var snapshot = Assert.IsType<ProductSalesSnapshotV1>(result.Snapshot);
        Assert.Equal((ProductId, Version, "PSQL-1", "Galvaniz bant"), (snapshot.ProductId, snapshot.ProductVersion, snapshot.Code, snapshot.Name));
        Assert.Equal(new ProductSalesUnitProfileV1(CompanyId, StockUnitId, world.Settings.ProfileIdFor(StockUnitId), "9007199254740993"), snapshot.StockUnit);
        Assert.Equal(new ProductSalesUnitProfileV1(CompanyId, SalesUnitId, world.Settings.ProfileIdFor(SalesUnitId), "9007199254740993"), snapshot.SalesUnit);
        Assert.Equal(snapshot.SalesUnit, snapshot.OrderUnit);
        Assert.Equal(On, snapshot.EffectiveOn);
        Assert.Equal([(StockUnitId, On), (SalesUnitId, On)], world.Settings.Requests);
    }

    [Fact]
    public async Task Stock_unit_without_owner_kilogram_identity_cannot_pass()
    {
        var world = new World();
        world.Settings.KilogramUnits.Clear();
        var error = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => world.Service().EvaluateAsync(Selection, default));
        Assert.Equal("settings_profile_invalid", error.Code);
    }

    [Fact]
    public async Task Real_unavailable_settings_provider_stays_unavailable()
    {
        var world = new World();
        var service = new ProductSalesEligibilityService(world.Company, world.User, world.Records, world.Family,
            new ProductUnitProfileAdapter(new UnavailableSettingsPrerequisiteProvider(world.Company, world.User), world.Company));
        var error = await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => service.EvaluateAsync(Selection, default));
        Assert.Equal("settings_provider_unavailable", error.Code);
    }

    [Theory]
    [InlineData(true, "company")]
    [InlineData(false, "company")]
    [InlineData(true, "actor")]
    [InlineData(false, "actor")]
    [InlineData(true, "revoked")]
    [InlineData(false, "revoked")]
    public async Task Scope_change_during_storage_read_hides_existing_and_missing_products(bool exists, string change)
    {
        var world = new World { Record = exists ? StoredProduct() : null };
        world.Records.During = () => world.Change(change);
        var error = await Assert.ThrowsAsync<ProductOperationException>(() => world.Service().EvaluateAsync(Selection, default));
        Assert.Equal(403, error.StatusCode);
        Assert.Equal(0, world.Family.Calls);
    }

    [Theory]
    [InlineData("errors")]
    [InlineData("unavailable")]
    public async Task Scope_change_during_family_policy_hides_both_rejection_and_unavailability(string outcome)
    {
        var world = new World();
        IProductFamilyRequirementPolicyV1 policy = outcome == "errors" ? world.Family : new UnacceptedProductFamilyRequirementPolicyV1();
        world.Family.Result = [new("attributes.coating", "required")];
        var changing = new ChangingPolicy(policy, () => world.Change("company"));
        var error = await Assert.ThrowsAsync<ProductOperationException>(() => world.Service(changing).EvaluateAsync(Selection, default));
        Assert.Equal(403, error.StatusCode);
        Assert.Equal(0, world.Settings.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scope_change_during_unit_profile_read_hides_profile_success_and_failure(bool fail)
    {
        var world = new World();
        world.Settings.During = () => world.Change("actor");
        if (fail) world.Settings.Failure = new SettingsPrerequisiteException("settings_provider_unavailable");
        var error = await Assert.ThrowsAsync<ProductOperationException>(() => world.Service().EvaluateAsync(Selection, default));
        Assert.Equal(403, error.StatusCode);
    }

    [Fact]
    public async Task Cancellation_during_storage_read_propagates_without_a_result()
    {
        using var cancellation = new CancellationTokenSource();
        var world = new World();
        world.Records.During = cancellation.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => world.Service().EvaluateAsync(Selection, cancellation.Token));
        Assert.Equal(0, world.Family.Calls);
    }

    private static void AssertRejected(ProductSalesEligibilityV1 result, string field, string code)
    {
        Assert.Null(result.Snapshot);
        var error = Assert.Single(result.Errors);
        Assert.Equal((field, code), (error.Field, error.Code));
    }

    internal static ProductStoredRecord StoredProduct(int lifecycle = 1, ProductStoredRole? sellable = null,
        ProductUnitReferences? units = null) =>
        new(ProductId, "PSQL-1", "Galvaniz bant", Guid.Parse("f4fc7672-0b46-41ba-a017-393401ba61dd"), null, null,
            new(null, 0), sellable ?? new(true, 1), new(null, 0), new(null, 0), lifecycle, long.Parse(Version),
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            JsonSerializer.Serialize(new ProductReferences(Units: units ?? new(Stock: StockUnitId, Sales: SalesUnitId)),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    internal sealed class World
    {
        public Context Company { get; } = new();
        public User User { get; } = new();
        public Records Records { get; } = new();
        public Family Family { get; } = new();
        public Settings Settings { get; }
        public ProductStoredRecord? Record { get => Records.Record; init => Records.Record = value; }

        public World()
        {
            Records.Record = StoredProduct();
            Settings = new Settings(Company);
        }

        public ProductSalesEligibilityService Service(IProductFamilyRequirementPolicyV1? policy = null) =>
            new(Company, User, Records, policy ?? Family, new ProductUnitProfileAdapter(Settings, Company));

        public void Change(string change)
        {
            switch (change)
            {
                case "company":
                    var other = Guid.NewGuid();
                    User.Allowed.Add(other);
                    Company.Set(other);
                    break;
                case "actor": User.UserId = "another-actor"; break;
                default: User.Allowed.Clear(); break;
            }
        }
    }

    internal sealed class Context : ICompanyContext
    {
        public bool IsResolved { get; set; } = true;
        public Guid CompanyId { get; private set; } = ProductSalesEligibilityServiceTests.CompanyId;
        public void Set(Guid companyId) => CompanyId = companyId;
    }

    internal sealed class User : ICurrentUser
    {
        public HashSet<Guid> Allowed { get; } = [ProductSalesEligibilityServiceTests.CompanyId];
        public bool IsAuthenticated { get; set; } = true;
        public string? UserId { get; set; } = "sales-actor";
        public IReadOnlySet<Guid> AllowedCompanyIds => Allowed;
        public bool CanAccessCompany(Guid companyId) => Allowed.Contains(companyId);
    }

    internal sealed class Records : IProductSalesRecordReader
    {
        public ProductStoredRecord? Record { get; set; }
        public Action? During { get; set; }
        public int Reads { get; private set; }

        public Task<ProductStoredRecord?> ReadForSalesSelectionAsync(ProductSalesReadScope scope, Guid productId,
            CancellationToken cancellationToken)
        {
            Reads++;
            Assert.Equal(ProductId, productId);
            During?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Record?.Id == productId ? Record : null);
        }
    }

    internal sealed class Family : IProductFamilyRequirementPolicyV1
    {
        public IReadOnlyList<ProductFieldError>? Result { get; set; } = [];
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ProductFieldError>> ValidateAsync(ProductCardView card, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result!);
        }
    }

    private sealed class ChangingPolicy(IProductFamilyRequirementPolicyV1 inner, Action change) : IProductFamilyRequirementPolicyV1
    {
        public async Task<IReadOnlyList<ProductFieldError>> ValidateAsync(ProductCardView card, CancellationToken cancellationToken)
        {
            change();
            return await inner.ValidateAsync(card, cancellationToken);
        }
    }

    internal sealed class Settings(Context company) : ISettingsPrerequisiteProvider
    {
        private readonly Dictionary<Guid, Guid> profiles = [];
        public HashSet<Guid> KilogramUnits { get; } = [StockUnitId];
        public List<(Guid, DateOnly)> Requests { get; } = [];
        public Action? During { get; set; }
        public Exception? Failure { get; set; }
        public int Calls => Requests.Count;

        public Guid ProfileIdFor(Guid unitId)
        {
            if (!profiles.TryGetValue(unitId, out var id)) profiles[unitId] = id = Guid.NewGuid();
            return id;
        }

        public Task<UnitProfileV1> GetUnitProfileAsync(Guid unitId, DateOnly effectiveOn, CancellationToken cancellationToken = default)
        {
            Requests.Add((unitId, effectiveOn));
            var profile = new UnitProfileV1(company.CompanyId, unitId, unitId == StockUnitId ? "synthetic-kg" : "synthetic-sales",
                "synthetic-mass", KilogramUnits.Contains(unitId), new(ProfileIdFor(unitId), 9007199254740993L), 3, true,
                effectiveOn.AddDays(-1), null);
            During?.Invoke();
            return Failure is null ? Task.FromResult(profile) : Task.FromException<UnitProfileV1>(Failure);
        }

        public Task<MoneyProfileV1> GetMoneyProfileAsync(Guid currencyId, string operation, DateOnly effectiveOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PeriodEligibilityV1> GetPeriodEligibilityAsync(string operation, DateOnly occurredOn, DateOnly? valueOn,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

}
