using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    // Pure domain prerequisite only: no authorized owner read, persisted supplier revision or genuine receipt is simulated.
    [Fact]
    public void Linked_supplier_revision_candidates_clear_old_supplier_prices_without_changing_parent_membership_or_original_child()
    {
        var f = LinkedRevisionFixture(); var before = JsonSerializer.Serialize(f.Child, PurchasingJson.Options);
        var membership = f.Parent.ServiceMemberships.ToArray(); var approval = f.Parent.Approval;
        var candidate = Assert.Single(PrepareLinked(f));
        Assert.Equal((f.Child.Version + 1, PurchaseServiceOrderStatus.Draft, Actor, (string?)null),
            (candidate.Version, candidate.Status, candidate.PreparedBy, candidate.ApprovedBy));
        Assert.Equal((NewSupplier, NewSupplierAccount, "17", "23"),
            (candidate.SupplierPartyId, candidate.CompanyAccountId, candidate.PartyVersion, candidate.AccountVersion));
        Assert.All(candidate.Lines, line => Assert.Null(line.UnitPrice));
        Assert.Equal(f.Child.Lines.Select(x => x.LineId), candidate.Lines.Select(x => x.LineId));
        Assert.Equal(f.Child.Lines.Select(x => x.Quantity), candidate.Lines.Select(x => x.Quantity));
        Assert.Equal(f.Child.Lines.Select(x => x.Tax), candidate.Lines.Select(x => x.Tax));
        Assert.Equal(f.Child.Materials, candidate.Materials); Assert.Equal(f.Child.ExpectedOutputs, candidate.ExpectedOutputs);
        Assert.Equal(f.Child.DocumentNumber, candidate.DocumentNumber); Assert.Equal(f.Child.Money, candidate.Money);
        Assert.Equal(f.Child.PriceRevision, candidate.PriceRevision); Assert.Equal(before, JsonSerializer.Serialize(f.Child, PurchasingJson.Options));
        Assert.Equal(4L, f.Parent.Version); Assert.Same(approval, f.Parent.Approval); Assert.Equal(membership, f.Parent.ServiceMemberships);
        Assert.Equal(1L, Assert.Single(f.Parent.ServiceMemberships).PreparedVersion);
    }
    [Fact]
    public void Linked_supplier_revision_can_prepare_an_actual_later_Draft_edit_without_rebinding_or_freezing_the_initial_child_forever()
    {
        var f = LinkedRevisionFixture();
        var draft = PurchaseServiceOrder.RevisePrices(f.Child, f.Child.Version,
            new Dictionary<Guid, decimal> { [f.Child.Lines[0].LineId] = 7m }, "SENTETIK child editor", "SENTETIK existing price revision", f.At);
        var candidate = Assert.Single(PrepareLinked(f with { Child = draft }));
        Assert.Equal(draft.Version + 1, candidate.Version); Assert.All(candidate.Lines, x => Assert.Null(x.UnitPrice));
        Assert.Equal(draft.PriceRevision, candidate.PriceRevision); Assert.Equal(1L, f.Parent.ServiceMemberships[0].PreparedVersion);
        Assert.Equal(7m, draft.Lines[0].UnitPrice); Assert.Equal(PurchaseOrderStatus.Approved, f.Parent.Status);
    }
    [Theory]
    [InlineData(PurchaseOrderInvoiceHistory.Unavailable, PurchaseOrder.SupplierRevisionInvoiceUnavailable)]
    [InlineData(PurchaseOrderInvoiceHistory.Exists, PurchaseOrder.SupplierRevisionBlocked)]
    public void Linked_supplier_revision_unknown_or_existing_invoice_history_is_not_treated_as_empty(PurchaseOrderInvoiceHistory history, string code)
    {
        var f = LinkedRevisionFixture();
        Assert.Equal(code, Assert.Throws<PurchasingRuleException>(() => PrepareLinked(f, history)).Code);
        Assert.Equal(4L, f.Parent.Version); Assert.All(f.Child.Lines, x => Assert.NotNull(x.UnitPrice));
    }
    [Theory]
    [InlineData("company")] [InlineData("supplier")] [InlineData("account")] [InlineData("currency")]
    [InlineData("version")] [InlineData("line")]
    public void Linked_supplier_revision_requires_exact_current_child_context_version_and_all_lines(string fault)
    {
        var f = LinkedRevisionFixture(); var selected = new PurchaseOrderServiceSelection(f.Child.OrderId, f.Child.Version, f.Child.Lines.Select(x => x.LineId).ToArray());
        var child = fault switch {
            "company" => f.Child with { CompanyId = Guid.NewGuid() },
            "supplier" => f.Child with { SupplierPartyId = Guid.NewGuid() },
            "account" => f.Child with { CompanyAccountId = Guid.NewGuid() },
            "currency" => f.Child with { Money = f.Child.Money with { CurrencyId = Guid.NewGuid() } },
            _ => f.Child };
        if (fault == "version") selected = selected with { ExpectedVersion = selected.ExpectedVersion - 1 };
        if (fault == "line") selected = selected with { LineIds = [Guid.NewGuid()] };
        Assert.Equal(PurchaseOrder.SupplierRevisionBlocked, Assert.Throws<PurchasingRuleException>(() => f.Parent.PrepareLinkedSupplierRevision(
            4, Account, [child], [selected], NewSupplier, NewSupplierAccount, "17", "23", PurchaseOrderInvoiceHistory.None,
            Actor, "SENTETIK revision", f.At)).Code);
        Assert.Equal(4L, f.Parent.Version); Assert.Equal(PurchaseOrderStatus.Approved, f.Parent.Status);
    }
    [Fact]
    public void Linked_supplier_revision_rejects_actual_child_acceptance_without_replacing_its_history()
    {
        var f = LinkedRevisionFixture();
        var accepted = PurchaseServiceOrder.Accept(f.Child, f.Child.Version, Guid.NewGuid(), f.Child.Lines[0].LineId,
            1m, Day, Actor, "SENTETIK native service acceptance", f.At);
        Assert.Equal(PurchaseOrder.SupplierRevisionBlocked, Assert.Throws<PurchasingRuleException>(() => PrepareLinked(f with { Child = accepted })).Code);
        Assert.Single(accepted.Acceptances); Assert.Equal(1m, accepted.Lines[0].AcceptedQuantity); Assert.Equal(4L, f.Parent.Version);
    }
    [Fact]
    public void Linked_supplier_revision_existing_native_mutation_and_missing_owner_port_remain_closed()
    {
        var f = LinkedRevisionFixture();
        Assert.Equal(PurchaseOrder.ServiceMembershipUnavailable, Assert.Throws<PurchasingRuleException>(() => f.Parent.ReviseSupplier(
            4, NewSupplier, PurchaseOrderInvoiceHistory.None, Actor, "SENTETIK closed revision", f.At)).Code);
        Assert.Equal(PurchaseServiceOrder.StateConflict, Assert.Throws<PurchaseServiceOrderException>(() => PurchaseServiceOrder.RevisePrices(
            Assert.Single(PrepareLinked(f)), f.Child.Version + 1, new Dictionary<Guid, decimal> { [f.Child.Lines[0].LineId] = 5m },
            Actor, "SENTETIK unsupported Draft setter", f.At)).Code);
        Assert.Equal(4L, f.Parent.Version); Assert.Equal(f.Child.Version, 3L);
    }
    [Fact]
    public async Task Linked_supplier_revision_preparation_defaults_unavailable_and_private_candidate_facts_do_not_become_public_wire()
    {
        var f = LinkedRevisionFixture(); IPurchaseServiceOrderApprovalPreparationV1 absent = new MissingLinkedRevisionPreparation();
        var callbacks = 0;
        Assert.Equal(503, (await Assert.ThrowsAsync<PurchasingServiceException>(() => absent.ReadSupplierRevisionPreparationAsync(f.Child.OrderId,
            "3", NewSupplier, NewSupplierAccount, "SENTETIK revision", _ => { callbacks++; return Task.CompletedTask; }))).Status);
        Assert.Equal(0, callbacks);
        var data = new PurchaseServiceSupplierRevisionPreparationV1(Company, Actor, f.Child, Assert.Single(PrepareLinked(f)));
        using var wire = JsonDocument.Parse(JsonSerializer.Serialize(data, PurchasingJson.Options));
        Assert.Equal(2, wire.RootElement.EnumerateObject().Count());
        Assert.False(wire.RootElement.TryGetProperty("original", out _)); Assert.False(wire.RootElement.TryGetProperty("candidate", out _));
    }
    [Fact]
    public void Linked_supplier_revision_native_atomic_domain_leaf_clears_all_prices_and_preserves_immutable_membership()
    {
        var f = LinkedRevisionFixture(); var member = Assert.Single(f.Parent.ServiceMemberships);
        var changed = f.Parent.ReviseSupplierWithServiceOrders(4, Account, [f.Child], PrepareLinked(f),
            [new(f.Child.OrderId, f.Child.Version, f.Child.Lines.Select(x => x.LineId).ToArray())], NewSupplier, NewSupplierAccount,
            PurchaseOrderInvoiceHistory.None, Actor, "SENTETIK locked ALS-D115 preparation", f.At);
        Assert.Equal((5L, PurchaseOrderStatus.Draft, NewSupplier, Actor), (f.Parent.Version, f.Parent.Status, f.Parent.SupplierPartyId, f.Parent.PreparedBy));
        Assert.Null(f.Parent.Approval); Assert.All(f.Parent.Lines, x => Assert.Null(x.Price)); Assert.Same(member, Assert.Single(f.Parent.ServiceMemberships));
        Assert.All(Assert.Single(changed).Lines, x => Assert.Null(x.UnitPrice));
        Assert.Equal(4L, Assert.Single(f.Parent.SupplierRevisions).PreviousApprovalVersion);
        Assert.Equal(f.Child.Version, 3L); Assert.Equal(PurchaseServiceOrderStatus.Approved, f.Child.Status);
    }
    [Theory]
    [InlineData(0)] [InlineData(7)]
    public void Linked_supplier_revision_Draft_price_leaf_preserves_native_zero_semantics_and_Draft_maker_without_second_approval(int price)
    {
        var f = LinkedRevisionFixture(); var candidate = Assert.Single(PrepareLinked(f)) with {
            SupplierRevisionOrigin = new(f.Parent.Id, 5, new string('A', 64)) };
        var result = PurchaseServiceOrder.SetSupplierRevisionPrices(candidate, candidate.Version,
            new Dictionary<Guid, decimal> { [candidate.Lines[0].LineId] = price }, "SENTETIK editor", "SENTETIK new supplier price", f.At);
        Assert.Equal(candidate.Version + 1, result.Version); Assert.Equal((decimal)price, result.Lines[0].UnitPrice);
        Assert.Equal(Actor, result.PreparedBy); Assert.Null(result.ApprovedBy); Assert.Equal(PurchaseServiceOrderStatus.Draft, result.Status);
        Assert.Equal(candidate.SupplierRevisionOrigin, result.SupplierRevisionOrigin); Assert.Equal(candidate.PriceRevision, result.PriceRevision);
        Assert.Null(candidate.Lines[0].UnitPrice);
        // Pure locator alone is NOT authorization; the actual store additionally verifies real parent artifacts.
    }
    [Fact]
    public void Linked_supplier_revision_Draft_price_leaf_rejects_missing_origin_wrong_line_negative_and_wrong_version()
    {
        var f = LinkedRevisionFixture(); var candidate = Assert.Single(PrepareLinked(f));
        Assert.Throws<PurchaseServiceOrderException>(() => PurchaseServiceOrder.SetSupplierRevisionPrices(candidate, candidate.Version,
            new Dictionary<Guid, decimal> { [candidate.Lines[0].LineId] = 1 }, Actor, "SENTETIK price", f.At));
        candidate = candidate with { SupplierRevisionOrigin = new(f.Parent.Id, 5, new string('A', 64)) };
        Assert.Throws<ArgumentException>(() => PurchaseServiceOrder.SetSupplierRevisionPrices(candidate, candidate.Version,
            new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 1 }, Actor, "SENTETIK price", f.At));
        Assert.Throws<ArgumentException>(() => PurchaseServiceOrder.SetSupplierRevisionPrices(candidate, candidate.Version,
            new Dictionary<Guid, decimal> { [candidate.Lines[0].LineId] = -1 }, Actor, "SENTETIK price", f.At));
        Assert.Throws<PurchaseServiceOrderException>(() => PurchaseServiceOrder.SetSupplierRevisionPrices(candidate, candidate.Version - 1,
            new Dictionary<Guid, decimal> { [candidate.Lines[0].LineId] = 1 }, Actor, "SENTETIK price", f.At));
    }
    [Fact]
    public void Linked_supplier_revision_null_intent_and_null_snapshot_origin_preserve_legacy_wire_and_hash()
    {
        var f = LinkedRevisionFixture(); var native = new RevisePurchaseOrderSupplierRequest("4", NewSupplier, NewSupplierAccount, "SENTETIK revision");
        Assert.DoesNotContain("mixedServices", JsonSerializer.Serialize(native, PurchasingJson.Options));
        Assert.Equal(PurchasingRequestHash.Compute(PurchasingCommands.OrderSupplierReviseV1, f.Parent.Id, native),
            PurchasingRequestHash.Compute(PurchasingCommands.OrderSupplierReviseV1, f.Parent.Id, native with { MixedServices = null }));
        var old = JsonSerializer.Serialize(f.Child, PurchasingJson.Options); Assert.DoesNotContain("supplierRevisionOrigin", old);
        Assert.Equal(f.Child, JsonSerializer.Deserialize<PurchaseServiceOrderState>(old, PurchasingJson.Options)! with {
            Lines = f.Child.Lines, Materials = f.Child.Materials, ExpectedOutputs = f.Child.ExpectedOutputs, Acceptances = f.Child.Acceptances });
    }
    private static readonly Guid NewSupplier = Guid.Parse("ab125621-513a-4cce-8e23-23452bfe9e34");
    private static readonly Guid NewSupplierAccount = Guid.Parse("ab125621-513a-4cce-8e23-23452bfe9e35");
    private sealed record LinkedRevisionCase(PurchaseOrder Parent, PurchaseServiceOrderState Child, DateTimeOffset At);
    private static LinkedRevisionCase LinkedRevisionFixture()
    {
        var source = MixedPreviewFixture().Source; var at = source.Parent.Order.UpdatedAtUtc.AddHours(1);
        var current = source.Services[0];
        var approved = source.Parent.Order.ApproveSelectedServiceOrders(3, Account, [current],
            [new(current.OrderId, current.Version, current.Lines.Select(x => x.LineId).ToArray())], Approver, at,
            current.Money.CurrencyId, new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>());
        return new(source.Parent.Order, approved[0], at.AddHours(1));
    }
    private static IReadOnlyList<PurchaseServiceOrderState> PrepareLinked(LinkedRevisionCase f,
        PurchaseOrderInvoiceHistory history = PurchaseOrderInvoiceHistory.None) => f.Parent.PrepareLinkedSupplierRevision(4, Account,
            [f.Child], [new(f.Child.OrderId, f.Child.Version, f.Child.Lines.Select(x => x.LineId).ToArray())], NewSupplier, NewSupplierAccount,
            "17", "23", history, Actor, "SENTETIK locked ALS-D115 preparation", f.At);
    private sealed class MissingLinkedRevisionPreparation : IPurchaseServiceOrderApprovalPreparationV1
    {
        public Task<PurchaseServiceOrderApprovalPreparationV1> ReadApprovalPreparationAsync(Guid id, string version,
            Func<CancellationToken, Task> fence, CancellationToken token = default) => throw new NotSupportedException();
    }
}
