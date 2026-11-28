using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Xunit;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>Pure native transition/contract evidence. No store, durable membership, command, audit or SQL acceptance.</summary>
public sealed class PurchaseMixedOrderApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 8, 0, 0, TimeSpan.Zero);
    private const string ParentMaker = "SENTETIK-parent-maker";
    private const string ServiceMaker = "SENTETIK-service-maker";
    private const string Checker = "SENTETIK-checker";

    [Fact]
    public void Explicit_membership_freezes_native_child_identity_version_and_all_lines_without_moving_acceptance()
    {
        var f = Fixture(); var ids = f.Service.Lines.Select(x => x.LineId).ToArray();
        Bind(f, [new(f.Service.OrderId, f.Service.Version, ids)]);
        ids[0] = Guid.NewGuid();
        var bound = Assert.Single(f.Parent.Order.ServiceMemberships);
        Assert.Equal((f.Parent.Order.CompanyId, f.Parent.Order.Id, f.Parent.CompanyAccountId, f.Service.OrderId, 1L),
            (bound.CompanyId, bound.PurchaseOrderId, bound.CompanyAccountId, bound.ServiceOrderId, bound.PreparedVersion));
        Assert.Equal(f.Service.Lines.Select(x => x.LineId), bound.LineIds);
        Assert.Equal(3, f.Parent.Order.Version); Assert.Equal(PurchaseOrderStatus.Draft, f.Parent.Order.Status);
        Assert.Empty(f.Parent.Order.Acceptances); Assert.Empty(f.Service.Acceptances);
        Assert.All(f.Service.Lines, x => Assert.Equal(0m, x.AcceptedQuantity));
    }

    [Theory]
    [InlineData("company")] [InlineData("supplier")] [InlineData("account")] [InlineData("currency")]
    [InlineData("version")] [InlineData("foreign-id")] [InlineData("foreign-line")] [InlineData("missing-line")] [InlineData("duplicate-line")]
    public void Similar_supplier_or_date_never_substitutes_exact_child_membership(string fault)
    {
        var f = Fixture(); var service = f.Service;
        if (fault == "company") service = service with { CompanyId = Guid.NewGuid() };
        if (fault == "supplier") service = service with { SupplierPartyId = Guid.NewGuid() };
        if (fault == "account") service = service with { CompanyAccountId = Guid.NewGuid() };
        if (fault == "currency") service = service with { Money = service.Money with { CurrencyId = Guid.NewGuid() } };
        var ids = service.Lines.Select(x => x.LineId).ToArray();
        if (fault == "foreign-line") ids[0] = Guid.NewGuid();
        if (fault == "missing-line") ids = ids.Take(1).ToArray();
        if (fault == "duplicate-line") ids[1] = ids[0];
        var selection = new PurchaseOrderServiceSelection(fault == "foreign-id" ? Guid.NewGuid() : service.OrderId,
            fault == "version" ? 2 : 1, ids);
        Assert.Throws<ArgumentException>(() => PurchaseMixedOrderBindingV1.Bind(new(f.Parent, [service], [selection]), 2, Now));
        Assert.Empty(f.Parent.Order.ServiceMemberships); Assert.Equal(2, f.Parent.Order.Version);
    }

    [Fact]
    public void A_later_invalid_child_leaves_no_partial_membership_or_parent_version_change()
    {
        var f = Fixture(); var second = f.Service with { OrderId = Guid.NewGuid(), CompanyAccountId = Guid.NewGuid() };
        Assert.Throws<ArgumentException>(() => PurchaseMixedOrderBindingV1.Bind(new(f.Parent, [f.Service, second],
            [Selection(f.Service), Selection(second)]), 2, Now));
        Assert.Empty(f.Parent.Order.ServiceMemberships); Assert.Equal(2, f.Parent.Order.Version);
    }

    [Fact]
    public void Mixed_transition_reuses_both_native_approvals_and_keeps_service_number_and_separate_partial_acceptance()
    {
        var f = Fixture(); Bind(f);
        var child = Assert.Single(Approve(f));
        Assert.Equal((PurchaseOrderStatus.Approved, 4L, Checker), (f.Parent.Order.Status, f.Parent.Order.Version, f.Parent.Order.Approval!.ApprovedBy));
        Assert.Equal((PurchaseServiceOrderStatus.Approved, 2L, Checker, f.Service.DocumentNumber),
            (child.Status, child.Version, child.ApprovedBy, child.DocumentNumber));
        Assert.Empty(f.Parent.Order.Acceptances); Assert.Empty(child.Acceptances); Assert.Empty(f.Service.Acceptances);
        var accepted = PurchaseServiceOrder.Accept(child, 2, Guid.NewGuid(), child.Lines[0].LineId, 1m,
            child.BusinessDate, "SENTETIK-receiver", "SENTETIK partial", Now);
        Assert.Single(accepted.Acceptances); Assert.Equal(1m, accepted.Lines[0].AcceptedQuantity);
        Assert.Empty(f.Parent.Order.Acceptances); Assert.Empty(child.Acceptances);
    }

    [Theory]
    [InlineData(ParentMaker)] [InlineData(ServiceMaker)]
    public void Either_native_maker_refusal_preserves_both_unapproved_records(string actor)
    {
        var f = Fixture(); Bind(f);
        if (actor == ServiceMaker)
            Assert.Equal(PurchaseServiceOrder.SameActor, Assert.Throws<PurchaseServiceOrderException>(() => Approve(f, actor)).Code);
        else Assert.Equal(PurchasingRuleCodes.OrderSameActor, Assert.Throws<PurchasingRuleException>(() => Approve(f, actor)).Code);
        Assert.Equal((PurchaseOrderStatus.Draft, 3L), (f.Parent.Order.Status, f.Parent.Order.Version));
        Assert.Null(f.Parent.Order.Approval); Assert.Equal(PurchaseServiceOrderStatus.Draft, f.Service.Status); Assert.Null(f.Service.ApprovedBy);
    }

    [Theory]
    [InlineData("parent")] [InlineData("child")]
    public void Both_native_expected_versions_are_preserved(string stale)
    {
        var f = Fixture(); Bind(f);
        if (stale == "parent") Assert.Throws<PurchasingVersionConflictException>(() => f.Parent.Order.ApproveWithServiceOrders(
            2, f.Parent.CompanyAccountId, [f.Service], Checker, Now, f.Service.Money.CurrencyId, new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>()));
        else Assert.Equal(PurchaseServiceOrder.VersionConflict, Assert.Throws<PurchaseServiceOrderException>(() =>
            f.Parent.Order.ApproveWithServiceOrders(3, f.Parent.CompanyAccountId, [f.Service with { Version = 2 }], Checker, Now,
                f.Service.Money.CurrencyId, new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>())).Code);
        Assert.Equal(PurchaseOrderStatus.Draft, f.Parent.Order.Status); Assert.Null(f.Parent.Order.Approval);
    }

    [Fact]
    public void Linked_order_cannot_use_single_owner_approval_or_silently_rebind_supplier_after_approval()
    {
        var f = Fixture(); Bind(f);
        Assert.Equal(PurchaseOrder.ServiceMembershipUnavailable, Assert.Throws<PurchasingRuleException>(() => f.Parent.Order.Approve(3, Checker, Now)).Code);
        Approve(f);
        Assert.Equal(PurchaseOrder.ServiceMembershipUnavailable, Assert.Throws<PurchasingRuleException>(() => f.Parent.Order.ReviseSupplier(
            4, Guid.NewGuid(), PurchaseOrderInvoiceHistory.None, ParentMaker, "SENTETIK correction", Now)).Code);
        Assert.Throws<PurchasingRuleException>(() => f.Parent.Order.BindServiceOrders(4, f.Parent.CompanyAccountId,
            [f.Service], [Selection(f.Service)], Now));
        Assert.Equal(f.Service.SupplierPartyId, f.Parent.Order.SupplierPartyId); Assert.Single(f.Parent.Order.ServiceMemberships);
        Assert.Empty(f.Parent.Order.SupplierRevisions);
    }

    [Theory]
    [InlineData("account")] [InlineData("line")]
    public void Approval_cannot_replace_the_account_or_full_child_line_membership_seen_at_binding(string changed)
    {
        var f = Fixture(); Bind(f); var child = f.Service;
        if (changed == "account") child = child with { CompanyAccountId = Guid.NewGuid() };
        else child = child with { Lines = [child.Lines[0], child.Lines[1] with { LineId = Guid.NewGuid() }] };
        Assert.Throws<ArgumentException>(() => f.Parent.Order.ApproveWithServiceOrders(3, f.Parent.CompanyAccountId,
            [child], Checker, Now, f.Service.Money.CurrencyId, new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>()));
        Assert.Equal(PurchaseOrderStatus.Draft, f.Parent.Order.Status); Assert.Null(f.Parent.Order.Approval);
        Assert.Empty(f.Parent.Order.Acceptances);
    }

    [Fact]
    public void Legacy_create_body_and_hash_have_no_membership_admission_and_unlinked_approval_remains_native()
    {
        var f = Fixture();
        var request = new CreatePurchaseOrderRequest(f.Service.SupplierPartyId, f.Service.CompanyAccountId, null,
            f.Service.BusinessDate, []);
        var legacy = new { request.SupplierPartyId, request.CompanyAccountId, request.SourceRequestId, request.EffectiveOn, request.Lines };
        Assert.Equal(JsonSerializer.Serialize(legacy, PurchasingJson.Options), JsonSerializer.Serialize(request, PurchasingJson.Options));
        Assert.Equal(PurchasingRequestHash.Compute(PurchasingCommands.OrderCreateV1, legacy),
            PurchasingRequestHash.Compute(PurchasingCommands.OrderCreateV1, request));
        f.Parent.Order.Approve(2, Checker, Now);
        Assert.Equal((PurchaseOrderStatus.Approved, 3L), (f.Parent.Order.Status, f.Parent.Order.Version));
        Assert.Empty(f.Parent.Order.ServiceMemberships);
    }

    [Fact]
    public async Task Missing_owned_participant_never_calls_fence_or_reports_a_durable_success()
    {
        var f = Fixture(); var fences = 0;
        IPurchaseMixedOrderApprovalParticipantV1 participant = new UnconfiguredParticipant();
        var refusal = await Assert.ThrowsAsync<PurchasingServiceException>(() => participant.ApproveWithinOwnerAsync(
            new(f.Parent, [f.Service], [Selection(f.Service)]), 2,
            new(Checker, PurchasingPermissionManifestV1.OrdersApprove, [], "SENTETIK-corr", null, "SENTETIK-key", "Web"), Now,
            _ => { fences++; return Task.CompletedTask; }));
        Assert.Equal(503, refusal.Status); Assert.Equal(0, fences);
        Assert.Equal(PurchaseOrderStatus.Draft, f.Parent.Order.Status); Assert.Equal(PurchaseServiceOrderStatus.Draft, f.Service.Status);
    }

    [Fact]
    public void Fresh_complete_child_selection_does_not_rewrite_the_original_binding_baseline()
    {
        var f = Fixture(); Bind(f); var baseline = f.Parent.Order.ServiceMemberships[0];
        var changed = f.Service with { Version = 2, Lines = [f.Service.Lines[0], f.Service.Lines[1] with { LineId = Guid.NewGuid() }] };
        Assert.Equal(PurchaseServiceOrder.VersionConflict, Assert.Throws<PurchaseServiceOrderException>(() => Approve(f with { Service = f.Service with { Version = 2 } })).Code);
        Assert.Equal(PurchaseOrderStatus.Draft, f.Parent.Order.Status);
        var approved = f.Parent.Order.ApproveSelectedServiceOrders(3, f.Parent.CompanyAccountId, [changed], [Selection(changed)],
            Checker, Now, changed.Money.CurrencyId, new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>());
        Assert.Equal((4L, PurchaseOrderStatus.Approved), (f.Parent.Order.Version, f.Parent.Order.Status));
        Assert.Equal((3L, PurchaseServiceOrderStatus.Approved), (approved[0].Version, approved[0].Status));
        Assert.Same(baseline, Assert.Single(f.Parent.Order.ServiceMemberships));
        Assert.Equal(1L, baseline.PreparedVersion); Assert.Equal(f.Service.Lines.Select(x => x.LineId), baseline.LineIds);
    }

    [Theory]
    [InlineData("version")] [InlineData("line")] [InlineData("duplicate")]
    public void Fresh_selection_rejects_stale_or_partial_child_intent_before_mutating_parent(string changed)
    {
        var f = Fixture(); Bind(f); var selection = Selection(f.Service);
        if (changed == "version") selection = selection with { ExpectedVersion = 2 };
        if (changed == "line") selection = selection with { LineIds = [selection.LineIds[0]] };
        var failure = Record.Exception(() => f.Parent.Order.ApproveSelectedServiceOrders(3, f.Parent.CompanyAccountId, [f.Service],
            changed == "duplicate" ? [selection, selection] : [selection], Checker, Now, f.Service.Money.CurrencyId,
            new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>()));
        Assert.True(failure is ArgumentException or PurchaseServiceOrderException, failure?.ToString());
        Assert.Equal((3L, PurchaseOrderStatus.Draft), (f.Parent.Order.Version, f.Parent.Order.Status)); Assert.Null(f.Parent.Order.Approval);
    }

    private sealed class UnconfiguredParticipant : IPurchaseMixedOrderApprovalParticipantV1 { }
    private static PurchaseOrderServiceSelection Selection(PurchaseServiceOrderState service) => new(service.OrderId, service.Version, service.Lines.Select(x => x.LineId).ToArray());
    private static void Bind(MixedFixture f, IReadOnlyList<PurchaseOrderServiceSelection>? selections = null) =>
        PurchaseMixedOrderBindingV1.Bind(new(f.Parent, [f.Service], selections ?? [Selection(f.Service)]), 2, Now);
    private static IReadOnlyList<PurchaseServiceOrderState> Approve(MixedFixture f, string actor = Checker) =>
        f.Parent.Order.ApproveWithServiceOrders(3, f.Parent.CompanyAccountId, [f.Service], actor, Now, f.Service.Money.CurrencyId,
            new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>());
    private sealed record MixedFixture(StoredPurchaseOrder Parent, PurchaseServiceOrderState Service);
    private static MixedFixture Fixture()
    {
        var company = Guid.NewGuid(); var supplier = Guid.NewGuid(); var account = Guid.NewGuid(); var currency = Guid.NewGuid();
        var money = PurchaseOrderMoney.Of(currency, "TRY", Guid.NewGuid(), 1, 2, 4);
        var parent = PurchaseOrder.Create(Guid.NewGuid(), company, supplier, money, Now, ParentMaker);
        parent.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Quantity.Of(4m, QuantityUnit.Kilogram), Now);
        PurchaseServiceLine Line() => new(Guid.NewGuid(), Guid.NewGuid(), 1, "SENTETIK service", Guid.NewGuid(), "kg", Guid.NewGuid(), 1, 3, 4m, 2m, 0m);
        var service = PurchaseServiceOrder.Create(new(company, Guid.NewGuid(), 1, PurchaseServiceOrderKind.ServicePurchase,
            PurchaseServiceOrderStatus.Draft, supplier, account, "1", "1", new(2026, 11, 28), ServiceMaker, null, Now, Now,
            new(currency, "TRY", money.ProfileId, 1, 2, 4), [Line(), Line()], [], [], [], "SENTETIK-SERVICE-1"));
        return new(new(parent, account, service.BusinessDate, new Dictionary<Guid, string>()), service);
    }
}
