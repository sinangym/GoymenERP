using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using Microsoft.EntityFrameworkCore;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    // Pure native domain/codec boundaries. These are not persisted approval evidence or SQL acceptance.
    [Theory]
    [InlineData(0)] [InlineData(7)]
    public void Mixed_service_price_revision_reuses_native_zero_and_preserves_partial_acceptance_and_parent_prices(int price)
    {
        var f = LinkedRevisionFixture(); var member = Assert.Single(f.Parent.ServiceMemberships);
        var child = PurchaseServiceOrder.Accept(f.Child, f.Child.Version, Guid.NewGuid(), f.Child.Lines[0].LineId,
            1m, Day, Actor, "SENTETIK partial service", f.At);
        f.Parent.RecordAcceptance(4, Guid.NewGuid(), f.Parent.Lines[0].Id, Quantity.Of(1m, f.Parent.Lines[0].Quantity.Unit), f.At);
        var oldParentPrice = f.Parent.Lines[0].Price; var before = JsonSerializer.Serialize(child, PurchasingJson.Options);
        var result = Assert.Single(f.Parent.ReviseSelectedServicePrices(5, Account, [child], [PriceSelection(child)],
            PriceChanges(child, price), Actor, "SENTETIK ALS-D9/D18", f.At.AddMinutes(1)));
        Assert.Equal((6L, PurchaseOrderStatus.Draft, Actor), (f.Parent.Version, f.Parent.Status, f.Parent.PreparedBy));
        Assert.Null(f.Parent.Approval); Assert.Equal(oldParentPrice, f.Parent.Lines[0].Price); Assert.Single(f.Parent.Acceptances);
        Assert.Equal(child.Version + 1, result.Version); Assert.Equal(PurchaseServiceOrderStatus.Draft, result.Status);
        Assert.Equal((decimal)price, result.Lines[0].UnitPrice); Assert.Equal(child.Lines[1].UnitPrice, result.Lines[1].UnitPrice);
        Assert.Equal(child.PriceRevision + 1, result.PriceRevision); Assert.Equal(child.Acceptances, result.Acceptances);
        Assert.Equal(1m, result.Lines[0].AcceptedQuantity); Assert.Equal(before, JsonSerializer.Serialize(child, PurchasingJson.Options));
        Assert.Same(member, Assert.Single(f.Parent.ServiceMemberships)); Assert.Equal(1L, member.PreparedVersion);
        var revision = Assert.Single(f.Parent.ServicePriceRevisions);
        Assert.Equal((5L, 6L, 4L, PurchaseOrder.ServicePriceRevisionKind),
            (revision.ExpectedVersion, revision.OrderVersionAfter, revision.PreviousApprovalVersion, revision.Kind));
    }

    [Fact]
    public void Mixed_service_price_revision_and_one_parent_reapproval_do_not_reapprove_or_increment_unchanged_sibling()
    {
        var f = PriceSiblings(); var old = f.Children[1]; var member = f.Parent.ServiceMemberships.ToArray();
        var revised = f.Parent.ReviseSelectedServicePrices(5, Account, f.Children, f.Children.Select(PriceSelection).ToArray(),
            PriceChanges(f.Children[0], 9m), Actor, "SENTETIK one changed sibling", f.At);
        Assert.Same(old, revised[1]); Assert.Equal(6L, f.Parent.Version);
        Assert.Equal(f.Children[0].Version + 1, revised[0].Version);
        var next = f.Parent.ApproveSelectedServiceOrdersWithRetained(6, Account, revised, revised.Select(PriceSelection).ToArray(),
            Approver, f.At.AddMinutes(1), revised[0].Money.CurrencyId, new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>(),
            new HashSet<Guid> { old.OrderId });
        Assert.Equal((7L, PurchaseOrderStatus.Approved), (f.Parent.Version, f.Parent.Status));
        Assert.Same(old, next[1]); Assert.Equal(old.Version, next[1].Version); Assert.Equal(old.ApprovedBy, next[1].ApprovedBy);
        Assert.Equal(revised[0].Version + 1, next[0].Version); Assert.Equal(member, f.Parent.ServiceMemberships);
    }

    [Fact]
    public void Mixed_service_price_reapproval_cannot_skip_the_existing_parent_or_changed_child_maker_rule()
    {
        var f = PriceSiblings();
        var revised = f.Parent.ReviseSelectedServicePrices(5, Account, f.Children, f.Children.Select(PriceSelection).ToArray(),
            PriceChanges(f.Children[0], 8m), Actor, "SENTETIK price", f.At);
        Assert.Equal(PurchaseServiceOrder.SameActor, Assert.Throws<PurchaseServiceOrderException>(() => f.Parent.ApproveSelectedServiceOrdersWithRetained(6, Account, revised,
            revised.Select(PriceSelection).ToArray(), Actor, f.At.AddMinutes(1), revised[0].Money.CurrencyId,
            new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>(), new HashSet<Guid> { revised[1].OrderId })).Code);
        Assert.Equal((6L, PurchaseOrderStatus.Draft), (f.Parent.Version, f.Parent.Status));
        Assert.Equal(PurchaseServiceOrderStatus.Draft, revised[0].Status); Assert.Equal(PurchaseServiceOrderStatus.Approved, revised[1].Status);
    }

    [Theory]
    [InlineData("company")] [InlineData("supplier")] [InlineData("account")] [InlineData("currency")]
    [InlineData("version")] [InlineData("line")] [InlineData("negative")]
    public void Mixed_service_price_revision_rejects_wrong_current_context_or_native_price_without_touching_parent(string fault)
    {
        var f = LinkedRevisionFixture(); var child = fault switch {
            "company" => f.Child with { CompanyId = Guid.NewGuid() }, "supplier" => f.Child with { SupplierPartyId = Guid.NewGuid() },
            "account" => f.Child with { CompanyAccountId = Guid.NewGuid() }, "currency" => f.Child with { Money = f.Child.Money with { CurrencyId = Guid.NewGuid() } },
            _ => f.Child };
        var selection = PriceSelection(child);
        if (fault == "version") selection = selection with { ExpectedVersion = child.Version - 1 };
        if (fault == "line") selection = selection with { LineIds = [Guid.NewGuid()] };
        Assert.Throws<ArgumentException>(() => f.Parent.ReviseSelectedServicePrices(4, Account, [child], [selection],
            PriceChanges(child, fault == "negative" ? -1m : 3m), Actor, "SENTETIK invalid", f.At));
        Assert.Equal((4L, PurchaseOrderStatus.Approved), (f.Parent.Version, f.Parent.Status));
        Assert.Empty(f.Parent.ServicePriceRevisions); Assert.NotNull(f.Parent.Approval);
    }

    [Fact]
    public void Mixed_service_price_intent_freezes_nested_lists_and_changes_hash_for_exact_child_version_or_price()
    {
        var f = LinkedRevisionFixture(); var ids = f.Child.Lines.Select(x => x.LineId).ToList();
        var lines = new List<PurchaseServiceLinePriceV1> { new(ids[0], "0") };
        var services = new List<PurchaseMixedOrderApprovalSelectionV1> { new(f.Child.OrderId, f.Child.Version.ToString(CultureInfo.InvariantCulture), ids) };
        var changes = new List<PurchaseMixedServicePriceChangeV1> { new(f.Child.OrderId, lines) };
        var frozen = PurchaseMixedServicePriceIntentV1.Freeze(new("4", services, changes, "SENTETIK exact intent"));
        var hash = PurchasingRequestHash.Compute(PurchaseMixedServicePriceIntentV1.Command, f.Parent.Id, frozen);
        ids.Clear(); lines.Clear(); services.Clear(); changes.Clear();
        Assert.Equal(2, frozen.Services[0].LineIds.Count); Assert.Single(frozen.Changes[0].Lines);
        Assert.Equal(hash, PurchasingRequestHash.Compute(PurchaseMixedServicePriceIntentV1.Command, f.Parent.Id, frozen));
        Assert.NotEqual(hash, PurchasingRequestHash.Compute(PurchaseMixedServicePriceIntentV1.Command, f.Parent.Id,
            frozen with { Changes = [new(f.Child.OrderId, [new(f.Child.Lines[0].LineId, "1")])] }));
        Assert.True(PurchasingCommands.IsKnown(PurchaseMixedServicePriceIntentV1.Command));
        Assert.False(PurchasingCommands.IsOrderMutation(PurchaseMixedServicePriceIntentV1.Command));
    }

    [Fact]
    public async Task Mixed_service_price_default_owner_is_unavailable_and_nullable_history_does_not_change_legacy_wire_or_hash()
    {
        IPurchaseMixedServicePriceRevisionStoreV1 absent = new MissingPriceStore(); Assert.False(absent.MixedServicePriceEntryIsClean);
        var f = LinkedRevisionFixture(); var request = new RevisePurchaseMixedServicePricesRequest("4",
            [new(f.Child.OrderId, "3", f.Child.Lines.Select(x => x.LineId).ToArray())], [new(f.Child.OrderId, [new(f.Child.Lines[0].LineId, "2")])], "SENTETIK");
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => absent.ReviseMixedServicePricesAsync(f.Parent.Id,
            request, null!, "hash", true, (_, _) => throw new NotSupportedException(), _ => throw new NotSupportedException()))).Status);
        var native = new ApprovePurchaseOrderRequest("3");
        Assert.DoesNotContain("heldFromPriceRevisionVersion", JsonSerializer.Serialize(native, PurchasingJson.Options));
        Assert.DoesNotContain("servicePriceRevisions", JsonSerializer.Serialize(f.Parent, PurchasingJson.Options));
        Assert.Empty(f.Parent.ServicePriceRevisions);
    }

    [Fact]
    public async Task Mixed_retained_sibling_owner_read_revalidates_actual_Approved_without_second_approval_attempt_or_read_grant()
    {
        var f = await PreparationFixture();
        await f.Service.ApproveAsync(f.Id, new("1", "SENTETIK actual native original approval"), Metadata("retained-original"));
        var original = f.Store.Inner.Saved!; var writes = f.Store.Inner.Writes; var replays = f.Store.Replays;
        f.User.CurrentActor = Actor; // Old maker is reading an already approved sibling, not approving it again.
        f.Authority.Denied.Add(PurchasingPermissionManifestV1.OrdersRead);
        f.Authority.Denied.Add(PurchasingPermissionManifestV1.OrdersPriceRead);
        var result = await f.Service.ReadRetainedApprovalPreparationAsync(f.Id, "2", _ => Task.CompletedTask);
        Assert.Equal(PurchaseServiceOrderStatus.Approved, result.Prepared.Status); Assert.Equal(original.ApprovedBy, result.Prepared.ApprovedBy);
        Assert.True(PurchaseMixedOrderApprovalFactsV1.SameChild(original, result.Prepared));
        Assert.Equal(writes, f.Store.Inner.Writes); Assert.Equal(replays, f.Store.Replays); Assert.Same(original, f.Store.Inner.Saved);
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(result, PurchasingJson.Options));
        Assert.False(wire.RootElement.TryGetProperty("prepared", out _));
    }

    [Theory]
    [InlineData("cancel")] [InlineData("authority")] [InlineData("actor")] [InlineData("company")] [InlineData("drift")]
    public async Task Mixed_retained_owner_final_completion_cannot_certify_stale_authority_context_or_version(string loss)
    {
        var f = await PreparationFixture(); await f.Service.ApproveAsync(f.Id, new("1", "SENTETIK native"), Metadata("retained-loss-original"));
        var original = f.Store.Inner.Saved!; var writes = f.Store.Inner.Writes; using var cancellation = new CancellationTokenSource();
        f.AfterRead = name => {
            if (name != "supplier") return;
            if (loss == "cancel") cancellation.Cancel();
            if (loss == "authority") f.Authority.Granted = false;
            if (loss == "actor") f.User.CurrentActor = "SENTETIK different";
            if (loss == "company") f.Company.CurrentCompanyId = Guid.NewGuid();
        };
        if (loss == "drift") f.Store.Project = state => state is null ? null : state with { Version = state.Version + 1 };
        if (loss == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            f.Service.ReadRetainedApprovalPreparationAsync(f.Id, "2", _ => Task.CompletedTask, cancellation.Token));
        else Assert.Equal(loss == "drift" ? 409 : 403, (await Assert.ThrowsAsync<PurchasingServiceException>(() =>
            f.Service.ReadRetainedApprovalPreparationAsync(f.Id, "2", _ => Task.CompletedTask, cancellation.Token))).Status);
        Assert.Equal(writes, f.Store.Inner.Writes); Assert.Same(original, f.Store.Inner.Saved);
    }

    [Fact]
    public void Mixed_service_price_history_is_typed_and_legacy_supplier_only_history_omits_new_nullable_member()
    {
        var legacy = LinkedRevisionFixture();
        legacy.Parent.ReviseSupplierWithServiceOrders(4, Account, [legacy.Child], PrepareLinked(legacy), [PriceSelection(legacy.Child)],
            NewSupplier, NewSupplierAccount, PurchaseOrderInvoiceHistory.None, Actor, "SENTETIK supplier", legacy.At);
        var old = new PurchaseSupplierRevisionHistory(legacy.Parent.CompanyId, legacy.Parent.Id,
            legacy.Parent.SupplierRevisions.ToArray(), legacy.Parent.SupplierRevisionPrices.ToArray(), legacy.Parent.ApprovalVersion);
        Assert.Equal(JsonSerializer.Serialize(old), PurchaseSupplierRevisionHistory.Write(legacy.Parent));
        Assert.DoesNotContain("ServicePriceRevisions", JsonSerializer.Serialize(old));
        var f = LinkedRevisionFixture(); f.Parent.ReviseSelectedServicePrices(4, Account, [f.Child], [PriceSelection(f.Child)],
            PriceChanges(f.Child, 3m), Actor, "SENTETIK price history", f.At);
        var row = new PurchaseOrderRow { CompanyId = f.Parent.CompanyId, Id = f.Parent.Id, Version = f.Parent.Version,
            SupplierPartyId = f.Parent.SupplierPartyId, CurrentPreparedBy = Actor, ApprovedBy = null,
            SupplierRevisionHistoryJson = PurchaseSupplierRevisionHistory.Write(f.Parent) };
        var history = Assert.IsType<PurchaseSupplierRevisionHistory>(PurchaseSupplierRevisionHistory.Read(row));
        Assert.Empty(history.Revisions); Assert.Equal(PurchaseOrder.ServicePriceRevisionKind, Assert.Single(history.ServicePriceRevisions!).Kind);
        row.SupplierRevisionHistoryJson = row.SupplierRevisionHistoryJson.Replace(PurchaseOrder.ServicePriceRevisionKind, "SENTETIK unknown transition", StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => PurchaseSupplierRevisionHistory.Read(row));
        row.SupplierRevisionHistoryJson = null;
        Assert.Throws<InvalidOperationException>(() => PurchaseSupplierRevisionHistory.Read(row));
    }

    [Theory]
    [InlineData("rewrite")] [InlineData("erase")] [InlineData("two")] [InlineData("supplier")] [InlineData("wrong-prior")]
    public void Mixed_service_price_history_append_guard_rejects_rewrite_or_unrelated_transition_before_database_io(string fault)
    {
        var f = LinkedRevisionFixture(); f.Parent.ReviseSelectedServicePrices(4, Account, [f.Child], [PriceSelection(f.Child)],
            PriceChanges(f.Child, 3m), Actor, "SENTETIK price history", f.At);
        var history = JsonSerializer.Deserialize<PurchaseSupplierRevisionHistory>(PurchaseSupplierRevisionHistory.Write(f.Parent))!;
        var row = new PurchaseOrderRow { CompanyId = f.Parent.CompanyId, Id = f.Parent.Id, Version = 5,
            SupplierPartyId = f.Parent.SupplierPartyId, CompanyAccountId = Account, CurrentPreparedBy = Actor,
            CreatedBy = Actor, Status = (int)PurchaseOrderStatus.Draft, SupplierRevisionHistoryJson = JsonSerializer.Serialize(history) };
        var company = new CompanyContext(); company.Set(Company);
        using var database = new PurchasingDbContext(new DbContextOptionsBuilder<PurchasingDbContext>()
            .UseSqlServer("Server=127.0.0.1,1;Database=SENTETIK_NOT_OPENED;User Id=placeholder;Password=placeholder;TrustServerCertificate=True").Options, company);
        database.Attach(row);
        var first = history.ServicePriceRevisions![0];
        if (fault == "rewrite") history = history with { ServicePriceRevisions = [first with { Reason = "SENTETIK replaced" }] };
        if (fault == "erase") history = history with { ServicePriceRevisions = [] };
        if (fault == "two") history = history with { ServicePriceRevisions = [first, first with { ExpectedVersion = 5, OrderVersionAfter = 6 },
            first with { ExpectedVersion = 6, OrderVersionAfter = 7 }] };
        if (fault == "supplier") { row.SupplierPartyId = Guid.NewGuid(); row.CurrentPreparedBy = Actor; }
        if (fault == "wrong-prior") history = history with { ServicePriceRevisions = [first, first with {
            ExpectedVersion = 5, OrderVersionAfter = 6, PreviousPreparer = "SENTETIK unrelated" }] };
        row.SupplierRevisionHistoryJson = JsonSerializer.Serialize(history);
        if (fault == "two") row.Version = 7;
        if (fault == "wrong-prior") row.Version = 6;
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        Assert.Null(database.Database.CurrentTransaction);
    }

    private sealed class MissingPriceStore : IPurchaseMixedServicePriceRevisionStoreV1 { }
    private sealed record PriceSiblingCase(PurchaseOrder Parent, IReadOnlyList<PurchaseServiceOrderState> Children, DateTimeOffset At);
    private static PriceSiblingCase PriceSiblings()
    {
        var source = MixedPreviewFixture().Source; var parent = source.Parent.Order; var at = parent.UpdatedAtUtc.AddHours(1);
        var first = source.Services[0];
        var second = PurchaseServiceOrder.Create(first with { OrderId = Guid.NewGuid(), Version = 1, Status = PurchaseServiceOrderStatus.Draft,
            PreparedBy = Actor, ApprovedBy = null, CreatedAtUtc = at, UpdatedAtUtc = at });
        parent.BindServiceOrders(3, Account, [second], [PriceSelection(second)], at);
        var children = parent.ApproveSelectedServiceOrders(4, Account, [first, second], [PriceSelection(first), PriceSelection(second)],
            Approver, at.AddMinutes(1), first.Money.CurrencyId, new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>());
        return new(parent, children, at.AddMinutes(2));
    }
    private static PurchaseOrderServiceSelection PriceSelection(PurchaseServiceOrderState state) =>
        new(state.OrderId, state.Version, state.Lines.Select(x => x.LineId).ToArray());
    private static IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, decimal>> PriceChanges(PurchaseServiceOrderState child, decimal price) =>
        new Dictionary<Guid, IReadOnlyDictionary<Guid, decimal>> { [child.OrderId] = new Dictionary<Guid, decimal> { [child.Lines[0].LineId] = price } };
}
