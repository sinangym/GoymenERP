using System.Text.Json;
using GoymenERP.BuildingBlocks.Work;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static GoymenERP.Purchasing.UnitTests.PurchasingWorkInboxTestWorld;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchaseServiceOrderWorkInboxSourceV1Tests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Purpose_read_uses_current_immutable_revision_for_pending_and_terminal_service_orders(bool pending)
    {
        var world = new PurchasingWorkInboxTestWorld();
        var draft = Draft(CompanyA, "SENTETIK-purpose", Start);
        var approved = PurchaseServiceOrder.Approve(draft, 1, Approver, Start.AddMinutes(1));
        await world.SeedAsync(CompanyA, db => { if (pending) Add(db, [draft]); else Add(db, [draft, approved]); });
        var source = Assert.IsAssignableFrom<IWorkApprovalDecisionSourceV1>(Source(world));
        var result = await source.ReadDecisionAsync(world.Caller(CompanyA), draft.OrderId, default);
        Assert.NotNull(result);
        Assert.Equal(CompanyA, result.CompanyId);
        Assert.Equal(pending ? "1" : "2", result.SourceVersion);
        Assert.Equal(pending, result.Pending);
        Assert.Equal(draft.PreparedBy, result.Item.PreparedBy);
        Assert.Equal("/onaylarim/purchasing.service-order.v1/" + draft.OrderId.ToString("D"), result.Item.Link);
        Assert.Equal(PurchasingPermissionManifestV1.OrdersApprove, world.LastPermission);
    }

    [Fact]
    public async Task Purpose_read_cannot_disclose_another_company_or_a_denied_service_order()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var foreign = Draft(CompanyB, "SENTETIK-foreign", Start);
        var own = Draft(CompanyA, "SENTETIK-own", Start);
        await world.SeedAsync(CompanyB, db => Add(db, [foreign]));
        await world.SeedAsync(CompanyA, db => Add(db, [own]));
        var source = Source(world);
        Assert.Null(await source.ReadDecisionAsync(world.Caller(CompanyA), foreign.OrderId, default));
        world.Granted = false;
        Assert.Null(await source.ReadDecisionAsync(world.Caller(CompanyA), own.OrderId, default));
    }

    [Fact]
    public async Task Purpose_read_refuses_a_snapshot_whose_native_identity_does_not_match_its_current_revision()
    {
        var world = new PurchasingWorkInboxTestWorld();
        var draft = Draft(CompanyA, "SENTETIK-broken", Start);
        await world.SeedAsync(CompanyA, db => Add(db, [draft]));
        await using (var db = world.Context(CompanyA))
        {
            var revision = await db.ServiceOrderRevisions.SingleAsync();
            revision.SnapshotJson = JsonSerializer.Serialize(draft with { OrderId = Guid.NewGuid() }, PurchaseServiceOrderStoreV1.Json);
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => Source(world).ReadDecisionAsync(world.Caller(CompanyA), draft.OrderId, default));
    }
}
