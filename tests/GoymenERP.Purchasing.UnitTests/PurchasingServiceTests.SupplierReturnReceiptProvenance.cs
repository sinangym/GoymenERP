using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Supplier_return_actual_saved_and_original_snapshot_replay_are_proven_but_legacy_current_projection_is_not(bool approve)
    {
        var (world, order, returns) = await ReturnWorldAsync(); var service = world.Service(reversals: new ReturnAdjustmentHost(), returns: returns); var request = ReturnRequest();
        var created = await service.CreateSupplierReturnAsync(order, request, Metadata("proven-create"));
        Assert.True(created.CommandReceiptProven);
        var decided = await world.AsApprover(() => approve
            ? service.ApproveSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", null), Metadata("proven-approve"))
            : service.RejectSupplierReturnCostExceptionAsync(order, request.ReturnId, new("1", "SENTETIK rejected"), Metadata("proven-reject")));
        Assert.True(decided.CommandReceiptProven);
        var replay = await service.CreateSupplierReturnAsync(order, request, Metadata("proven-create"));
        Assert.True(replay.IsReplay); Assert.True(replay.CommandReceiptProven); Assert.Equal("PendingCostException", replay.Return.Status);
        returns.LegacyReplay = true;
        var legacy = await service.CreateSupplierReturnAsync(order, request, Metadata("proven-create"));
        Assert.True(legacy.IsReplay); Assert.False(legacy.CommandReceiptProven); Assert.Equal(approve ? "Recorded" : "Rejected", legacy.Return.Status);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.DoesNotContain("commandReceiptProven", JsonSerializer.Serialize(created, options));
        Assert.Equal(JsonSerializer.Serialize(created with { CommandReceiptProven = false }, options), JsonSerializer.Serialize(created, options));
    }
}
