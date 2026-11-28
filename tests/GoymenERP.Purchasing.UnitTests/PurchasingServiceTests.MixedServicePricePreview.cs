using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    // Application/read port protocol only. Real SQL original approval evidence is covered by separate SQL source cases.
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Mixed_price_preview_uses_OrdersRevise_and_native_disclosure_without_OrdersApprove_or_general_read(bool withheld)
    {
        var f = LinkedRevisionFixture(); var world = new World(); world.Store.PricePreviewAnswer = new(new(f.Parent, Account, Day,
            new Dictionary<Guid, string> { [f.Parent.Lines[0].Id] = "7" }), [f.Child]);
        world.Authority.Denied.Add(ApprovePermission); world.Authority.Denied.Add(ReadPermission);
        if (withheld) world.Authority.Denied.Add(world.Bindings.PriceReadPermission!);
        var result = await world.Service().ReadMixedServicePriceRevisionPreviewAsync(f.Parent.Id, "4");
        Assert.Equal((Company, "Approved", "4"), (result.CompanyId, result.Parent.Order.Status, result.Parent.Order.Version));
        var child = Assert.Single(result.Services);
        Assert.Equal(("Approved", "3", "1"), (child.Service.Status, child.Selection.ExpectedVersion, child.InitialPreparedVersion));
        Assert.Equal(f.Child.Lines.Select(x => x.LineId), child.Selection.LineIds); Assert.Equal(f.Child.Lines.Count, child.Service.Lines.Count);
        Assert.Equal(withheld ? PurchasingPriceDisclosureV1.Withheld : PurchasingPriceDisclosureV1.Visible, child.Service.PriceDisclosure);
        if (withheld) Assert.All(child.Service.Lines, line => Assert.Null(line.UnitPrice));
        else Assert.Equal("2", child.Service.Lines[0].UnitPrice);
        Assert.Equal(1, world.Store.PricePreviewReads); Assert.Equal(0, world.Store.PriceAdmissionCalls);
        Assert.Equal(world.Bindings.PriceReadPermission, world.Authority.Measured[^1]);
        Assert.DoesNotContain(ApprovePermission, world.Authority.Measured); Assert.DoesNotContain(ReadPermission, world.Authority.Measured);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result, PurchasingJson.Options));
        Assert.Equal(new[] { "companyId", "parent", "services" }, json.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.False(json.RootElement.TryGetProperty("receipt", out _)); Assert.Equal(4L, f.Parent.Version);
    }

    [Theory]
    [InlineData("cancel")] [InlineData("authority")] [InlineData("company")] [InlineData("actor")]
    public async Task Mixed_price_read_completed_owner_cannot_release_after_authority_context_or_cancellation_loss(string loss)
    {
        var f = LinkedRevisionFixture(); var world = new World(); using var cancellation = new CancellationTokenSource();
        world.Store.PricePreviewAnswer = new(new(f.Parent, Account, Day, new Dictionary<Guid, string>()), [f.Child]);
        world.Store.AfterPricePreview = () => {
            if (loss == "cancel") cancellation.Cancel();
            if (loss == "authority") world.Authority.Granted = false;
            if (loss == "company") world.Company.CurrentCompanyId = Guid.NewGuid();
            if (loss == "actor") world.User.CurrentActor = Approver;
        };
        if (loss == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            world.Service().ReadMixedServicePriceRevisionPreviewAsync(f.Parent.Id, "4", cancellation.Token));
        else Assert.Equal(403, (await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            world.Service().ReadMixedServicePriceRevisionPreviewAsync(f.Parent.Id, "4", cancellation.Token))).Status);
        Assert.Equal(0, world.Store.PriceAdmissionCalls); Assert.Equal(4L, f.Parent.Version);
    }

    [Theory]
    [InlineData("missing")] [InlineData("company")] [InlineData("id")] [InlineData("version")] [InlineData("Draft")]
    public async Task Mixed_price_read_missing_or_foreign_projection_is_not_an_empty_authorized_Approved_preview(string fault)
    {
        var f = LinkedRevisionFixture(); var world = new World();
        if (fault == "Draft") f.Parent.ReviseSelectedServicePrices(4, Account, [f.Child], [PriceSelection(f.Child)], PriceChanges(f.Child, 3m), Actor, "SENTETIK", f.At);
        world.Store.PricePreviewAnswer = fault == "missing" ? null : new(new(f.Parent, Account, Day, new Dictionary<Guid, string>()), [f.Child]);
        if (fault == "company") world.Company.CurrentCompanyId = Guid.NewGuid();
        var id = fault == "id" ? Guid.NewGuid() : f.Parent.Id; var version = fault == "version" ? "5" : "4";
        Assert.Equal(fault == "missing" ? 404 : fault == "company" ? 403 : 503,
            (await Assert.ThrowsAsync<PurchasingServiceException>(() => world.Service().ReadMixedServicePriceRevisionPreviewAsync(id, version))).Status);
        Assert.Equal(0, world.Store.PriceAdmissionCalls);
    }

    private sealed partial class FakeStore : IPurchaseMixedServicePriceRevisionPreviewStoreV1
    {
        internal PurchaseMixedOrderPreparedPreviewV1? PricePreviewAnswer;
        internal Action? AfterPricePreview;
        internal int PricePreviewReads;
        public async Task<PurchaseMixedOrderPreparedPreviewV1?> ReadMixedServicePriceRevisionPreviewAsync(Guid id, long version,
            Func<CancellationToken, Task> freshFence, CancellationToken token = default)
        {
            PricePreviewReads++; await freshFence(token); var result = PricePreviewAnswer;
            await Task.Yield(); AfterPricePreview?.Invoke(); await freshFence(token); return result;
        }
    }
}
