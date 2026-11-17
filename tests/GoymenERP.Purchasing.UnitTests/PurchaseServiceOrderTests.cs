using GoymenERP.Modules.Purchasing.Domain.Services;
using Xunit;

namespace GoymenERP.Purchasing.UnitTests;

public sealed class PurchaseServiceOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 0, 0, 0, TimeSpan.Zero);
    [Fact]
    public void Service_acceptance_records_actual_quantity_without_creating_material_or_stock_facts()
    {
        var draft = Draft(); var approved = PurchaseServiceOrder.Approve(draft, 1, "SENTETIK-checker", Now);
        var accepted = PurchaseServiceOrder.Accept(approved, 2, Guid.NewGuid(), draft.Lines[0].LineId, 3m,
            draft.BusinessDate, "SENTETIK-receiver", "SENTETIK gerçek hizmet kabulü", Now);
        Assert.Equal(3, accepted.Version); Assert.Equal(3m, accepted.Lines[0].AcceptedQuantity); Assert.True(accepted.IsOpen);
        Assert.Empty(accepted.Materials); Assert.Empty(accepted.ExpectedOutputs); Assert.Single(accepted.Acceptances);
        Assert.Empty(draft.Acceptances); Assert.Equal(0m, draft.Lines[0].AcceptedQuantity);
    }
    [Fact]
    public void Approval_preserves_the_existing_purchase_maker_checker_boundary()
    {
        var draft = Draft();
        Assert.Equal(PurchaseServiceOrder.SameActor, Assert.Throws<PurchaseServiceOrderException>(() => PurchaseServiceOrder.Approve(draft, 1, draft.PreparedBy, Now)).Code);
        Assert.Equal(PurchaseServiceOrder.VersionConflict, Assert.Throws<PurchaseServiceOrderException>(() => PurchaseServiceOrder.Approve(draft, 2, "SENTETIK-checker", Now)).Code);
    }
    [Fact]
    public void Acceptance_cannot_exceed_the_approved_service_quantity_or_erase_prior_acceptances()
    {
        var approved = PurchaseServiceOrder.Approve(Draft(), 1, "SENTETIK-checker", Now);
        var first = PurchaseServiceOrder.Accept(approved, 2, Guid.NewGuid(), approved.Lines[0].LineId, 8m,
            approved.BusinessDate, "SENTETIK-receiver", "SENTETIK kabul", Now);
        Assert.Equal(PurchaseServiceOrder.AcceptanceExceeded, Assert.Throws<PurchaseServiceOrderException>(() =>
            PurchaseServiceOrder.Accept(first, 3, Guid.NewGuid(), approved.Lines[0].LineId, 3m,
                approved.BusinessDate, "SENTETIK-receiver", "SENTETIK kalan aşımı", Now)).Code);
        Assert.Single(first.Acceptances); Assert.Equal(8m, first.Lines[0].AcceptedQuantity);
    }
    [Fact]
    public void Complete_service_is_closed_without_a_physical_stock_transaction()
    {
        var approved = PurchaseServiceOrder.Approve(Draft(), 1, "SENTETIK-checker", Now);
        var result = PurchaseServiceOrder.Accept(approved, 2, Guid.NewGuid(), approved.Lines[0].LineId, 10m,
            approved.BusinessDate, "SENTETIK-receiver", "SENTETIK tam hizmet", Now);
        Assert.False(result.IsOpen); Assert.Empty(result.Materials); PurchaseServiceOrder.Validate(result);
    }
    [Fact]
    public void Own_material_subcontract_requires_explicit_material_and_expected_output_references()
    {
        var draft = Draft();
        Assert.Throws<ArgumentException>(() => PurchaseServiceOrder.Create(draft with { Kind = PurchaseServiceOrderKind.OwnMaterialSubcontract }));
        var own = draft with { Kind = PurchaseServiceOrderKind.OwnMaterialSubcontract,
            Materials = [new(Guid.NewGuid(), "1", Guid.NewGuid(), "1", 10m)], ExpectedOutputs = [new(Guid.NewGuid(), "1", 9m)] };
        Assert.Equal(own, PurchaseServiceOrder.Create(own));
        Assert.Throws<ArgumentException>(() => PurchaseServiceOrder.Create(own with { Kind = PurchaseServiceOrderKind.ServicePurchase }));
        Assert.Throws<ArgumentException>(() => PurchaseServiceOrder.Approve(own, 1, "SENTETIK-checker", Now));
    }
    [Fact]
    public void Corrupt_acceptance_totals_and_overprecision_are_refused_without_repair()
    {
        var draft = Draft();
        Assert.Throws<ArgumentException>(() => PurchaseServiceOrder.Validate(draft with { Lines = [draft.Lines[0] with { AcceptedQuantity = 1m }] }));
        Assert.Throws<ArgumentException>(() => PurchaseServiceOrder.Create(draft with { Lines = [draft.Lines[0] with { Quantity = 1.0000001m }] }));
    }
    [Fact]
    public void Later_price_entry_preserves_service_acceptance_and_requires_a_new_draft_and_a_different_approver()
    {
        var source = Draft(); source = PurchaseServiceOrder.Create(source with { Lines = [source.Lines[0] with { UnitPrice = null }] });
        var approved = PurchaseServiceOrder.Approve(source, 1, "SENTETIK-checker", Now);
        var actual = PurchaseServiceOrder.Accept(approved, 2, Guid.NewGuid(), source.Lines[0].LineId, 4m, source.BusinessDate,
            "SENTETIK-receiver", "SENTETIK kabul", Now);
        var revision = PurchaseServiceOrder.RevisePrices(actual, 3, new Dictionary<Guid, decimal> { [source.Lines[0].LineId] = 2m },
            "SENTETIK-price-preparer", "SENTETIK sonradan fiyat", Now);
        Assert.Equal(PurchaseServiceOrderStatus.Draft, revision.Status); Assert.Equal(4, revision.Version); Assert.Equal(1, revision.PriceRevision);
        Assert.Equal(actual.Acceptances, revision.Acceptances); Assert.Equal(4m, revision.Lines[0].AcceptedQuantity);
        Assert.Null(actual.Lines[0].UnitPrice); Assert.Equal(2m, revision.Lines[0].UnitPrice); Assert.Null(revision.ApprovedBy);
        Assert.Equal(PurchaseServiceOrder.SameActor, Assert.Throws<PurchaseServiceOrderException>(() =>
            PurchaseServiceOrder.Approve(revision, 4, revision.PreparedBy, Now)).Code);
        var final = PurchaseServiceOrder.Approve(revision, 4, "SENTETIK-other-checker", Now);
        Assert.Equal(PurchaseServiceOrderStatus.Approved, final.Status); Assert.Equal(5, final.Version);
        Assert.Equal(actual.Acceptances, final.Acceptances);
    }
    [Fact]
    public void An_approved_subcontract_tax_revision_preserves_physical_and_accepted_facts_and_reenters_normal_approval()
    {
        var source = Draft();
        source = PurchaseServiceOrder.Create(source with { Kind = PurchaseServiceOrderKind.OwnMaterialSubcontract,
            Lines = [source.Lines[0] with { Tax = new(true, 20m, null, false, null, null, 1) }],
            Materials = [new(Guid.NewGuid(), "1", Guid.NewGuid(), "1", 10m)], ExpectedOutputs = [new(Guid.NewGuid(), "1", 9m)] });
        var approved = PurchaseServiceOrder.Approve(source, 1, "SENTETIK-checker", Now);
        var accepted = PurchaseServiceOrder.Accept(approved, 2, Guid.NewGuid(), source.Lines[0].LineId, 4m,
            source.BusinessDate, "SENTETIK-receiver", "SENTETIK kabul", Now);
        var tax = new PurchaseServiceTax(true, 10m, null, false, null, null, 1,
            new("SENTETIK-lists:2", "SENTETIK-10", null, "SENTETIK-tax-editor", "SENTETIK vergi düzeltmesi", Now));
        var revision = PurchaseServiceOrder.ReviseTax(accepted, 3, source.Lines[0].LineId, tax,
            "SENTETIK-tax-editor", "SENTETIK vergi düzeltmesi", Now);
        Assert.Equal((4L, PurchaseServiceOrderStatus.Draft, 0, "SENTETIK-tax-editor", null),
            (revision.Version, revision.Status, revision.PriceRevision, revision.PreparedBy, revision.ApprovedBy));
        Assert.Equal(accepted.Materials, revision.Materials); Assert.Equal(accepted.ExpectedOutputs, revision.ExpectedOutputs);
        Assert.Equal(accepted.Acceptances, revision.Acceptances);
        Assert.Equal(accepted.Lines[0] with { Tax = tax }, revision.Lines[0]);
        Assert.Equal(20m, accepted.Lines[0].Tax!.VatPercent); Assert.Equal(10m, revision.Lines[0].Tax!.VatPercent);
        Assert.Equal(PurchaseServiceOrder.SameActor, Assert.Throws<PurchaseServiceOrderException>(() =>
            PurchaseServiceOrder.Approve(revision, 4, revision.PreparedBy, Now)).Code);
        Assert.Equal(PurchaseServiceOrderStatus.Approved, PurchaseServiceOrder.Approve(revision, 4, "SENTETIK-new-checker", Now).Status);
    }
    [Fact]
    public void A_service_tax_revision_cannot_change_an_ordinary_service_order_or_reuse_a_stale_version()
    {
        var draft = Draft(); var tax = new PurchaseServiceTax(true, 10m, null, false, null, null, 1);
        Assert.Equal(PurchaseServiceOrder.StateConflict, Assert.Throws<PurchaseServiceOrderException>(() =>
            PurchaseServiceOrder.ReviseTax(draft, 1, draft.Lines[0].LineId, tax, "SENTETIK-editor", "SENTETIK", Now)).Code);
        Assert.Equal(PurchaseServiceOrder.VersionConflict, Assert.Throws<PurchaseServiceOrderException>(() =>
            PurchaseServiceOrder.ReviseTax(draft, 2, draft.Lines[0].LineId, tax, "SENTETIK-editor", "SENTETIK", Now)).Code);
    }
    private static PurchaseServiceOrderState Draft() => PurchaseServiceOrder.Create(new(Guid.NewGuid(), Guid.NewGuid(), 1,
        PurchaseServiceOrderKind.ServicePurchase, PurchaseServiceOrderStatus.Draft, Guid.NewGuid(), Guid.NewGuid(), "1", "1",
        new(2026, 11, 17), "SENTETIK-maker", null, Now, Now, new(Guid.NewGuid(), "TRY", Guid.NewGuid(), 1, 2, 4),
        [new(Guid.NewGuid(), Guid.NewGuid(), 1, "SENTETIK hizmet", Guid.NewGuid(), "kg", Guid.NewGuid(), 1, 3, 10m, 2m, 0m)], [], [], []));
}
