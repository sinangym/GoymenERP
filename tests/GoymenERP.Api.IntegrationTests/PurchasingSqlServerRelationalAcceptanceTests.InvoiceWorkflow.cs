using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Invoices;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class PurchasingSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Incoming_invoice_workflow_commits_the_pending_source_before_Finance_and_acknowledges_once()
    {
        await using var run = await Run.StartAsync();
        var receipt = await InvoiceRateReceiptAsync(run);
        var invoice = await StoreRateInvoiceAsync(run, receipt, 4m, 35m, finalize: false);
        await using var scope = run.Store(CompanyA);
        var finance = new InvoiceWorkflowFinanceProbe(async _ =>
        {
            Assert.Null(scope.Context.Database.CurrentTransaction);
            // A separate SQL connection sees the committed source before the Finance boundary is crossed.
            Assert.Equal(1, await run.ScalarAsync<int>(
                "SELECT COUNT(*) FROM purchasing.IncomingInvoiceRevisions WHERE InvoiceId=@id AND Status=3", ("@id", invoice.Id)));
        });
        var service = InvoiceWorkflow(run, scope, finance);
        var metadata = new PurchasingCommandMetadata("SENTETIK-workflow-finalize", run.Correlation, null, AcceptanceTime);
        var result = await service.FinalizeAsync(invoice.Id, new("2"), metadata);
        Assert.Equal(PurchaseIncomingInvoiceStatus.FinanceAccepted, result.Invoice.Status);
        var again = await service.FinalizeAsync(invoice.Id, new("2"), metadata);
        Assert.True(again.IsReplay);
        Assert.Equal(result.Invoice.AdmissionId, again.Invoice.AdmissionId);
        Assert.Equal(1, finance.Calls);
        Assert.Equal(1, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.IncomingInvoiceRevisions WHERE InvoiceId=@id AND Status=4", ("@id", invoice.Id)));
    }

    [SqlServerFact]
    public async Task Incoming_invoice_workflow_keeps_real_pending_history_when_Finance_is_unknown_and_redrives_it()
    {
        await using var run = await Run.StartAsync();
        var receipt = await InvoiceRateReceiptAsync(run);
        var invoice = await StoreRateInvoiceAsync(run, receipt, 4m, 35m, finalize: false);
        await using var scope = run.Store(CompanyA);
        var finance = new InvoiceWorkflowFinanceProbe(_ => Task.CompletedTask) { Available = false };
        var service = InvoiceWorkflow(run, scope, finance);
        var metadata = new PurchasingCommandMetadata("SENTETIK-workflow-pending", run.Correlation, null, AcceptanceTime);
        var pending = await service.FinalizeAsync(invoice.Id, new("2"), metadata);
        Assert.Equal(PurchaseIncomingInvoiceStatus.FinancePending, pending.Invoice.Status);
        Assert.Equal("unavailable", pending.FinanceOutcome);
        Assert.Equal(0, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM purchasing.IncomingInvoiceRevisions WHERE InvoiceId=@id AND Status=4", ("@id", invoice.Id)));
        finance.Available = true;
        var accepted = await service.RetryFinanceAsync(invoice.Id, new("3"), metadata with { IdempotencyKey = "SENTETIK-workflow-retry" });
        Assert.Equal(PurchaseIncomingInvoiceStatus.FinanceAccepted, accepted.Invoice.Status);
        Assert.Equal(pending.Invoice.AdmissionId, accepted.Invoice.AdmissionId);
        Assert.Equal(2, finance.Calls);
    }

    private static PurchaseIncomingInvoiceWorkflow InvoiceWorkflow(Run run, StoreScope scope, InvoiceWorkflowFinanceProbe finance,
        IPurchaseIncomingInvoiceMatchSourceV1? matching = null)
    {
        var company = new CompanyContext(); company.Set(CompanyA);
        var correlation = new CorrelationContext(); correlation.Set(run.Correlation, null, "Test", null, null);
        return new(company, new NumberTestUser { UserId = run.Actor }, new NumberTestAuthority(), correlation,
            (IPurchasingIncomingInvoiceStoreV1)scope.Store, TimeProvider.System, finance, matching: matching);
    }
    // Purchasing SQL/commit proof only. Finance business acceptance is deliberately not claimed by this probe.
    private sealed class InvoiceWorkflowFinanceProbe(Func<PurchaseInvoiceFinanceAdmissionRequestV1, Task> readCommitted)
        : IPurchaseInvoiceFinanceAdmissionPortV1
    {
        public int Calls { get; private set; }
        public bool Available { get; set; } = true;
        public async Task<PurchaseInvoiceFinanceAdmissionAnswerV1> AdmitAsync(PurchaseInvoiceFinanceAdmissionRequestV1 request,
            CancellationToken cancellationToken = default)
        {
            Calls++; await readCommitted(request);
            return Available ? new(PurchaseInvoiceFinanceAdmissionOutcomeV1.Admitted, "SENTETIK-probe")
                : PurchaseInvoiceFinanceAdmissionAnswerV1.Unavailable;
        }
    }
}
