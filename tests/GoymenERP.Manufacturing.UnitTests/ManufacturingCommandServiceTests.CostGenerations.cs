using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Manufacturing.Application;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Fact]
    public async Task Cost_recognition_replays_the_immutable_generation_without_repeating_the_inventory_effect_and_requires_reports_disclosure()
    {
        var fixture = new Fixture(); var order = await fixture.BalancedOrderAsync(); order.Close(order.Version, Actor, At);
        var port = new CostRecognitionPort(fixture); var service = CostRecognitionService(fixture, port);
        var request = new RecognizeManufacturingOrderCostRequestV1(order.Version, 0, "Synthetic stamp became readable");
        var first = await service.RecognizeCostAsync(order.Id, request, "cost-recognize-exact", CancellationToken.None);
        var replay = await service.RecognizeCostAsync(order.Id, request, "cost-recognize-exact", CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, first.Status); Assert.True(replay.IsReplay); Assert.Equal(1, port.Writes);
        Assert.Equal(first.Resource!.GenerationId, replay.Resource!.GenerationId);
        Assert.Contains(ErpPermissions.ManufacturingOrdersClose, fixture.Authority.Permissions);
        Assert.Contains(ErpPermissions.ManufacturingReportsRead, fixture.Authority.Permissions);
        fixture.Authority.Scope = Scope(companyWide: false);
        var refused = await service.RecognizeCostAsync(order.Id, request, "cost-recognize-exact", CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Denied, refused.Status); Assert.Null(refused.Resource); Assert.Equal(1, port.Writes);
    }

    private static ManufacturingCommandService CostRecognitionService(Fixture fixture, IManufacturingClosedOrderCostRecognitionPortV1 port) =>
        new(fixture.Store, fixture.Store, fixture.Store, new ManufacturingMassVerifier(fixture.Settings), fixture.Authority,
            fixture.CompanyContext, fixture.User, fixture.Correlation, new FixedTime(), costRecognition: port);

    private sealed class CostRecognitionPort(Fixture fixture) : IManufacturingClosedOrderCostRecognitionPortV1
    {
        public int Writes { get; private set; }
        public async Task<ManufacturingCostGenerationV1> RecognizeAsync(Guid orderId, long expectedOrderVersion, long expectedGenerationRevision,
            string reason, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken)
        {
            Writes++;
            var result = new ManufacturingCostGenerationV1(Company, orderId, Guid.NewGuid(), expectedOrderVersion, expectedGenerationRevision + 1,
                "1", ManufacturingCostGenerationStateV1.Unreadable, new string('a', 64), "synthetic:v1", "{}",
                "{\"outputs\":[],\"costDifferenceEventId\":null}", At, reason, metadata.CorrelationId);
            if (metadata.Completion is { } complete) await fixture.Store.RecordCommandAsync(complete(result), cancellationToken);
            return result;
        }
    }
}
