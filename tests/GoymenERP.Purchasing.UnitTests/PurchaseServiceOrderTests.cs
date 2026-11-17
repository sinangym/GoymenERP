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
    }
    [Fact]
    public void Corrupt_acceptance_totals_and_overprecision_are_refused_without_repair()
    {
        var draft = Draft();
        Assert.Throws<ArgumentException>(() => PurchaseServiceOrder.Validate(draft with { Lines = [draft.Lines[0] with { AcceptedQuantity = 1m }] }));
        Assert.Throws<ArgumentException>(() => PurchaseServiceOrder.Create(draft with { Lines = [draft.Lines[0] with { Quantity = 1.0000001m }] }));
    }
    private static PurchaseServiceOrderState Draft() => PurchaseServiceOrder.Create(new(Guid.NewGuid(), Guid.NewGuid(), 1,
        PurchaseServiceOrderKind.ServicePurchase, PurchaseServiceOrderStatus.Draft, Guid.NewGuid(), Guid.NewGuid(), "1", "1",
        new(2026, 11, 17), "SENTETIK-maker", null, Now, Now, new(Guid.NewGuid(), "TRY", Guid.NewGuid(), 1, 2, 4),
        [new(Guid.NewGuid(), Guid.NewGuid(), 1, "SENTETIK hizmet", Guid.NewGuid(), "kg", Guid.NewGuid(), 1, 3, 10m, 2m, 0m)], [], [], []));
}
