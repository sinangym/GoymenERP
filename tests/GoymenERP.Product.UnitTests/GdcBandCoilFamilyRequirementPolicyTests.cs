using System.Text.Json;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using static GoymenERP.Product.UnitTests.ProductSalesEligibilityServiceTests;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// The first accepted family rule set (Business #265 5722701085): GDC band/coil, kg→kg. Every
/// family, field, quality and coating value here is an isolated SYNTHETIC test definition — the
/// authorised data does not exist yet, so the shipped source stays unavailable and nothing here is
/// seeded or master data.
/// </summary>
public sealed class GdcBandCoilFamilyRequirementPolicyTests
{
    private static readonly Guid Family = Guid.Parse("3c9d1e27-6a1b-4f0e-9d5c-2b7a8e4f6c11");
    private const string StoredVersion = ProductSalesEligibilityServiceTests.Version;
    private const string Thickness = "syn-thickness-mm";
    private const string Width = "syn-width-mm";
    private const string Quality = "syn-quality";
    private const string Coating = "syn-coating";
    private const string Uncoated = "syn-uncoated";
    private const string Galvanized = "syn-galvanized";
    private const string QualityA = "syn-quality-a";

    private static GdcBandCoilFamilyDefinitionV1 Definition() => new(Family, Thickness, Width, Quality, Coating,
        new HashSet<string>(StringComparer.Ordinal) { QualityA, "syn-quality-b" },
        new HashSet<string>(StringComparer.Ordinal) { Uncoated, Galvanized },
        Uncoated);

    private static Dictionary<string, object?> ValidAttributes() => new()
    {
        [Thickness] = 1.5m,
        [Width] = 1250m,
        [Quality] = QualityA,
        [Coating] = Uncoated,
    };

    [Fact]
    public async Task A_complete_card_of_the_family_passes_including_the_explicit_uncoated_status()
    {
        // "Kaplamasız" is a valid, stated status; no colour is required of this family.
        var errors = await Policy().ValidateAsync(Card(ValidAttributes()), default);
        Assert.Empty(errors);

        var galvanized = ValidAttributes();
        galvanized[Coating] = Galvanized;
        Assert.Empty(await Policy().ValidateAsync(Card(galvanized), default));
    }

    [Theory]
    [InlineData("missing", ProductFamilyPolicyCodesV1.Required)]
    [InlineData("null", ProductFamilyPolicyCodesV1.Required)]
    [InlineData("zero", ProductFamilyPolicyCodesV1.InvalidValue)]
    [InlineData("negative", ProductFamilyPolicyCodesV1.InvalidValue)]
    [InlineData("text", ProductFamilyPolicyCodesV1.InvalidValue)]
    [InlineData("empty-text", ProductFamilyPolicyCodesV1.InvalidValue)]
    [InlineData("four-decimals", ProductFamilyPolicyCodesV1.PrecisionExceeded)]
    public async Task Thickness_and_width_must_be_positive_millimetres_with_at_most_three_decimals(string fault, string code)
    {
        foreach (var field in new[] { Thickness, Width })
        {
            var attributes = ValidAttributes();
            Apply(attributes, field, fault);

            var errors = await Policy().ValidateAsync(Card(attributes), default);

            var error = Assert.Single(errors);
            Assert.Equal(($"references.attributes.{field}", code), (error.Field, error.Code));
        }
    }

    [Fact]
    public async Task Three_decimals_and_trailing_zeros_are_within_precision()
    {
        var attributes = ValidAttributes();
        attributes[Thickness] = 0.125m;
        attributes[Width] = 1250.500m;
        Assert.Empty(await Policy().ValidateAsync(Card(attributes), default));
    }

    [Theory]
    [InlineData("missing", ProductFamilyPolicyCodesV1.Required)]
    [InlineData("null", ProductFamilyPolicyCodesV1.Required)]
    [InlineData("unknown-reference", ProductFamilyPolicyCodesV1.NotControlled)]
    [InlineData("number", ProductFamilyPolicyCodesV1.NotControlled)]
    public async Task Quality_and_coating_must_be_controlled_references(string fault, string code)
    {
        foreach (var field in new[] { Quality, Coating })
        {
            var attributes = ValidAttributes();
            Apply(attributes, field, fault);

            var error = Assert.Single(await Policy().ValidateAsync(Card(attributes), default));
            Assert.Equal(($"references.attributes.{field}", code), (error.Field, error.Code));
        }
    }

    [Fact]
    public async Task An_unknown_coating_is_not_uncoated()
    {
        // Business: "kaplamasız" ≠ "bilinmiyor/null". A missing status and a status the owner does
        // not list are both refused; only the explicit uncoated reference passes.
        foreach (var value in new object?[] { null, "syn-unknown", "", "kaplamasız" })
        {
            var attributes = ValidAttributes();
            attributes[Coating] = value;
            Assert.NotEmpty(await Policy().ValidateAsync(Card(attributes), default));
        }
    }

