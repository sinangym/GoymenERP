using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Sales.Application.Contracts;
using Microsoft.EntityFrameworkCore;

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

// Reuses the existing real receipt/allocation/reversal fixture. Bank/account/permissions/period are its SENTETIK owner inputs;
// no receipt, money event, allocation or reversal is inserted by hand. This is Finance boundary evidence, not Logistics E2E.
public sealed partial class FinanceRecordSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Second_shipment_a_fully_allocated_invoice_remains_processed_even_with_zero_receivables_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var paid = await PaidRiskInvoiceAsync(run);
        var answer = await run.ProcessedRiskAsync(paid.Owners, paid.Invoice.InvoiceId);

        Assert.Equal(SalesPartyRiskProcessedSetStateV2.Available, answer.ProcessedSalesInvoices.State);
        Assert.Equal([paid.Invoice.InvoiceId], answer.ProcessedSalesInvoices.ProcessedSalesInvoiceIds);
        Assert.Equal(SalesPartyRiskFigureStateV1.Available, answer.OpenReceivables.State);
        Assert.Empty(answer.OpenReceivables.Amounts);
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND Status = 2 AND OutstandingAmountText = '0.00'",
            ("@id", paid.Invoice.InvoiceId)));
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.Allocations WHERE SourceId = @id AND Status = 1", ("@id", paid.Receipt.Id)));
        // Sales' existing consumer test feeds this exact AR=0 / processed-id shape and verifies it removes the shipment.
        // This SQL fact verifies that real settlement produces that shape; it does not execute Sales/Logistics.
    }

    [SqlServerFact]
    public async Task Second_shipment_allocation_release_and_approved_receipt_reversal_restore_debt_without_unprocessing_the_invoice_on_real_sql()
    {
        await using var run = await Run.StartAsync();
        var paid = await PaidRiskInvoiceAsync(run);
        foreach (var allocation in paid.Allocations)
        {
            var version = (await run.ScalarAsync<long>("SELECT Version FROM finance.OpenItems WHERE Id = @id",
                ("@id", allocation.OpenItemId))).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await using var owner = run.AllocationService(CompanyA, run.Actor, new SyntheticPermissions());
            var result = await owner.Service.ReleaseAsync(owner.Context, Key($"risk-release-{allocation.Id:N}"),
                new(allocation.Id, allocation.Version, version, "SENTETIK wrong allocation"), default);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            Assert.Equal(FinanceAllocationStatus.Released, result.Resource!.Status);
        }
        Assert.Equal(2, await run.ScalarAsync<int>(
            "SELECT COUNT(*) FROM finance.OpenItems WHERE SourceId = @id AND Status = 1 AND OutstandingAmountText = OriginalAmountText",
            ("@id", paid.Invoice.InvoiceId)));
        var released = await run.ProcessedRiskAsync(paid.Owners, paid.Invoice.InvoiceId);
        Assert.Equal([paid.Invoice.InvoiceId], released.ProcessedSalesInvoices.ProcessedSalesInvoiceIds);
        // SAT-D52: release is not a reversed payment. The now-unallocated recorded receipt still offsets the debt.
        Assert.Equal([new SalesPartyRiskAmountV1(Currency, 0m)], released.OpenReceivables.Amounts);

        ReversalProposalCommandResult proposed;
        await using (var maker = run.ReversalService(CompanyA, run.Actor))
        {
            proposed = await maker.Service.ProposeAsync(maker.Context, Key("risk-paid-reverse-propose"),
                new(paid.Receipt.Id, paid.Receipt.Version, OccurredOn.AddDays(2), "SENTETIK wrong receipt"), default);
            Assert.Equal(CommandOutcome.Succeeded, proposed.Outcome);
        }
        await using (var checker = run.ReversalService(CompanyA, Guid.NewGuid().ToString("D")))
        {
            Assert.Equal(CommandOutcome.Succeeded, (await checker.Service.ApproveAsync(checker.Context,
                Key("risk-paid-reverse-approve"), new(proposed.Resource!.Id, proposed.Resource.Version), default)).Outcome);
        }
        var reversed = await run.ProcessedRiskAsync(paid.Owners, paid.Invoice.InvoiceId);
        Assert.Equal(SalesPartyRiskProcessedSetStateV2.Available, reversed.ProcessedSalesInvoices.State);
        Assert.Equal([paid.Invoice.InvoiceId], reversed.ProcessedSalesInvoices.ProcessedSalesInvoiceIds);
        Assert.Equal(SalesPartyRiskFigureStateV1.Available, reversed.OpenReceivables.State);
        Assert.Equal([new SalesPartyRiskAmountV1(Currency, 1000m)], reversed.OpenReceivables.Amounts);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.InvoiceCancellations WHERE InvoiceId = @id",
            ("@id", paid.Invoice.InvoiceId))); // Receipt reversal is not official invoice reversal (FNS-D53).
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents WHERE SourceId = @id OR SourceId IN "
            + "(SELECT ReversalSourceId FROM finance.ReversalProposals WHERE OriginalSourceId = @id)", ("@id", paid.Receipt.Id)));
    }

    private sealed record PaidRiskInvoice(Owners Owners, FinanceInvoiceOpenItemAdmissionV1 Invoice, SavedDraft Receipt,
        IReadOnlyList<FinanceAllocationView> Allocations);

    private static async Task<PaidRiskInvoice> PaidRiskInvoiceAsync(Run run)
    {
        var owners = Owners.For(CompanyA);
        await run.SeedApprovedBankAccountAsync(owners);
        var invoice = new FinanceInvoiceOpenItemAdmissionV1(Guid.NewGuid(), FinanceInvoiceProducerV1.Sales,
            Guid.NewGuid(), "invoice-v1", owners.Party, owners.Account, Currency, null,
            [new(Guid.NewGuid(), Guid.NewGuid(), 1, OccurredOn.AddMonths(1), "600.00", null),
             new(Guid.NewGuid(), Guid.NewGuid(), 2, OccurredOn.AddMonths(2), "400.00", null)]);
        await run.AdmitRiskInvoiceAsync(owners, invoice);
        var draft = await run.SaveDraftAsync(owners, "1000.00");
        SavedDraft receipt;
        await using (var recording = run.Service(CompanyA, owners))
        {
            var recorded = await recording.Service.RecordAsync(recording.Context, Key("risk-paid-record"),
                new(draft.Id, draft.Version), default);
            Assert.Equal(CommandOutcome.Succeeded, recorded.Outcome);
            receipt = new(draft.Id, recorded.Resource!.Source.Version);
        }
        var allocations = new List<FinanceAllocationView>();
        foreach (var installment in invoice.Installments)
        {
            var version = (await run.ScalarAsync<long>("SELECT Version FROM finance.OpenItems WHERE Id = @id",
                ("@id", installment.OpenItemId))).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await using var allocation = run.AllocationService(CompanyA, run.Actor, new SyntheticPermissions());
            var result = await allocation.Service.CreateAsync(allocation.Context, Key($"risk-paid-allocate-{installment.Sequence}"),
                new(Guid.NewGuid(), receipt.Id, receipt.Version, installment.OpenItemId, version,
                    installment.Amount, "SENTETIK complete installment allocation"), default);
            Assert.Equal(CommandOutcome.Succeeded, result.Outcome);
            allocations.Add(result.Resource!);
        }
        return new(owners, invoice, receipt, allocations);
    }

    private sealed partial class Run
    {
        public async Task<SalesPartyRiskReadAnswerV2> ProcessedRiskAsync(Owners owners, Guid invoiceId)
        {
            var company = new CompanyContext(); company.Set(owners.Company);
            await using var db = Context(owners.Company);
            return await new FinanceSalesPartyRiskReadPortV1(db, company).ReadPartyRiskAsync(new(owners.Party, owners.Account, [invoiceId]));
        }

        public async Task AdmitRiskInvoiceAsync(Owners owners, FinanceInvoiceOpenItemAdmissionV1 invoice)
        {
            var company = new CompanyContext(); company.Set(owners.Company);
            var audit = new AuditEventCollector();
            await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
            var owner = new FinanceInvoiceOpenItemAdmissionService(db, company, new FixedCurrentUser(Actor, owners.Company),
                new PaidRiskInvoiceInputs(owners), new PaidRiskInvoiceInputs(owners), audit,
                new("synthetic.risk.invoice-admit", ["SENTETIK-FINANCE"], "Web"), TimeProvider.System);
            Assert.Equal(CommandOutcome.Succeeded, (await owner.AdmitAsync(new(owners.Company, Actor, $"risk-{Guid.NewGuid():N}", null),
                Key("risk-paid-admit"), invoice, default)).Outcome);
        }
    }

    private sealed class PaidRiskInvoiceInputs(Owners owners) : IFinancePartyAccountPort, IFinanceInvoiceOpenItemAdmissionAuthorityV1
    {
        public Task<ReadResult<PartyAccountEligibility>> GetAuthorizedAsync(FinanceContext context, Guid partyId,
            Guid companyAccountId, CancellationToken token) => Task.FromResult(
                context.CompanyId == owners.Company && partyId == owners.Party && companyAccountId == owners.Account
                    ? new ReadResult<PartyAccountEligibility>(ReadOutcome.Found,
                        new(owners.Party, owners.Account, owners.Company, true, IsPartyActive: true))
                    : new ReadResult<PartyAccountEligibility>(ReadOutcome.NotFound, null));
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer,
            Guid invoiceId, CancellationToken token) => Task.FromResult(PermissionDecision.Allowed);
    }
}
