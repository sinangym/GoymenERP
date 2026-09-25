using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Infrastructure.Fixtures;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Extensions.DependencyInjection;
using static GoymenERP.Product.UnitTests.ProductSalesEligibilityServiceTests;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductPurchaseSelectionTests
{
    private const string Version = ProductSalesEligibilityServiceTests.Version;
    private static readonly Guid PurchaseUnit = Guid.Parse("7a1d3b8e-2b7c-4c55-9d61-0a6f2f3b1c03");
    private static ProductPurchaseSelectionV1 Selection => new(ProductId, Version, PurchaseUnit, On);

    [Fact]
    public async Task Actual_family_policy_accepts_purchase_only_without_sales_and_keeps_sales_policy_closed()
    {
        var w = new PurchaseWorld();
        var references = new ProductReferences(ProductTechnicalCardFixture.TemplateId, "1",
            new Dictionary<string, JsonElement>
            {
                ["syn-thickness-mm"] = JsonSerializer.SerializeToElement(1.000m),
                ["syn-width-mm"] = JsonSerializer.SerializeToElement(1000.000m),
                ["syn-grade"] = JsonSerializer.SerializeToElement("SYN-A"),
                ["syn-coating"] = JsonSerializer.SerializeToElement("syn-none"),
            }, new ProductUnitReferences(Stock: StockUnitId, Purchase: PurchaseUnit));
        w.Reader.Record = Record() with { FamilyId = ProductTechnicalCardFixture.FamilyId,
            ReferencesJson = JsonSerializer.Serialize(references, new JsonSerializerOptions(JsonSerializerDefaults.Web)) };
        var policy = new ProductPurchaseFamilyRequirementPolicyV1(new Definition());
        var service = new ProductPurchaseEligibilityService(w.Common.Company, w.Common.User, w.Common.Authorization,
            w.Reader, policy, new(w.Common.Settings, w.Common.Company));
        var answer = await service.EvaluateAsync(Selection, default);
        Assert.NotNull(answer.Snapshot);
        Assert.Empty(answer.Errors);
        var card = new ProductCardView(ProductDraftView.FromProduct(w.Reader.Record.RestoreProduct()), references);
        var salesErrors = await new ProductFamilyRequirementPolicyV2(new Definition()).ValidateAsync(card, default);
        Assert.Contains(new ProductFieldError("references.units.sales", ProductFamilyPolicyCodesV1.Required), salesErrors);
        var malformed = card with { References = references with { TemplateVersion = "99" } };
        Assert.NotEmpty(await policy.ValidateAsync(malformed, default));
    }

    private sealed class Definition : IProductFamilyDefinitionSourceV2
    {
        public Task<ProductFamilyDefinitionV2> GetAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ProductTechnicalCardFixture.Definition());
    }

    [Fact]
    public void Candidate_contract_keeps_exact_versions_as_strings_and_only_purchase_projection()
    {
        var candidate = new ProductPurchaseCandidatesV1([new(ProductId, Version, "PSQL-1", "Purchase",
            PurchaseUnit, "kg", Guid.Parse("4a790fdc-2a6b-5126-835f-d3a13d28b71c"), Version)], true);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(candidate, options));
        var item = json.RootElement.GetProperty("items")[0];
        Assert.Equal(new[] { "productId", "productVersion", "code", "name", "unitId", "unitCode", "profileId", "profileVersion" },
            item.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.String, item.GetProperty("productVersion").ValueKind);
        Assert.Equal(Version, item.GetProperty("profileVersion").GetString());
        var roundTrip = JsonSerializer.Deserialize<ProductPurchaseCandidatesV1>(json.RootElement.GetRawText(), options)!;
        Assert.Equal(candidate.Items.Single(), roundTrip.Items.Single());
        Assert.True(roundTrip.SearchWindowFull);
    }

    [Fact]
    public async Task Purchase_uses_own_role_and_unit_not_sales_alias()
    {
        var w = new PurchaseWorld();
        var result = await w.Service().EvaluateAsync(Selection, default);
        Assert.Empty(result.Errors);
        var snapshot = Assert.IsType<ProductPurchaseSnapshotV1>(result.Snapshot);
        Assert.Equal((ProductId, Version, PurchaseUnit, CompanyId, On),
            (snapshot.ProductId, snapshot.ProductVersion, snapshot.PurchaseUnit.UnitId, snapshot.PurchaseUnit.CompanyId, snapshot.EffectiveOn));
        Assert.Equal(w.Common.Settings.ProfileIdFor(PurchaseUnit), snapshot.PurchaseUnit.ProfileId);
        Assert.Equal([(PurchaseUnit, On)], w.Common.Settings.Requests);
        Assert.Equal(1, w.Common.Family.Calls);
        Assert.All(w.Common.Authorization.AskedPermissions, p => Assert.Equal(ProductPermissionManifestV1.OperationalSelectionRead, p));
    }

    [Theory]
    [InlineData("role", "productId", "not_purchasable")]
    [InlineData("draft", "productId", "lifecycle_conflict")]
    [InlineData("inactive", "productId", "lifecycle_conflict")]
    [InlineData("version", "expectedProductVersion", "version_conflict")]
    [InlineData("invalid-version", "expectedProductVersion", "invalid_version")]
    [InlineData("unit", "unitId", "unsupported_purchase_unit")]
    [InlineData("empty-unit", "unitId", "invalid_reference")]
    [InlineData("missing-unit", "references.units.purchase", "invalid_reference")]
    [InlineData("missing-product", "productId", "invalid_reference")]
    public async Task Refusal_channels_are_explicit(string fault, string field, string code)
    {
        var w = new PurchaseWorld();
        var request = Selection;
        switch (fault)
        {
            case "role": w.Reader.Record = w.Reader.Record! with { Purchasable = new(false, 1) }; break;
            case "draft": w.Reader.Record = w.Reader.Record! with { Lifecycle = 0 }; break;
            case "inactive": w.Reader.Record = w.Reader.Record! with { Lifecycle = 2 }; break;
            case "version": request = request with { ExpectedProductVersion = "1" }; break;
            case "invalid-version": request = request with { ExpectedProductVersion = "01" }; break;
            case "unit": request = request with { UnitId = SalesUnitId }; break;
            case "empty-unit": request = request with { UnitId = Guid.Empty }; break;
            case "missing-unit": w.Reader.Record = Record(new(Stock: StockUnitId, Sales: SalesUnitId)); break;
            default: w.Reader.Record = null; break;
        }
        var result = await w.Service().EvaluateAsync(request, default);
        Assert.Null(result.Snapshot);
        Assert.Contains(new ProductFieldError(field, code), result.Errors);
        Assert.Equal(0, w.Common.Settings.Calls);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("future")]
    [InlineData("expired")]
    public async Task Settings_unusable_profile_is_a_selection_refusal(string fault)
    {
        var w = new PurchaseWorld();
        w.Common.Settings.Shape = (_, p) => fault switch
        {
            "inactive" => p with { IsActive = false },
            "future" => p with { EffectiveFrom = On.AddDays(1) },
            _ => p with { EffectiveThrough = On.AddDays(-1) },
        };
        var result = await w.Service().EvaluateAsync(Selection, default);
        Assert.Null(result.Snapshot);
        Assert.Equal([new ProductFieldError("references.units.purchase", "invalid_reference")], result.Errors);
    }

    [Theory]
    [InlineData("company")]
    [InlineData("unit")]
    [InlineData("version")]
    public async Task Malformed_or_foreign_Settings_profile_is_never_positive(string fault)
    {
        var w = new PurchaseWorld();
        w.Common.Settings.Shape = (_, p) => fault switch
        {
            "company" => p with { CompanyId = Guid.NewGuid() },
            "unit" => p with { UnitId = Guid.NewGuid() },
            _ => p with { Profile = p.Profile with { Version = 0 } },
        };
        await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => w.Service().EvaluateAsync(Selection, default));
    }

    [Fact]
    public async Task Denied_scope_precedes_reads_and_invalid_arguments()
    {
        var w = new PurchaseWorld();
        w.Common.Authorization.Granted = false;
        Assert.Equal(403, (await Assert.ThrowsAsync<ProductOperationException>(() => w.Service().EvaluateAsync(null!, default))).StatusCode);
        Assert.Equal(0, w.Reader.Reads);
        Assert.Equal(403, (await Assert.ThrowsAsync<ProductOperationException>(() => w.Candidates().SearchAsync(null!, default))).StatusCode);
    }

    [Theory]
    [InlineData("grant", "record")]
    [InlineData("company", "record")]
    [InlineData("actor", "record")]
    [InlineData("grant", "settings")]
    [InlineData("company", "settings")]
    [InlineData("actor", "settings")]
    [InlineData("grant", "window")]
    [InlineData("company", "window")]
    [InlineData("actor", "window")]
    public async Task Awaited_reads_cannot_release_after_revocation_or_scope_change(string change, string stage)
    {
        var w = new PurchaseWorld();
        void Change() { if (change == "grant") w.Common.Authorization.Granted = false; else w.Common.Change(change); }
        if (stage == "settings") w.Common.Settings.During = Change;
        else w.Reader.During = Change;
        var exception = stage == "window"
            ? await Assert.ThrowsAsync<ProductOperationException>(() => w.Candidates().SearchAsync(new(null, On), default))
            : await Assert.ThrowsAsync<ProductOperationException>(() => w.Service().EvaluateAsync(Selection, default));
        Assert.Equal((403, "product_access_denied"), (exception.StatusCode, exception.Code));
    }

    [Fact]
    public async Task Failed_read_after_revocation_releases_denial_only()
    {
        var w = new PurchaseWorld();
        w.Common.Settings.During = () => w.Common.Authorization.Granted = false;
        w.Common.Settings.Failure = new SettingsPrerequisiteException("settings_provider_unavailable");
        Assert.Equal(403, (await Assert.ThrowsAsync<ProductOperationException>(() => w.Service().EvaluateAsync(Selection, default))).StatusCode);
    }

    [Fact]
    public async Task Provider_unavailable_and_family_failure_never_become_positive_or_empty_success()
    {
        var w = new PurchaseWorld();
        w.Common.Settings.Failure = new SettingsPrerequisiteException("settings_provider_unavailable");
        await Assert.ThrowsAsync<SettingsPrerequisiteException>(() => w.Candidates().SearchAsync(new(null, On), default));
        w.Common.Settings.Failure = null;
        w.Common.Family.Result = [new("references.templateId", "template_mismatch")];
        Assert.Null((await w.Service().EvaluateAsync(Selection, default)).Snapshot);
        Assert.Empty((await w.Candidates().SearchAsync(new(null, On), default)).Items);
        w.Common.Family.Result = null;
        Assert.Equal(503, (await Assert.ThrowsAsync<ProductOperationException>(() => w.Candidates().SearchAsync(new(null, On), default))).StatusCode);
    }

    [Fact]
    public async Task Candidates_return_exact_purchase_profile_and_omit_stale_or_ineligible_rows()
    {
        var w = new PurchaseWorld();
        w.Reader.HasMore = true;
        var answer = await w.Candidates().SearchAsync(new("  PSQL  ", On), default);
        var item = Assert.Single(answer.Items);
        Assert.Equal((ProductId, Version, PurchaseUnit, w.Common.Settings.ProfileIdFor(PurchaseUnit), Version),
            (item.ProductId, item.ProductVersion, item.UnitId, item.ProfileId, item.ProfileVersion));
        Assert.Equal("PSQL", w.Reader.Query!.Q);
        Assert.True(answer.SearchWindowFull);
        w.Reader.Record = Record() with { Version = long.Parse(Version) + 1 };
        Assert.Empty((await w.Candidates().SearchAsync(new(null, On), default)).Items);
        w.Reader.Record = Record() with { Purchasable = new(false, 1) };
        Assert.Empty((await w.Candidates().SearchAsync(new(null, On), default)).Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Candidate_bounds_fail_before_storage(int limit)
    {
        var w = new PurchaseWorld();
        await Assert.ThrowsAsync<ProductValidationException>(() => w.Candidates().SearchAsync(new(null, On, limit), default));
        Assert.Equal(0, w.Reader.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_runtime_or_provider_composition_is_503_without_resolving_readers(bool enabled)
    {
        var services = new ServiceCollection();
        services.AddProductPurchaseSelection(_ => enabled);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(503, (await Assert.ThrowsAsync<ProductOperationException>(() => provider.GetRequiredService<IProductPurchaseEligibilityV1>()
            .EvaluateAsync(Selection, default))).StatusCode);
        Assert.Equal(503, (await Assert.ThrowsAsync<ProductOperationException>(() => provider.GetRequiredService<IProductPurchaseCandidatesV1>()
            .SearchAsync(new(null, On), default))).StatusCode);
    }

    private static ProductStoredRecord Record(ProductUnitReferences? units = null) => StoredProduct() with
    {
        Purchasable = new(true, 1), Sellable = new(false, 1),
        ReferencesJson = JsonSerializer.Serialize(new ProductReferences(Units: units ?? new(Stock: StockUnitId,
            Sales: SalesUnitId, Purchase: PurchaseUnit)), new JsonSerializerOptions(JsonSerializerDefaults.Web)),
    };
    private sealed class PurchaseWorld
    {
        public World Common { get; } = new();
        public Reader Reader { get; } = new();
        public ProductPurchaseEligibilityService Service() => new(Common.Company, Common.User, Common.Authorization,
            Reader, new FamilyAdapter(Common.Family), new(Common.Settings, Common.Company));
        public ProductPurchaseCandidateService Candidates() => new(Common.Company, Common.User, Common.Authorization, Reader, Service());
    }
    private sealed class FamilyAdapter(Family family) : IProductPurchaseFamilyRequirementPolicyV1
    {
        public Task<IReadOnlyList<ProductFieldError>> ValidateAsync(ProductCardView card, CancellationToken ct) => family.ValidateAsync(card, ct);
    }
    private sealed class Reader : IProductPurchaseRecordReader, IProductPurchaseCandidateReader
    {
        public ProductStoredRecord? Record { get; set; } = ProductPurchaseSelectionTests.Record();
        public int Reads { get; private set; }
        public Action? During { get; set; }
        public bool HasMore { get; set; }
        public ProductPurchaseCandidateQueryV1? Query { get; private set; }
        public Task<ProductStoredRecord?> ReadAsync(ProductPurchaseReadScope scope, Guid id, CancellationToken ct)
        { Reads++; During?.Invoke(); return Task.FromResult(Record); }
        public Task<ProductPurchaseCandidateWindow> ReadAsync(ProductPurchaseReadScope scope, ProductPurchaseCandidateQueryV1 query, CancellationToken ct)
        { Reads++; Query = query; During?.Invoke(); return Task.FromResult(new ProductPurchaseCandidateWindow([ProductPurchaseSelectionTests.Record()], HasMore)); }
    }
}
