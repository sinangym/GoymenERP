using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// AK-7b (URT-D112): the one reading of the Product owner's theoretical kg per piece answer that both the output
/// declaration and the output product selector use, so the form knows beforehand what the command will ask.
/// </summary>
public sealed class ManufacturingOutputPieceCountingRuleTests
{
    private static readonly Guid Company = Guid.Parse("a7b00000-0000-0000-0000-000000000001");
    private static readonly Guid Product = Guid.Parse("a7b00000-0000-0000-0000-000000000002");
    private static readonly Guid Other = Guid.Parse("a7b00000-0000-0000-0000-0000000000ff");

    private static ProductTheoreticalKilogramsPerUnitAnswerV1 Found(Guid company, Guid product, Guid unit,
        ProductPhysicalUnitKindV1 kind) =>
        ProductTheoreticalKilogramsPerUnitAnswerV1.Found(0.4m, new ProductTheoreticalKilogramsPerUnitEvidenceV1(
            company, product, unit, kind, "12", "product-unit-conversion.v1", 4, null, null, null, null, null,
            Guid.Parse("a7b00000-0000-0000-0000-0000000000a7"), 1, "unit-semantics.v1"));

    [Fact]
    public void A_found_figure_for_this_company_product_and_the_piece_unit_is_counted() =>
        Assert.Equal(ManufacturingOutputPieceCountingV1.Counted, ManufacturingOutputPieceCountingRuleV1.Classify(
            Found(Company, Product, SettingsUnitIdentitiesV1.Piece, ProductPhysicalUnitKindV1.Piece), Company, Product));

    [Fact]
    public void A_card_that_does_not_name_the_piece_unit_is_not_counted() =>
        Assert.Equal(ManufacturingOutputPieceCountingV1.NotCounted, ManufacturingOutputPieceCountingRuleV1.Classify(
            ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource,
                ProductTheoreticalKilogramsPerUnitCodesV1.UnitNotOnCard), Company, Product));

    [Theory]
    [InlineData(ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource, ProductTheoreticalKilogramsPerUnitCodesV1.NoSource)]
    [InlineData(ProductTheoreticalKilogramsPerUnitOutcomeV1.Refused, ProductTheoreticalKilogramsPerUnitCodesV1.WeightConflict)]
    public void A_piece_card_without_a_usable_kilogram_source_is_source_missing(
        ProductTheoreticalKilogramsPerUnitOutcomeV1 outcome, string code) =>
        Assert.Equal(ManufacturingOutputPieceCountingV1.SourceMissing, ManufacturingOutputPieceCountingRuleV1.Classify(
            ProductTheoreticalKilogramsPerUnitAnswerV1.Without(outcome, code), Company, Product));

    [Fact]
    public void No_answer_an_unreadable_owner_or_evidence_of_another_company_product_or_unit_is_unavailable()
    {
        ProductTheoreticalKilogramsPerUnitAnswerV1?[] answers =
        [
            null,
            ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.Unavailable,
                ProductTheoreticalKilogramsPerUnitCodesV1.Unavailable),
            ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.Denied),
            ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.NotFound),
            Found(Other, Product, SettingsUnitIdentitiesV1.Piece, ProductPhysicalUnitKindV1.Piece),
            Found(Company, Other, SettingsUnitIdentitiesV1.Piece, ProductPhysicalUnitKindV1.Piece),
            Found(Company, Product, SettingsUnitIdentitiesV1.Kilogram, ProductPhysicalUnitKindV1.Piece),
        ];

        Assert.All(answers, answer => Assert.Equal(ManufacturingOutputPieceCountingV1.Unavailable,
            ManufacturingOutputPieceCountingRuleV1.Classify(answer, Company, Product)));
    }
}
