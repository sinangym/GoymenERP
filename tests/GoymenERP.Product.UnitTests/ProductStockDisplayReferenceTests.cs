using System.Text.Json;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static GoymenERP.Product.UnitTests.ProductSalesEligibilityServiceTests;

namespace GoymenERP.Product.UnitTests;

public sealed class ProductStockDisplayReferenceTests
{
    private static readonly Guid FamilyId = Guid.Parse("c6100000-0000-4000-8000-000000000301");
    private static readonly Guid TemplateId = Guid.Parse("c6200000-0000-4000-8000-000000000301");
    private static readonly Guid FirstId = Guid.Parse("c6300000-0000-4000-8000-000000000301");
    private static readonly Guid SecondId = Guid.Parse("c6300000-0000-4000-8000-000000000302");

    [Fact]
    public async Task One_authorized_batch_returns_exact_identity_and_accepted_nominal_dimensions_without_N_plus_one()
    {
        var world = new World([Record(FirstId, 7, "P-1", "Galvaniz bant", 1.25m, 1250m),
            Record(SecondId, 3, "P-2", "Galvaniz rulo", 2m, 1000m)]);

        var result = await world.Service().ReadAsync(new([
            new(FirstId, "7"), new(SecondId, "3"),
        ]));

        Assert.Equal(2, result.Items.Count);
        Assert.Collection(result.Items,
            first => AssertResult(first, FirstId, "7", "P-1", "Galvaniz bant", 1.25m, 1250m),
            second => AssertResult(second, SecondId, "3", "P-2", "Galvaniz rulo", 2m, 1000m));
        Assert.Equal(1, world.Records.Reads);
        Assert.Equal(1, world.Definitions.Reads);
        Assert.All(world.Authorization.AskedPermissions,
            permission => Assert.Equal(ProductPermissionManifestV1.OperationalSelectionRead, permission));
    }

    [Fact]
    public async Task Technical_match_reads_many_versions_of_one_product_in_one_product_batch()
    {
        var world = new World([Record(FirstId, 100, "P-1", "Galvaniz bant", 1.25m, 1250m)]);
        var selections = Enumerable.Range(1, ProductStockDisplayCodesV1.MaximumBatchSize)
            .Select(version => new ProductStockTechnicalMatchSelectionV1(FirstId, version.ToString()))
            .ToArray();

        var result = await world.Service().MatchAsync(new(
            selections, FamilyId, null, 1m, 2m, 1250m, null, null));

        var match = Assert.Single(result.Matches);
        Assert.Equal((FirstId, "100"), (match.ProductId, match.ExpectedProductVersion));
        Assert.Equal(1, world.Records.Reads);
        Assert.Equal(1, world.Definitions.Reads);
    }

    [Fact]
    public async Task Technical_match_never_treats_a_missing_width_as_zero_or_as_an_unfiltered_match()
    {
        var missingWidth = Record(FirstId, 7, "P-1", "Genişliği eksik bant", 1.25m, null);
        var complete = Record(SecondId, 3, "P-2", "Tam ölçülü bant", 1.25m, 1250m);
        var world = new World([missingWidth, complete]);
        var selections = new ProductStockTechnicalMatchSelectionV1[]
        {
            new(FirstId, "7"),
            new(SecondId, "3"),
        };

        var unfiltered = await world.Service().MatchAsync(new(
            selections, null, null, null, null, null, null, null));
        var widthFiltered = await world.Service().MatchAsync(new(
            selections, null, null, null, null, 1250m, null, null));

        Assert.Equal(new ProductStockTechnicalMatchSelectionV1(SecondId, "3"),
            Assert.Single(unfiltered.Matches));
        Assert.Equal(new ProductStockTechnicalMatchSelectionV1(SecondId, "3"),
            Assert.Single(widthFiltered.Matches));
        Assert.Equal(2, world.Records.Reads);
        Assert.Equal(2, world.Definitions.Reads);
    }

    [Fact]
    public async Task Missing_stale_and_invalid_versions_never_relabel_a_stock_row_with_current_identity()
    {
        var invalidId = Guid.NewGuid();
        var world = new World([Record(FirstId, 7, "P-1", "Current name", 1m, 10m),
            Record(invalidId, 4, "P-3", "Other current name", 1m, 10m)]);

        var result = await world.Service().ReadAsync(new([
            new(Guid.NewGuid(), "1"), new(FirstId, "6"), new(invalidId, "not-a-version"),
        ]));

        Assert.Collection(result.Items,
            item => AssertRejected(item, "productId", "invalid_reference"),
            item => AssertRejected(item, "expectedProductVersion", ProductApiV1.VersionConflict),
            item => AssertRejected(item, "expectedProductVersion", "invalid_version"));
        Assert.Equal(1, world.Records.Reads);
        Assert.Equal(0, world.Definitions.Reads);
    }

