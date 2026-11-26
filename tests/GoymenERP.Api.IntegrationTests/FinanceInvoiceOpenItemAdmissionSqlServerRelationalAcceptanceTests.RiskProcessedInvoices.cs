using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceInvoiceOpenItemAdmissionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Second_shipment_membership_and_receivables_follow_real_admission_and_official_reversal_together()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Sales, Lira, null, Installment(1, "600.00"), Installment(2, "400.00"));
        var query = new SalesPartyRiskReadQueryV2(Party, Account, [invoice.InvoiceId]);
        var missing = await run.RiskAsync(CompanyA, query);
        Assert.Equal(SalesPartyRiskProcessedSetStateV2.Available, missing.ProcessedSalesInvoices.State);
        Assert.Empty(missing.ProcessedSalesInvoices.ProcessedSalesInvoiceIds);
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync(CompanyA, "risk-admit", invoice)).Outcome);
        var admitted = await run.RiskAsync(CompanyA, query);
        Assert.Equal([invoice.InvoiceId], admitted.ProcessedSalesInvoices.ProcessedSalesInvoiceIds);
        Assert.Equal([new SalesPartyRiskAmountV1(Lira, 1000m)], admitted.OpenReceivables.Amounts);
        var foreign = await run.RiskAsync(CompanyB, query);
        Assert.Empty(foreign.ProcessedSalesInvoices.ProcessedSalesInvoiceIds);
        Assert.Empty(foreign.OpenReceivables.Amounts);

        Assert.Equal(CommandOutcome.Succeeded, (await run.ReverseForRiskAsync(invoice)).Outcome);
        var reversed = await run.RiskAsync(CompanyA, query);
        Assert.Equal(SalesPartyRiskProcessedSetStateV2.Available, reversed.ProcessedSalesInvoices.State);
        Assert.Empty(reversed.ProcessedSalesInvoices.ProcessedSalesInvoiceIds);
        Assert.Empty(reversed.OpenReceivables.Amounts);
    }

    private sealed partial class Run
    {
        public async Task<SalesPartyRiskReadAnswerV2> RiskAsync(Guid company, SalesPartyRiskReadQueryV2 query)
        {
            var companyContext = new CompanyContext(); companyContext.Set(company);
            await using var db = new FinanceDbContext(Options(companyContext, null), companyContext);
            return await new FinanceSalesPartyRiskReadPortV1(db, companyContext).ReadPartyRiskAsync(query);
        }

        public async Task<FinanceInvoiceOpenItemCancellationResultV1> ReverseForRiskAsync(FinanceInvoiceOpenItemAdmissionV1 invoice)
        {
            var companyContext = new CompanyContext(); companyContext.Set(CompanyA);
            var collector = new AuditEventCollector();
            await using var db = new FinanceDbContext(Options(companyContext, collector), companyContext);
            var owner = new FinanceInvoiceOpenItemCancellationService(db, companyContext, new FixedCurrentUser(Actor, CompanyA),
                new RiskCancellationAuthority(), collector,
                new FinanceAuditEnvelope("synthetic.risk.invoice-reverse", ["SENTETIK-FINANCE"], "Web"), TimeProvider.System);
            return await owner.CancelAsync(new(CompanyA, Actor, $"risk-{Guid.NewGuid():N}", null),
                new($"risk-reverse-{invoice.InvoiceId:N}"),
                new(Guid.NewGuid(), FinanceInvoiceProducerV1.Sales, invoice.InvoiceId, invoice.InvoiceVersion, "SENTETIK"), default);
        }
    }

    private sealed class RiskCancellationAuthority : IFinanceInvoiceOpenItemCancellationAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer,
            Guid invoiceId, CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }
}
