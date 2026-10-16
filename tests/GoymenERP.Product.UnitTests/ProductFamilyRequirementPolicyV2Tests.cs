using System.Text.Json;
using GoymenERP.Modules.Product.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Product.Infrastructure.Persistence;
using static GoymenERP.Product.UnitTests.ProductSalesEligibilityServiceTests;

namespace GoymenERP.Product.UnitTests;

/// <summary>
/// The accepted GDC band/coil rule set (Business #265 5722701085) over a released family definition
/// (PRD-FAM-1). Every family, template, field, dictionary and code here is an isolated SYNTHETIC test
/// definition: it has the shape of the release (#66 5739827887: families with fixed Cins/Tür/Tip, a
/// steel grade, a coating type with an explicit "none", a coating designation for the zinc coating, a
/// colour for the painted one, and a candidate-only sub-quality under its grade), never its values.
/// Nothing here is seeded or master data.
/// </summary>
public sealed class ProductFamilyRequirementPolicyV2Tests
{
    private static readonly Guid HotCoil = Guid.Parse("3c9d1e27-6a1b-4f0e-9d5c-2b7a8e4f6c11");
    private static readonly Guid ColdStrip = Guid.Parse("3c9d1e27-6a1b-4f0e-9d5c-2b7a8e4f6c12");
    private static readonly Guid TemplateId = Guid.Parse("3c9d1e27-6a1b-4f0e-9d5c-2b7a8e4f6c21");
    private const string TemplateVersion = "1";
    private const string StoredVersion = ProductSalesEligibilityServiceTests.Version;

    private const string Thickness = "syn-thickness-mm";
    private const string Width = "syn-width-mm";
    private const string Grade = "syn-grade";
    private const string Coating = "syn-coating";
    private const string Designation = "syn-designation";
    private const string Colour = "syn-colour";
    private const string SubGrade = "syn-subgrade";

    private const string GradeA = "SYN-A";
    private const string GradeB = "SYN-B";
    private const string GradeCandidate = "SYN-C";
    private const string Uncoated = "syn-none";
    private const string Zinc = "syn-zinc";
    private const string Painted = "syn-painted";
    private const string DesignationApproved = "SYN-Z1";
    private const string DesignationCandidate = "SYN-Z2";
    private const string ColourApproved = "syn-red";
    private const string SubGradeOfA = "SYN-1";
    private const string SubGradeOfB = "SYN-2";

    private static ProductFamilyDefinitionV2 Definition() => new(
        [
            new(HotCoil, "SYN-HR-COIL", "Sentetik sıcak rulo", "Sentetik cins", "Sentetik tür sıcak", "Sentetik tip rulo",
                ProductFamilyRuleSetsV2.GdcBandCoil, "SYNTHETIC"),
            new(ColdStrip, "SYN-CR-STRIP", "Sentetik soğuk bant", "Sentetik cins", "Sentetik tür soğuk", "Sentetik tip bant",
                ProductFamilyRuleSetsV2.GdcBandCoil, "SYNTHETIC"),
        ],
        [
            new(TemplateId, TemplateVersion, "SYN-BAND-COIL", "Sentetik bant/rulo", new HashSet<Guid> { HotCoil, ColdStrip },
                Fields(), "SYNTHETIC"),
        ],
        Entries());

    private static ProductTemplateFieldV2[] Fields() =>
    [
        new(Thickness, ProductTemplateFieldKindV2.MeasureMillimetre, ProductTemplateFieldRolesV2.Thickness, true, null, false, null, null, null),
        new(Width, ProductTemplateFieldKindV2.MeasureMillimetre, ProductTemplateFieldRolesV2.Width, true, null, false, null, null, null),
        new(Grade, ProductTemplateFieldKindV2.DictionaryReference, ProductTemplateFieldRolesV2.Quality, true, "syn-grades", false, null, null, null),
        new(Coating, ProductTemplateFieldKindV2.DictionaryReference, ProductTemplateFieldRolesV2.Coating, true, "syn-coatings", false, null, null, null),
        new(Designation, ProductTemplateFieldKindV2.DictionaryReference, null, false, "syn-designations", false, Coating, Zinc, null),
        new(Colour, ProductTemplateFieldKindV2.DictionaryReference, null, false, "syn-colours", false, Coating, Painted, null),
        new(SubGrade, ProductTemplateFieldKindV2.DictionaryReference, null, false, "syn-subgrades", true, null, null, Grade),
    ];

