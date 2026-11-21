using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseOrderSupplierRevisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Unit = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private const string Maker = "SENTETIK-maker";
    private const string Checker = "SENTETIK-checker";

    [Fact]
    public void Supplier_revision_keeps_approved_prices_in_history_and_requires_explicit_new_prices_and_another_approver()
    {
        var order = Order(); var oldSupplier = order.SupplierPartyId; var oldPrice = order.Lines[0].Price;
        var oldVersion = order.Version; var oldApproval = order.Approval; var target = Guid.NewGuid();
        var quantity = order.Lines[0].Quantity;
        order.ReviseSupplier(oldVersion, target, PurchaseOrderInvoiceHistory.None, Maker, "SENTETIK supplier correction", Now.AddMinutes(1));
        Assert.Equal((PurchaseOrderStatus.Draft, oldVersion + 1, target), (order.Status, order.Version, order.SupplierPartyId));
        Assert.Null(order.Approval); Assert.Null(order.Lines[0].Price);
        Assert.Equal(quantity, order.Lines[0].Quantity);
        var history = Assert.Single(order.SupplierRevisions);
        Assert.Equal(oldSupplier, history.PreviousSupplierPartyId);
        Assert.Equal(oldApproval, history.PreviousApproval);
        Assert.Equal(oldPrice, Assert.Single(history.PreviousLines).Price);
        Assert.Equal(PurchaseOrder.SupplierRevisionPriceRequired, Refusal(() => order.Approve(order.Version, Checker, Now)));
        var newPrice = new PurchaseOrderLinePrice(14.25m, Unit, DiscountPercent: 10m);
        order.SetSupplierRevisionPrice(order.Version, order.Lines[0].Id, newPrice, Maker, Now.AddMinutes(2));
        Assert.Equal(PurchasingRuleCodes.OrderSameActor, Refusal(() => order.Approve(order.Version, Maker, Now)));
        order.Approve(order.Version, Checker, Now.AddMinutes(3));
        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
        Assert.Equal(newPrice, order.Lines[0].Price);
        Assert.Equal(oldPrice, Assert.Single(history.PreviousLines).Price);
        Assert.Null(Assert.Single(order.SupplierRevisionPrices).PreviousPrice);
        Assert.Empty(order.Acceptances);
    }

    [Theory]
    [InlineData(PurchaseOrderInvoiceHistory.Unavailable, PurchaseOrder.SupplierRevisionInvoiceUnavailable)]
    [InlineData(PurchaseOrderInvoiceHistory.Exists, PurchaseOrder.SupplierRevisionBlocked)]
    [InlineData((PurchaseOrderInvoiceHistory)9, PurchaseOrder.SupplierRevisionInvoiceUnavailable)]
    public void Invoice_history_is_required_and_an_invoice_blocks_supplier_change_without_mutation(PurchaseOrderInvoiceHistory evidence, string code)
    {
        var order = Order(); var before = (order.Version, order.SupplierPartyId, order.Approval, order.Lines[0].Price);
        Assert.Equal(code, Refusal(() => order.ReviseSupplier(order.Version, Guid.NewGuid(), evidence, Maker, "SENTETIK", Now)));
        Assert.Equal(before, (order.Version, order.SupplierPartyId, order.Approval, order.Lines[0].Price));
        Assert.Empty(order.SupplierRevisions);
    }

    [Fact]
    public void Stale_version_unchanged_supplier_or_missing_reason_never_discard_approval()
    {
        var order = Order(); var before = (order.Version, order.Approval, order.Lines[0].Price);
        Assert.Throws<PurchasingVersionConflictException>(() => order.ReviseSupplier(order.Version - 1, Guid.NewGuid(), PurchaseOrderInvoiceHistory.None, Maker, "SENTETIK", Now));
        Assert.Equal(PurchaseOrder.SupplierRevisionNoChange, Refusal(() => order.ReviseSupplier(order.Version, order.SupplierPartyId, PurchaseOrderInvoiceHistory.None, Maker, "SENTETIK", Now)));
        Assert.Throws<ArgumentException>(() => order.ReviseSupplier(order.Version, Guid.NewGuid(), PurchaseOrderInvoiceHistory.None, Maker, " ", Now));
        Assert.Equal(before, (order.Version, order.Approval, order.Lines[0].Price));
    }

    [Fact]
    public void The_supplier_revision_preparer_cannot_approve_even_when_they_approved_the_original_order()
    {
        var order = Order();
        order.ReviseSupplier(order.Version, Guid.NewGuid(), PurchaseOrderInvoiceHistory.None, Checker, "SENTETIK", Now);
        order.SetSupplierRevisionPrice(order.Version, order.Lines[0].Id, new(19m, Unit), Checker, Now);
        Assert.Equal(Checker, order.PreparedBy);
        Assert.Equal(Maker, Assert.Single(order.SupplierRevisions).PreviousPreparer);
        Assert.Equal(PurchasingRuleCodes.OrderSameActor, Refusal(() => order.Approve(order.Version, Checker, Now)));
        order.Approve(order.Version, Maker, Now);
        Assert.Equal(Maker, order.Approval!.ApprovedBy);
    }

    [Fact]
    public void Repeated_supplier_revisions_preserve_each_approval_and_each_supplier_price()
    {
        var order = Order(); var firstPrice = order.Lines[0].Price;
        order.ReviseSupplier(order.Version, Guid.NewGuid(), PurchaseOrderInvoiceHistory.None, Maker, "SENTETIK first", Now);
        var secondPrice = new PurchaseOrderLinePrice(22m, Unit);
        order.SetSupplierRevisionPrice(order.Version, order.Lines[0].Id, secondPrice, Maker, Now);
        order.Approve(order.Version, Checker, Now);
        var previousSupplier = order.SupplierPartyId;
        order.ReviseSupplier(order.Version, Guid.NewGuid(), PurchaseOrderInvoiceHistory.None, Maker, "SENTETIK second", Now);
        Assert.Equal(2, order.SupplierRevision);
        Assert.Equal(firstPrice, Assert.Single(order.SupplierRevisions[0].PreviousLines).Price);
        Assert.Equal(secondPrice, Assert.Single(order.SupplierRevisions[1].PreviousLines).Price);
        Assert.Equal(previousSupplier, order.SupplierRevisions[1].PreviousSupplierPartyId);
        Assert.Null(order.Lines[0].Price);
        Assert.Equal(PurchaseOrder.SupplierRevisionPriceRequired, Refusal(() => order.Approve(order.Version, Checker, Now)));
    }

    [Fact]
    public void A_receipt_blocks_supplier_revision_even_when_invoice_history_is_empty()
    {
        var order = Order();
        order.RecordAcceptance(order.Version, Guid.NewGuid(), order.Lines[0].Id, Quantity.Of(2m, QuantityUnit.Kilogram), Now);
        var before = (order.Version, order.SupplierPartyId, order.Lines[0].AcceptedQuantity, order.Lines[0].Price);
        Assert.Equal(PurchaseOrder.SupplierRevisionBlocked, Refusal(() => order.ReviseSupplier(order.Version,
            Guid.NewGuid(), PurchaseOrderInvoiceHistory.None, Maker, "SENTETIK", Now)));
        Assert.Equal(before, (order.Version, order.SupplierPartyId, order.Lines[0].AcceptedQuantity, order.Lines[0].Price));
        Assert.Empty(order.SupplierRevisions);
    }

    [Fact]
    public void Ordinary_unpriced_orders_keep_the_existing_approval_rule()
    {
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Guid.NewGuid(), Now, Maker);
        order.AddLine(order.Version, Guid.NewGuid(), Guid.NewGuid(), Unit, Quantity.Of(10m, QuantityUnit.Kilogram), Now);
        order.Approve(order.Version, Checker, Now);
        Assert.Equal(PurchaseOrderStatus.Approved, order.Status);
        Assert.Empty(order.SupplierRevisions);
        var version = order.Version;
        Assert.Equal(PurchaseOrder.SupplierRevisionMoneyUnavailable, Refusal(() => order.ReviseSupplier(version,
            Guid.NewGuid(), PurchaseOrderInvoiceHistory.None, Maker, "SENTETIK", Now)));
        Assert.Equal((version, PurchaseOrderStatus.Approved), (order.Version, order.Status));
    }

    private static string Refusal(Action action) => Assert.Throws<PurchasingRuleException>(action).Code;
    private static PurchaseOrder Order()
    {
        var money = PurchaseOrderMoney.Of(Guid.NewGuid(), "TRY", Guid.NewGuid(), 1, 2, 4);
        var order = PurchaseOrder.Create(Guid.NewGuid(), Company, Guid.NewGuid(), money, Now, Maker);
        order.AddLine(order.Version, Guid.NewGuid(), Guid.NewGuid(), Unit, Quantity.Of(10m, QuantityUnit.Kilogram), null,
            new PurchaseOrderLinePrice(12.5m, Unit), Now);
        order.Approve(order.Version, Checker, Now);
        return order;
    }
}
