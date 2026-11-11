using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Requests;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A2a-5a (ALS-D3, Fable F-015): the order's preparer never approves it (hazırlayan ≠ onaylayan, ordinal); an order whose preparer is
/// unknown is never approved (fail-closed). The approval threshold (API-96) is A2a-5's, not tested here.
/// </summary>
public sealed class PurchaseOrderApprovalTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 11, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Supplier = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private const string Preparer = "SENTETIK-hazirlayan";
    private const string Approver = "SENTETIK-onaylayan";

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static string CodeOf(Action action) => Assert.Throws<PurchasingRuleException>(action).Code;

    private static PurchaseOrder DraftWithLine(string? preparedBy)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0, preparedBy);
        order.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(1_000m), T0);
        return order;
    }

    [Fact]
    public void A_different_actor_approves_and_the_approval_records_who_and_when()
    {
        var order = DraftWithLine(Preparer);

        order.Approve(2, Approver, T0.AddHours(1));

        Assert.Equal((PurchaseOrderStatus.Approved, 3L), (order.Status, order.Version));
        Assert.Equal(Preparer, order.PreparedBy);
        Assert.Equal(new PurchaseOrderApproval(Approver, T0.AddHours(1).ToUniversalTime()), order.Approval);
        Assert.Equal(TimeSpan.Zero, order.Approval!.ApprovedAtUtc.Offset);
    }

    [Fact]
    public void The_preparer_cannot_approve_the_same_order_and_nothing_moves()
    {
        var order = DraftWithLine(Preparer);

        Assert.Equal(PurchasingRuleCodes.OrderSameActor, CodeOf(() => order.Approve(2, Preparer, T0)));

        Assert.Equal((PurchaseOrderStatus.Draft, 2L), (order.Status, order.Version));
        Assert.Null(order.Approval);
    }

    [Fact]
    public void The_comparison_is_ordinal_so_a_case_variant_is_another_actor()
    {
        // The database compares binary (Latin1_General_100_BIN2); the domain must agree, or a case variant passes one and fails the other.
        var order = DraftWithLine(Preparer);

        order.Approve(2, Preparer.ToUpperInvariant(), T0);

        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
    }

    [Fact]
    public void An_order_with_an_unknown_preparer_is_never_approved()
    {
        var order = DraftWithLine(preparedBy: null);

        Assert.Equal(PurchasingRuleCodes.OrderMakerUnknown, CodeOf(() => order.Approve(2, Approver, T0)));
        Assert.Equal((PurchaseOrderStatus.Draft, 2L), (order.Status, order.Version));
        Assert.Null(order.Approval);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" SENTETIK-onaylayan")]
    [InlineData("SENTETIK-onaylayan ")]
    public void A_blank_or_padded_approver_or_preparer_is_refused_as_an_argument(string actor)
    {
        var order = DraftWithLine(Preparer);
        Assert.Throws<ArgumentException>(() => order.Approve(2, actor, T0));
        Assert.Throws<ArgumentException>(() => PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0, actor));
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
    }

    [Fact]
    public void An_overlong_actor_is_refused_and_not_cut()
    {
        var order = DraftWithLine(Preparer);
        var tooLong = new string('a', PurchaseOrder.MaximumActorLength + 1);

        Assert.Throws<ArgumentException>(() => order.Approve(2, tooLong, T0));
        Assert.Throws<ArgumentException>(() => PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0, tooLong));
    }

    [Fact]
    public void The_version_and_state_rules_still_come_first()
    {
        var order = DraftWithLine(Preparer);
        Assert.Throws<PurchasingVersionConflictException>(() => order.Approve(1, Preparer, T0));
        order.Approve(2, Approver, T0);
        Assert.Equal(PurchasingRuleCodes.OrderNotDraft, CodeOf(() => order.Approve(3, Approver, T0)));
        Assert.Equal(Approver, order.Approval!.ApprovedBy);
    }

    [Fact]
    public void Every_factory_carries_the_preparer()
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, "SENTETIK-talep", T0);
        Assert.Equal(Preparer, PurchaseOrder.CreateForRequest(Guid.NewGuid(), Company, Supplier, request.Id, T0, Preparer).PreparedBy);
        Assert.Equal(Preparer, PurchaseOrder.CreateForRequest(Guid.NewGuid(), Company, Supplier, request.Id, null, T0, Preparer).PreparedBy);
        Assert.Equal(Preparer, PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, null, T0, Preparer).PreparedBy);
        Assert.Null(PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0).PreparedBy);
    }

    [Fact]
    public void The_store_stamps_the_create_actor_as_preparer_and_refuses_another()
    {
        var unstamped = DraftWithLine(preparedBy: null);
        unstamped.StampPreparer(Preparer);
        Assert.Equal(Preparer, unstamped.PreparedBy);
        unstamped.StampPreparer(Preparer);

        var prepared = DraftWithLine(Preparer);
        Assert.Throws<ArgumentException>(() => prepared.StampPreparer(Approver));
        Assert.Throws<ArgumentException>(() => DraftWithLine(null).StampPreparer(" "));
        Assert.Equal(Preparer, prepared.PreparedBy);
    }

    [Fact]
    public void A_legacy_approval_is_rebuilt_without_an_approver_and_only_from_a_draft_with_lines()
    {
        // An order stored as approved before A2a-5a has no approver; the rebuild neither invents one nor applies the rule backwards.
        var order = DraftWithLine(Preparer);
        order.RestoreLegacyApproval(2, T0);
        Assert.Equal((PurchaseOrderStatus.Approved, 3L), (order.Status, order.Version));
        Assert.Null(order.Approval);
        Assert.Equal(PurchasingRuleCodes.OrderNotDraft, CodeOf(() => order.RestoreLegacyApproval(3, T0)));

        var empty = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0, Preparer);
        Assert.Equal(PurchasingRuleCodes.OrderWithoutLines, CodeOf(() => empty.RestoreLegacyApproval(1, T0)));
    }
}