    private static ProductDictionaryEntryV2[] Entries() =>
    [
        Approved("syn-grades", GradeA),
        Approved("syn-grades", GradeB),
        Candidate("syn-grades", GradeCandidate),
        Approved("syn-coatings", Uncoated) with { IsExplicitNone = true },
        Approved("syn-coatings", Zinc),
        Approved("syn-coatings", Painted),
        Approved("syn-designations", DesignationApproved) with { MassGm2 = 100m },
        Candidate("syn-designations", DesignationCandidate),
        Approved("syn-colours", ColourApproved),
        Candidate("syn-subgrades", SubGradeOfA) with { ParentCode = GradeA },
        Candidate("syn-subgrades", SubGradeOfB) with { ParentCode = GradeB },
    ];

    private static ProductDictionaryEntryV2 Approved(string dictionary, string code) =>
        new(dictionary, code, $"Sentetik {code}", ProductDefinitionEntryStatusV2.Approved, "SYNTHETIC", null, null, false);

    private static ProductDictionaryEntryV2 Candidate(string dictionary, string code) =>
        new(dictionary, code, $"Sentetik aday {code}", ProductDefinitionEntryStatusV2.Candidate, "SYNTHETIC", null, null, false);

    private static Dictionary<string, object?> ValidAttributes() => new()
    {
        [Thickness] = 1.5m,
        [Width] = 1250m,
        [Grade] = GradeA,
        [Coating] = Uncoated,
    };

    [Fact]
    public async Task A_complete_card_of_either_family_passes_including_the_explicit_uncoated_status()
    {
        // The explicit "none" (Kaplamasız) is a valid, stated coating; no colour is required.
        Assert.Empty(await Policy().ValidateAsync(Card(ValidAttributes()), default));
        Assert.Empty(await Policy().ValidateAsync(Card(ValidAttributes(), family: ColdStrip, useFamily: true), default));
    }

