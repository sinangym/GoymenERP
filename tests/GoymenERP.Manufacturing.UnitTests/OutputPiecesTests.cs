using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// AK-7 (URT-D112, KLN-D119): a counted output is declared with its piece count and the weighed kg, and carries the
/// theoretical kg per piece captured with it; corrections take pieces back consistently with the kg; the deviation of the
/// real kg per piece from the theoretical one is one deterministic calculation with no threshold (URT-D51 (5) open).
/// </summary>
public sealed class OutputPiecesTests
{
    private static readonly Guid Company = Guid.Parse("10000000-0000-0000-0000-0000000a7001");
    private static readonly Guid WorkCenter = Guid.Parse("20000000-0000-0000-0000-0000000a7001");
    private static readonly Guid Roll = Guid.Parse("30000000-0000-0000-0000-0000000a7001");
    private static readonly Guid Planner = Guid.Parse("40000000-0000-0000-0000-0000000a7001");
    private static readonly Guid Proposer = Guid.Parse("40000000-0000-0000-0000-0000000a7002");
    private static readonly Guid Approver = Guid.Parse("40000000-0000-0000-0000-0000000a7003");
    private static readonly Guid CutLine = Guid.Parse("50000000-0000-0000-0000-0000000a7001");
    private const string Reason = "Sayım hatası";
    private const string Source = "product:7;formula:product-unit-conversion.v1;dimension:3;weight-table:-;policy:-;unit-profile:a/1;unit-semantics:1";

    private static Kilograms Kg(decimal value) => new(value);

    private static PlanOutputLineReference Line => PlanOutputLineReference.ForCutLine(CutLine);

    private static OutputPieces Pieces(int count, decimal kgPerPiece = 12.5m) =>
        new(new PieceCount(count), new OutputTheoreticalKilogramsPerPiece(kgPerPiece, Source));

    private static ProductionOrder Order()
    {
        var spec = new CutToLengthSpec([new CutToLengthLine(CutLine, 1, 6000m, 3)]);
        var plan = new ProductionPlan(Guid.NewGuid(), Company, WorkCenter, ManufacturingProcess.CutToLength,
            [new PlannedInput(Roll, Kg(1_000m))], cutToLengthSpec: spec);
        plan.WithDueDate().Approve(plan.Version, Planner, DateTimeOffset.UnixEpoch);
        var order = ProductionOrder.FromApprovedPlan(Guid.NewGuid(), plan);
        order.Issue(Roll, Kg(1_000m));
        return order;
    }

    private static OutputDeclaration Declare(ProductionOrder order, decimal mass, OutputPieces? pieces,
        OutputClass outputClass = OutputClass.GoodProduct) =>
        order.DeclareOutput(order.Version, Guid.NewGuid(), Roll, outputClass, Kg(mass), OrderSteps.Next(),
            outputClass == OutputClass.GoodProduct ? Line : PlanOutputLineReference.ClassLine, pieces);

    private static OutputReversal Reverse(ProductionOrder order, Guid outputId, decimal mass, int? pieces) =>
        order.ReverseOutput(order.Version, Guid.NewGuid(), outputId, Kg(mass), Reason, OrderSteps.Next(),
            pieces is { } count ? new PieceCount(count) : null);

    [Fact]
    public void A_counted_output_keeps_its_pieces_and_theoretical_figure_and_its_kg_stays_the_balance_quantity()
    {
        var order = Order();

        var output = Declare(order, 600m, Pieces(48));

        Assert.Equal(48, output.Pieces!.Count.Value);
        Assert.Equal(12.5m, output.Pieces.Theoretical.KilogramsPerPiece);
        Assert.Equal(Source, output.Pieces.Theoretical.SourceVersion);
        Assert.Equal(new PieceCount(48), order.NetPiecesOf(output.OutputId));
        Assert.Equal(Kg(600m), order.NetMassOf(output.OutputId));
    }

    [Fact]
    public void An_uncounted_output_has_no_net_pieces()
    {
        var order = Order();

        var output = Declare(order, 600m, null);

        Assert.Null(output.Pieces);
        Assert.Null(order.NetPiecesOf(output.OutputId));
    }

