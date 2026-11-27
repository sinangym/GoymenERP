using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Inventory.UnitTests;

public sealed partial class InventoryTransferTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_loss_decision_without_an_audit_writer_fails_unavailable_before_reads_or_callbacks(bool replay)
    {
        await using var database = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer("Server=unused.invalid;Database=unused;Connect Timeout=1;").Options, new TestContext());
        var owner = new InventoryStore(database, new TestContext(), new AuditEventCollector(), new InventoryKilogramScale(null!), TimeProvider.System);
        var body = new DecideInventoryTransferLossV1("2", InventoryTransferDecisionKindV1.Approve, At, "Synthetic decision", 1);
        var called = false;
        Task Fence(CancellationToken _) { called = true; return Task.CompletedTask; }
        var refusal = replay
            ? await Assert.ThrowsAsync<InventoryTransferException>(() => owner.ReplayDecideLossAsync(Guid.NewGuid(), Guid.NewGuid(), body, Transport("actor"), Fence))
            : await Assert.ThrowsAsync<InventoryTransferException>(() => owner.DecideLossAsync(Guid.NewGuid(), Guid.NewGuid(), body, Transport("actor"), Fence));
        Assert.Equal((503, InventoryTransferCodesV1.Unavailable), (refusal.Status, refusal.Code));
        Assert.False(called); Assert.Empty(database.ChangeTracker.Entries()); Assert.Null(database.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData(false, "permission")]
    [InlineData(false, "actor")]
    [InlineData(false, "company")]
    [InlineData(true, "permission")]
    [InlineData(true, "actor")]
    [InlineData(true, "company")]
    public async Task Loss_native_write_and_immutable_replay_callbacks_remeasure_the_captured_authority(bool replay, string fault)
    {
        var world = new ReceiptWorld();
        void Revoke()
        {
            if (fault == "permission") world.Authority.Granted = false;
            if (fault == "actor") world.User.UserId = "other";
            if (fault == "company") world.Context.CompanyId = Guid.NewGuid();
        }
        if (replay)
        {
            world.Writer.ReplayResult = new(TransferView(Company), ReceiptFacts(), true, "original");
            world.Writer.BeforeReplayFence = Revoke;
        }
        else world.Writer.BeforeFence = Revoke;
        await Refused(Invoke(world.Service(), "loss-decision", Transport("actor")), 403, InventoryTransferCodesV1.Forbidden);
        Assert.Empty(world.Writer.Calls);
        Assert.All(world.Authority.Measured, permission => Assert.Equal(InventoryPermissionManifestV1.StockTransferLossApprove, permission));
    }
}
