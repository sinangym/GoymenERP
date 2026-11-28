using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    [Fact]
    public async Task Linked_supplier_revision_real_preparation_uses_OrdersRevise_only_and_never_writes_an_attempt_or_approval()
    {
        var f = await SupplierPreparationFixture(); var before = f.Store.Inner.Saved; var callbacks = 0;
        f.Authority.Denied.UnionWith([PurchasingPermissionManifestV1.OrdersRead, PurchasingPermissionManifestV1.OrdersApprove,
            PurchasingPermissionManifestV1.OrdersPriceRead]);
        var prepared = await f.Service.ReadSupplierRevisionPreparationAsync(f.Id, "1", NewSupplier, NewSupplierAccount,
            new string('x', 1500), _ => { callbacks++; return Task.CompletedTask; });
        Assert.Equal((Company, Actor, NewSupplier, NewSupplierAccount, 2L, PurchaseServiceOrderStatus.Draft),
            (prepared.CompanyId, prepared.ActorId, prepared.Candidate.SupplierPartyId, prepared.Candidate.CompanyAccountId,
                prepared.Candidate.Version, prepared.Candidate.Status));
        Assert.Null(prepared.Candidate.Lines[0].UnitPrice); Assert.Null(prepared.Candidate.ApprovedBy);
        Assert.Null(prepared.Candidate.SupplierRevisionOrigin); Assert.Same(before, f.Store.Inner.Saved);
        Assert.Equal(2, f.Store.Reads); Assert.Equal(0, f.Store.Inner.Writes); Assert.Equal(0, f.Store.Replays); Assert.True(callbacks > 4);
        Assert.All(f.Authority.Measured, permission => Assert.Equal(PurchasingPermissionManifestV1.OrdersRevise, permission));
    }

    [Theory]
    [InlineData("version")] [InlineData("invoice")] [InlineData("line")] [InlineData("company")]
    public async Task Linked_supplier_revision_final_actual_owner_reread_rejects_drift_without_mutation(string fault)
    {
        var f = await SupplierPreparationFixture(); var before = f.Store.Inner.Saved!;
        f.Store.Change = value => fault switch {
            "version" => value with { Version = value.Version + 1 },
            "line" => value with { Lines = [value.Lines[0] with { Quantity = 8 }] },
            "company" => value with { CompanyId = Guid.NewGuid() }, _ => value };
        if (fault == "invoice") f.Store.LateInvoice = true;
        var failure = await Assert.ThrowsAsync<PurchasingServiceException>(() => f.Service.ReadSupplierRevisionPreparationAsync(f.Id, "1",
            NewSupplier, NewSupplierAccount, "SENTETIK revision", _ => Task.CompletedTask));
        Assert.Equal(409, failure.Status); Assert.Same(before, f.Store.Inner.Saved); Assert.Equal(0, f.Store.Inner.Writes);
    }

    [Theory]
    [InlineData("authority")] [InlineData("cancel")] [InlineData("actor")] [InlineData("company")]
    public async Task Linked_supplier_revision_every_completed_consumer_await_and_final_release_rechecks_actual_context(string loss)
    {
        var baseline = await SupplierPreparationFixture(); var count = 0;
        await baseline.Service.ReadSupplierRevisionPreparationAsync(baseline.Id, "1", NewSupplier, NewSupplierAccount,
            "SENTETIK revision", _ => { count++; return Task.CompletedTask; });
        Assert.True(count > 4);
        for (var boundary = 1; boundary <= count; boundary++)
        {
            var f = await SupplierPreparationFixture(); using var cancellation = new CancellationTokenSource(); var calls = 0;
            Task Fence(CancellationToken _)
            {
                if (++calls == boundary)
                {
                    if (loss == "authority") f.Authority.Granted = false;
                    if (loss == "cancel") cancellation.Cancel();
                    if (loss == "actor") f.User.CurrentActor = "SENTETIK other";
                    if (loss == "company") f.Company.CurrentCompanyId = Guid.NewGuid();
                }
                return Task.CompletedTask;
            }
            if (loss == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service.ReadSupplierRevisionPreparationAsync(f.Id, "1",
                NewSupplier, NewSupplierAccount, "SENTETIK revision", Fence, cancellation.Token));
            else Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() => f.Service.ReadSupplierRevisionPreparationAsync(f.Id, "1",
                NewSupplier, NewSupplierAccount, "SENTETIK revision", Fence))).Status);
            Assert.Equal(boundary, calls); Assert.Equal(0, f.Store.Inner.Writes); Assert.Equal(0, f.Store.Replays);
        }
    }

    private sealed record SupplierPreparationCase(PurchaseServiceOrderServiceV1 Service, SupplierPreparationStore Store,
        FixedAuthority Authority, FixedUser User, MutableCompanyContext Company, Guid Id);
    private static async Task<SupplierPreparationCase> SupplierPreparationFixture()
    {
        var source = new ServiceCommandSource(); var raw = new ServiceCommandStore(); var company = new MutableCompanyContext();
        var user = new FixedUser(); var authority = new FixedAuthority();
        var create = new PurchaseServiceOrderServiceV1(company, user, authority, raw, source, source, source, source, TimeProvider.System);
        await create.CreateAsync(ServiceCreate(source), Metadata("SENTETIK supplier preparation source"));
        var inner = new ServiceCommandStore(); inner.Seed(raw.Saved!); var store = new SupplierPreparationStore(inner);
        authority.Measured.Clear();
        return new(new(company, user, authority, store, source, source, source, source, TimeProvider.System), store, authority, user, company, raw.Saved!.OrderId);
    }

    // Controlled owner protocol only; real shared TX/artifacts are separate SQL-source tests.
    private sealed class SupplierPreparationStore(ServiceCommandStore inner) : IPurchaseServiceOrderStoreV1
    {
        internal ServiceCommandStore Inner => inner;
        internal int Reads, Replays;
        internal bool LateInvoice;
        internal Func<PurchaseServiceOrderState, PurchaseServiceOrderState>? Change;
        public async Task<PurchaseServiceSupplierRevisionFactsV1?> ReadSupplierRevisionFactsAsync(Guid id, Func<CancellationToken, Task> fence, CancellationToken token)
        {
            await fence(token); Reads++; var actual = await inner.ReadAsync(id, token); await fence(token);
            await fence(token); var invoice = Reads > 1 && LateInvoice ? PurchaseOrderInvoiceHistory.Exists : PurchaseOrderInvoiceHistory.None; await fence(token);
            return actual is null ? null : new(Reads > 1 && Change is not null ? Change(actual) : actual, invoice);
        }
        public Task<PurchaseServiceOrderState?> ReadAsync(Guid id, CancellationToken token) => inner.ReadAsync(id, token);
        public Task<IReadOnlyList<PurchaseServiceOrderState>> ListAsync(CancellationToken token) => inner.ListAsync(token);
        public Task<PurchaseServiceOrderWriteV1?> ReplayAsync(string command, PurchasingWriteEnvelope e, string hash, CancellationToken token)
        { Replays++; return inner.ReplayAsync(command, e, hash, token); }
        public Task<PurchaseServiceOrderWriteV1?> WriteAsync(Guid id, string command, PurchasingWriteEnvelope e, string hash, string reason,
            Func<PurchaseServiceOrderState?, CancellationToken, Task<PurchaseServiceOrderState>> mutate, Func<CancellationToken, Task> fence, CancellationToken token) =>
            inner.WriteAsync(id, command, e, hash, reason, mutate, fence, token);
    }
}