    [Fact]
    public void A_process_loss_is_never_counted_in_pieces()
    {
        var order = Order();
        var version = order.Version;

        var error = Assert.Throws<ManufacturingRuleException>(() => Declare(order, 5m, Pieces(1), OutputClass.ProcessLoss));

        Assert.Equal(ManufacturingRuleCodes.OutputPiecesNotApplicable, error.Code);
        Assert.Equal(version, order.Version);
        Assert.Empty(order.Outputs);
    }

    [Fact]
    public void A_piece_count_is_never_negative_and_a_declaration_counts_at_least_one_piece()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PieceCount(-1));
        Assert.Throws<ArgumentException>(() => Pieces(0));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.0000000000001")]
    public void A_theoretical_figure_is_positive_and_never_rounded(string value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OutputTheoreticalKilogramsPerPiece(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), Source));
    }

    [Fact]
    public void A_theoretical_figure_names_its_source()
    {
        Assert.Throws<ArgumentException>(() => new OutputTheoreticalKilogramsPerPiece(1m, " "));
        Assert.Throws<ArgumentException>(() => new OutputTheoreticalKilogramsPerPiece(1m,
            new string('x', OutputTheoreticalKilogramsPerPiece.SourceVersionMaximumLength + 1)));
    }

    [Fact]
    public void A_correction_of_a_counted_output_takes_pieces_back_and_may_correct_only_the_kg()
    {
        var order = Order();
        var output = Declare(order, 600m, Pieces(48));

        var weighOnly = Reverse(order, output.OutputId, 10m, 0);
        var counted = Reverse(order, output.OutputId, 50m, 4);

        Assert.Equal(new PieceCount(0), weighOnly.Pieces);
        Assert.Equal(new PieceCount(4), counted.Pieces);
        Assert.Equal(new PieceCount(44), order.NetPiecesOf(output.OutputId));
        Assert.Equal(Kg(540m), order.NetMassOf(output.OutputId));
    }

    [Fact]
    public void A_full_correction_takes_back_every_piece_with_the_last_kg()
    {
        var order = Order();
        var output = Declare(order, 600m, Pieces(48));

        Reverse(order, output.OutputId, 600m, 48);

        Assert.Equal(new PieceCount(0), order.NetPiecesOf(output.OutputId));
        Assert.Equal(Kg(0m), order.NetMassOf(output.OutputId));
    }

    [Fact]
    public void A_correction_of_a_counted_output_without_pieces_is_refused()
    {
        var order = Order();
        var output = Declare(order, 600m, Pieces(48));
        var version = order.Version;

        var error = Assert.Throws<ManufacturingRuleException>(() => Reverse(order, output.OutputId, 10m, null));

        Assert.Equal(ManufacturingRuleCodes.OutputPiecesRequired, error.Code);
        Assert.Equal(version, order.Version);
        Assert.Empty(order.OutputReversals);
    }

    [Fact]
    public void A_correction_of_an_uncounted_output_with_pieces_is_refused()
    {
        var order = Order();
        var output = Declare(order, 600m, null);

        var error = Assert.Throws<ManufacturingRuleException>(() => Reverse(order, output.OutputId, 10m, 1));

        Assert.Equal(ManufacturingRuleCodes.OutputPiecesNotApplicable, error.Code);
        Assert.Empty(order.OutputReversals);
    }

    [Fact]
    public void Corrections_never_take_back_more_pieces_than_were_declared()
    {
        var order = Order();
        var output = Declare(order, 600m, Pieces(48));
        Reverse(order, output.OutputId, 100m, 40);

        var error = Assert.Throws<ManufacturingRuleException>(() => Reverse(order, output.OutputId, 100m, 9));

        Assert.Equal(ManufacturingRuleCodes.OutputPieceReversalExceedsDeclared, error.Code);
        Assert.Equal(new PieceCount(8), order.NetPiecesOf(output.OutputId));
    }

    [Theory]
    [InlineData(600, 47)]
    [InlineData(100, 48)]
    public void The_net_kg_and_the_net_pieces_reach_zero_together(int mass, int pieces)
    {
        var order = Order();
        var output = Declare(order, 600m, Pieces(48));

        var error = Assert.Throws<ManufacturingRuleException>(() => Reverse(order, output.OutputId, mass, pieces));

        Assert.Equal(ManufacturingRuleCodes.OutputPieceReversalInconsistent, error.Code);
        Assert.Empty(order.OutputReversals);
    }

    [Fact]
    public void A_stored_counted_output_and_its_corrections_restore_with_their_pieces()
    {
        var order = Order();
        var output = Declare(order, 600m, Pieces(48));
        var reversal = Reverse(order, output.OutputId, 50m, 4);

        var restored = ProductionOrder.Restore(order.Id, order.PlanId, order.CompanyId, order.WorkCenterId, order.Process,
            order.Target, order.PlannedInputs, order.Issues, order.Returns, order.Outputs, order.WipMeasurements, null, null,
            order.Version, plannedOutputLines: order.PlannedOutputLines, outputReversals: order.OutputReversals,
            dueDate: order.DueDate, planDueDate: order.PlanDueDate);

        Assert.Equal(output, restored.Outputs.Single());
        Assert.Equal(reversal, restored.OutputReversals.Single());
        Assert.Equal(new PieceCount(44), restored.NetPiecesOf(output.OutputId));
    }

    [Fact]
    public void An_approved_weighing_correction_keeps_the_pieces_of_a_counted_output()
    {
        var order = Order();
        var output = Declare(order, 600m, Pieces(48));
        var correction = WeighingCorrection.Propose(order, order.Version, Guid.NewGuid(), output.OutputId, Kg(580m), Reason,
            Proposer, OrderSteps.Next());

        var applied = correction.Approve(order, order.Version, Approver, Guid.NewGuid(), Guid.NewGuid(), OrderSteps.Next());

        Assert.Equal(new PieceCount(48), applied.Reversal.Pieces);
        Assert.Equal(output.Pieces, applied.CorrectedOutput.Pieces);
        Assert.Equal(new PieceCount(0), order.NetPiecesOf(output.OutputId));
        Assert.Equal(new PieceCount(48), order.NetPiecesOf(applied.CorrectedOutput.OutputId));
    }

    [Fact]
    public void The_deviation_is_real_kg_per_piece_against_the_theoretical_figure_rounded_once_half_away_from_zero()
    {
        // 600 kg / 48 = 12.5 kg real against 12 kg theoretical: +0.5 kg per piece, +4.17 %.
        var heavier = OutputPieceDeviationCalculatorV1.Calculate(Kg(600m), new PieceCount(48), 12m)!;
        // 100 kg / 3 = 33.3333… kg real against 34 kg: −0.667 kg per piece, −1.96 %.
        var lighter = OutputPieceDeviationCalculatorV1.Calculate(Kg(100m), new PieceCount(3), 34m)!;

        Assert.Equal(new OutputPieceDeviationV1(12.500m, 0.500m, 4.17m), heavier);
        Assert.Equal(new OutputPieceDeviationV1(33.333m, -0.667m, -1.96m), lighter);
    }

    [Fact]
    public void A_midpoint_rounds_away_from_zero()
    {
        // 1.0005 kg real against 1 kg: 0.0005 kg rounds to 0.001; 0.05 % stays 0.05.
        var answer = OutputPieceDeviationCalculatorV1.Calculate(Kg(2.001m), new PieceCount(2), 1m)!;

        Assert.Equal(1.001m, answer.RealKilogramsPerPiece);
        Assert.Equal(0.001m, answer.DeviationKilogramsPerPiece);
        Assert.Equal(0.05m, answer.DeviationPercent);
    }

    [Fact]
    public void An_output_with_no_net_pieces_has_no_deviation_and_a_theoretical_figure_must_be_positive()
    {
        Assert.Null(OutputPieceDeviationCalculatorV1.Calculate(Kg(0m), new PieceCount(0), 12m));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OutputPieceDeviationCalculatorV1.Calculate(Kg(1m), new PieceCount(1), 0m));
    }
}
