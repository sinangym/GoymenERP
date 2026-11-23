using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Domain.Plans;

namespace GoymenERP.Manufacturing.UnitTests;

/// <summary>
/// F-009 D3 (API-25, URT-S2): the output correction command without a database or HTTP — its request grammar, the
/// identities, versions, mass and reason it forwards to the one-transaction correction port, the refusal codes it keeps,
/// the replay of one Idempotency-Key and the fail-closed answer when no correction port is wired.
/// </summary>
public sealed partial class ManufacturingCommandServiceTests
{
    private const string CorrectionReason = "Tartım hatası";
    private static readonly Guid CorrectionCutLine = Guid.Parse("c2000000-0000-0000-0000-00000000000c");

    [Fact]
    public void Output_correction_permission_is_registered_company_wide_and_not_critical()
    {
        Assert.Equal("manufacturing.facts.correct", ErpPermissions.ManufacturingFactsCorrect);
        Assert.Contains(ErpPermissions.ManufacturingFactsCorrect, ErpPermissions.All);
        Assert.Contains(ErpPermissions.ManufacturingFactsCorrect, ErpPermissions.CompanyWideOnly);
        Assert.DoesNotContain(ErpPermissions.ManufacturingFactsCorrect, ErpPermissions.Critical);
    }

    [Fact]
    public async Task Output_correction_forwards_identities_versions_mass_and_trimmed_reason_and_returns_the_fresh_order()
    {
        var fixture = new Fixture();
        var (order, outputId) = CorrectableOrder(fixture);
        var versionBefore = order.Version;
        var reversalId = Guid.NewGuid();
        var labelId = Guid.NewGuid();
        var movementId = Guid.NewGuid();

        var result = await fixture.Service.CorrectOutputAsync(order.Id, outputId,
            new CorrectOutputRequestV1(reversalId, versionBefore, labelId, LabelVersion, movementId, "5",
                "  " + CorrectionReason + "  ", At.AddMinutes(2)),
            "correct-output-1", ErpPermissions.ManufacturingFactsCorrect, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, versionBefore + 1, false),
            (result.Status, result.Resource?.Version, result.IsReplay));
        var command = Assert.Single(fixture.OutputCorrections.Commands);
        Assert.Equal((order.Id, versionBefore, outputId, reversalId, labelId, 7L, movementId, 5m, CorrectionReason),
            (command.OrderId, command.ExpectedOrderVersion, command.OutputId, command.ReversalId, command.LabelId,
                command.ExpectedLabelVersion, command.MovementId, command.Quantity.Value, command.Reason));
        var reversal = Assert.Single(order.OutputReversals);
        Assert.Equal((reversalId, outputId, 5m), (reversal.ReversalId, reversal.OutputId, reversal.Mass.Value));
        Assert.Equal(ErpPermissions.ManufacturingFactsCorrect, fixture.Authority.LastAsked.Permission);
        Assert.Equal(ManufacturingCommandNames.CorrectOutput,
            Assert.Single(fixture.Store.Commands).Value.CommandName);
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("expectedLabelVersion")]
    [InlineData("kg")]
    [InlineData("occurredAtUtc")]
    [InlineData("fact")]
    public async Task Output_correction_refuses_an_invalid_request_before_any_owner_call(string field)
    {
        var fixture = new Fixture();
        var (order, outputId) = CorrectableOrder(fixture);
        var reversalId = Guid.NewGuid();
        var request = new CorrectOutputRequestV1(reversalId, order.Version, Guid.NewGuid(), LabelVersion,
            Guid.NewGuid(), "5", CorrectionReason, At.AddMinutes(2));
        request = field switch
        {
            "reason" => request with { Reason = "   " },
            "expectedLabelVersion" => request with { ExpectedLabelVersion = "08" },
            "kg" => request with { Kg = "-1" },
            "occurredAtUtc" => request with { OccurredAtUtc = new DateTimeOffset(2026, 9, 18, 13, 0, 0, TimeSpan.FromHours(3)) },
            _ => request with { ReversalId = outputId },
        };

        var result = await fixture.Service.CorrectOutputAsync(order.Id, outputId, request,
            "correct-output-invalid-" + field, ErpPermissions.ManufacturingFactsCorrect, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Invalid, field), (result.Status, result.Field));
        Assert.Empty(fixture.OutputCorrections.Commands);
        Assert.Empty(order.OutputReversals);
    }

    [Fact]
    public async Task Output_correction_keeps_a_shipped_refusal_retryable_then_replays_only_the_recorded_success()
    {
        var fixture = new Fixture();
        var (order, outputId) = CorrectableOrder(fixture);
        fixture.OutputCorrections.Exception = new ManufacturingInventoryRealizationPortException(
            ManufacturingApiStatus.Conflict, ManufacturingOutputCorrectionCodesV1.Shipped);
        var request = new CorrectOutputRequestV1(Guid.NewGuid(), order.Version, Guid.NewGuid(), LabelVersion,
            Guid.NewGuid(), "5", CorrectionReason, At.AddMinutes(2));

        var result = await fixture.Service.CorrectOutputAsync(order.Id, outputId, request,
            "correct-output-shipped", ErpPermissions.ManufacturingFactsCorrect, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingOutputCorrectionCodesV1.Shipped),
            (result.Status, result.Code));
        Assert.Empty(order.OutputReversals);
        Assert.Empty(fixture.Store.Commands);
        Assert.Equal(request.ExpectedVersion, order.Version);

        // ADR-011 section 7 does not make a reversible Inventory business-rule refusal terminal.
        var refusedAgain = await fixture.Service.CorrectOutputAsync(order.Id, outputId, request,
            "correct-output-shipped", ErpPermissions.ManufacturingFactsCorrect, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingOutputCorrectionCodesV1.Shipped, false),
            (refusedAgain.Status, refusedAgain.Code, refusedAgain.IsReplay));
        Assert.Equal(2, fixture.OutputCorrections.Commands.Count);
        Assert.Empty(fixture.Store.Commands);
        Assert.Empty(order.OutputReversals);

        // URT-D31: Inventory can subsequently confirm that the shipment lock has been removed.
        fixture.OutputCorrections.Exception = null;
        var corrected = await fixture.Service.CorrectOutputAsync(order.Id, outputId, request,
            "correct-output-shipped", ErpPermissions.ManufacturingFactsCorrect, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Ok, false), (corrected.Status, corrected.IsReplay));
        Assert.Equal(3, fixture.OutputCorrections.Commands.Count);
        Assert.Single(fixture.Store.Commands);
        Assert.Single(order.OutputReversals);

        var replay = await fixture.Service.CorrectOutputAsync(order.Id, outputId, request,
            "correct-output-shipped", ErpPermissions.ManufacturingFactsCorrect, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Ok, true), (replay.Status, replay.IsReplay));
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(corrected.Resource),
            System.Text.Json.JsonSerializer.Serialize(replay.Resource));
        Assert.Equal(3, fixture.OutputCorrections.Commands.Count);
        Assert.Single(order.OutputReversals);
    }

    [Fact]
    public async Task Output_correction_replays_the_same_key_without_a_second_owner_call()
    {
        var fixture = new Fixture();
        var (order, outputId) = CorrectableOrder(fixture);
        var request = new CorrectOutputRequestV1(Guid.NewGuid(), order.Version, Guid.NewGuid(), LabelVersion,
            Guid.NewGuid(), "5", CorrectionReason, At.AddMinutes(2));

        var first = await fixture.Service.CorrectOutputAsync(order.Id, outputId, request,
            "correct-output-replay", ErpPermissions.ManufacturingFactsCorrect, CancellationToken.None);
        var replay = await fixture.Service.CorrectOutputAsync(order.Id, outputId, request,
            "correct-output-replay", ErpPermissions.ManufacturingFactsCorrect, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Ok, false), (first.Status, first.IsReplay));
        Assert.Equal((ManufacturingApiStatus.Ok, true), (replay.Status, replay.IsReplay));
        Assert.Single(fixture.OutputCorrections.Commands);
        Assert.Single(order.OutputReversals);
    }

    [Fact]
    public async Task Output_correction_without_the_correction_port_is_unavailable_and_writes_nothing()
    {
        var fixture = new Fixture(withOutputCorrections: false);
        var (order, outputId) = CorrectableOrder(fixture);

        var result = await fixture.Service.CorrectOutputAsync(order.Id, outputId,
            new CorrectOutputRequestV1(Guid.NewGuid(), order.Version, Guid.NewGuid(), LabelVersion,
                Guid.NewGuid(), "5", CorrectionReason, At.AddMinutes(2)),
            "correct-output-no-port", ErpPermissions.ManufacturingFactsCorrect, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unavailable, InventoryManufacturingRealizationCodesV1.Unavailable),
            (result.Status, result.Code));
        Assert.Empty(fixture.OutputCorrections.Commands);
        Assert.Empty(order.OutputReversals);
    }

    [Fact]
    public async Task Output_correction_of_a_declaration_without_a_plan_output_line_is_refused_by_the_domain()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(20m), At);
        var outputId = Guid.NewGuid();
        order.DeclareOutput(order.Version, outputId, Roll, OutputClass.GoodProduct, new Kilograms(20m), At);

        var result = await fixture.Service.CorrectOutputAsync(order.Id, outputId,
            new CorrectOutputRequestV1(Guid.NewGuid(), order.Version, Guid.NewGuid(), LabelVersion,
                Guid.NewGuid(), "5", CorrectionReason, At.AddMinutes(2)),
            "correct-output-no-line", ErpPermissions.ManufacturingFactsCorrect, CancellationToken.None);

        Assert.Equal((ManufacturingApiStatus.Unprocessable, ManufacturingRuleCodes.OutputLineMissing),
            (result.Status, result.Code));
        Assert.Empty(order.OutputReversals);
    }

    /// <summary>An open cut-to-length order with one issued roll and one good-product declaration on its plan line.</summary>
    private static (ProductionOrder Order, Guid OutputId) CorrectableOrder(Fixture fixture)
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
            PlanOutputLineReference.ForCutLine(CorrectionCutLine));
        return (order, outputId);
    }

    private sealed class OutputCorrectionPort(FakeStore store) : IManufacturingInventoryOutputCorrectionPortV1
    {
        public Exception? Exception { get; set; }

        public List<ManufacturingInventoryOutputCorrectionPortCommandV1> Commands { get; } = [];

        public Task<ManufacturingInventoryOutputCorrectionPortResultV1> CorrectOutputAsync(
            ManufacturingInventoryOutputCorrectionPortCommandV1 command,
            ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken)
        {
            Commands.Add(command);
            if (Exception is { } exception)
            {
                throw exception;
            }

            var order = store.Orders[command.OrderId];
            order.ReverseOutput(command.ExpectedOrderVersion, command.ReversalId, command.OutputId, command.Quantity,
                command.Reason, command.OccurredAtUtc, command.Pieces);
            Complete(store, metadata, order);
            return Task.FromResult(new ManufacturingInventoryOutputCorrectionPortResultV1(
                order.Version, command.ExpectedLabelVersion + 1, false));
        }
    }
}
