using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Theory]
    [InlineData(SalesPermissionManifestV1.InvoicesApprove)]
    [InlineData(SalesPermissionManifestV1.OrdersPriceRead)]
    public async Task Native_invoice_decision_passes_its_current_authority_fence_into_the_owner_store(string revokedPermission)
    {
        var world = await InvoiceWorld.CreateAsync();
        var created = (await world.Service().CreateInvoiceAsync(world.Request(), Metadata("guard-create"))).Invoice;
        await world.Service().SubmitInvoiceAsync(created.Id, Command("1"), Metadata("guard-submit"));
        world.World.User.Id = "SENTETIK-independent-checker";
        var before = world.Invoices.DecideCalls;
        // The store is entered after the service's original checks. The late owner-read fence must still refuse.
        world.Invoices.BeforeGuardedDecisionRead = () => world.World.Authority.Denied.Add(revokedPermission);
        var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ApproveInvoiceAsync(created.Id, Command("2"), Metadata("guard-approve")));
        Assert.Equal(403, denied.Status);
        Assert.Equal(before, world.Invoices.DecideCalls);
        Assert.False(world.Invoices.StateAttempts.ContainsKey("guard-approve"));
        Assert.Equal("PendingApproval", world.Invoices.Stored[created.Id].State.ToString());
    }

    [Fact]
    public async Task An_invoice_decision_replay_rechecks_price_permission_after_the_owner_read()
    {
        var world = await InvoiceWorld.CreateAsync();
        var created = (await world.Service().CreateInvoiceAsync(world.Request(), Metadata("replay-fence-create"))).Invoice;
        await world.Service().SubmitInvoiceAsync(created.Id, Command("1"), Metadata("replay-fence-submit"));
        world.World.User.Id = "SENTETIK-independent-checker";
        await world.Service().ApproveInvoiceAsync(created.Id, Command("2"), Metadata("replay-fence-approve"));
        var before = world.Invoices.DecideCalls;
        world.Invoices.AfterStateReplayRead = () => world.World.Authority.Denied.Add(SalesPermissionManifestV1.OrdersPriceRead);
        var denied = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ApproveInvoiceAsync(
            created.Id, Command("2"), Metadata("replay-fence-approve", "SENTETIK-retry")));
        Assert.Equal(403, denied.Status);
        Assert.Equal(before, world.Invoices.DecideCalls);
        Assert.Equal("Approved", world.Invoices.Stored[created.Id].State.ToString());
    }
}
