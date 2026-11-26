using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

// Existing native admission/cancellation fixture: no cancellation header, reversal, or zero projection is seeded.
public sealed partial class FinanceInvoiceOpenItemCancellationSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Native_official_invoice_reversal_has_its_own_closure_evidence_and_never_reports_settled()
    {
        await using var run = await Run.StartAsync();
        var invoice = Invoice(FinanceInvoiceProducerV1.Sales, Installment(1, "600.00"), Installment(2, "400.00"));
        Assert.Equal(CommandOutcome.Succeeded, (await run.AdmitAsync("closure-native", invoice)).Outcome);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Open, (await run.SettlementProofAsync(invoice.InvoiceId)).Status);
        var cancellation = Cancellation(invoice);
        Assert.Equal(CommandOutcome.ValidationFailed,
            (await run.CancelAsync("closure-invalid-version", cancellation with { InvoiceVersion = "other-version" })).Outcome);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Open, (await run.SettlementProofAsync(invoice.InvoiceId)).Status);
        Assert.Equal(CommandOutcome.Succeeded, (await run.CancelAsync("closure-native-cancel", cancellation)).Outcome);
        var proof = await run.SettlementProofAsync(invoice.InvoiceId);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.OfficiallyReversed, proof.Status);
        Assert.NotEqual(FinanceSalesInvoiceSettlementStatusV1.Settled, proof.Status);
        Assert.Equal(invoice.AdmissionId, proof.ScopeId); Assert.Equal(invoice.InvoiceVersion, proof.SourceVersion);
        Assert.True((await run.CancelAsync("closure-native-cancel", cancellation)).IsReplay);
        Assert.Equal(proof, await run.SettlementProofAsync(invoice.InvoiceId));
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Unknown, (await run.SettlementProofAsync(Guid.NewGuid())).Status);
    }

    private sealed partial class Run
    {
        public async Task<FinanceSalesInvoiceSettlementV1> SettlementProofAsync(Guid invoiceId)
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            await using var db = new FinanceDbContext(Options(company, null), company);
            var answer = await new FinanceSalesInvoiceSettlementReaderV1(db, company, new FixedCurrentUser(Actor, CompanyA))
                .ReadAsync(new(CompanyA, Actor, "SENTETIK-reversal-proof", null), [invoiceId], _ => Task.CompletedTask);
            Assert.Equal(ReadOutcome.Found, answer.Outcome);
            Assert.False(db.ChangeTracker.HasChanges());
            return Assert.Single(answer.Resource!.Invoices);
        }
    }
}

// Full real recorded receipt/allocation fixture reused from the second-shipment tests. The only synthetic inputs
// are account/bank/permissions/period owners; receipt, money event, allocations and official cancellation are native commands.
public sealed partial class FinanceRecordSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Native_confirmed_official_cancellation_releases_real_allocations_and_is_reversed_instead_of_settled()
    {
        await using var run = await Run.StartAsync();
        var paid = await PaidRiskInvoiceAsync(run);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Settled,
            (await run.OfficialReversalSettlementProofAsync(paid.Invoice.InvoiceId)).Status);
        var cancellation = new FinanceInvoiceOpenItemCancellationV1(Guid.NewGuid(), FinanceInvoiceProducerV1.Sales,
            paid.Invoice.InvoiceId, paid.Invoice.InvoiceVersion, "SENTETIK official cancellation");
        Assert.Equal(CommandOutcome.ValidationFailed, (await run.ReverseForSettlementAsync(cancellation)).Outcome);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.Settled,
            (await run.OfficialReversalSettlementProofAsync(paid.Invoice.InvoiceId)).Status);
        Assert.Equal(CommandOutcome.Succeeded,
            (await run.ReverseForSettlementAsync(cancellation with { ReleaseActiveAllocations = true })).Outcome);
        var proof = await run.OfficialReversalSettlementProofAsync(paid.Invoice.InvoiceId);
        Assert.Equal(FinanceSalesInvoiceSettlementStatusV1.OfficiallyReversed, proof.Status);
        Assert.Equal(paid.Invoice.AdmissionId, proof.ScopeId); Assert.Equal(paid.Invoice.InvoiceVersion, proof.SourceVersion);
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.Allocations WHERE SourceId = @id AND Status = 2",
            ("@id", paid.Receipt.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.MoneyEvents WHERE SourceId = @id", ("@id", paid.Receipt.Id)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM finance.SourceAllocationStates WHERE SourceId = @id AND UnallocatedAmountText = N'1000.00'",
            ("@id", paid.Receipt.Id)));
    }

    private sealed partial class Run
    {
        public async Task<FinanceSalesInvoiceSettlementV1> OfficialReversalSettlementProofAsync(Guid invoiceId)
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            await using var db = Context(CompanyA);
            var answer = await new FinanceSalesInvoiceSettlementReaderV1(db, company, new FixedCurrentUser(Actor, CompanyA))
                .ReadAsync(new(CompanyA, Actor, "SENTETIK-reversal-proof", null), [invoiceId], _ => Task.CompletedTask);
            Assert.Equal(ReadOutcome.Found, answer.Outcome);
            Assert.False(db.ChangeTracker.HasChanges());
            return Assert.Single(answer.Resource!.Invoices);
        }
        public async Task<FinanceInvoiceOpenItemCancellationResultV1> ReverseForSettlementAsync(FinanceInvoiceOpenItemCancellationV1 cancellation)
        {
            var company = new CompanyContext(); company.Set(CompanyA);
            var audit = new AuditEventCollector();
            await using var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
                .UseSqlServer(Factory.DatabaseConnectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", FinanceDbContext.Schema))
                .AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
            var owner = new FinanceInvoiceOpenItemCancellationService(db, company, new FixedCurrentUser(Actor, CompanyA),
                new SettlementCancellationAuthority(), audit, new("synthetic.settlement.invoice-reverse", ["SENTETIK-FINANCE"], "Web"), TimeProvider.System);
            return await owner.CancelAsync(new(CompanyA, Actor, "SENTETIK-native-reversal", null),
                new("SENTETIK-native-reversal"), cancellation, default);
        }
    }
    private sealed class SettlementCancellationAuthority : IFinanceInvoiceOpenItemCancellationAuthorityV1
    {
        public Task<PermissionDecision> CheckAsync(FinanceContext context, FinanceInvoiceProducerV1 producer,
            Guid invoiceId, CancellationToken cancellationToken) => Task.FromResult(PermissionDecision.Allowed);
    }
}
