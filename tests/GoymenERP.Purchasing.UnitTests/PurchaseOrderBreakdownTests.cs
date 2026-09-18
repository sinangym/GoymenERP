using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>#275 §7.2 and §13: 1.000 ton general → 925 general + 75 technical; the same share is never sliced twice.</summary>
public sealed class PurchaseOrderBreakdownTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Supplier = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static (PurchaseOrder Order, PurchaseOrderLine General) ApprovedGeneral(decimal kilograms)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0);
        var general = order.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(kilograms), T0);
        order.Approve(2, T0);
        return (order, general);
    }

    private static string CodeOf(Action action) => Assert.Throws<PurchasingRuleException>(action).Code;

    [Fact]
    public void A_technical_slice_moves_share_out_of_the_general_line_without_changing_the_total()
    {
        var (order, general) = ApprovedGeneral(1_000_000m);
        var technical = order.BreakDown(3, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(75_000m), T0);

        Assert.Equal(Kg(1_000_000m), general.Quantity);
        Assert.Equal(Kg(75_000m), general.TechnicalQuantity);
        Assert.Equal(Kg(925_000m), general.CommittedQuantity);
        Assert.Equal(PurchaseOrderLineKind.Technical, technical.Kind);
        Assert.Equal(general.Id, technical.ParentLineId);
        Assert.Equal(KgUnit, technical.UnitId);
        Assert.Equal(Kg(75_000m), technical.CommittedQuantity);
        Assert.Equal(Kg(1_000_000m), order.RemainingCommitment(QuantityUnit.Kilogram));
    }

    [Fact]
    public void Slices_beyond_the_general_remainder_are_refused_by_the_aggregate_so_the_same_share_is_not_sliced_twice()
    {
        var (order, general) = ApprovedGeneral(1_000m);
        order.BreakDown(3, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(75m), T0);
        order.BreakDown(4, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(925m), T0);
        Assert.True(general.CommittedQuantity.IsZero);
        Assert.Equal(PurchasingRuleCodes.SliceExceedsRemaining,
            CodeOf(() => order.BreakDown(5, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(0.001m), T0)));
        Assert.Equal(Kg(1_000m), order.RemainingCommitment(QuantityUnit.Kilogram));
        Assert.Equal(5, order.Version);
    }

    [Fact]
    public void A_technical_line_cannot_be_broken_down_again()
    {
        var (order, general) = ApprovedGeneral(1_000m);
        var technical = order.BreakDown(3, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(75m), T0);
        Assert.Equal(PurchasingRuleCodes.TechnicalLineNotBreakable,
            CodeOf(() => order.BreakDown(4, technical.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(10m), T0)));
    }

    [Fact]
    public void An_accepted_general_share_cannot_be_sliced()
    {
        var (order, general) = ApprovedGeneral(1_000m);
        order.RecordAcceptance(3, Guid.NewGuid(), general.Id, Kg(100m), T0);
        Assert.Equal(PurchasingRuleCodes.SliceExceedsRemaining,
            CodeOf(() => order.BreakDown(4, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(901m), T0)));
        var technical = order.BreakDown(4, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(900m), T0);
        Assert.True(general.RemainingQuantity.IsZero);
        Assert.Equal(Kg(900m), technical.RemainingQuantity);
    }

    [Fact]
    public void A_breakdown_is_allowed_while_draft_and_after_approval_and_keeps_the_general_unit()
    {
        // §7.2: a general purchase is broken into technical lines when they become known — before
        // or after the commitment. The total never changes; whether a post-approval breakdown
        // needs re-approval is §8 policy and is not chosen here (README).
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0);
        var general = order.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(1_000m), T0);
        var whileDraft = order.BreakDown(2, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(100m), T0);
        Assert.Equal((PurchaseOrderStatus.Draft, KgUnit), (order.Status, whileDraft.UnitId));

        order.Approve(3, T0);
        var afterApproval = order.BreakDown(4, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(200m), T0);
        Assert.Equal((PurchaseOrderStatus.Approved, KgUnit, Kg(700m)), (order.Status, afterApproval.UnitId, general.CommittedQuantity));
        Assert.Equal(Kg(1_000m), order.RemainingCommitment(QuantityUnit.Kilogram));
    }

    [Fact]
    public void A_slice_needs_the_line_unit_a_positive_amount_and_new_identities()
    {
        var (order, general) = ApprovedGeneral(1_000m);
        Assert.Equal(PurchasingRuleCodes.UnitMismatch,
            CodeOf(() => order.BreakDown(3, general.Id, Guid.NewGuid(), Guid.NewGuid(), Quantity.Of(10m, QuantityUnit.Piece), T0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            order.BreakDown(3, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(0m), T0));
        Assert.Equal(PurchasingRuleCodes.DuplicateLineId,
            CodeOf(() => order.BreakDown(3, general.Id, general.Id, Guid.NewGuid(), Kg(1m), T0)));
        Assert.Throws<ArgumentException>(() =>
            order.BreakDown(3, general.Id, Guid.NewGuid(), Guid.Empty, Kg(1m), T0));
        Assert.Equal(PurchasingRuleCodes.LineNotFound,
            CodeOf(() => order.BreakDown(3, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Kg(1m), T0)));
        Assert.Equal(3, order.Version);
    }

    [Fact]
    public void The_line_level_guard_is_a_second_authority_and_reports_itself_as_such()
    {
        // The aggregate decides every bound; the line's own check can only fire on an internal
        // inconsistency, and then it says so with its own code, never with the aggregate's.
        var line = new PurchaseOrderLine(Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(100m), PurchaseOrderLineKind.General, null, null);
        line.AddAcceptedShare(Kg(60m));
        Assert.Equal(PurchasingRuleCodes.InternalInvariant, CodeOf(() => line.AddAcceptedShare(Kg(41m))));
        Assert.Equal(PurchasingRuleCodes.InternalInvariant, CodeOf(() => line.AddTechnicalSlice(Kg(41m))));
        Assert.Equal(Kg(60m), line.AcceptedQuantity);
        Assert.True(line.TechnicalQuantity.IsZero);
    }
}
