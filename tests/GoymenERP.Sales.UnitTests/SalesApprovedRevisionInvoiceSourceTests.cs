using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Sales.UnitTests;

public sealed partial class SalesDraftServiceTests
{
    [Fact]
    public async Task Invoice_source_reads_the_exact_accepted_archive_with_rich_price_and_both_fresh_fences()
    {
        var world = new World { PriceReferences = new FakePriceReferences { Price = 12.50m } };
        var created = (await world.Service().CreateDraftAsync(Request(), Metadata("invoice-create"))).Detail;
        var lineId = created.Lines[0].LineId;
        var priced = await world.Service().ReviseCommercialAsync(created.Draft.Id,
            Revision(created.Draft.Version, new SalesDraftLineCommercialV1(lineId, "13.00", KgUnit, null,
                new SalesDraftPriceReferenceRequestV1(PriceList, 1), DiscountAmount: "0.50", SurchargeAmount: "0.25")),
            Metadata("invoice-price"));
        var submitted = await world.Service().SubmitDraftAsync(created.Draft.Id,
            new SalesDraftStateCommandRequest(priced.Detail.Draft.Version), Metadata("invoice-submit"));
        var approved = await world.Service().ApproveDraftAsync(created.Draft.Id,
            new SalesDraftStateCommandRequest(submitted.Detail.Draft.Version), Metadata("invoice-approve"));
        var accepted = await world.Service().RecordCustomerAcceptanceAsync(created.Draft.Id,
            new SalesDraftStateCommandRequest(approved.Detail.Draft.Version), Metadata("invoice-accept"));
        Owners(world);
        await world.Service().ReviseQuantityAsync(created.Draft.Id,
            new SalesDraftQuantityRevisionRequest(accepted.Detail.Draft.Version, "SENTETIK exact archive",
                [new SalesDraftLineQuantityV1(lineId, "2")]), Metadata("invoice-archive"));

        world.Authority.Measured.Clear();
        var answer = await ((IApprovedSalesRevisionInvoiceSourceV1)world.Service()).ReadAsync(created.Draft.Id, 0, [lineId]);

        Assert.Equal(ApprovedSalesRevisionInvoiceSourceOutcomeV1.Available, answer.Outcome);
        var source = Assert.IsType<ApprovedSalesRevisionInvoiceSourceV1>(answer.Source);
        var line = Assert.Single(source.Lines);
        Assert.Equal((Company, created.Draft.Id, 0, Party, Account, Currency),
            (source.CompanyId, source.OrderId, source.Revision, source.PartyId, source.CompanyAccountId, source.CurrencyId));
        Assert.Equal((true, 0, Actor),
            (source.CustomerAcceptance.Required, source.CustomerAcceptance.AcceptedRevision, source.CustomerAcceptance.AcceptedBy));
        Assert.Equal(("13.00", "0.50", "0.25", "12.75", PriceList),
            (line.UnitPrice, line.DiscountAmount, line.SurchargeAmount, line.NetUnitPrice, line.PriceReference!.PriceListId));
        Assert.Equal([ReadPermission, PriceReadPermission, ReadPermission, PriceReadPermission], world.Authority.Measured);
    }

    [Fact]
    public async Task A_later_current_acceptance_never_repairs_an_unaccepted_archived_revision()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        var lineId = approved.Lines[0].LineId;
        Owners(world);
        var revised = await world.Service().ReviseQuantityAsync(approved.Draft.Id,
            new SalesDraftQuantityRevisionRequest(approved.Draft.Version, "SENTETIK archive without acceptance",
                [new SalesDraftLineQuantityV1(lineId, "2")]), Metadata("invoice-unaccepted-archive"));
        var submitted = await world.Service().SubmitDraftAsync(approved.Draft.Id,
            new SalesDraftStateCommandRequest(revised.Detail.Draft.Version), Metadata("invoice-submit-current"));
        var approvedCurrent = await world.Service().ApproveDraftAsync(approved.Draft.Id,
            new SalesDraftStateCommandRequest(submitted.Detail.Draft.Version), Metadata("invoice-approve-current"));
        await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id,
            new SalesDraftStateCommandRequest(approvedCurrent.Detail.Draft.Version), Metadata("invoice-accept-current"));

        var answer = await ((IApprovedSalesRevisionInvoiceSourceV1)world.Service()).ReadAsync(approved.Draft.Id, 0, [lineId]);

        Assert.Equal(ApprovedSalesRevisionInvoiceSourceOutcomeV1.CustomerAcceptanceUnavailable, answer.Outcome);
        Assert.Null(answer.Source);
    }

    [Fact]
    public async Task Revoking_price_read_after_the_store_read_releases_no_invoice_source()
    {
        var world = new World();
        var approved = await ApprovedAsync(world);
        await world.Service().RecordCustomerAcceptanceAsync(approved.Draft.Id,
            new SalesDraftStateCommandRequest(approved.Draft.Version), Metadata("invoice-current-accept"));
        world.Store.OnFindDraft = () => world.Authority.Denied.Add(PriceReadPermission);

        var refusal = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            ((IApprovedSalesRevisionInvoiceSourceV1)world.Service()).ReadAsync(approved.Draft.Id, 0, [approved.Lines[0].LineId]));

        Assert.Equal(403, refusal.Status);
    }
}
