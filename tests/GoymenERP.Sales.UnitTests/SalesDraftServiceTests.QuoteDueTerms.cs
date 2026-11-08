using System.Text.Json;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D132 (EK18): the quote's due terms ("teklif vade") — entered by hand, never defaulted (SAT-D127), disclosed only with the
/// price read, replaced as a whole by a revision and carried to the order at the conversion. SAT-D152 (EK21): the converted
/// order's customer never changes, asked through the service. SENTETIK / NOT_ENV.
/// </summary>
public sealed partial class SalesDraftServiceTests
{
    private static SalesDueTermsV1 TwoInstalments() => new([
        new SalesDueInstalmentV1(1, SalesDueBasisNamesV1.OrderDate, 15, "40"),
        new SalesDueInstalmentV1(2, SalesDueBasisNamesV1.ShipmentDate, 45, "60"),
    ]);

    [Fact]
    public async Task Quote_due_terms_are_stored_by_hand_never_defaulted_and_disclosed_only_with_the_price_read()
    {
        var world = new World { Clock = QuoteClock() };

        var without = await world.Service().CreateQuoteAsync(QuoteOf(), Metadata("q-none"));
        Assert.Equal(("Visible", (SalesDueTermsV1?)null), (without.Detail.DueTermsDisclosure, without.Detail.DueTerms));

        var created = await world.Service().CreateQuoteAsync(QuoteOf() with { DueTerms = TwoInstalments() }, Metadata("q-due"));
        var terms = Assert.IsType<SalesDueTermsV1>(created.Detail.DueTerms);
        Assert.Equal(("Visible", 2, 15, "40", SalesDueBasisNamesV1.ShipmentDate), (created.Detail.DueTermsDisclosure, terms.Instalments.Count,
            terms.Instalments[0].DaysAfter, terms.Instalments[0].SharePercent, terms.Instalments[1].Basis));

        world.Authority.Denied.Add(PriceReadPermission);
        var withheld = await world.Service().GetQuoteDetailAsync(created.Detail.Quote.Id);
        Assert.Equal(("Withheld", (SalesDueTermsV1?)null), (withheld.DueTermsDisclosure, withheld.DueTerms));

        // Writing due terms without the price read is 403 (field dueTerms), before anything is read or recorded.
        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().CreateQuoteAsync(QuoteOf() with
        {
            Lines = [new SalesQuoteLineRequest(Guid.NewGuid(), Product, "7", KgUnit, "1.25")],
            DueTerms = TwoInstalments(),
        }, Metadata("q-due-withheld")));
        Assert.Equal((403, SalesQuoteProblemCodesV1.PriceReadRequired, "dueTerms"), (refused.Status, refused.Code, Assert.Single(refused.Errors).Field));
        Assert.Equal(2, world.Quotes!.Envelopes.Count);
    }

    [Fact]
    public async Task Quote_due_terms_that_break_the_due_terms_rules_are_422_naming_the_field_and_store_nothing()
    {
        var world = new World { Clock = QuoteClock() };
        var shares = new SalesDueTermsV1([new SalesDueInstalmentV1(1, SalesDueBasisNamesV1.OrderDate, 15, "40"),
            new SalesDueInstalmentV1(2, SalesDueBasisNamesV1.OrderDate, 30, "50")]);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() =>
            world.Service().CreateQuoteAsync(QuoteOf() with { DueTerms = shares }, Metadata("q-shares")));

        Assert.Equal((422, "validation_failed", "dueTerms.instalments", SalesDueTermsCodes.SharesNotWhole),
            (refused.Status, refused.Code, Assert.Single(refused.Errors).Field, refused.Errors[0].Code));
        Assert.Empty(world.Quotes!.Envelopes);
    }

    [Fact]
    public async Task Quote_revision_replaces_the_due_terms_as_a_whole_so_an_omitted_value_clears_them()
    {
        var world = new World { Clock = QuoteClock() };
        var id = (await world.Service().CreateQuoteAsync(QuoteOf() with { DueTerms = TwoInstalments() }, Metadata("q-1"))).Detail.Quote.Id;

        var revised = await world.Service().ReviseQuoteAsync(id, RevisionOf("1") with { DueTerms = TestDueTerms.PointDue30Wire }, Metadata("q-rev-1"));
        Assert.Equal(("Rev.1", 1), (revised.Detail.Quote.Revision, Assert.IsType<SalesDueTermsV1>(revised.Detail.DueTerms).Instalments.Count));

        var cleared = await world.Service().ReviseQuoteAsync(id, RevisionOf("2"), Metadata("q-rev-2"));
        Assert.Equal(("Rev.2", (SalesDueTermsV1?)null), (cleared.Detail.Quote.Revision, cleared.Detail.DueTerms));
    }

    [Fact]
    public async Task Quote_due_terms_carry_to_the_order_at_the_conversion_and_the_seller_may_change_them_there()
    {
        var world = new World { Clock = QuoteClock() };
        Owners(world);
        var request = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"))).Detail.Request.Id;
        await world.Service().ApproveRequestAsync(request, Decision("1"), Metadata("r-1-approve"));
        var id = (await world.Service().CreateQuoteAsync(QuoteOf([request]) with { DueTerms = TwoInstalments() }, Metadata("q-1"))).Detail.Quote.Id;

        var order = (await world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("1", Day.AddDays(1)), Metadata("q-convert"))).Detail.Draft;

        var header = await world.Service().GetDraftHeaderAsync(order.Id);
        var carried = Assert.IsType<SalesDueTermsV1>(header.DueTerms);
        Assert.Equal((2, 15, "40", 45, "60"), (carried.Instalments.Count, carried.Instalments[0].DaysAfter, carried.Instalments[0].SharePercent,
            carried.Instalments[1].DaysAfter, carried.Instalments[1].SharePercent));

        var revised = await world.Service().ReviseHeaderAsync(order.Id, new SalesDraftHeaderRevisionRequest(header.DraftVersion, "SENTETIK vade",
            header.PartyId, header.CompanyAccountId, header.CurrencyId, header.DeliveryAddress, header.Contact, header.SalesOwnerUserId, NetDays(60)),
            Metadata("q-header"));
        Assert.False(revised.IsReplay);
        Assert.Equal(60, Assert.Single(Assert.IsType<SalesDueTermsV1>((await world.Service().GetDraftHeaderAsync(order.Id)).DueTerms).Instalments).DaysAfter);
    }

    [Fact]
    public async Task Quote_without_due_terms_converts_into_an_order_without_due_terms()
    {
        var world = new World { Clock = QuoteClock() };
        Owners(world);
        var request = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"))).Detail.Request.Id;
        await world.Service().ApproveRequestAsync(request, Decision("1"), Metadata("r-1-approve"));
        var id = (await world.Service().CreateQuoteAsync(QuoteOf([request]), Metadata("q-1"))).Detail.Quote.Id;

        var order = (await world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("1", Day.AddDays(1)), Metadata("q-convert"))).Detail.Draft;

        Assert.Null((await world.Service().GetDraftHeaderAsync(order.Id)).DueTerms);
    }

    [Fact]
    public async Task SAT_D152_the_service_refuses_a_customer_change_of_a_quote_converted_order_with_its_own_code_and_records_nothing()
    {
        var world = new World { Clock = QuoteClock() };
        Owners(world);
        var request = (await world.Service().CreateRequestAsync(RequestOf(), Metadata("r-1"))).Detail.Request.Id;
        await world.Service().ApproveRequestAsync(request, Decision("1"), Metadata("r-1-approve"));
        var id = (await world.Service().CreateQuoteAsync(QuoteOf([request]), Metadata("q-1"))).Detail.Quote.Id;
        var order = (await world.Service().ConvertQuoteAsync(id, new SalesQuoteConversionRequest("1", Day.AddDays(1)), Metadata("q-convert"))).Detail.Draft;
        var header = await world.Service().GetDraftHeaderAsync(order.Id);
        var recorded = (world.Store.Envelopes.Count, world.Store.Refusals.Count);

        var refused = await Assert.ThrowsAsync<SalesDraftServiceException>(() => world.Service().ReviseHeaderAsync(order.Id,
            new SalesDraftHeaderRevisionRequest(header.DraftVersion, "SENTETIK baska musteri", header.PartyId, NewAccount, header.CurrencyId,
                header.DeliveryAddress, header.Contact, header.SalesOwnerUserId, TestDueTerms.PointDue30Wire), Metadata("q-customer")));

        Assert.Equal((422, "validation_failed", "companyAccountId", SalesRuleCodes.HeaderRevisionQuoteCustomer),
            (refused.Status, refused.Code, Assert.Single(refused.Errors).Field, refused.Errors[0].Code));
        Assert.Equal(recorded, (world.Store.Envelopes.Count, world.Store.Refusals.Count));
        Assert.Equal(Account, (await world.Service().GetDraftHeaderAsync(order.Id)).CompanyAccountId);
    }

    [Fact]
    public void Quote_due_terms_stay_out_of_the_replay_hash_and_json_when_absent_and_shape_errors_are_refused()
    {
        var plain = QuoteOf();
        var withTerms = plain with { DueTerms = TwoInstalments() };

        Assert.DoesNotContain("dueTerms", JsonSerializer.Serialize(plain, SalesDraftJson.Options), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("dueTerms", JsonSerializer.Serialize(RevisionOf("1"), SalesDraftJson.Options), StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(SalesQuoteHash.Compute(plain), SalesQuoteHash.Compute(withTerms));
        Assert.Equal(SalesQuoteHash.Compute(plain), SalesQuoteHash.Compute(plain with { DueTerms = null }));

        var json = JsonSerializer.Serialize(withTerms, SalesDraftJson.Options);
        Assert.Equal(2, SalesQuoteJson.ReadCreate(json).DueTerms!.Instalments.Count);
        var badBasis = json.Replace(SalesDueBasisNamesV1.ShipmentDate, "Nope", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => SalesQuoteJson.ReadCreate(badBasis));
        var revisionJson = JsonSerializer.Serialize(RevisionOf("1") with { DueTerms = TwoInstalments() }, SalesDraftJson.Options);
        Assert.Equal(2, SalesQuoteJson.ReadRevision(revisionJson).DueTerms!.Instalments.Count);
        Assert.Throws<JsonException>(() => SalesQuoteJson.ReadRevision(revisionJson.Replace("\"40\"", "\"-1\"", StringComparison.Ordinal)));
    }
}