    [Fact]
    public async Task Candidate_or_missing_technical_data_keeps_exact_identity_but_withholds_dimensions()
    {
        var record = Record(FirstId, 7, "P-1", "Galvaniz bant", 1.25m, 1250m,
            quality: "CANDIDATE");
        var world = new World([record], Definition(candidateQuality: true));

        var result = await world.Service().ReadAsync(new([new(FirstId, "7")]));

        var item = Assert.Single(result.Items);
        Assert.NotNull(item.Identity);
        Assert.Null(item.Technical);
        Assert.Contains(item.Errors, error => error.Code == ProductFamilyPolicyCodesV1.ValueCandidate);
    }

    [Fact]
    public async Task Template_revision_mismatch_keeps_exact_identity_and_does_not_read_measures()
    {
        var record = Record(FirstId, 7, "P-1", "Galvaniz bant", 1.25m, 1250m,
            templateVersion: "6");
        var world = new World([record]);

        var item = Assert.Single((await world.Service().ReadAsync(new([new(FirstId, "7")]))).Items);

        Assert.NotNull(item.Identity);
        Assert.Null(item.Technical);
        Assert.Equal(new ProductFieldError("references.templateId", ProductFamilyPolicyCodesV1.TemplateMismatch),
            Assert.Single(item.Errors));
    }

    [Fact]
    public async Task Synthetic_release_is_unavailable_for_the_whole_batch()
    {
        var world = new World([Record(FirstId, 7, "P-1", "Galvaniz bant", 1m, 10m)],
            Definition(source: "SYNTHETIC-browser"));

        var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
            world.Service().ReadAsync(new([new(FirstId, "7")])));

