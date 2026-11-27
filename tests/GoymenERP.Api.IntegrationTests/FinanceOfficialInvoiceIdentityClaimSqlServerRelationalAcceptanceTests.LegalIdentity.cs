using GoymenERP.Api.Shell;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Sales.Application.Contracts;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class FinanceOfficialInvoiceIdentityClaimSqlServerRelationalAcceptanceTests
{
    // FNS-D19/D28 reproduction specifications. These are deliberately NOT assertions that the current gap is acceptable.
    // Current SalesFinanceAdmissionAdapter drops OfficialInvoiceNo/Ettn: the duplicate/generation assertions expose it.
    // Real SQL, Finance owners and actual Sales adapter; only the finalized Sales offer, party and authority ports are
    // controlled fixture inputs. This does not prove the full native Sales finalization/official-document matching flow.
    [SqlServerFact]
    public async Task Legal_identity_Sales_adapter_first_then_different_CSV_document_cannot_open_a_second_receivable()
    {
        await using var run = await Run.StartAsync();
        var sales = LegalSalesOffer("600.00");
        var csv = await LegalCsvAsync(run);
        Assert.NotEqual(sales.InvoiceId, csv.Batch.Rows[0].SourceDocumentId);
        Assert.Equal(SalesFinanceAdmissionOutcomeV1.Admitted, (await run.AdmitLegalSalesAsync(sales)).Outcome);

        var duplicate = await run.AdmitAsync(csv, "legal-csv-second");
        Assert.Equal(CommandOutcome.ValidationFailed, duplicate.Outcome);
        Assert.NotEmpty(duplicate.Issues);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems WHERE SourceBatchId=@id", ("@id", csv.BatchId)));
        await AssertSingleLiveLegalFamilyAsync(run, sales.InvoiceId, "600.00");
        Assert.Equal(SalesFinanceAdmissionOutcomeV1.Admitted, (await run.AdmitLegalSalesAsync(sales)).Outcome);
        await AssertSingleLiveLegalFamilyAsync(run, sales.InvoiceId, "600.00");
    }

    [SqlServerFact]
    public async Task Legal_identity_CSV_first_then_different_native_Sales_invoice_cannot_open_a_second_receivable()
    {
        await using var run = await Run.StartAsync();
        var csv = await LegalCsvAsync(run);
        var sales = LegalSalesOffer("600.00");
        Assert.NotEqual(sales.InvoiceId, csv.Batch.Rows[0].SourceDocumentId);
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync(csv, "legal-csv-first")).Outcome);

        var duplicate = await run.AdmitLegalSalesAsync(sales);
        Assert.Equal(SalesFinanceAdmissionOutcomeV1.Refused, duplicate.Outcome);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId=@id", ("@id", sales.InvoiceId)));
        await AssertSingleLiveLegalFamilyAsync(run, csv.Batch.Rows[0].SourceDocumentId, "600.00");
        Assert.True((await run.AdmitAsync(csv, "legal-csv-first")).IsReplay);
        await AssertSingleLiveLegalFamilyAsync(run, csv.Batch.Rows[0].SourceDocumentId, "600.00");
    }

    [SqlServerFact]
    public async Task Legal_identity_different_amounts_are_refused_in_both_arrival_orders_without_silently_merging_or_replacing_installments()
    {
        await using var run = await Run.StartAsync();
        // Each order has its own disposable database, so an earlier scenario cannot supply a false duplicate proof.
        await Check(run, salesFirst: true);
        await using var reverse = await Run.StartAsync();
        await Check(reverse, salesFirst: false);

        static async Task Check(Run run, bool salesFirst)
        {
            var sales = LegalSalesOffer("650.00"); // 1300.00; the immutable CSV document is 1200.00.
            var csv = await LegalCsvAsync(run);
            if (salesFirst)
            {
                Assert.Equal(SalesFinanceAdmissionOutcomeV1.Admitted, (await run.AdmitLegalSalesAsync(sales)).Outcome);
                Assert.Equal(CommandOutcome.ValidationFailed, (await run.AdmitAsync(csv, "different-csv")).Outcome);
                await AssertSingleLiveLegalFamilyAsync(run, sales.InvoiceId, "650.00");
            }
            else
            {
                Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync(csv, "different-csv")).Outcome);
                Assert.Equal(SalesFinanceAdmissionOutcomeV1.Refused, (await run.AdmitLegalSalesAsync(sales)).Outcome);
                await AssertSingleLiveLegalFamilyAsync(run, csv.Batch.Rows[0].SourceDocumentId, "600.00");
            }
            Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems"));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
        }
    }

    [SqlServerFact]
    public async Task Legal_identity_cancelled_holder_advances_generation_once_and_original_admission_and_cancellation_replays_never_reopen_it()
    {
        // FNS-D19 already allows a next generation only after the prior holder has no not-cancelled item.
        // Both directions use the real Finance cancellation port, never UPDATE Status or seed a claim/reversal.
        await using var first = await Run.StartAsync();
        await Check(first, salesFirst: true);
        await using var second = await Run.StartAsync();
        await Check(second, salesFirst: false);

        static async Task Check(Run run, bool salesFirst)
        {
            var sales = LegalSalesOffer("600.00");
            var csv = await LegalCsvAsync(run);
            var cancelledId = salesFirst ? sales.InvoiceId : csv.Batch.Rows[0].SourceDocumentId;
            if (salesFirst)
                Assert.Equal(SalesFinanceAdmissionOutcomeV1.Admitted, (await run.AdmitLegalSalesAsync(sales)).Outcome);
            else
                Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync(csv, "cancel-csv")).Outcome);

            var cancellation = new FinanceInvoiceOpenItemCancellationV1(Guid.NewGuid(), FinanceInvoiceProducerV1.Sales,
                cancelledId, "invoice-v2", "SENTETIK confirmed external official cancellation");
            var cancelled = await run.CancelLegalAsync(cancellation);
            Assert.Equal(CommandOutcome.Succeeded, cancelled.Outcome);
            Assert.Equal(2, cancelled.OpenItemIds.Count);
            var cancelReplay = await run.CancelLegalAsync(cancellation);
            Assert.True(cancelReplay.IsReplay);
            Assert.Equal(cancelled.OpenItemIds, cancelReplay.OpenItemIds);

            if (salesFirst)
            {
                Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync(csv, "cancel-csv")).Outcome);
                Assert.Equal(SalesFinanceAdmissionOutcomeV1.Admitted, (await run.AdmitLegalSalesAsync(sales)).Outcome);
            }
            else
            {
                Assert.Equal(SalesFinanceAdmissionOutcomeV1.Admitted, (await run.AdmitLegalSalesAsync(sales)).Outcome);
                Assert.True((await run.AdmitAsync(csv, "cancel-csv")).IsReplay);
            }
            Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId=@id AND Status=3 AND OutstandingAmountText='0'", ("@id", cancelledId)));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.InvoiceCancellations"));
            Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.InvoiceCancellationItems"));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents"));
            await AssertSingleLiveLegalFamilyAsync(run, salesFirst ? csv.Batch.Rows[0].SourceDocumentId : sales.InvoiceId, "600.00");
            Assert.Equal("ettn:2|official-no:2", await run.ScalarAsync<string>(
                "SELECT STRING_AGG(CONCAT(IdentityKind, ':', Generation), '|') WITHIN GROUP (ORDER BY IdentityKind) "
                + "FROM finance.OfficialInvoiceIdentityClaims WHERE Generation=(SELECT MAX(Generation) FROM finance.OfficialInvoiceIdentityClaims)"));
            Assert.Equal(4, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceIdentityClaims"));
        }
    }

    private const string LegalActor = "SENTETIK-legal-finalizer";
    private static SalesFinanceAdmissionRequestV1 LegalSalesOffer(string amount) => new(Company, LegalActor,
        "SENTETIK-legal-sales-original", null, "sales-legal-original", Guid.NewGuid(), Guid.NewGuid(), "invoice-v2", Party, Account,
        Currency, null, [new(Guid.NewGuid(), Guid.NewGuid(), 1, new(2026, 11, 30), amount, null),
            new(Guid.NewGuid(), Guid.NewGuid(), 2, new(2026, 11, 30), amount, null)], "SNT2026000000001", EttnA);

    private static Task<Approved> LegalCsvAsync(Run run)
    {
        var document = Guid.NewGuid();
        return run.ApproveAsync(Guid.NewGuid(), Csv(Row(Guid.NewGuid(), document, 1, "SNT2026000000001", EttnA),
            Row(Guid.NewGuid(), document, 2, "SNT2026000000001", EttnA)));
    }

    private static async Task AssertSingleLiveLegalFamilyAsync(Run run, Guid id, string amount)
    {
        // A document has two installments; "once" means one source family, not suppressing its legitimate installments.
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(DISTINCT SourceId) FROM finance.OpenItems WHERE Status<>3"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId=@id AND Status<>3 AND OriginalAmountText=@amount AND OutstandingAmountText=@amount",
            ("@id", id), ("@amount", amount)));
    }

    private sealed partial class Run
    {
        public async Task<SalesFinanceAdmissionAnswerV1> AdmitLegalSalesAsync(SalesFinanceAdmissionRequestV1 request)
        {
            var company = CompanyScope();
            var collector = new AuditEventCollector();
            await using var db = Context(company, collector);
            var owner = new FinanceInvoiceOpenItemAdmissionService(db, company, new User(LegalActor), new ActiveParty(),
                new LegalAuthority(), collector, new FinanceAuditEnvelope("synthetic.finance.sales-finalize", [], "Integration"), TimeProvider.System);
            return await new SalesFinanceAdmissionAdapterV1(owner).AdmitAsync(request, default);
        }

        public async Task<FinanceInvoiceOpenItemCancellationResultV1> CancelLegalAsync(FinanceInvoiceOpenItemCancellationV1 request)
        {
            var company = CompanyScope();
            var collector = new AuditEventCollector();
            await using var db = Context(company, collector);
            var owner = new FinanceInvoiceOpenItemCancellationService(db, company, new User(LegalActor), new LegalAuthority(),
                collector, new FinanceAuditEnvelope("synthetic.finance.sales-cancel", [], "Integration"), TimeProvider.System);
            return await owner.CancelAsync(new(Company, LegalActor, "SENTETIK-legal-cancel", null), new("legal-cancel"), request, default);
        }
    }

    private sealed class LegalAuthority : IFinanceInvoiceOpenItemAdmissionAuthorityV1, IFinanceInvoiceOpenItemCancellationAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer, Guid invoiceId,
            CancellationToken token) => Task.FromResult(PermissionDecision.Allowed);
    }
}
