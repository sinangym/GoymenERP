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
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");

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
        Assert.Throws<ArgumentException>(() => request.AddLine(1, Guid.Empty, Guid.NewGuid(), KgUnit, Kg(1), T0));
        Assert.Throws<ArgumentException>(() => request.AddLine(1, Guid.NewGuid(), Guid.Empty, KgUnit, Kg(1), T0));
        Assert.Throws<ArgumentException>(() => request.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Kg(1), T0));
    }

    [Fact]
    public void Lines_need_a_positive_quantity_and_a_new_id_and_bump_the_version()
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, T0);
        var lineId = Guid.NewGuid();
        Assert.Throws<ArgumentOutOfRangeException>(() => request.AddLine(1, lineId, Guid.NewGuid(), KgUnit, Kg(0), T0));
        var line = request.AddLine(1, lineId, Guid.NewGuid(), KgUnit, Kg(1000), T0.AddMinutes(1));
        Assert.Equal(2, request.Version);
        Assert.Equal(KgUnit, line.UnitId);
        Assert.Equal(PurchasingRuleCodes.DuplicateLineId,
            Assert.Throws<PurchasingRuleException>(() => request.AddLine(2, lineId, Guid.NewGuid(), KgUnit, Kg(1), T0)).Code);
    }

    [Fact]
    public void A_stale_expected_version_is_a_conflict()
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, T0);
        var conflict = Assert.Throws<PurchasingVersionConflictException>(() =>
            request.AddLine(7, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(1), T0));
        Assert.Equal((7L, 1L), (conflict.ExpectedVersion, conflict.CurrentVersion));
    }

    [Fact]
    public void Submitting_needs_a_line_and_freezes_the_request()
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, T0);
        Assert.Equal(PurchasingRuleCodes.RequestWithoutLines, Assert.Throws<PurchasingRuleException>(() => request.Submit(1, T0)).Code);
        request.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(1000), T0);
        request.Submit(2, T0.AddMinutes(1));
        Assert.Equal(PurchaseRequestStatus.Submitted, request.Status);
        Assert.Equal(PurchasingRuleCodes.RequestNotDraft,
            Assert.Throws<PurchasingRuleException>(() => request.AddLine(3, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(1), T0)).Code);
        Assert.Equal(PurchasingRuleCodes.RequestNotDraft, Assert.Throws<PurchasingRuleException>(() => request.Submit(3, T0)).Code);
    }

    [Fact]
    public void An_order_is_sourced_only_from_a_submitted_request_and_stays_in_its_company_with_its_units()
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, T0);
        var productId = Guid.NewGuid();
        var requestLine = request.AddLine(1, Guid.NewGuid(), productId, KgUnit, Kg(1000), T0);
        Assert.Equal(PurchasingRuleCodes.RequestNotSubmitted, Assert.Throws<PurchasingRuleException>(() =>
            PurchaseOrder.FromRequest(Guid.NewGuid(), request, Supplier, Guid.NewGuid, T0)).Code);

        request.Submit(2, T0);
        var order = PurchaseOrder.FromRequest(Guid.NewGuid(), request, Supplier, Guid.NewGuid, T0);

        Assert.Equal(Company, order.CompanyId);
        Assert.Equal(request.Id, order.SourceRequestId);
        Assert.Equal(PurchaseOrderStatus.Draft, order.Status);
        var line = Assert.Single(order.Lines);
        Assert.Equal((productId, KgUnit, Kg(1000), PurchaseOrderLineKind.General, (Guid?)requestLine.Id),
            (line.ProductId, line.UnitId, line.Quantity, line.Kind, line.SourceRequestLineId));
    }

    [Fact]
    public void Sourcing_the_same_submitted_request_twice_is_not_refused_today_pinned_until_business_decides()
    {
        // Open Business question (#66): a request can source several full-quantity orders. The
        // behaviour is pinned so a later decision changes it on purpose, not by accident.
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, T0);
        request.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(1000), T0);
        request.Submit(2, T0);

        var first = PurchaseOrder.FromRequest(Guid.NewGuid(), request, Supplier, Guid.NewGuid, T0);
        var second = PurchaseOrder.FromRequest(Guid.NewGuid(), request, Guid.NewGuid(), Guid.NewGuid, T0);

        Assert.Equal(PurchaseRequestStatus.Submitted, request.Status);
        Assert.Equal(Kg(1000), Assert.Single(first.Lines).Quantity);
        Assert.Equal(Kg(1000), Assert.Single(second.Lines).Quantity);
    }

    [Fact]
    public void The_line_view_cannot_be_used_to_bypass_the_rules()
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, T0);
        request.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(1), T0);
        Assert.False(request.Lines is List<PurchaseRequestLine>);
        Assert.Throws<NotSupportedException>(() => ((IList<PurchaseRequestLine>)request.Lines).Clear());
        Assert.Single(request.Lines);
    }
}