        Assert.Equal((503, ProductManufacturingTechnicalSnapshotService.UnavailableCode),
            (error.StatusCode, error.Code));
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("company")]
    [InlineData("actor")]
    public async Task Awaited_batch_read_is_followed_by_a_fresh_authority_and_scope_fence(string change)
    {
        var world = new World([Record(FirstId, 7, "P-1", "Galvaniz bant", 1m, 10m)]);
        world.Records.During = () =>
        {
            if (change == "permission") world.Authorization.Granted = false;
            else world.Change(change);
        };

        var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
            world.Service().ReadAsync(new([new(FirstId, "7")])));

        Assert.Equal((403, ProductSalesEligibilityService.AccessDeniedCode),
            (error.StatusCode, error.Code));
        Assert.Equal(0, world.Definitions.Reads);
    }

    [Fact]
    public async Task Invalid_or_duplicate_batch_is_rejected_before_owner_reads()
    {
        var world = new World([]);
        foreach (var query in new[]
        {
            new ProductStockDisplayBatchQueryV1([]),
            new ProductStockDisplayBatchQueryV1([new(FirstId, "1"), new(FirstId, "1")]),
            new ProductStockDisplayBatchQueryV1(Enumerable.Range(0,
                ProductStockDisplayCodesV1.MaximumBatchSize + 1)
                .Select(_ => new ProductStockDisplaySelectionV1(Guid.NewGuid(), "1")).ToArray()),
        })
        {
            var error = await Assert.ThrowsAsync<ProductOperationException>(() => world.Service().ReadAsync(query));
            Assert.Equal((400, ProductStockDisplayCodesV1.InvalidRequest), (error.StatusCode, error.Code));
        }
        Assert.Equal(0, world.Records.Reads);
    }

    [Fact]
    public async Task Disabled_runtime_resolves_only_the_fail_closed_port()
    {
        var services = new ServiceCollection();
        services.AddProductStockDisplayReferences(_ => false);
        await using var provider = services.BuildServiceProvider();

        var port = provider.GetRequiredService<IProductStockDisplayReferencesV1>();
        var error = await Assert.ThrowsAsync<ProductOperationException>(() =>
            port.ReadAsync(new([new(FirstId, "7")])));

        Assert.Equal((503, "product_runtime_unconfigured"), (error.StatusCode, error.Code));
        Assert.Single(provider.GetServices<IProductStockDisplayReferencesV1>());

        var matcher = provider.GetRequiredService<IProductStockTechnicalMatcherV1>();
        var matchError = await Assert.ThrowsAsync<ProductOperationException>(() =>
            matcher.MatchAsync(new([new(FirstId, "7")], FamilyId, null, null, null, null, null, null)));
        Assert.Equal((503, "product_runtime_unconfigured"), (matchError.StatusCode, matchError.Code));
        Assert.Single(provider.GetServices<IProductStockTechnicalMatcherV1>());
    }

    private static void AssertResult(ProductStockDisplayResultV1 item, Guid id, string version,
        string code, string name, decimal thickness, decimal width)
    {
        Assert.Empty(item.Errors);
        Assert.Equal((id, version, code, name),
            (item.Identity!.ProductId, item.Identity.ProductVersion, item.Identity.Code, item.Identity.Name));
        Assert.Equal((FamilyId, TemplateId, thickness, width),
            (item.Technical!.Family.FamilyId, item.Technical.Template.TemplateId,
                item.Technical.ThicknessMm, item.Technical.WidthMm));
    }

    private static void AssertRejected(ProductStockDisplayResultV1 item, string field, string code)
    {
        Assert.Null(item.Identity);
        Assert.Null(item.Technical);
        Assert.Equal(new ProductFieldError(field, code), Assert.Single(item.Errors));
    }

    private static ProductStoredRecord Record(Guid id, long version, string code, string name,
        decimal thickness, decimal? width, string quality = "DX51D", string templateVersion = "7")
    {
        var attributes = new Dictionary<string, JsonElement>
        {
            ["thicknessMm"] = JsonSerializer.SerializeToElement(thickness),
            ["quality"] = JsonSerializer.SerializeToElement(quality),
            ["coating"] = JsonSerializer.SerializeToElement("none"),
        };
        if (width.HasValue) attributes["widthMm"] = JsonSerializer.SerializeToElement(width.Value);
        var references = new ProductReferences(TemplateId, templateVersion, attributes);
        return new(id, code, name, Guid.NewGuid(), null, FamilyId,
            new(null, 0), new(true, 1), new(null, 0), new(null, 0), 1, version,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            JsonSerializer.Serialize(references, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static ProductFamilyDefinitionV2 Definition(
        bool candidateQuality = false,
        string source = "#66/stock-display") => new(
        [new(FamilyId, "gdc-coil", "GDC Coil", "coil", "rolling", "band",
            ProductFamilyRuleSetsV2.GdcBandCoil, source)],
        [new(TemplateId, "7", "gdc-coil-v7", "GDC Coil v7", new HashSet<Guid> { FamilyId },
            [new("thicknessMm", ProductTemplateFieldKindV2.MeasureMillimetre,
                    ProductTemplateFieldRolesV2.Thickness, true, null, false, null, null, null),
                new("widthMm", ProductTemplateFieldKindV2.MeasureMillimetre,
                    ProductTemplateFieldRolesV2.Width, true, null, false, null, null, null),
                new("quality", ProductTemplateFieldKindV2.DictionaryReference,
                    ProductTemplateFieldRolesV2.Quality, true, "qualities", false, null, null, null),
                new("coating", ProductTemplateFieldKindV2.DictionaryReference,
                    ProductTemplateFieldRolesV2.Coating, true, "coatings", false, null, null, null)], source)],
        [new("qualities", "DX51D", "DX51D", ProductDefinitionEntryStatusV2.Approved, source,
                null, null, false),
            new("qualities", "CANDIDATE", "Candidate", candidateQuality
                ? ProductDefinitionEntryStatusV2.Candidate : ProductDefinitionEntryStatusV2.Approved,
                source, null, null, false),
            new("coatings", "none", "Uncoated", ProductDefinitionEntryStatusV2.Approved, source,
                null, null, true)]);

    private sealed class World(IReadOnlyList<ProductStoredRecord> records,
        ProductFamilyDefinitionV2? definition = null)
    {
        public Context Company { get; } = new();
        public User User { get; } = new();
        public Authorization Authorization { get; } = new();
        public Records Records { get; } = new(records);
        public Definitions Definitions { get; } = new(definition ?? ProductStockDisplayReferenceTests.Definition());

        public ProductStockDisplayReferenceService Service() =>
            new(Company, User, Authorization, Records, Definitions);

        public void Change(string change)
        {
            if (change == "company")
            {
                var other = Guid.NewGuid();
                User.Allowed.Add(other);
                Company.Set(other);
            }
            else
            {
                User.UserId = "another-actor";
            }
        }
    }

    private sealed class Records(IReadOnlyList<ProductStoredRecord> rows) : IProductStockDisplayRecordReader
    {
        public int Reads { get; private set; }
        public Action? During { get; set; }

        public Task<IReadOnlyList<ProductStoredRecord>> ReadForStockDisplayAsync(
            ProductStockDisplayReadScope scope,
            IReadOnlySet<Guid> productIds,
            CancellationToken cancellationToken)
        {
            Reads++;
            During?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ProductStoredRecord>>(
                rows.Where(row => productIds.Contains(row.Id)).ToArray());
        }
    }

    private sealed class Definitions(ProductFamilyDefinitionV2 definition) : IProductFamilyDefinitionSourceV2
    {
        public int Reads { get; private set; }
        public Task<ProductFamilyDefinitionV2> GetAsync(CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(definition);
        }
    }
}
