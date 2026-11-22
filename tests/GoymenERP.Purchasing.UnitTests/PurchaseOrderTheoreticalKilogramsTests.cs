using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// AK-3 (SAT-D437, ALS-D121) in the domain: a piece general line records its theoretical kg per piece exactly once at the first
/// approval, the approval refuses a piece line without one before anything moves, and the shared piece/kg arithmetic reproduces the
/// golden rows (<c>ADET_KG_FORMUL_GOLDEN_V1</c> R1/K01, K10, A09, A13). SENTETIK.
/// </summary>
public sealed class PurchaseOrderTheoreticalKilogramsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Supplier = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid PieceUnit = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Lira = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private const string Preparer = "SENTETIK-hazirlayan";
    private const string Approver = "SENTETIK-onaylayan";

    private static (PurchaseOrder Order, Guid PieceLine, Guid KgLine) Draft()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0, Preparer);
        var piece = Guid.NewGuid();
        var kg = Guid.NewGuid();
        order.AddLine(1, piece, Guid.NewGuid(), PieceUnit, Quantity.Of(100m, QuantityUnit.Piece), T0);
        order.AddLine(2, kg, Guid.NewGuid(), KgUnit, Quantity.Of(500m, QuantityUnit.Kilogram), T0);
        return (order, piece, kg);
    }

    private static PurchaseOrderLineTheoreticalKilograms Snapshot(decimal k) => new(k, "product.kg-per-unit.v1/sentetik");

    [Fact]
    public void Approval_records_the_snapshot_on_the_piece_line_only()
    {
        var (order, piece, kg) = Draft();

        order.Approve(3, Approver, T0, Lira, new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms> { [piece] = Snapshot(12.345m) });

        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
        Assert.Equal(12.345m, order.Lines.Single(line => line.Id == piece).TheoreticalKilograms!.KilogramsPerUnit);
        Assert.Null(order.Lines.Single(line => line.Id == kg).TheoreticalKilograms);
    }

    [Fact]
    public void A_piece_line_without_a_snapshot_refuses_the_approval_and_nothing_moves()
    {
        var (order, _, _) = Draft();

        var code = Assert.Throws<PurchasingRuleException>(() =>
            order.Approve(3, Approver, T0, Lira, new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>())).Code;

        Assert.Equal(PurchasingRuleCodes.TheoreticalKilogramsMissing, code);
        Assert.Equal((PurchaseOrderStatus.Draft, 3L, (PurchaseOrderApproval?)null), (order.Status, order.Version, order.Approval));
    }

    [Fact]
    public void A_snapshot_for_a_kilogram_line_is_an_internal_inconsistency()
    {
        var (order, piece, kg) = Draft();

        var code = Assert.Throws<PurchasingRuleException>(() => order.Approve(3, Approver, T0, Lira,
            new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms> { [piece] = Snapshot(1m), [kg] = Snapshot(1m) })).Code;

        Assert.Equal(PurchasingRuleCodes.InternalInvariant, code);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
    }

    [Fact]
    public void A_stale_version_is_a_version_conflict_before_any_kilogram_rule()
    {
        var (order, _, _) = Draft();

        Assert.Throws<PurchasingVersionConflictException>(() =>
            order.Approve(2, Approver, T0, Lira, new Dictionary<Guid, PurchaseOrderLineTheoreticalKilograms>()));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.0000001")]
    public void A_snapshot_figure_that_is_not_positive_or_too_fine_is_refused_never_rounded(string figure)
    {
        var code = Assert.Throws<PurchasingRuleException>(() =>
            Snapshot(decimal.Parse(figure, System.Globalization.CultureInfo.InvariantCulture))).Code;
        Assert.Equal(PurchasingRuleCodes.TheoreticalKilogramsInvalid, code);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" padded")]
    public void A_snapshot_without_a_canonical_source_version_is_refused(string source)
    {
        Assert.Equal(PurchasingRuleCodes.TheoreticalKilogramsInvalid,
            Assert.Throws<PurchasingRuleException>(() => new PurchaseOrderLineTheoreticalKilograms(1m, source)).Code);
    }

    [Fact]
    public void Theoretical_kilograms_are_pieces_times_the_recorded_figure_written_with_three_decimals()
    {
        // Golden R1/K01: 100 × 12,345 = 1 234,500; K10: 200 × 2,500 = 500,000; TK2 half away from zero on a finer figure.
        Assert.Equal(1234.500m, PurchasePieceKilograms.TheoreticalKilograms(100m, 12.345m));
        Assert.Equal(500.000m, PurchasePieceKilograms.TheoreticalKilograms(200m, 2.5m));
        Assert.Equal(0.002m, PurchasePieceKilograms.TheoreticalKilograms(1m, 0.0015m));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchasePieceKilograms.TheoreticalKilograms(1.5m, 1m));
    }

    [Fact]
    public void A_partial_invoice_takes_the_weighed_kilograms_share_of_its_invoiced_pieces()
    {
        // Golden A09/A13 (ALS-D121): 1 250,400 kg × 40 ÷ 100 = 500,160 kg; the remaining 60 pieces carry 750,240 kg.
        Assert.Equal(500.160m, PurchasePieceKilograms.InvoicedKilogramShare(1250.4m, 40m, 100m));
        Assert.Equal(750.240m, PurchasePieceKilograms.InvoicedKilogramShare(1250.4m, 60m, 100m));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchasePieceKilograms.InvoicedKilogramShare(1250.4m, 101m, 100m));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchasePieceKilograms.InvoicedKilogramShare(0m, 1m, 100m));
    }
}
