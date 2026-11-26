using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;

namespace GoymenERP.Sales.UnitTests;

// Synthetic owners; real HTTP/SQL/runtime acceptance is NOT_RUN.
public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task A_subcontract_tax_only_revision_uses_Sales_approval_without_conferring_price_write_and_replays_original_facts()
    {
        var world = new World { ItemTypes = new FakeItemTypes(), LineTaxes = new FakeLineTaxes() };
        var created = (await world.Service().CreateDraftAsync(Subcontract("10"), Metadata("SENTETIK-tax-create"))).Detail;
        var submitted = await world.Service().SubmitDraftAsync(created.Draft.Id, Command(created.Draft.Version), Metadata("SENTETIK-tax-submit"));
        var approved = (await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(created.Draft.Id, Command(submitted.Detail.Draft.Version), Metadata("SENTETIK-tax-approve")))).Detail;
        world.Authority.Denied.Add(PricePermission); world.Authority.Measured.Clear();
        var request = new SalesSubcontractTaxRevisionV1(approved.Draft.Version, approved.Lines[0].LineId,
            new(SalesDraftLineTaxModesV1.Override, TaxBuyerSituationsV1.FinalConsumer, "SENTETIK alıcı vergi dayanağı"), "SENTETIK satır vergisi");
        var result = await world.Service().ReviseSubcontractTaxAsync(created.Draft.Id, request, Metadata("SENTETIK-tax-revision"));
        Assert.Equal(("Preparing", "Rev.1"), (result.Detail.Draft.ApprovalState, result.Detail.Draft.Revision));
        Assert.Equal(approved.Lines[0].Quantity, result.Detail.Lines[0].Quantity);
        Assert.Equal(approved.Lines[0].UnitPrice, result.Detail.Lines[0].UnitPrice);
        Assert.Single(world.Store.Archived);
        Assert.Contains(ApprovePermission, world.Authority.Measured); Assert.DoesNotContain(PricePermission, world.Authority.Measured);
        var before = world.Store.Transitions.Count;
        var replay = await world.Service().ReviseSubcontractTaxAsync(created.Draft.Id, request, Metadata("SENTETIK-tax-revision"));
        Assert.True(replay.IsReplay); Assert.Equal(result.Detail.Draft.Version, replay.Detail.Draft.Version);
        Assert.Equal(before, world.Store.Transitions.Count);
        Assert.Equal(CompanyPolicyKeysV1.SalesSubcontractTaxDefault, Assert.Single(world.Store.Drafts).Lines[0].Tax!.Source);
    }
    [Fact]
    public async Task Subcontract_tax_authority_is_measured_before_replay_or_order_read_and_cannot_be_replaced_by_price_write()
    {
        var world = new World { ItemTypes = new FakeItemTypes(), LineTaxes = new FakeLineTaxes() };
        var created = (await world.Service().CreateDraftAsync(Subcontract("10"), Metadata("SENTETIK-tax-deny-create"))).Detail;
        world.Authority.Denied.Add(ApprovePermission);
        var request = new SalesSubcontractTaxRevisionV1(created.Draft.Version, created.Lines[0].LineId,
            new(SalesDraftLineTaxModesV1.Default), "SENTETIK gerekçe");
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().ReviseSubcontractTaxAsync(created.Draft.Id, request, Metadata("SENTETIK-tax-denied")))).Status);
        Assert.Empty(world.Store.Transitions);
    }
    [Fact]
    public async Task Subcontract_tax_revision_needs_only_the_Sales_approval_and_never_measures_the_Purchasing_approval()
    {
        // SAT-D469: the sales side changes with sales.orders.approve; the two approvals are not required together.
        var world = new World { ItemTypes = new FakeItemTypes(), LineTaxes = new FakeLineTaxes() };
        var created = (await world.Service().CreateDraftAsync(Subcontract("10"), Metadata("SENTETIK-tax-sales-only-create"))).Detail;
        var submitted = await world.Service().SubmitDraftAsync(created.Draft.Id, Command(created.Draft.Version), Metadata("SENTETIK-tax-sales-only-submit"));
        var approved = (await WithIndependentApproverAsync(world, () => world.Service().ApproveDraftAsync(created.Draft.Id, Command(submitted.Detail.Draft.Version), Metadata("SENTETIK-tax-sales-only-approve")))).Detail;
        world.Authority.Denied.Add(GoymenERP.BuildingBlocks.Security.ErpPermissions.PurchasingOrdersApprove);
        world.Authority.Measured.Clear();
        var request = new SalesSubcontractTaxRevisionV1(approved.Draft.Version, approved.Lines[0].LineId,
            new(SalesDraftLineTaxModesV1.Override, TaxBuyerSituationsV1.FinalConsumer, "SENTETIK alıcı vergi dayanağı"), "SENTETIK satır vergisi");
        var result = await world.Service().ReviseSubcontractTaxAsync(created.Draft.Id, request, Metadata("SENTETIK-tax-sales-only"));
        Assert.False(result.IsReplay);
        Assert.Contains(ApprovePermission, world.Authority.Measured);
        Assert.DoesNotContain(GoymenERP.BuildingBlocks.Security.ErpPermissions.PurchasingOrdersApprove, world.Authority.Measured);
    }

    [Fact]
    public async Task A_normal_goods_order_cannot_use_the_subcontract_tax_only_entry_point()
    {
        var world = new World { LineTaxes = new FakeLineTaxes() };
        var created = (await world.Service().CreateDraftAsync(PricedRequest("1", "10"), Metadata("SENTETIK-tax-normal-create"))).Detail;
        Assert.Equal(422, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseSubcontractTaxAsync(created.Draft.Id,
            new(created.Draft.Version, created.Lines[0].LineId, new(SalesDraftLineTaxModesV1.Default), "SENTETIK"), Metadata("SENTETIK-tax-normal")))).Status);
        Assert.Empty(world.Store.Transitions);
    }
    [Fact]
    public async Task Losing_Sales_approval_during_subcontract_tax_owner_reads_prevents_every_write()
    {
        var world = new World { ItemTypes = new FakeItemTypes(), LineTaxes = new FakeLineTaxes() };
        var created = (await world.Service().CreateDraftAsync(Subcontract("10"), Metadata("SENTETIK-tax-loss-create"))).Detail;
        world.Authority.LostLater = (ApprovePermission, world.Authority.Calls + 2);
        Assert.Equal(403, (await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseSubcontractTaxAsync(created.Draft.Id,
            new(created.Draft.Version, created.Lines[0].LineId, new(SalesDraftLineTaxModesV1.Override,
                TaxBuyerSituationsV1.FinalConsumer, "SENTETIK"), "SENTETIK"), Metadata("SENTETIK-tax-loss")))).Status);
        Assert.Empty(world.Store.Transitions); Assert.Equal(created.Draft.Version, Assert.Single(world.Store.Drafts).Version.ToString());
    }
}
