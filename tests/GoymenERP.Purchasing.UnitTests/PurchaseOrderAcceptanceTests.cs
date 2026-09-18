using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>#275 §13: 100 ton ordered, 40 accepted → 40 accepted + 60 future supply; never 140. Shares only.</summary>
public sealed class PurchaseOrderAcceptanceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Supplier = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid PieceUnit = Guid.Parse("66666666-6666-4666-8666-666666666666");

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    private static string CodeOf(Action action) => Assert.Throws<PurchasingRuleException>(action).Code;

    private static (PurchaseOrder Order, PurchaseOrderLine Line) Approved(Quantity ordered)
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0);
        var line = order.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, ordered, T0);
        order.Approve(2, T0);
        return (order, line);
    }

    [Fact]
    public void Forty_of_a_hundred_tons_accepted_leaves_sixty_expected_and_never_a_hundred_and_forty()
    {
        var (order, line) = Approved(Kg(100_000m));
        var share = order.RecordAcceptance(3, Guid.NewGuid(), line.Id, Kg(40_000m), T0.AddHours(1));

        Assert.Equal((Kg(40_000m), Kg(60_000m)), (line.AcceptedQuantity, line.RemainingQuantity));
        Assert.Equal(Kg(60_000m), order.RemainingCommitment(QuantityUnit.Kilogram));
        Assert.Equal((line.Id, Kg(40_000m), TimeSpan.Zero), (share.LineId, share.Quantity, share.RecordedAtUtc.Offset));
        Assert.Equal(PurchasingRuleCodes.AcceptanceExceedsRemaining,
            CodeOf(() => order.RecordAcceptance(4, Guid.NewGuid(), line.Id, Kg(100_000m), T0)));
        Assert.Equal(Kg(40_000m), line.AcceptedQuantity);
    }

    [Fact]
    public void Acceptance_can_reach_the_commitment_exactly_but_not_beyond_it()
    {
        var (order, line) = Approved(Kg(100m));
        order.RecordAcceptance(3, Guid.NewGuid(), line.Id, Kg(40m), T0);
        order.RecordAcceptance(4, Guid.NewGuid(), line.Id, Kg(60m), T0);
        Assert.True(line.RemainingQuantity.IsZero);
        Assert.Equal(PurchasingRuleCodes.AcceptanceExceedsRemaining,
            CodeOf(() => order.RecordAcceptance(5, Guid.NewGuid(), line.Id, Kg(0.001m), T0)));
    }

    [Fact]
    public void The_same_acceptance_id_is_not_counted_twice()
    {
        var (order, line) = Approved(Kg(100m));
        var acceptanceId = Guid.NewGuid();
        order.RecordAcceptance(3, acceptanceId, line.Id, Kg(40m), T0);
        Assert.Equal(PurchasingRuleCodes.DuplicateAcceptanceId,
            CodeOf(() => order.RecordAcceptance(4, acceptanceId, line.Id, Kg(40m), T0)));
        Assert.Equal(Kg(40m), line.AcceptedQuantity);
        Assert.Single(order.Acceptances);
    }

    [Fact]
    public void A_draft_order_accepts_nothing_and_approval_needs_a_line()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0);
        Assert.Equal(PurchasingRuleCodes.OrderWithoutLines, CodeOf(() => order.Approve(1, T0)));
        var line = order.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(100m), T0);
        Assert.Equal(PurchasingRuleCodes.OrderNotApproved,
            CodeOf(() => order.RecordAcceptance(2, Guid.NewGuid(), line.Id, Kg(1m), T0)));
        order.Approve(2, T0);
        Assert.Equal(PurchasingRuleCodes.OrderNotDraft, CodeOf(() => order.AddLine(3, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(1m), T0)));
        Assert.Equal(PurchasingRuleCodes.OrderNotDraft, CodeOf(() => order.Approve(3, T0)));
    }

    [Fact]
    public void Acceptance_on_a_technical_line_is_capped_by_its_slice()
    {
        var (order, general) = Approved(Kg(1_000m));
        var technical = order.BreakDown(3, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(75m), T0);
        Assert.Equal(PurchasingRuleCodes.AcceptanceExceedsRemaining,
            CodeOf(() => order.RecordAcceptance(4, Guid.NewGuid(), technical.Id, Kg(76m), T0)));
        order.RecordAcceptance(4, Guid.NewGuid(), technical.Id, Kg(75m), T0);
        Assert.True(technical.RemainingQuantity.IsZero);
        Assert.Equal(Kg(925m), order.RemainingCommitment(QuantityUnit.Kilogram));
    }

    [Fact]
    public void Acceptance_needs_the_line_unit_a_positive_amount_a_known_line_and_the_current_version()
    {
        var (order, line) = Approved(Kg(100m));
        Assert.Equal(PurchasingRuleCodes.UnitMismatch,
            CodeOf(() => order.RecordAcceptance(3, Guid.NewGuid(), line.Id, Quantity.Of(1m, QuantityUnit.Piece), T0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            order.RecordAcceptance(3, Guid.NewGuid(), line.Id, Kg(0m), T0));
        Assert.Equal(PurchasingRuleCodes.LineNotFound,
            CodeOf(() => order.RecordAcceptance(3, Guid.NewGuid(), Guid.NewGuid(), Kg(1m), T0)));
        Assert.Throws<ArgumentException>(() =>
            order.RecordAcceptance(3, Guid.Empty, line.Id, Kg(1m), T0));
        var conflict = Assert.Throws<PurchasingVersionConflictException>(() =>
            order.RecordAcceptance(2, Guid.NewGuid(), line.Id, Kg(1m), T0));
        Assert.Equal((2L, 3L), (conflict.ExpectedVersion, conflict.CurrentVersion));
        Assert.True(line.AcceptedQuantity.IsZero);
        Assert.Empty(order.Acceptances);
    }

    [Fact]
    public void Pieces_are_accepted_whole_and_kept_apart_from_kilograms()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0);
        var pieces = order.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), PieceUnit, Quantity.Of(100m, QuantityUnit.Piece), T0);
        var kilograms = order.AddLine(2, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(50m), T0);
        order.Approve(3, T0);
        order.RecordAcceptance(4, Guid.NewGuid(), pieces.Id, Quantity.Of(30m, QuantityUnit.Piece), T0);
        Assert.Equal(Quantity.Of(70m, QuantityUnit.Piece), order.RemainingCommitment(QuantityUnit.Piece));
        Assert.Equal(Kg(50m), order.RemainingCommitment(QuantityUnit.Kilogram));
        Assert.Equal(Kg(50m), kilograms.RemainingQuantity);
    }

    [Fact]
    public void Missing_identities_are_refused_at_creation()
    {
        Assert.Throws<ArgumentException>(() => PurchaseOrder.Create(Guid.Empty, Company, Supplier, T0));
        Assert.Throws<ArgumentException>(() => PurchaseOrder.Create(Guid.NewGuid(), Guid.Empty, Supplier, T0));
        Assert.Throws<ArgumentException>(() => PurchaseOrder.Create(Guid.NewGuid(), Company, Guid.Empty, T0));
    }

    [Fact]
    public void The_views_cannot_be_used_to_bypass_the_rules()
    {
        // ÜRÜN #278 P3-1: a cast to the backing list would skip version, bound and duplicate checks.
        var (order, line) = Approved(Kg(100m));
        order.RecordAcceptance(3, Guid.NewGuid(), line.Id, Kg(40m), T0);
        Assert.False(order.Lines is List<PurchaseOrderLine>);
        Assert.False(order.Acceptances is List<PurchaseAcceptanceShare>);
        Assert.Throws<NotSupportedException>(() => ((IList<PurchaseOrderLine>)order.Lines).Add(line));
        Assert.Throws<NotSupportedException>(() => ((IList<PurchaseAcceptanceShare>)order.Acceptances).Clear());
        Assert.Single(order.Lines);
        Assert.Single(order.Acceptances);
        Assert.Equal(3, order.Version);
    }
}
