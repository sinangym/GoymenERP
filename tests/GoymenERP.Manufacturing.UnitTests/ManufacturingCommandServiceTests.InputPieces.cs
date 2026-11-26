using System.Globalization;
using System.Text.Json;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;

namespace GoymenERP.Manufacturing.UnitTests;

public sealed partial class ManufacturingCommandServiceTests
{
    [Fact]
    public async Task An_uncounted_input_preserves_the_exact_legacy_command_hash()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var request = new IssueInventoryInputRequestV1(Guid.NewGuid(), order.Version, Roll, Guid.NewGuid(), "10", At);
        var result = await fixture.Service.IssueInventoryInputAsync(order.Id, request, "legacy-input-pieces", Permission,
            CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var canonical = new
        {
            orderId = order.Id, issueId = request.IssueId, expectedVersion = request.ExpectedVersion,
            parentLabelId = Roll, movementId = request.MovementId, kg = "10",
            occurredAtUtc = At.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        };
        Assert.Equal(TestSha256(ManufacturingCommandNames.IssueInput + "|" + JsonSerializer.Serialize(canonical,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))), Assert.Single(fixture.Store.Commands).Value.RequestHash);
        Assert.Null(Assert.Single(fixture.InputIssues.Commands).PieceCount);
    }

    [Fact]
    public async Task Counted_input_is_forwarded_replayed_and_bound_to_the_command_identity()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var request = new IssueInventoryInputRequestV1(Guid.NewGuid(), order.Version, Roll, Guid.NewGuid(), "10", At, 4);
        var result = await fixture.Service.IssueInventoryInputAsync(order.Id, request, "counted-input", Permission,
            CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        Assert.Equal(4, Assert.Single(fixture.InputIssues.Commands).PieceCount);
        var replay = await fixture.Service.IssueInventoryInputAsync(order.Id, request, "counted-input", Permission,
            CancellationToken.None);
        Assert.True(replay.IsReplay);
        var changed = await fixture.Service.IssueInventoryInputAsync(order.Id, request with { PieceCount = 3 },
            "counted-input", Permission, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingApiCodes.IdempotencyConflict),
            (changed.Status, changed.Code));
        Assert.Single(fixture.InputIssues.Commands);
    }

    [Fact]
    public async Task Counted_return_keeps_the_explicit_issue_trace_and_rejects_a_changed_count_on_replay()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        order.IssueInput(order.Version, Guid.NewGuid(), Roll, new Kilograms(20m), At);
        var source = Guid.NewGuid();
        var request = new ReturnInventoryInputRequestV1(Guid.NewGuid(), order.Version, Roll, "8", Guid.NewGuid(),
            "2", At, source, 1);
        var result = await fixture.Service.ReturnInventoryInputAsync(order.Id, request, "counted-return", Permission,
            CancellationToken.None);
        Assert.Equal(ManufacturingApiStatus.Ok, result.Status);
        var command = Assert.Single(fixture.ReturnOutputs.Returns);
        Assert.Equal((source, 1), (command.SourceIssueMovementId, command.PieceCount));
        var replay = await fixture.Service.ReturnInventoryInputAsync(order.Id, request, "counted-return", Permission,
            CancellationToken.None);
        Assert.True(replay.IsReplay);
        var changed = await fixture.Service.ReturnInventoryInputAsync(order.Id, request with { PieceCount = 0 },
            "counted-return", Permission, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Conflict, ManufacturingApiCodes.IdempotencyConflict),
            (changed.Status, changed.Code));
        Assert.Single(fixture.ReturnOutputs.Returns);
    }

    [Fact]
    public async Task Negative_input_and_return_counts_are_refused_before_any_owner_or_command_record()
    {
        var fixture = new Fixture();
        var order = await fixture.ApprovedOrderAsync();
        var issued = await fixture.Service.IssueInventoryInputAsync(order.Id,
            new IssueInventoryInputRequestV1(Guid.NewGuid(), order.Version, Roll, Guid.NewGuid(), "1", At, -1),
            "negative-issue-pieces", Permission, CancellationToken.None);
        var returned = await fixture.Service.ReturnInventoryInputAsync(order.Id,
            new ReturnInventoryInputRequestV1(Guid.NewGuid(), order.Version, Roll, "8", Guid.NewGuid(), "1", At,
                PieceCount: -1), "negative-return-pieces", Permission, CancellationToken.None);
        Assert.Equal((ManufacturingApiStatus.Invalid, "pieceCount"), (issued.Status, issued.Field));
        Assert.Equal((ManufacturingApiStatus.Invalid, "pieceCount"), (returned.Status, returned.Field));
        Assert.Empty(fixture.InputIssues.Commands);
        Assert.Empty(fixture.ReturnOutputs.Returns);
        Assert.Empty(fixture.Store.Commands);
    }
}
