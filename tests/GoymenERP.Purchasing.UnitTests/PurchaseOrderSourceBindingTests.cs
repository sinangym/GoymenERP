using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// PUR-4a: an order opened for ONE submitted request binds each general line to at most one line of that
/// request at the share this supplier takes (multi-sourcing, Sinan #66 5738113623 §1). The binding shape is the
/// aggregate's; the sum of the shares across orders is the store's rule at approval (not here).
/// </summary>
public sealed class PurchaseOrderSourceBindingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Supplier = Guid.NewGuid();
    private static readonly Guid Request = Guid.NewGuid();
    private static readonly Guid RequestLine = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();
    private static readonly Guid KgUnit = Guid.NewGuid();

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    [Fact]
    public void A_line_bound_to_a_request_line_keeps_the_binding_and_the_share()
    {
        var order = PurchaseOrder.CreateForRequest(Guid.NewGuid(), Company, Supplier, Request, T0);
        var lineId = Guid.NewGuid();

        var line = order.AddLine(1, lineId, Product, KgUnit, Kg(40m), RequestLine, T0);

        Assert.Equal((Request, RequestLine, PurchaseOrderLineKind.General, 40m, 2L), (order.SourceRequestId, line.SourceRequestLineId, line.Kind, line.Quantity.Value, order.Version));
        // The old shape still adds an unbound general line.
        var free = order.AddLine(2, Guid.NewGuid(), Product, KgUnit, Kg(1m), T0);
        Assert.Null(free.SourceRequestLineId);
    }

    [Fact]
    public void A_binding_needs_a_source_request_and_the_same_request_line_binds_once_per_order()
    {
        var withoutRequest = PurchaseOrder.Create(Guid.NewGuid(), Company, Supplier, T0);
        var refused = Assert.Throws<PurchasingRuleException>(() => withoutRequest.AddLine(1, Guid.NewGuid(), Product, KgUnit, Kg(1m), RequestLine, T0));
        Assert.Equal(PurchasingRuleCodes.SourceLineWithoutRequest, refused.Code);
        Assert.Equal(1L, withoutRequest.Version);

        var order = PurchaseOrder.CreateForRequest(Guid.NewGuid(), Company, Supplier, Request, T0);
        order.AddLine(1, Guid.NewGuid(), Product, KgUnit, Kg(40m), RequestLine, T0);
        var twice = Assert.Throws<PurchasingRuleException>(() => order.AddLine(2, Guid.NewGuid(), Product, KgUnit, Kg(20m), RequestLine, T0));
        Assert.Equal(PurchasingRuleCodes.DuplicateSourceLine, twice.Code);
        Assert.Equal(2L, order.Version);

        Assert.Throws<ArgumentException>(() => PurchaseOrder.CreateForRequest(Guid.NewGuid(), Company, Supplier, Guid.Empty, T0));
        Assert.Throws<ArgumentException>(() => order.AddLine(2, Guid.NewGuid(), Product, KgUnit, Kg(1m), Guid.Empty, T0));
    }

    [Fact]
    public void A_technical_breakdown_of_a_bound_line_keeps_the_binding()
    {
        var order = PurchaseOrder.CreateForRequest(Guid.NewGuid(), Company, Supplier, Request, T0);
        var general = order.AddLine(1, Guid.NewGuid(), Product, KgUnit, Kg(1000m), RequestLine, T0);
        var technical = order.BreakDown(2, general.Id, Guid.NewGuid(), Guid.NewGuid(), Kg(75m), T0);
        Assert.Equal((RequestLine, general.Id), (technical.SourceRequestLineId, technical.ParentLineId));
        Assert.Equal((925m, 75m), (general.CommittedQuantity.Value, technical.Quantity.Value));
    }
}
