using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Quantity = GoymenERP.BuildingBlocks.Quantities.Quantity<GoymenERP.Modules.Purchasing.Domain.Quantities.PurchasingQuantityRefusals>;

namespace GoymenERP.Purchasing.UnitTests;

public sealed partial class PurchasingServiceTests
{
    // Pure native domain progression only, not persisted history/participant/SQL acceptance evidence.
    [Fact]
    public void Mixed_approved_owners_accept_partial_goods_and_service_independently_without_another_approval_or_membership_change()
    {
        var f = LinkedRevisionFixture(); var membership = f.Parent.ServiceMemberships.ToArray();
        var parentApproval = f.Parent.Approval; var goods = f.Parent.Lines[0]; var childVersion = f.Child.Version;
        f.Parent.RecordAcceptance(4, Guid.NewGuid(), goods.Id, Quantity.Of(1m, goods.Quantity.Unit), f.At);
        Assert.Equal(5L, f.Parent.Version); Assert.Equal(1m, goods.AcceptedQuantity.Value);
        Assert.Equal(childVersion, f.Child.Version); Assert.Empty(f.Child.Acceptances);
        var accepted = PurchaseServiceOrder.Accept(f.Child, childVersion, Guid.NewGuid(), f.Child.Lines[0].LineId,
            1m, DateOnly.FromDateTime(f.At.UtcDateTime), Actor, "SENTETIK partial service", f.At);
        Assert.Equal(childVersion + 1, accepted.Version); Assert.Single(accepted.Acceptances);
        Assert.Equal(PurchaseServiceOrderStatus.Approved, accepted.Status);
        Assert.Equal(5L, f.Parent.Version); Assert.Same(parentApproval, f.Parent.Approval);
        Assert.Equal(membership, f.Parent.ServiceMemberships); Assert.Equal(1L, membership[0].PreparedVersion);
    }

    [Fact]
    public void Mixed_goods_correction_preserves_original_share_child_and_immutable_membership()
    {
        var f = LinkedRevisionFixture(); var goods = f.Parent.Lines[0]; var acceptance = Guid.NewGuid();
        var membership = f.Parent.ServiceMemberships.ToArray(); var child = f.Child;
        f.Parent.RecordAcceptance(4, acceptance, goods.Id, Quantity.Of(1m, goods.Quantity.Unit), f.At);
        f.Parent.AdjustReceipt(5, Guid.NewGuid(), acceptance, Quantity.Of(0.25m, goods.Quantity.Unit),
            false, false, Actor, "SENTETIK actual correction", f.At.AddMinutes(1));
        Assert.Equal(6L, f.Parent.Version); Assert.Equal(0.75m, goods.AcceptedQuantity.Value);
        Assert.Equal(1m, Assert.Single(f.Parent.Acceptances).Quantity.Value);
        Assert.Single(f.Parent.ReceiptAdjustments); Assert.Equal(membership, f.Parent.ServiceMemberships);
        Assert.Same(child, f.Child); Assert.Equal(child.Version, f.Child.Version); Assert.Empty(f.Child.Acceptances);
    }
}
