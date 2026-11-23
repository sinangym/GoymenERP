using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;
using GoymenERP.Modules.Product.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// AK-7 (URT-D112): the output declaration asks the Product owner whether the product is counted in pieces; a counted
/// product's output carries the piece count with the weighed kg and the captured theoretical kg per piece, an uncounted
/// one carries none; a card without a figure, or an unreadable owner, refuses fail-closed; the correction forwards the
/// pieces it takes back; the order read shows the real/theoretical kg per piece deviation without a threshold.
/// </summary>
public sealed partial class ManufacturingCommandServiceTests
{
    private static readonly Guid PieceProfile = Guid.Parse("a7000000-0000-0000-0000-0000000000a7");

    private static ProductTheoreticalKilogramsPerUnitAnswerV1 PieceFound(decimal kilogramsPerPiece,
        Guid? company = null, Guid? product = null) =>
        ProductTheoreticalKilogramsPerUnitAnswerV1.Found(kilogramsPerPiece, new ProductTheoreticalKilogramsPerUnitEvidenceV1(
            company ?? Company, product ?? Product, SettingsUnitIdentitiesV1.Piece, ProductPhysicalUnitKindV1.Piece, "12",
            "product-unit-conversion.v1", 4, null, null, null, null, null, PieceProfile, 1, "unit-semantics.v1"));

    private static DeclareInventoryOutputRequestV1 PieceOutputRequest(ProductionOrder order, int? pieceCount,
        string kg = "20") =>
        new(Guid.NewGuid(), order.Version, Roll, "8", "GoodProduct", Product, "12", Guid.NewGuid(), Guid.NewGuid(),
            "LBL-PIECE", null, null, [], "Company", Company, "Company", Company, Location, "Free", Guid.NewGuid(), kg,
            At.AddMinutes(1), OutputLine: new("ClassLine", null, null), PieceCount: pieceCount);

