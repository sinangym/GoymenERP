using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Finance.UnitTests;

public sealed partial class FinanceOfficialSalesInvoiceServiceV1Tests
{
    [Fact]
    public async Task Official_invoice_currency_candidates_use_the_same_real_source_and_read_permission_without_credit_access()
    {
        var fixture = Fixture.Create();
        var answer = await fixture.Service("reader", new Documents(), currencies: new Candidates([
            new MoneyCurrencyCandidateV1(Currency, "TRY", "SENTETIK Lira")])).ReadCurrenciesAsync(Context("reader"), default);
        Assert.Equal(ReadOutcome.Found, answer.Outcome);
        Assert.Equal(Company, answer.Resource!.CompanyId);
        Assert.Equal(Currency, Assert.Single(answer.Resource.Items).CurrencyId);
        var denied = await fixture.Service("reader", new Documents(), new Authority(PermissionDecision.Denied)).ReadCurrenciesAsync(Context("reader"), default);
        Assert.Equal(ReadOutcome.Denied, denied.Outcome);
        Assert.Null(denied.Resource);
    }

    [Theory]
    [InlineData(ReadOutcome.NotFound, CommandOutcome.ValidationFailed)]
    [InlineData(ReadOutcome.Denied, CommandOutcome.Denied)]
    [InlineData(ReadOutcome.Unavailable, CommandOutcome.Unavailable)]
    public async Task Official_invoice_record_refuses_missing_denied_or_unavailable_customer_owner_without_mutation(ReadOutcome owner, CommandOutcome expected)
    {
        var fixture = Fixture.Create(); var documents = new Documents();
        var answer = await fixture.Service("maker", documents, customers: new CustomerOwner(owner)).RecordAsync(
            Context("maker"), new("owner-record"), Request(), default);
        Assert.Equal(expected, answer.Outcome);
        Assert.Empty(await fixture.Db.OfficialSalesInvoices.ToListAsync());
        Assert.Empty(await fixture.Db.OfficialSalesInvoiceCommands.ToListAsync());
        Assert.Equal(0, documents.Calls);
    }

    [Fact]
    public async Task Official_invoice_approval_rechecks_customer_company_owner_and_keeps_pending_on_owner_loss()
    {
        var fixture = Fixture.Create();
        var created = await fixture.Service("maker", new Documents()).RecordAsync(Context("maker"), new("record"), Request(), default);
        Assert.Equal(CommandOutcome.Succeeded, created.Outcome);
        var refused = await fixture.Service("checker", new Documents(), customers: new CustomerOwner(ReadOutcome.NotFound)).ApproveAsync(
            Context("checker"), new("approve"), new(Invoice, created.Resource!.Version), default);
        Assert.Equal(CommandOutcome.ValidationFailed, refused.Outcome);
        Assert.Equal(1, (await fixture.Db.OfficialSalesInvoices.AsNoTracking().SingleAsync()).Status);
        Assert.Single(await fixture.Db.OfficialSalesInvoiceCommands.ToListAsync());
    }
}
