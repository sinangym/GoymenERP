using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    [Fact]
    public void Mixed_optional_intent_is_hashed_only_when_present_and_keeps_legacy_null_wire_identical()
    {
        var request = new ApprovePurchaseOrderRequest("3"); var legacy = new { request.ExpectedVersion }; var id = Guid.NewGuid();
        Assert.Equal(JsonSerializer.Serialize(legacy, PurchasingJson.Options), JsonSerializer.Serialize(request, PurchasingJson.Options));
        Assert.Equal(PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, id, legacy),
            PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, id, request));
        var mixed = request with { MixedServices = [new(Guid.NewGuid(), "5", [Guid.NewGuid()])] };
        Assert.NotEqual(PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, id, request),
            PurchasingRequestHash.Compute(PurchasingCommands.OrderApproveV1, id, mixed));
        Assert.Equal(JsonSerializer.Serialize(mixed, PurchasingJson.Options), JsonSerializer.Serialize(PurchasingJson.ReadApprove(JsonSerializer.Serialize(mixed, PurchasingJson.Options)), PurchasingJson.Options));
    }

    [Fact]
    public async Task Mixed_preview_shows_actual_edited_child_complete_lines_and_keeps_original_membership_baseline()
    {
        var f = MixedPreviewFixture(); var original = f.Source.Parent.Order.ServiceMemberships[0];
        var result = await MixedPreviewService(f.World, f.Preparation).ReadMixedOrderApprovalPreviewAsync(f.Source.Parent.Order.Id, "3");
        var child = Assert.Single(result.Services);
        Assert.Equal("1", child.InitialPreparedVersion); Assert.Equal(original.LineIds, child.InitialLineIds);
        Assert.Equal("2", child.Selection.ExpectedVersion); Assert.Equal(f.Source.Services[0].Lines.Select(x => x.LineId), child.Selection.LineIds);
        Assert.Equal(2, child.Service.Lines.Count); Assert.Equal("Visible", child.Service.PriceDisclosure);
        Assert.All(child.Service.Lines, x => Assert.Equal("2", x.UnitPrice));
        Assert.Equal("SENTETIK-child", child.PreparedBy); Assert.Equal(3L, f.Source.Parent.Order.Version);
        Assert.Null(f.Source.Parent.Order.Approval); Assert.All(f.Source.Services, x => Assert.Null(x.ApprovedBy));
        Assert.Single(f.Preparation.Reads); Assert.Equal((f.Source.Services[0].OrderId, "2"), f.Preparation.Reads[0]);
        using var hidden = JsonDocument.Parse(JsonSerializer.Serialize(f.Source, PurchasingJson.Options));
        Assert.Empty(hidden.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Mixed_preview_uses_native_withheld_view_without_requiring_general_read_or_recalculating_money()
    {
        var f = MixedPreviewFixture(); f.World.Authority.Denied.Add(ReadPermission);
        f.World.Authority.Denied.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
        var result = await MixedPreviewService(f.World, f.Preparation).ReadMixedOrderApprovalPreviewAsync(f.Source.Parent.Order.Id, "3");
        Assert.Equal("Withheld", result.Services[0].Service.PriceDisclosure);
        Assert.All(result.Services[0].Service.Lines, x => { Assert.Null(x.UnitPrice); Assert.Null(x.Amount); });
        Assert.DoesNotContain(ReadPermission, f.World.Authority.Measured);
    }

    [Theory]
    [InlineData("actor")] [InlineData("company")] [InlineData("quantity")] [InlineData("line")] [InlineData("version")]
    public async Task Mixed_preparation_cannot_replace_the_actual_current_owner_or_seen_child(string fault)
    {
        var f = MixedPreviewFixture();
        f.Preparation.Fault = fault;
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedPreviewService(f.World, f.Preparation)
            .ReadMixedOrderApprovalPreviewAsync(f.Source.Parent.Order.Id, "3"));
        Assert.Equal(503, denied.Status); Assert.Null(f.Source.Parent.Order.Approval);
    }

    [Fact]
    public async Task Mixed_intent_admission_remains_closed_before_legacy_hash_replay_or_mutation()
    {
        var f = MixedPreviewFixture(); var before = f.World.Store.Replays;
        var denied = await Assert.ThrowsAsync<PurchasingServiceException>(() => MixedPreviewService(f.World, f.Preparation)
            .ApproveOrderAsync(f.Source.Parent.Order.Id, new("3") { MixedServices = [new(f.Source.Services[0].OrderId, "2", f.Source.Services[0].Lines.Select(x => x.LineId).ToArray())] }, Metadata("mixed-still-closed")));
        Assert.Equal(503, denied.Status); Assert.Equal(before, f.World.Store.Replays); Assert.Empty(f.Preparation.Reads);
        Assert.Null(f.Source.Parent.Order.Approval);
    }

    [Fact]
    public async Task Mixed_default_store_preview_port_and_missing_preparation_fail_closed_without_admission()
    {
        IPurchaseMixedOrderApprovalPreviewStoreV1 absent = new MissingMixedPreview(); var called = false;
        var failed = await Assert.ThrowsAsync<PurchasingServiceException>(() => absent.ReadMixedOrderApprovalPreviewAsync(Guid.NewGuid(), 1,
            (_, _, _) => { called = true; throw new InvalidOperationException(); }, _ => Task.CompletedTask));
        Assert.Equal(503, failed.Status); Assert.False(called);
        var f = MixedPreviewFixture();
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => f.World.Service()
            .ReadMixedOrderApprovalPreviewAsync(f.Source.Parent.Order.Id, "3"))).Status);
    }

    private sealed class MissingMixedPreview : IPurchaseMixedOrderApprovalPreviewStoreV1 { }
    private sealed record MixedPreviewCase(World World, PurchaseMixedOrderPreparedPreviewV1 Source, MixedPreparation Preparation);
    private static MixedPreviewCase MixedPreviewFixture()
    {
        var world = new World(); world.User.CurrentActor = Approver;
        var at = new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero); var currency = Guid.NewGuid();
        var money = PurchaseOrderMoney.Of(currency, "TRY", Guid.NewGuid(), 1, 2, 4);
        var parent = PurchaseOrder.Create(Guid.NewGuid(), Company, Party, money, at, Actor);
        parent.AddLine(1, Guid.NewGuid(), Product, KgUnit, Quantity.Of(4m, QuantityUnit.Kilogram), at);
        PurchaseServiceLine Line() => new(Guid.NewGuid(), Guid.NewGuid(), 1, "SENTETIK service", Guid.NewGuid(), "kg", Guid.NewGuid(), 1, 3, 4m, 2m, 0m);
        var child = PurchaseServiceOrder.Create(new(Company, Guid.NewGuid(), 1, PurchaseServiceOrderKind.ServicePurchase,
            PurchaseServiceOrderStatus.Draft, Party, Account, "1", "1", Day, "SENTETIK-child", null, at, at,
            new(currency, "TRY", money.ProfileId, 1, 2, 4), [Line()], [], [], [], "SENTETIK-SERVICE"));
        parent.BindServiceOrders(2, Account, [child], [new(child.OrderId, 1, child.Lines.Select(x => x.LineId).ToArray())], at);
        // Synthetic owner snapshot of a later permitted Draft edit. This does not pretend to be a persisted command.
        child = child with { Version = 2, Lines = [child.Lines[0], Line()], UpdatedAtUtc = at.AddMinutes(1) };
        var source = new PurchaseMixedOrderPreparedPreviewV1(new(parent, Account, Day,
            parent.Lines.ToDictionary(x => x.Id, _ => "1")), [child]);
        world.Store.MixedPreviewSource = source;
        return new(world, source, new(child));
    }
    private static PurchasingService MixedPreviewService(World w, IPurchaseServiceOrderApprovalPreparationV1 preparation) =>
        new(w.Company, w.User, w.Authority, new FixedCorrelation(), w.Accounts, w.Products, new PurchaseUnitVerifier(w.Settings),
            w.Store, w.Bindings, TimeProvider.System, serviceApprovalPreparation: preparation);
    private sealed class MixedPreparation(PurchaseServiceOrderState child) : IPurchaseServiceOrderApprovalPreparationV1
    {
        internal string? Fault; internal List<(Guid, string)> Reads = [];
        public async Task<PurchaseServiceOrderApprovalPreparationV1> ReadApprovalPreparationAsync(Guid id, string version,
            Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            Reads.Add((id, version)); await fence(token); var value = child;
            if (Fault == "quantity") value = value with { Lines = [child.Lines[0] with { Quantity = 99m }, child.Lines[1]] };
            if (Fault == "line") value = value with { Lines = [child.Lines[0] with { LineId = Guid.NewGuid() }, child.Lines[1]] };
            if (Fault == "version") value = value with { Version = 3 };
            await fence(token);
            return new(Fault == "company" ? Guid.NewGuid() : Company, Fault == "actor" ? Actor : Approver, value);
        }
    }
    private sealed partial class FakeStore : IPurchaseMixedOrderApprovalPreviewStoreV1
    {
        internal PurchaseMixedOrderPreparedPreviewV1? MixedPreviewSource;
        public async Task<PurchaseMixedOrderPreparedPreviewV1?> ReadMixedOrderApprovalPreviewAsync(Guid id, long version,
            Func<StoredPurchaseOrder, IReadOnlyList<PurchaseServiceOrderState>, CancellationToken, Task<PurchaseMixedOrderPreparedPreviewV1>> prepare,
            Func<CancellationToken, Task> fence, CancellationToken token = default)
        {
            await fence(token); var source = MixedPreviewSource;
            if (source is null || source.Parent.Order.Id != id) return null;
            if (source.Parent.Order.Version != version) throw new PurchasingServiceException(409, PurchasingProblemCodesV1.VersionConflict);
            var answer = await prepare(source.Parent, source.Services, token); await fence(token); return answer;
        }
    }
}
