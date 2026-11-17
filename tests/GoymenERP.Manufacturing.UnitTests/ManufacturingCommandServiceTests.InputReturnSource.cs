using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Fact]
    public async Task A_null_return_source_preserves_the_legacy_request_hash_and_an_explicit_source_changes_the_same_keys_identity()
    {
        var fixture = new Fixture(); var order = await fixture.ApprovedOrderAsync();
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(20m), At);
        var request = new ReturnInventoryInputRequestV1(Guid.NewGuid(), order.Version, Roll, "8", Guid.NewGuid(), "2", At);
        var result = await fixture.Service.ReturnInventoryInputAsync(order.Id, request, "return-source-legacy", Permission, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var legacyCanonical = new
        {
            orderId = order.Id, returnId = request.ReturnId, expectedVersion = request.ExpectedVersion,
            parentLabelId = request.ParentRollId, expectedParentLabelVersion = 8L, movementId = request.MovementId,
            kg = "2", occurredAtUtc = At.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        };
        var record = Assert.Single(fixture.Store.Commands).Value;
        Assert.Equal(TestSha256(ManufacturingCommandNames.ReturnInput + "|" + JsonSerializer.Serialize(legacyCanonical,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))), record.RequestHash);
        Assert.Null(Assert.Single(fixture.ReturnOutputs.Returns).SourceIssueMovementId);
        var replay = await fixture.Service.ReturnInventoryInputAsync(order.Id, request with { SourceIssueMovementId = null },
            "return-source-legacy", Permission, CancellationToken.None);
        Assert.True(replay.IsReplay); Assert.Single(fixture.ReturnOutputs.Returns);
        var changed = await fixture.Service.ReturnInventoryInputAsync(order.Id, request with { SourceIssueMovementId = Guid.NewGuid() },
            "return-source-legacy", Permission, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingApiCodes.IdempotencyConflict), (changed.Status, changed.Code));
        Assert.Single(fixture.ReturnOutputs.Returns);
    }

    [Fact]
    public async Task An_explicit_return_source_is_forwarded_unchanged_and_an_empty_identity_is_refused_before_any_owner()
    {
        var fixture = new Fixture(); var order = await fixture.ApprovedOrderAsync();
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(20m), At);
        var source = Guid.NewGuid(); var request = new ReturnInventoryInputRequestV1(Guid.NewGuid(), order.Version,
            Roll, "8", Guid.NewGuid(), "2", At, source);
        var invalid = await fixture.Service.ReturnInventoryInputAsync(order.Id, request with { SourceIssueMovementId = Guid.Empty },
            "return-source-invalid", Permission, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Invalid, "sourceIssueMovementId"), (invalid.Status, invalid.Field));
        Assert.Empty(fixture.ReturnOutputs.Returns); Assert.Empty(fixture.Store.Commands);
        var result = await fixture.Service.ReturnInventoryInputAsync(order.Id, request, "return-source-explicit", Permission, CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Equal(source, Assert.Single(fixture.ReturnOutputs.Returns).SourceIssueMovementId);
    }
}