    [Fact]
    public async Task Every_failing_rule_is_reported_together()
    {
        var card = Card(new Dictionary<string, object?>(), units: new(Stock: StockUnitId, Sales: SalesUnitId));

        var errors = await Policy().ValidateAsync(card, default);

        Assert.Equal(
            [
                ($"references.attributes.{Thickness}", ProductFamilyPolicyCodesV1.Required),
                ($"references.attributes.{Width}", ProductFamilyPolicyCodesV1.Required),
                ($"references.attributes.{Quality}", ProductFamilyPolicyCodesV1.Required),
                ($"references.attributes.{Coating}", ProductFamilyPolicyCodesV1.Required),
                ("references.units.sales", ProductFamilyPolicyCodesV1.UnitPathMismatch),
            ],
            errors.Select(error => (error.Field, error.Code)));
    }

    [Fact]
    public async Task The_first_path_is_kilogram_to_kilogram_without_conversion()
    {
        var errors = await Policy().ValidateAsync(Card(ValidAttributes(), units: new(Stock: StockUnitId, Sales: SalesUnitId)), default);

        var error = Assert.Single(errors);
        Assert.Equal(("references.units.sales", ProductFamilyPolicyCodesV1.UnitPathMismatch), (error.Field, error.Code));
    }

    [Theory]
    [InlineData("no-units")]
    [InlineData("no-stock")]
    [InlineData("no-sales")]
    [InlineData("empty-ids")]
    public async Task The_kilogram_path_needs_both_unit_bindings_and_a_missing_one_is_not_a_pass(string fault)
    {
        // SİPARİŞ #300 P3-1: without a binding the kg→kg rule has nothing to compare; that is
        // "required", never a silent pass.
        ProductUnitReferences? units = fault switch
        {
            "no-units" => null,
            "no-stock" => new(Sales: StockUnitId),
            "no-sales" => new(Stock: StockUnitId),
            _ => new(Stock: Guid.Empty, Sales: Guid.Empty),
        };
        var card = Card(ValidAttributes(), units: units, noUnits: units is null);

        var errors = (await Policy().ValidateAsync(card, default)).Select(error => (error.Field, error.Code)).ToArray();

        (string, string)[] expected = fault switch
        {
            "no-stock" => [("references.units.stock", ProductFamilyPolicyCodesV1.Required)],
            "no-sales" => [("references.units.sales", ProductFamilyPolicyCodesV1.Required)],
            _ =>
            [
                ("references.units.stock", ProductFamilyPolicyCodesV1.Required),
                ("references.units.sales", ProductFamilyPolicyCodesV1.Required),
            ],
        };
        Assert.Equal(expected, errors);
    }

    [Fact]
    public async Task A_card_of_another_family_or_no_family_is_never_passed()
    {
        // No accepted rule set covers it: unavailable, never an empty error list.
        foreach (Guid? family in new Guid?[] { Guid.Parse("00000000-0000-0000-0000-00000000000f"), null })
        {
            var error = await Assert.ThrowsAsync<ProductOperationException>(
                () => Policy().ValidateAsync(Card(ValidAttributes(), family: family, useFamily: true), default));
            Assert.Equal((503, UnacceptedProductFamilyRequirementPolicyV1.UnavailableCode), (error.StatusCode, error.Code));
        }
    }

    [Fact]
    public async Task Without_the_authorised_definition_the_data_is_missing_and_nothing_passes()
    {
        // The shipped source: the rule set is accepted, its data is not there yet ("veri eksik").
        var policy = new GdcBandCoilFamilyRequirementPolicyV1(new UnavailableGdcBandCoilFamilyDefinitionSourceV1());

        var error = await Assert.ThrowsAsync<ProductOperationException>(() => policy.ValidateAsync(Card(ValidAttributes()), default));

        Assert.Equal((503, UnacceptedProductFamilyRequirementPolicyV1.UnavailableCode), (error.StatusCode, error.Code));
    }

    [Theory]
    [InlineData("empty-family")]
    [InlineData("blank-field")]
    [InlineData("duplicate-field")]
    [InlineData("no-qualities")]
    [InlineData("no-coatings")]
    [InlineData("uncoated-not-listed")]
    [InlineData("blank-reference")]
    public async Task A_malformed_definition_is_invalid_not_a_pass(string fault)
    {
        var definition = Definition();
        definition = fault switch
        {
            "empty-family" => definition with { FamilyId = Guid.Empty },
            "blank-field" => definition with { WidthFieldId = " " },
            "duplicate-field" => definition with { WidthFieldId = Thickness },
            "no-qualities" => definition with { QualityReferences = new HashSet<string>() },
            "no-coatings" => definition with { CoatingStatusReferences = new HashSet<string>() },
            "uncoated-not-listed" => definition with { UncoatedStatusReference = "syn-not-listed" },
            _ => definition with { QualityReferences = new HashSet<string>(StringComparer.Ordinal) { QualityA, " " } },
        };

        var error = await Assert.ThrowsAsync<ProductOperationException>(
            () => Policy(definition).ValidateAsync(Card(ValidAttributes()), default));

        Assert.Equal((503, GdcBandCoilFamilyRequirementPolicyV1.InvalidDefinitionCode), (error.StatusCode, error.Code));
    }