    private static async Task<ProductionOrder> IssuedOrderAsync(Fixture fixture)
    {
        var order = await fixture.ApprovedOrderAsync();
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(20m), At);
        return order;
    }

    [Fact]
    public async Task A_counted_product_output_records_pieces_with_the_weighed_kg_and_the_captured_theoretical_figure()
    {
        var fixture = new Fixture();
        fixture.ProductPieces.Answer = PieceFound(0.4m);
        var order = await IssuedOrderAsync(fixture);

        var result = await fixture.Service.DeclareInventoryOutputAsync(order.Id, PieceOutputRequest(order, 48, "20"),
            "piece-output-1", Permission, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var asked = Assert.Single(fixture.ProductPieces.Asked);
        Assert.Equal((Product, SettingsUnitIdentitiesV1.Piece, DateOnly.FromDateTime(At.AddMinutes(1).UtcDateTime)), asked);
        var pieces = Assert.Single(fixture.ReturnOutputs.Outputs).Pieces!;
        Assert.Equal((48, 0.4m), (pieces.Count.Value, pieces.Theoretical.KilogramsPerPiece));
        Assert.Equal(
            "product:12;formula:product-unit-conversion.v1;dimension:4;weight-table:-;policy:-;unit-profile:"
            + PieceProfile.ToString("D") + "/1;unit-semantics:unit-semantics.v1",
            pieces.Theoretical.SourceVersion);
        Assert.Equal(20m, Assert.Single(order.Outputs).Mass.Value);
        // 20 kg / 48 = 0.41666… real against 0.4 theoretical: +0.017 kg per piece, +4.17 %; no threshold applied.
        var view = Assert.Single(result.Resource!.OutputPieces!);
        Assert.Equal((48, 48, "20", "0.4", "0.417", "0.017", "4.17", false),
            (view.DeclaredPieceCount, view.NetPieceCount, view.NetKg, view.TheoreticalKgPerPiece, view.RealKgPerPiece,
                view.DeviationKgPerPiece, view.DeviationPercent, view.DeviationThresholdApplied));
        Assert.Equal(OutputPieceDeviationCalculatorV1.FormulaVersion, view.FormulaVersion);
    }

    [Fact]
    public async Task A_counted_product_output_without_pieces_is_refused_before_the_inventory_port()
    {
        var fixture = new Fixture();
        fixture.ProductPieces.Answer = PieceFound(0.4m);
        var order = await IssuedOrderAsync(fixture);

        var result = await fixture.Service.DeclareInventoryOutputAsync(order.Id, PieceOutputRequest(order, null),
            "piece-output-missing", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.OutputPiecesRequired),
            (result.Status, result.Code));
        Assert.Empty(fixture.ReturnOutputs.Outputs);
        Assert.Empty(order.Outputs);
    }

    [Fact]
    public async Task An_uncounted_product_output_keeps_no_pieces_and_refuses_a_piece_count()
    {
        var fixture = new Fixture();
        var order = await IssuedOrderAsync(fixture);

        var refused = await fixture.Service.DeclareInventoryOutputAsync(order.Id, PieceOutputRequest(order, 3),
            "uncounted-with-pieces", Permission, CancellationToken.None);
        var recorded = await fixture.Service.DeclareInventoryOutputAsync(order.Id, PieceOutputRequest(order, null),
            "uncounted-output", Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.OutputPiecesNotApplicable),
            (refused.Status, refused.Code));
        Assert.Equal(ManufacturingApiStatus.Ok, recorded.Status);
        Assert.Null(Assert.Single(fixture.ReturnOutputs.Outputs).Pieces);
        Assert.Null(recorded.Resource!.OutputPieces);
    }

    [Theory]
    [InlineData(ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource, ProductTheoreticalKilogramsPerUnitCodesV1.NoSource)]
    [InlineData(ProductTheoreticalKilogramsPerUnitOutcomeV1.Refused, ProductTheoreticalKilogramsPerUnitCodesV1.WeightConflict)]
    public async Task A_counted_product_whose_card_gives_no_theoretical_figure_is_refused_fail_closed(
        ProductTheoreticalKilogramsPerUnitOutcomeV1 outcome, string code)
    {
        var fixture = new Fixture();
        fixture.ProductPieces.Answer = ProductTheoreticalKilogramsPerUnitAnswerV1.Without(outcome, code);
        var order = await IssuedOrderAsync(fixture);

        var result = await fixture.Service.DeclareInventoryOutputAsync(order.Id, PieceOutputRequest(order, 48),
            "piece-no-source-" + outcome, Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingOutputPieceCodesV1.KilogramsPerPieceSourceMissing),
            (result.Status, result.Code));
        Assert.Empty(fixture.ReturnOutputs.Outputs);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("denied")]
    [InlineData("not-found")]
    [InlineData("other-company")]
    [InlineData("other-product")]
    [InlineData("too-fine")]
    [InlineData("no-port")]
    public async Task An_unreadable_or_foreign_piece_answer_is_unavailable_and_records_nothing(string answer)
    {
        var fixture = new Fixture(withProductPieces: answer != "no-port");
        fixture.ProductPieces.Answer = answer switch
        {
            "unavailable" => ProductTheoreticalKilogramsPerUnitAnswerV1.Without(
                ProductTheoreticalKilogramsPerUnitOutcomeV1.Unavailable, ProductTheoreticalKilogramsPerUnitCodesV1.Unavailable),
            "denied" => ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.Denied),
            "not-found" => ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.NotFound),
            "other-company" => PieceFound(0.4m, company: Guid.NewGuid()),
            "other-product" => PieceFound(0.4m, product: Guid.NewGuid()),
            "too-fine" => PieceFound(0.0000000000001m),
            _ => PieceFound(0.4m),
        };
        var order = await IssuedOrderAsync(fixture);

        var result = await fixture.Service.DeclareInventoryOutputAsync(order.Id, PieceOutputRequest(order, 48),
            "piece-unavailable-" + answer, Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, ManufacturingOutputPieceCodesV1.SourceUnavailable),
            (result.Status, result.Code));
        Assert.Empty(fixture.ReturnOutputs.Outputs);
        Assert.Empty(order.Outputs);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_declared_piece_count_below_one_is_an_invalid_request_before_any_owner_call(int pieceCount)
    {
        var fixture = new Fixture();
        fixture.ProductPieces.Answer = PieceFound(0.4m);
        var order = await IssuedOrderAsync(fixture);

        var result = await fixture.Service.DeclareInventoryOutputAsync(order.Id, PieceOutputRequest(order, pieceCount),
            "piece-invalid-" + pieceCount, Permission, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, "pieceCount"), (result.Status, result.Field));
        Assert.Empty(fixture.ProductPieces.Asked);
        Assert.Empty(fixture.ReturnOutputs.Outputs);
    }

    [Fact]
    public async Task The_piece_count_joins_the_request_hash_so_the_same_key_with_another_count_is_an_idempotency_conflict()
    {
        var fixture = new Fixture();
        fixture.ProductPieces.Answer = PieceFound(0.4m);
        var order = await IssuedOrderAsync(fixture);
        var request = PieceOutputRequest(order, 48);

        var first = await fixture.Service.DeclareInventoryOutputAsync(order.Id, request, "piece-hash", Permission,
            CancellationToken.None);
        var replay = await fixture.Service.DeclareInventoryOutputAsync(order.Id, request, "piece-hash", Permission,
            CancellationToken.None);
        var changed = await fixture.Service.DeclareInventoryOutputAsync(order.Id, request with { PieceCount = 47 },
            "piece-hash", Permission, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, first.Status);
        Assert.True(replay.IsReplay);
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingApiCodes.IdempotencyConflict),
            (changed.Status, changed.Code));
        Assert.Single(fixture.ReturnOutputs.Outputs);
        Assert.Single(fixture.ProductPieces.Asked);
    }

    [Fact]
    public async Task A_correction_forwards_the_pieces_it_takes_back_and_the_order_read_is_net_of_them()
    {
        var fixture = new Fixture();
        var (order, outputId) = CountedCorrectableOrder(fixture);

        var result = await fixture.Service.CorrectOutputAsync(order.Id, outputId,
            new CorrectOutputRequestV1(Guid.NewGuid(), order.Version, Guid.NewGuid(), LabelVersion, Guid.NewGuid(), "5",
                CorrectionReason, At.AddMinutes(2), PieceCount: 12),
            "correct-pieces-1", ErpPermissions.ManufacturingFactsCorrect, CancellationToken.None);

        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Equal(new PieceCount(12), Assert.Single(fixture.OutputCorrections.Commands).Pieces);
        var view = Assert.Single(result.Resource!.OutputPieces!);
        Assert.Equal((48, 36, "15"), (view.DeclaredPieceCount, view.NetPieceCount, view.NetKg));
    }

    [Fact]
    public async Task A_correction_of_a_counted_output_without_pieces_is_refused_by_the_domain()
    {
        var fixture = new Fixture();
        var (order, outputId) = CountedCorrectableOrder(fixture);

        var result = await fixture.Service.CorrectOutputAsync(order.Id, outputId,
            new CorrectOutputRequestV1(Guid.NewGuid(), order.Version, Guid.NewGuid(), LabelVersion, Guid.NewGuid(), "5",
                CorrectionReason, At.AddMinutes(2)),
            "correct-pieces-missing", ErpPermissions.ManufacturingFactsCorrect, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.OutputPiecesRequired),
            (result.Status, result.Code));
        Assert.Empty(order.OutputReversals);
    }

    [Fact]
    public async Task A_negative_correction_piece_count_is_an_invalid_request()
    {
        var fixture = new Fixture();
        var (order, outputId) = CountedCorrectableOrder(fixture);

        var result = await fixture.Service.CorrectOutputAsync(order.Id, outputId,
            new CorrectOutputRequestV1(Guid.NewGuid(), order.Version, Guid.NewGuid(), LabelVersion, Guid.NewGuid(), "5",
                CorrectionReason, At.AddMinutes(2), PieceCount: -1),
            "correct-pieces-negative", ErpPermissions.ManufacturingFactsCorrect, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, "pieceCount"), (result.Status, result.Field));
        Assert.Empty(fixture.OutputCorrections.Commands);
    }

    private static (ProductionOrder Order, Guid OutputId) CountedCorrectableOrder(Fixture fixture)
    {
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.CutToLength,
            [new PlannedInput(Roll, new Kilograms(1000m))],
            cutToLengthSpec: new CutToLengthSpec([new CutToLengthLine(CorrectionCutLine, 1, 6000m, 3)]));
        plan.WithDueDate().Approve(plan.Version, Actor, At);
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        fixture.Store.Orders[order.Id] = order;
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(20m), At);
        var outputId = Guid.NewGuid();
        order.DeclareOutput(order.Version, outputId, Roll, OutputClass.GoodProduct, new Kilograms(20m), At,
            PlanOutputLineReference.ForCutLine(CorrectionCutLine),
            new OutputPieces(new PieceCount(48), new OutputTheoreticalKilogramsPerPiece(0.4m, "SENTETIK-source")));
        return (order, outputId);
    }

    /// <summary>AK-7: a scripted Product owner answer; records each (product, unit, date) it was asked.</summary>
    private sealed class ProductPiecePort : IProductTheoreticalKilogramsPerUnitReadPortV1
    {
        public ProductTheoreticalKilogramsPerUnitAnswerV1 Answer { get; set; } =
            ProductTheoreticalKilogramsPerUnitAnswerV1.Without(ProductTheoreticalKilogramsPerUnitOutcomeV1.NoSource,
                ProductTheoreticalKilogramsPerUnitCodesV1.UnitNotOnCard);

        public List<(Guid ProductId, Guid UnitId, DateOnly EffectiveOn)> Asked { get; } = [];

        public Task<ProductTheoreticalKilogramsPerUnitAnswerV1> ReadAsync(Guid productId, Guid unitId, DateOnly effectiveOn,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((productId, unitId, effectiveOn));
            return Task.FromResult(Answer);
        }
    }
}
