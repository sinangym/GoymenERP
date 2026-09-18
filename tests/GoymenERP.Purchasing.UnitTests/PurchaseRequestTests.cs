using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Requests;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseRequestTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 18, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Supplier = Guid.Parse("22222222-2222-4222-8222-222222222222");

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    [Fact]
    public void A_request_starts_as_a_draft_in_its_company_at_version_1()
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, T0);
        Assert.Equal(PurchaseRequestStatus.Draft, request.Status);
        Assert.Equal(Company, request.CompanyId);
        Assert.Equal(1, request.Version);
        Assert.Equal(TimeSpan.Zero, request.CreatedAtUtc.Offset);
        Assert.Empty(request.Lines);
    }

    [Fact]
    public void Missing_identities_are_refused()
    {
        Assert.Throws<ArgumentException>(() => PurchaseRequest.Create(Guid.Empty, Company, T0));
        Assert.Throws<ArgumentException>(() => PurchaseRequest.Create(Guid.NewGuid(), Guid.Empty, T0));
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, T0);
        Assert.Throws<ArgumentException>(() => request.AddLine(1, Guid.Empty, Guid.NewGuid(), Kg(1), T0));
        Assert.Throws<ArgumentException>(() => request.AddLine(1, Guid.NewGuid(), Guid.Empty, Kg(1), T0));
    }

    [Fact]
    public void Lines_need_a_positive_quantity_and_a_new_id_and_bump_the_version()
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, T0);
        var lineId = Guid.NewGuid();
        Assert.Throws<ArgumentOutOfRangeException>(() => request.AddLine(1, lineId, Guid.NewGuid(), Kg(0), T0));
        request.AddLine(1, lineId, Guid.NewGuid(), Kg(1000), T0.AddMinutes(1));
        Assert.Equal(2, request.Version);
        Assert.Throws<InvalidOperationException>(() => request.AddLine(2, lineId, Guid.NewGuid(), Kg(1), T0));
    }

    [Fact]
    public void A_stale_expected_version_is_a_conflict()
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, T0);
        var conflict = Assert.Throws<PurchasingVersionConflictException>(() =>
            request.AddLine(7, Guid.NewGuid(), Guid.NewGuid(), Kg(1), T0));
        Assert.Equal((7L, 1L), (conflict.ExpectedVersion, conflict.CurrentVersion));
    }

    [Fact]
    public void Submitting_needs_a_line_and_freezes_the_request()
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, T0);
        Assert.Throws<InvalidOperationException>(() => request.Submit(1, T0));
        request.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), Kg(1000), T0);
        request.Submit(2, T0.AddMinutes(1));
        Assert.Equal(PurchaseRequestStatus.Submitted, request.Status);
        Assert.Throws<InvalidOperationException>(() => request.AddLine(3, Guid.NewGuid(), Guid.NewGuid(), Kg(1), T0));
        Assert.Throws<InvalidOperationException>(() => request.Submit(3, T0));
    }

    [Fact]
    public void An_order_is_sourced_only_from_a_submitted_request_and_stays_in_its_company()
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, T0);
        var productId = Guid.NewGuid();
        var requestLine = request.AddLine(1, Guid.NewGuid(), productId, Kg(1000), T0);
        Assert.Throws<InvalidOperationException>(() =>
            PurchaseOrder.FromRequest(Guid.NewGuid(), request, Supplier, Guid.NewGuid, T0));

        request.Submit(2, T0);
        var order = PurchaseOrder.FromRequest(Guid.NewGuid(), request, Supplier, Guid.NewGuid, T0);

        Assert.Equal(Company, order.CompanyId);
        Assert.Equal(request.Id, order.SourceRequestId);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        var line = Assert.Single(order.Lines);
        Assert.Equal((productId, Kg(1000), PurchaseOrderLineKind.General, (Guid?)requestLine.Id),
            (line.ProductId, line.Quantity, line.Kind, line.SourceRequestLineId));
    }
}