    [Fact]
    public async Task With_the_eligibility_read_a_passing_family_card_is_one_part_of_a_positive_answer()
    {
        // Active + the family rules + the Settings profiles + the authority fences, together. The
        // stock unit is the synthetic kilogram unit of the eligibility test world.
        var world = new World { Record = Stored(lifecycle: 1) };
        var policy = Policy();

        var result = await world.Service(policy).EvaluateAsync(
            new ProductSalesSelectionV1(ProductId, StoredVersion, StockUnitId, On), default);

        Assert.NotNull(result.Snapshot);
        Assert.Empty(result.Errors);

        // The same card as Draft never reaches the family rules: lifecycle refuses it first.
        var draft = new World { Record = Stored(lifecycle: 0) };
        var refused = await draft.Service(policy).EvaluateAsync(
            new ProductSalesSelectionV1(ProductId, StoredVersion, StockUnitId, On), default);
        Assert.Null(refused.Snapshot);
        Assert.Contains(refused.Errors, error => error.Code == ProductApiV1.LifecycleConflict);

        // And a card that breaks a family rule is the family's field error, not a snapshot.
        var attributes = ValidAttributes();
        attributes[Coating] = null;
        var broken = new World { Record = Stored(lifecycle: 1, attributes: attributes) };
        var answer = await broken.Service(policy).EvaluateAsync(
            new ProductSalesSelectionV1(ProductId, StoredVersion, StockUnitId, On), default);
        Assert.Null(answer.Snapshot);
        Assert.Equal(($"references.attributes.{Coating}", ProductFamilyPolicyCodesV1.Required),
            (Assert.Single(answer.Errors).Field, Assert.Single(answer.Errors).Code));
    }

    private static GdcBandCoilFamilyRequirementPolicyV1 Policy(GdcBandCoilFamilyDefinitionV1? definition = null) =>
        new(new FixedSource(definition ?? Definition()));

    private static void Apply(Dictionary<string, object?> attributes, string field, string fault)
    {
        switch (fault)
        {
            case "missing": attributes.Remove(field); break;
            case "null": attributes[field] = null; break;
            case "zero": attributes[field] = 0m; break;
            case "negative": attributes[field] = -1.5m; break;
            case "text": attributes[field] = "1.5"; break;
            case "empty-text": attributes[field] = ""; break;
            case "four-decimals": attributes[field] = 1.2345m; break;
            case "unknown-reference": attributes[field] = "syn-not-controlled"; break;
            case "number": attributes[field] = 7m; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> Json(Dictionary<string, object?> attributes) =>
        attributes.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value), StringComparer.Ordinal);

    private static ProductCardView Card(Dictionary<string, object?> attributes, ProductUnitReferences? units = null,
        Guid? family = null, bool useFamily = false, bool noUnits = false)
    {
        var product = new ProductDraftView(ProductId, "PSQL-1", "Sentetik bant", Guid.Parse("f4fc7672-0b46-41ba-a017-393401ba61dd"),
            Guid.Parse("3c9d1e27-6a1b-4f0e-9d5c-2b7a8e4f6c10"), useFamily ? family : Family,
            new(new(null, "not-evaluated"), new(true, "manual"), new(null, "not-evaluated"), new(null, "not-evaluated")),
            "Active", StoredVersion, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            new(null, null, null, null), new("NotEvaluated", []));
        return new ProductCardView(product, new ProductReferences(Attributes: Json(attributes),
            Units: noUnits ? null : units ?? new(Stock: StockUnitId, Sales: StockUnitId)));
    }

    private static ProductStoredRecord Stored(int lifecycle, Dictionary<string, object?>? attributes = null) =>
        new(ProductId, "PSQL-1", "Sentetik bant", Guid.Parse("f4fc7672-0b46-41ba-a017-393401ba61dd"),
            Guid.Parse("3c9d1e27-6a1b-4f0e-9d5c-2b7a8e4f6c10"), Family,
            new(null, 0), new(true, 1), new(null, 0), new(null, 0), lifecycle, long.Parse(StoredVersion),
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            JsonSerializer.Serialize(new ProductReferences(Attributes: Json(attributes ?? ValidAttributes()),
                Units: new(Stock: StockUnitId, Sales: StockUnitId)), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private sealed class FixedSource(GdcBandCoilFamilyDefinitionV1 definition) : IGdcBandCoilFamilyDefinitionSourceV1
    {
        public Task<GdcBandCoilFamilyDefinitionV1> GetAsync(CancellationToken cancellationToken) => Task.FromResult(definition);
    }
}
