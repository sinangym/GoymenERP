using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Sales.UnitTests;

/// <summary>
/// SAT-D152 (EK21): an order converted from a quote never changes its customer (party or account) — a clear rule code, before the
/// SAT-D128 priced-line rule; its currency still follows SAT-D128 (an unpriced order may change it). A directly created order keeps
/// the SAT-D128 behaviour.
/// </summary>
public sealed class SalesOrderDraftQuoteCustomerTests
{
    private static readonly Guid Company = Guid.Parse("7b1d2c3b-4a59-4687-9a0b-1c2d3e4f5a01");
    private static readonly Guid Currency = Guid.Parse("7b1d2c3b-4a59-4687-9a0b-1c2d3e4f5a02");
    private static readonly Guid OtherCurrency = Guid.Parse("7b1d2c3b-4a59-4687-9a0b-1c2d3e4f5a03");
    private static readonly DateOnly Day = new(2026, 11, 2);
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static SalesOrderDraft Draft(bool fromQuote)
    {
        var draft = SalesOrderDraft.Create(Guid.NewGuid(), Company, "S-1", new SalesDraftHeader(Guid.NewGuid(), Guid.NewGuid(), Currency, Day),
            Money(Currency), T0);
        if (fromQuote)
        {
            draft.RestoreSourceQuote(Guid.NewGuid());
        }

        return draft;
    }

    private static MoneyProfileSnapshot Money(Guid currency) =>
        new(Company, currency, SalesOrderDraft.MoneyOperation, Guid.NewGuid(), "1", Day, true, Day, null, 2);

    private static SalesHeaderRevisionInput Input(SalesOrderDraft draft, Guid? party = null, Guid? account = null, Guid? currency = null) =>
        new(party ?? draft.Header.PartyId, account ?? draft.Header.CompanyAccountId, currency ?? draft.Header.CurrencyId,
            currency is { } changed ? Money(changed) : null, null, null, null, TestDueTerms.PointDue30);

    [Fact]
    public void A_quote_converted_order_refuses_a_customer_change_with_its_own_code_and_turkish_text()
    {
        foreach (var change in new Func<SalesOrderDraft, SalesHeaderRevisionInput>[]
                 {
                     draft => Input(draft, party: Guid.NewGuid()),
                     draft => Input(draft, account: Guid.NewGuid()),
                     draft => Input(draft, party: Guid.NewGuid(), currency: OtherCurrency),
                 })
        {
            var draft = Draft(fromQuote: true);
            var refused = Assert.Throws<SalesRuleException>(() => draft.ReviseHeader(1, change(draft), "SENTETIK", "SENTETIK cari", T0));
            Assert.Equal(SalesRuleCodes.HeaderRevisionQuoteCustomer, refused.Code);
            Assert.Equal(SalesOrderDraft.QuoteCustomerLockedMessage, refused.Message);
            Assert.Contains("Tekliften dönen siparişin carisi değiştirilemez", refused.Message, StringComparison.Ordinal);
            Assert.Equal(1L, draft.Version);
        }
    }

    [Fact]
    public void A_quote_converted_order_still_changes_its_currency_under_SAT_D128_and_a_direct_order_its_customer()
    {
        var converted = Draft(fromQuote: true);
        converted.ReviseHeader(1, Input(converted, currency: OtherCurrency), "SENTETIK", "SENTETIK para birimi", T0);
        Assert.Equal((OtherCurrency, 2L), (converted.Header.CurrencyId, converted.Version));

        var direct = Draft(fromQuote: false);
        var party = Guid.NewGuid();
        direct.ReviseHeader(1, Input(direct, party: party), "SENTETIK", "SENTETIK cari", T0);
        Assert.Equal(party, direct.Header.PartyId);
        Assert.Throws<InvalidOperationException>(() => converted.RestoreSourceQuote(Guid.NewGuid()));
    }
}
