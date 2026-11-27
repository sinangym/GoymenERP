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
    // Regression of the adapter's previous dropped OfficialInvoiceNo/Ettn and upload-only claim parent.
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

    [SqlServerFact]
    public async Task Legal_identity_additive_proof_keeps_a_pre_extension_admission_key_hash_and_immutable_replay_without_backfilling_identity()
    {
        await using var run = await Run.StartAsync();
        var withProof = LegalSalesOffer("600.00");
        var legacy = withProof with { OfficialInvoiceNo = null, Ettn = null };
        Assert.Equal(SalesFinanceAdmissionOutcomeV1.Admitted, (await run.AdmitLegalSalesAsync(legacy)).Outcome);
        var before = await run.ScalarAsync<string>("SELECT PayloadHash FROM finance.OpenItemSourceBatches WHERE Id=@id", ("@id", legacy.AdmissionId));
        Assert.Equal(SalesFinanceAdmissionOutcomeV1.Admitted, (await run.AdmitLegalSalesAsync(withProof)).Outcome);
        Assert.Equal(before, await run.ScalarAsync<string>("SELECT PayloadHash FROM finance.OpenItemSourceBatches WHERE Id=@id", ("@id", legacy.AdmissionId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceIdentityClaims"));
        await AssertSingleLiveLegalFamilyAsync(run, legacy.InvoiceId, "600.00");
        // Replay does not retrofit claims from a later offer. Legacy preflight needs authoritative retained Sales proof.
    }

    [SqlServerFact]
    public async Task Legal_identity_foreign_company_and_authority_loss_after_claim_reads_never_store_claims_receipts_or_items()
    {
        await using var run = await Run.StartAsync();
        var sales = LegalSalesOffer("600.00");
        Assert.Equal(SalesFinanceAdmissionOutcomeV1.Refused,
            (await run.AdmitLegalSalesAsync(sales with { CompanyId = TestCompanyIds.CompanyB })).Outcome);
        var checks = 0;
        Assert.Equal(SalesFinanceAdmissionOutcomeV1.Refused, (await run.AdmitLegalSalesAsync(sales,
            new LegalAuthority(() => ++checks >= 4 ? PermissionDecision.Denied : PermissionDecision.Allowed))).Outcome);
        Assert.Equal(4, checks); // Initial fence, claim read, live-holder read, final pre-save fence.
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceIdentityClaims"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItems"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EventType=N'finance.invoice-open-items.admitted.v1'"));
        Assert.Equal(SalesFinanceAdmissionOutcomeV1.Admitted, (await run.AdmitLegalSalesAsync(sales)).Outcome);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceIdentityClaims"));
    }

    [SqlServerFact]
    public async Task Legal_identity_legacy_unclaimed_native_receipt_blocks_same_pair_but_unrelated_proven_pair_can_admit_without_backfill()
    {
        foreach (var samePair in new[] { true, false })
        {
            await using var run = await Run.StartAsync();
            var legacy = LegalSalesOffer("600.00") with { InvoiceVersion = "4", OfficialInvoiceNo = null, Ettn = null };
            Assert.Equal(SalesFinanceAdmissionOutcomeV1.Admitted, (await run.AdmitLegalSalesAsync(legacy)).Outcome);
            var csv = await LegalCsvAsync(run);
            // Controlled Sales owner proof tests the Finance consumer boundary. The separate native Sales SQL facts
            // exercise the real retained finalization+outbox reader; this is not an integrated cross-owner E2E claim.
            var proof = new FinanceSalesInvoiceIdentityV1(Company, legacy.InvoiceId, 4, legacy.AdmissionId, Party, Account,
                samePair ? "SNT2026000000001" : "SENTETIK-UNRELATED", samePair ? EttnA : Guid.NewGuid().ToString("D"),
                Guid.NewGuid(), new string('a', 64));
            var port = new LegalIdentityPort(new(Company, true, [proof]));
            var result = await run.AdmitAsync(csv, "legacy-csv", port);
            Assert.Equal(samePair ? CommandOutcome.ValidationFailed : CommandOutcome.Succeeded, result.Outcome);
            Assert.Equal(1, port.Calls);
            Assert.Equal(samePair ? 0 : 2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceIdentityClaims"));
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceIdentityClaims WHERE HolderBatchId=@id", ("@id", legacy.AdmissionId)));
            Assert.Equal(samePair ? 1 : 2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
        }
    }

    [SqlServerFact]
    public async Task Legal_identity_legacy_missing_foreign_or_wrong_version_owner_proof_refuses_only_affected_admission_without_mutation()
    {
        foreach (var fault in new[] { "missing", "foreign", "version", "admission", "party" })
        {
            await using var run = await Run.StartAsync();
            var legacy = LegalSalesOffer("600.00") with { InvoiceVersion = "4", OfficialInvoiceNo = null, Ettn = null };
            Assert.Equal(SalesFinanceAdmissionOutcomeV1.Admitted, (await run.AdmitLegalSalesAsync(legacy)).Outcome);
            var csv = await LegalCsvAsync(run);
            var proof = new FinanceSalesInvoiceIdentityV1(fault == "foreign" ? TestCompanyIds.CompanyB : Company,
                legacy.InvoiceId, fault == "version" ? 5 : 4, fault == "admission" ? Guid.NewGuid() : legacy.AdmissionId,
                fault == "party" ? Guid.NewGuid() : Party, Account, "SNT2026000000001", EttnA, Guid.NewGuid(), new string('a', 64));
            var port = new LegalIdentityPort(new(Company, fault != "missing", fault == "missing" ? [] : [proof]));
            Assert.Equal(CommandOutcome.Unavailable, (await run.AdmitAsync(csv, "legacy-csv", port)).Outcome);
            Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceIdentityClaims"));
            Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
            await AssertSingleLiveLegalFamilyAsync(run, legacy.InvoiceId, "600.00");
            Assert.Equal(SalesFinanceAdmissionOutcomeV1.Admitted, (await run.AdmitLegalSalesAsync(legacy)).Outcome);
        }
    }

    [SqlServerFact]
    public async Task Legal_identity_concurrent_native_and_CSV_sources_commit_at_most_one_live_family()
    {
        await using var run = await Run.StartAsync();
        var csv = await LegalCsvAsync(run);
        var sales = LegalSalesOffer("600.00");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<SalesFinanceAdmissionAnswerV1> Native() { await start.Task; return await run.AdmitLegalSalesAsync(sales); }
        async Task<FinanceOpenItemAdmissionResult> File() { await start.Task; return await run.AdmitAsync(csv, "legal-concurrent-csv"); }
        var native = Native(); var file = File(); start.SetResult();
        await Task.WhenAll(native, file);
        Assert.Equal(1, (native.Result.Outcome == SalesFinanceAdmissionOutcomeV1.Admitted ? 1 : 0)
            + (file.Result.Outcome == CommandOutcome.Succeeded ? 1 : 0));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(DISTINCT SourceId) FROM finance.OpenItems WHERE Status<>3"));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OfficialInvoiceIdentityClaims"));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.OpenItemSourceBatches"));
    }

    private sealed class LegalIdentityPort(FinanceSalesInvoiceIdentityBatchV1 answer) : IFinanceSalesInvoiceIdentityReadPortV1
    {
        public int Calls { get; private set; }
        public async Task<FinanceSalesInvoiceIdentityBatchV1> ReadAsync(Guid companyId,
            IReadOnlyCollection<FinanceSalesInvoiceIdentityRequestV1> invoices, Func<CancellationToken, Task> freshConsumerFence,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Assert.Equal(Company, companyId); Assert.All(invoices, x => Assert.Equal(4, x.OriginalFinalizedVersion));
            await freshConsumerFence(cancellationToken);
            return answer;
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
        public async Task<SalesFinanceAdmissionAnswerV1> AdmitLegalSalesAsync(SalesFinanceAdmissionRequestV1 request, LegalAuthority? authority = null)
        {
            var company = CompanyScope();
            var collector = new AuditEventCollector();
            await using var db = Context(company, collector);
            var owner = new FinanceInvoiceOpenItemAdmissionService(db, company, new User(LegalActor), new ActiveParty(),
                authority ?? new LegalAuthority(), collector, new FinanceAuditEnvelope("synthetic.finance.sales-finalize", [], "Integration"), TimeProvider.System);
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

    private sealed class LegalAuthority(Func<PermissionDecision>? answer = null) : IFinanceInvoiceOpenItemAdmissionAuthorityV1, IFinanceInvoiceOpenItemCancellationAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer, Guid invoiceId,
            CancellationToken token) => Task.FromResult(answer?.Invoke() ?? PermissionDecision.Allowed);
    }
}