    [Fact]
    public async Task A_zinc_coating_takes_an_approved_designation_and_does_not_require_one()
    {
        // Whether a designation is required with zinc is open (Q3): the field applies, it is not required.
        var zinc = ValidAttributes();
        zinc[Coating] = Zinc;
        Assert.Empty(await Policy().ValidateAsync(Card(zinc), default));

        zinc[Designation] = DesignationApproved;
        Assert.Empty(await Policy().ValidateAsync(Card(zinc), default));
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

            var error = Assert.Single(await Policy().ValidateAsync(Card(attributes), default));
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
    [InlineData("zero", ProductFamilyPolicyCodesV1.InvalidValue)]
    [InlineData("four-decimals", ProductFamilyPolicyCodesV1.PrecisionExceeded)]
    public async Task Cut_sheet_plate_requires_exact_positive_length_while_coil_cards_remain_length_free(
        string fault, string code)
    {
        var definition = ProductInitialTechnicalDefinitionV1.Definition();
        var attributes = new Dictionary<string, object?>
        {
            ["thickness-mm"] = 1.5m,
            ["width-mm"] = 1250m,
            ["length-mm"] = 2500.125m,
            ["steel-grade"] = "S235JR",
            ["coating"] = "uncoated",
        };
        Apply(attributes, "length-mm", fault);

        var error = Assert.Single(await Policy(definition).ValidateAsync(
            Card(attributes,
                family: ProductInitialTechnicalDefinitionV1.HotRolledCutSheetPlateFamilyId,
                useFamily: true,
                templateId: ProductInitialTechnicalDefinitionV1.HotRolledCutSheetPlateTemplateId,
                templateVersion: "1",
                overrideTemplate: true), default));

        Assert.Equal(("references.attributes.length-mm", code), (error.Field, error.Code));

        var legacy = new Dictionary<string, object?>(attributes, StringComparer.Ordinal);
        legacy.Remove("length-mm");
        Assert.Empty(await Policy(definition).ValidateAsync(
            Card(legacy,
                family: ProductInitialTechnicalDefinitionV1.HotRolledCoilFamilyId,
                useFamily: true,
                templateId: ProductInitialTechnicalDefinitionV1.TemplateId,
                templateVersion: "1",
                overrideTemplate: true), default));
    }

    [Fact]
    public async Task Released_round_square_and_canonical_rectangle_cards_pass_their_distinct_geometries()
    {
        var definition = ProductInitialTechnicalDefinitionV1.Definition();

        Assert.Empty(await ValidateReleasedAsync(definition, RoundPipeAttributes(),
            ProductInitialTechnicalDefinitionV1.WeldedRoundPipeFamilyId,
            ProductInitialTechnicalDefinitionV1.WeldedRoundPipeTemplateId));
        Assert.Empty(await ValidateReleasedAsync(definition, ProfileAttributes(40m, 40m),
            ProductInitialTechnicalDefinitionV1.WeldedSquareProfileFamilyId,
            ProductInitialTechnicalDefinitionV1.WeldedSquareProfileTemplateId));
        Assert.Empty(await ValidateReleasedAsync(definition, ProfileAttributes(60m, 40m),
            ProductInitialTechnicalDefinitionV1.WeldedRectangleProfileFamilyId,
            ProductInitialTechnicalDefinitionV1.WeldedRectangleProfileTemplateId));
    }

    [Theory]
    [InlineData("missing", ProductFamilyPolicyCodesV1.Required)]
    [InlineData("zero", ProductFamilyPolicyCodesV1.InvalidValue)]
    [InlineData("four-decimals", ProductFamilyPolicyCodesV1.PrecisionExceeded)]
    public async Task Released_tube_profile_geometry_fields_keep_the_exact_measure_contract(string fault, string code)
    {
        var definition = ProductInitialTechnicalDefinitionV1.Definition();
        var cases = new[]
        {
            (RoundPipeAttributes(), ProductInitialTechnicalDefinitionV1.WeldedRoundPipeFamilyId,
                ProductInitialTechnicalDefinitionV1.WeldedRoundPipeTemplateId,
                new[] { "outside-diameter-mm", "wall-thickness-mm", "length-mm" }),
            (ProfileAttributes(40m, 40m), ProductInitialTechnicalDefinitionV1.WeldedSquareProfileFamilyId,
                ProductInitialTechnicalDefinitionV1.WeldedSquareProfileTemplateId,
                new[] { "section-width-mm", "section-height-mm", "wall-thickness-mm", "length-mm" }),
        };

        foreach (var (valid, familyId, templateId, fields) in cases)
        {
            foreach (var field in fields)
            {
                var attributes = new Dictionary<string, object?>(valid, StringComparer.Ordinal);
                Apply(attributes, field, fault);
                var error = Assert.Single(await ValidateReleasedAsync(definition, attributes, familyId, templateId));
                Assert.Equal(($"references.attributes.{field}", code), (error.Field, error.Code));
            }
        }
    }

    [Fact]
    public async Task Square_and_rectangle_geometry_is_canonical_and_does_not_alias_round_pipe_fields()
    {
        var definition = ProductInitialTechnicalDefinitionV1.Definition();

        var square = Assert.Single(await ValidateReleasedAsync(definition, ProfileAttributes(60m, 40m),
            ProductInitialTechnicalDefinitionV1.WeldedSquareProfileFamilyId,
            ProductInitialTechnicalDefinitionV1.WeldedSquareProfileTemplateId));
        Assert.Equal(("references.attributes.section-height-mm", ProductFamilyPolicyCodesV1.InvalidValue),
            (square.Field, square.Code));

        foreach (var dimensions in new[] { (Width: 40m, Height: 40m), (Width: 40m, Height: 60m) })
        {
            var rectangle = Assert.Single(await ValidateReleasedAsync(definition,
                ProfileAttributes(dimensions.Width, dimensions.Height),
                ProductInitialTechnicalDefinitionV1.WeldedRectangleProfileFamilyId,
                ProductInitialTechnicalDefinitionV1.WeldedRectangleProfileTemplateId));
            Assert.Equal(("references.attributes.section-width-mm", ProductFamilyPolicyCodesV1.InvalidValue),
                (rectangle.Field, rectangle.Code));
        }

        var round = RoundPipeAttributes();
        round["section-width-mm"] = 48.3m;
        var alien = Assert.Single(await ValidateReleasedAsync(definition, round,
            ProductInitialTechnicalDefinitionV1.WeldedRoundPipeFamilyId,
            ProductInitialTechnicalDefinitionV1.WeldedRoundPipeTemplateId));
        Assert.Equal(("references.attributes.section-width-mm", ProductFamilyPolicyCodesV1.FieldUnknown),
            (alien.Field, alien.Code));
    }

    [Theory]
    [InlineData("missing", ProductFamilyPolicyCodesV1.Required)]
    [InlineData("null", ProductFamilyPolicyCodesV1.Required)]
    [InlineData("unknown-reference", ProductFamilyPolicyCodesV1.NotControlled)]
    [InlineData("number", ProductFamilyPolicyCodesV1.NotControlled)]
    public async Task Grade_and_coating_must_be_controlled_references(string fault, string code)
    {
        foreach (var field in new[] { Grade, Coating })
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
        // Business: "kaplamasız" ≠ "bilinmiyor/null". Only the explicit "none" code passes; a code
        // compares exactly, so another spelling is not it either.
        foreach (var value in new object?[] { null, "syn-unknown", "", "SYN-NONE" })
        {
            var attributes = ValidAttributes();
            attributes[Coating] = value;
            Assert.NotEmpty(await Policy().ValidateAsync(Card(attributes), default));
        }
    }

    [Fact]
    public async Task A_candidate_never_satisfies_a_controlled_field()
    {
        // #66 5739827887: a candidate is held without meaning; it is not an approved value.
        var grade = ValidAttributes();
        grade[Grade] = GradeCandidate;
        var error = Assert.Single(await Policy().ValidateAsync(Card(grade), default));
        Assert.Equal(($"references.attributes.{Grade}", ProductFamilyPolicyCodesV1.ValueCandidate), (error.Field, error.Code));

        var designation = ValidAttributes();
        designation[Coating] = Zinc;
        designation[Designation] = DesignationCandidate;
        error = Assert.Single(await Policy().ValidateAsync(Card(designation), default));
        Assert.Equal(($"references.attributes.{Designation}", ProductFamilyPolicyCodesV1.ValueCandidate), (error.Field, error.Code));
    }

    [Fact]
    public async Task A_field_that_takes_candidates_holds_one_under_its_own_parent_only()
    {
        var own = ValidAttributes();
        own[SubGrade] = SubGradeOfA;
        Assert.Empty(await Policy().ValidateAsync(Card(own), default));

        var other = ValidAttributes();
        other[SubGrade] = SubGradeOfB;
        var error = Assert.Single(await Policy().ValidateAsync(Card(other), default));
        Assert.Equal(($"references.attributes.{SubGrade}", ProductFamilyPolicyCodesV1.ParentMismatch), (error.Field, error.Code));
    }

    [Fact]
    public async Task A_field_that_does_not_apply_is_refused_when_supplied()
    {
        // A designation belongs to the zinc coating and a colour to the painted one (#66 5739827887).
        var designation = ValidAttributes();
        designation[Designation] = DesignationApproved;
        var error = Assert.Single(await Policy().ValidateAsync(Card(designation), default));
        Assert.Equal(($"references.attributes.{Designation}", ProductFamilyPolicyCodesV1.FieldNotApplicable), (error.Field, error.Code));

        var colour = ValidAttributes();
        colour[Coating] = Zinc;
        colour[Colour] = ColourApproved;
        error = Assert.Single(await Policy().ValidateAsync(Card(colour), default));
        Assert.Equal(($"references.attributes.{Colour}", ProductFamilyPolicyCodesV1.FieldNotApplicable), (error.Field, error.Code));

        var painted = ValidAttributes();
        painted[Coating] = Painted;
        painted[Colour] = ColourApproved;
        Assert.Empty(await Policy().ValidateAsync(Card(painted), default));
    }

    [Fact]
    public async Task A_required_colour_with_no_values_keeps_only_painted_cards_closed()
    {
        // Business 5740811346: colour applies with the painted coating, but no colour value is activated
        // yet. The field is required and its dictionary is empty: the release stays well formed, a painted
        // card cannot pass (missing → required, any value → not controlled), and nothing else changes.
        var definition = Definition();
        definition = definition with
        {
            Templates = [definition.Templates[0] with
            {
                Fields = [.. definition.Templates[0].Fields.Select(field => field.Key == Colour ? field with { Required = true } : field)],
            }],
            Entries = [.. definition.Entries.Where(entry => entry.Dictionary != "syn-colours")],
        };
        var policy = Policy(definition);

        Assert.Empty(await policy.ValidateAsync(Card(ValidAttributes()), default));
        var zinc = ValidAttributes();
        zinc[Coating] = Zinc;
        Assert.Empty(await policy.ValidateAsync(Card(zinc), default));

        var painted = ValidAttributes();
        painted[Coating] = Painted;
        var error = Assert.Single(await policy.ValidateAsync(Card(painted), default));
        Assert.Equal(($"references.attributes.{Colour}", ProductFamilyPolicyCodesV1.Required), (error.Field, error.Code));

        painted[Colour] = ColourApproved;
        error = Assert.Single(await policy.ValidateAsync(Card(painted), default));
        Assert.Equal(($"references.attributes.{Colour}", ProductFamilyPolicyCodesV1.NotControlled), (error.Field, error.Code));
    }

    [Fact]
    public async Task An_attribute_the_template_does_not_have_is_refused()
    {
        var attributes = ValidAttributes();
        attributes["syn-invented"] = "x";
        var error = Assert.Single(await Policy().ValidateAsync(Card(attributes), default));
        Assert.Equal(("references.attributes.syn-invented", ProductFamilyPolicyCodesV1.FieldUnknown), (error.Field, error.Code));
    }

    [Theory]
    [InlineData("none", ProductFamilyPolicyCodesV1.Required)]
    [InlineData("other-template", ProductFamilyPolicyCodesV1.TemplateMismatch)]
    [InlineData("other-version", ProductFamilyPolicyCodesV1.TemplateMismatch)]
    public async Task The_card_is_bound_to_its_familys_template_version(string binding, string code)
    {
        var (id, version) = binding switch
        {
            "none" => ((Guid?)null, (string?)null),
            "other-template" => (Guid.Parse("3c9d1e27-6a1b-4f0e-9d5c-2b7a8e4f6c29"), TemplateVersion),
            _ => (TemplateId, "2"),
        };

        var error = Assert.Single(await Policy().ValidateAsync(Card(ValidAttributes(), templateId: id, templateVersion: version,
            overrideTemplate: true), default));

        Assert.Equal(("references.templateId", code), (error.Field, error.Code));
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
                ($"references.attributes.{Grade}", ProductFamilyPolicyCodesV1.Required),
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
    public async Task Without_a_released_definition_the_data_is_missing_and_nothing_passes()
    {
        var policy = new ProductFamilyRequirementPolicyV2(new UnavailableProductFamilyDefinitionSourceV2());

        var error = await Assert.ThrowsAsync<ProductOperationException>(() => policy.ValidateAsync(Card(ValidAttributes()), default));

        Assert.Equal((503, UnacceptedProductFamilyRequirementPolicyV1.UnavailableCode), (error.StatusCode, error.Code));
    }

    [Theory]
    [InlineData("no-families")]
    [InlineData("other-rule-set")]
    [InlineData("blank-cins")]
    [InlineData("duplicate-family-code")]
    [InlineData("family-without-template")]
    [InlineData("family-in-two-templates")]
    [InlineData("template-of-unknown-family")]
    [InlineData("bad-version")]
    [InlineData("duplicate-field")]
    [InlineData("role-missing")]
    [InlineData("role-twice")]
    [InlineData("role-optional")]
    [InlineData("role-conditional")]
    [InlineData("role-takes-candidates")]
    [InlineData("role-wrong-kind")]
    [InlineData("required-field-takes-candidates")]
    [InlineData("quality-without-approved-entry")]
    [InlineData("no-explicit-none")]
    [InlineData("two-explicit-nones")]
    [InlineData("candidate-explicit-none")]
    [InlineData("role-empty-dictionary")]
    [InlineData("measure-with-dictionary")]
    [InlineData("condition-on-unknown-field")]
    [InlineData("condition-on-candidate-code")]
    [InlineData("parent-of-unknown-code")]
    [InlineData("non-positive-mass")]
    [InlineData("duplicate-entry")]
    [InlineData("blank-source")]
    public async Task A_malformed_release_is_invalid_not_a_pass(string fault)
    {
        var definition = Malformed(Definition(), fault);

        var error = await Assert.ThrowsAsync<ProductOperationException>(
            () => Policy(definition).ValidateAsync(Card(ValidAttributes()), default));

        Assert.Equal((503, ProductFamilyDefinitionRulesV2.InvalidDefinitionCode), (error.StatusCode, error.Code));
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
        attributes[Grade] = GradeCandidate;
        var broken = new World { Record = Stored(lifecycle: 1, attributes: attributes) };
        var answer = await broken.Service(policy).EvaluateAsync(
            new ProductSalesSelectionV1(ProductId, StoredVersion, StockUnitId, On), default);
        Assert.Null(answer.Snapshot);
        Assert.Equal(($"references.attributes.{Grade}", ProductFamilyPolicyCodesV1.ValueCandidate),
            (Assert.Single(answer.Errors).Field, Assert.Single(answer.Errors).Code));
    }

    private static ProductFamilyDefinitionV2 Malformed(ProductFamilyDefinitionV2 definition, string fault)
    {
        var template = definition.Templates[0];
        ProductFamilyDefinitionV2 WithFields(params ProductTemplateFieldV2[] fields) =>
            definition with { Templates = [template with { Fields = fields }] };
        ProductTemplateFieldV2[] ChangeField(string key, Func<ProductTemplateFieldV2, ProductTemplateFieldV2> change) =>
            [.. template.Fields.Select(field => field.Key == key ? change(field) : field)];
        ProductFamilyDefinitionV2 WithEntries(IEnumerable<ProductDictionaryEntryV2> entries) =>
            definition with { Entries = [.. entries] };

        return fault switch
        {
            "no-families" => definition with { Families = [] },
            "other-rule-set" => definition with { Families = [definition.Families[0] with { RuleSet = "syn-other" }, definition.Families[1]] },
            "blank-cins" => definition with { Families = [definition.Families[0] with { Cins = " " }, definition.Families[1]] },
            "duplicate-family-code" => definition with
            {
                Families = [definition.Families[0], definition.Families[1] with { Code = definition.Families[0].Code }],
            },
            "family-without-template" => definition with { Templates = [template with { FamilyIds = new HashSet<Guid> { HotCoil } }] },
            "family-in-two-templates" => definition with
            {
                Templates = [template, template with { Id = Guid.Parse("3c9d1e27-6a1b-4f0e-9d5c-2b7a8e4f6c22") }],
            },
            "template-of-unknown-family" => definition with
            {
                Templates = [template with { FamilyIds = new HashSet<Guid> { HotCoil, ColdStrip, Guid.Parse("00000000-0000-0000-0000-00000000000f") } }],
            },
            "bad-version" => definition with { Templates = [template with { Version = "v1" }] },
            "duplicate-field" => WithFields([.. template.Fields, template.Fields[0]]),
            "role-missing" => WithFields(ChangeField(Width, field => field with { Role = null })),
            "role-twice" => WithFields(ChangeField(Width, field => field with { Role = ProductTemplateFieldRolesV2.Thickness })),
            "role-optional" => WithFields(ChangeField(Grade, field => field with { Required = false })),
            "role-conditional" => WithFields(ChangeField(Grade, field => field with { AppliesWhenField = Coating, AppliesWhenCode = Zinc })),
            "role-takes-candidates" => WithFields(ChangeField(Grade, field => field with { AllowsCandidate = true })),
            "role-wrong-kind" => WithFields(ChangeField(Thickness, field => field with
            {
                Kind = ProductTemplateFieldKindV2.DictionaryReference, Dictionary = "syn-grades",
            })),
            // ORG #334 P3-1: an optional field that takes candidates made required would let a candidate pass.
            "required-field-takes-candidates" => WithFields(ChangeField(SubGrade, field => field with { Required = true })),
            "quality-without-approved-entry" => WithEntries(definition.Entries.Where(entry => entry.Dictionary != "syn-grades"
                || entry.Status == ProductDefinitionEntryStatusV2.Candidate)),
            "no-explicit-none" => WithEntries(definition.Entries.Select(entry => entry with { IsExplicitNone = false })),
            "two-explicit-nones" => WithEntries(definition.Entries.Select(entry => entry.Code == Zinc ? entry with { IsExplicitNone = true } : entry)),
            "candidate-explicit-none" => WithEntries(definition.Entries.Select(entry =>
                entry.Code == Uncoated ? entry with { Status = ProductDefinitionEntryStatusV2.Candidate } : entry)),
            // A role field's dictionary must hold an approved entry; only a non-role field may be empty.
            "role-empty-dictionary" => WithFields(ChangeField(Grade, field => field with { Dictionary = "syn-empty" })),
            "measure-with-dictionary" => WithFields(ChangeField(Width, field => field with { Dictionary = "syn-grades" })),
            "condition-on-unknown-field" => WithFields(ChangeField(Colour, field => field with { AppliesWhenField = "syn-missing" })),
            "condition-on-candidate-code" => WithFields(ChangeField(Designation, field => field with { AppliesWhenCode = GradeCandidate })),
            "parent-of-unknown-code" => WithEntries(definition.Entries.Select(entry =>
                entry.Code == SubGradeOfA ? entry with { ParentCode = "SYN-NOT-A-GRADE" } : entry)),
            "non-positive-mass" => WithEntries(definition.Entries.Select(entry => entry.Code == DesignationApproved ? entry with { MassGm2 = 0m } : entry)),
            "duplicate-entry" => WithEntries([.. definition.Entries, definition.Entries[0]]),
            "blank-source" => WithEntries(definition.Entries.Select(entry => entry.Code == GradeA ? entry with { SourceRef = "" } : entry)),
            _ => throw new ArgumentOutOfRangeException(nameof(fault)),
        };
    }

    private static ProductFamilyRequirementPolicyV2 Policy(ProductFamilyDefinitionV2? definition = null) =>
        new(new FixedSource(definition ?? Definition()));

    private static Task<IReadOnlyList<ProductFieldError>> ValidateReleasedAsync(ProductFamilyDefinitionV2 definition,
        Dictionary<string, object?> attributes, Guid familyId, Guid templateId) =>
        Policy(definition).ValidateAsync(Card(attributes, family: familyId, useFamily: true,
            templateId: templateId, templateVersion: "1", overrideTemplate: true), default);

    private static Dictionary<string, object?> RoundPipeAttributes() => new(StringComparer.Ordinal)
    {
        ["outside-diameter-mm"] = 48.3m,
        ["wall-thickness-mm"] = 1.5m,
        ["length-mm"] = 6000m,
        ["steel-grade"] = "S235JR",
        ["coating"] = "uncoated",
    };

    private static Dictionary<string, object?> ProfileAttributes(decimal width, decimal height) => new(StringComparer.Ordinal)
    {
        ["section-width-mm"] = width,
        ["section-height-mm"] = height,
        ["wall-thickness-mm"] = 1.4m,
        ["length-mm"] = 6000m,
        ["steel-grade"] = "S235JR",
        ["coating"] = "uncoated",
    };

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
        Guid? family = null, bool useFamily = false, bool noUnits = false,
        Guid? templateId = null, string? templateVersion = null, bool overrideTemplate = false)
    {
        var product = new ProductDraftView(ProductId, "PSQL-1", "Sentetik bant", Guid.Parse("f4fc7672-0b46-41ba-a017-393401ba61dd"),
            null, useFamily ? family : HotCoil,
            new(new(null, "not-evaluated"), new(true, "manual"), new(null, "not-evaluated"), new(null, "not-evaluated")),
            "Active", StoredVersion, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            new(null, null, null, null), new("NotEvaluated", []));
        return new ProductCardView(product, new ProductReferences(
            TemplateId: overrideTemplate ? templateId : TemplateId,
            TemplateVersion: overrideTemplate ? templateVersion : TemplateVersion,
            Attributes: Json(attributes),
            Units: noUnits ? null : units ?? new(Stock: StockUnitId, Sales: StockUnitId)));
    }

    private static ProductStoredRecord Stored(int lifecycle, Dictionary<string, object?>? attributes = null) =>
        new(ProductId, "PSQL-1", "Sentetik bant", Guid.Parse("f4fc7672-0b46-41ba-a017-393401ba61dd"),
            null, HotCoil,
            new(null, 0), new(true, 1), new(null, 0), new(null, 0), lifecycle, long.Parse(StoredVersion),
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            JsonSerializer.Serialize(new ProductReferences(TemplateId, TemplateVersion, Json(attributes ?? ValidAttributes()),
                new(Stock: StockUnitId, Sales: StockUnitId)), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private sealed class FixedSource(ProductFamilyDefinitionV2 definition) : IProductFamilyDefinitionSourceV2
    {
        public Task<ProductFamilyDefinitionV2> GetAsync(CancellationToken cancellationToken) => Task.FromResult(definition);
    }
}
