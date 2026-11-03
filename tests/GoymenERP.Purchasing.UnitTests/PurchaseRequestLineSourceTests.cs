using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Quantities;
using GoymenERP.Modules.Purchasing.Domain.Requests;

namespace GoymenERP.Purchasing.UnitTests;

/// <summary>
/// A1-2 (plan API-02; Üretim A-2, Satış S-A1, ALS-D16): a request line's optional source in the domain — a defined kind with
/// two non-empty opaque identities or nothing (manual), fixed with the line and outside every decision. SENTETIK / NOT_ENV.
/// </summary>
public sealed class PurchaseRequestLineSourceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 9, 0, 0, TimeSpan.FromHours(3));
    private static readonly Guid Company = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid KgUnit = Guid.Parse("55555555-5555-4555-8555-555555555555");

    private static Quantity Kg(decimal value) => Quantity.Of(value, QuantityUnit.Kilogram);

    [Theory]
    [InlineData(PurchaseRequestSourceKind.Stock)]
    [InlineData(PurchaseRequestSourceKind.Sales)]
    [InlineData(PurchaseRequestSourceKind.Production)]
    public void A_line_keeps_the_source_it_was_added_with(PurchaseRequestSourceKind kind)
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, "synthetic-preparer", T0);
        var document = Guid.NewGuid();
        var documentLine = Guid.NewGuid();

        var line = request.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(5), T0, PurchaseRequestLineSource.Of(kind, document, documentLine));

        Assert.Equal((kind, document, documentLine), (line.Source!.Kind, line.Source.DocumentId, line.Source.DocumentLineId));
        Assert.Equal(2, request.Version);
    }

    [Fact]
    public void A_line_without_a_source_is_manual()
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, "synthetic-preparer", T0);
        Assert.Null(request.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(5), T0).Source);
    }

    [Fact]
    public void An_undefined_kind_or_an_empty_identity_is_refused_never_defaulted()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseRequestLineSource.Of((PurchaseRequestSourceKind)0, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<ArgumentOutOfRangeException>(() => PurchaseRequestLineSource.Of((PurchaseRequestSourceKind)4, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Sales, Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Production, Guid.NewGuid(), Guid.Empty));
    }

    [Fact]
    public void The_stored_kind_numbers_never_change_meaning()
    {
        Assert.Equal((1, 2, 3), ((int)PurchaseRequestSourceKind.Stock, (int)PurchaseRequestSourceKind.Sales, (int)PurchaseRequestSourceKind.Production));
        Assert.Equal(3, Enum.GetValues<PurchaseRequestSourceKind>().Length);
    }

    [Fact]
    public void A_production_sourced_request_still_waits_for_the_manager_and_keeps_its_makers_rule()
    {
        // ALS-D16: a request opened from a production plan waits for the purchase manager's approval like any other.
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, "synthetic-planner", T0);
        request.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(5), T0,
            PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Production, Guid.NewGuid(), Guid.NewGuid()));
        request.Submit(2, "synthetic-planner", T0);
        Assert.Equal(PurchaseRequestStatus.Submitted, request.Status);

        var same = Assert.Throws<PurchasingRuleException>(() => request.Approve(3, "synthetic-planner", T0));
        Assert.Equal(PurchasingRuleCodes.RequestSameActor, same.Code);
        request.Approve(3, "synthetic-manager", T0);
        Assert.Equal(PurchaseRequestStatus.Approved, request.Status);
        Assert.Equal(PurchaseRequestSourceKind.Production, request.Lines.Single().Source!.Kind);
    }

    [Fact]
    public void A_submitted_request_takes_no_further_line_with_or_without_a_source()
    {
        var request = PurchaseRequest.Create(Guid.NewGuid(), Company, "synthetic-preparer", T0);
        request.AddLine(1, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(5), T0);
        request.Submit(2, "synthetic-preparer", T0);
        var refusal = Assert.Throws<PurchasingRuleException>(() => request.AddLine(3, Guid.NewGuid(), Guid.NewGuid(), KgUnit, Kg(1), T0,
            PurchaseRequestLineSource.Of(PurchaseRequestSourceKind.Sales, Guid.NewGuid(), Guid.NewGuid())));
        Assert.Equal(PurchasingRuleCodes.RequestNotDraft, refusal.Code);
    }
}
