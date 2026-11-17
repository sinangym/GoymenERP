using GoymenERP.Api.Endpoints;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain;
using GoymenERP.Modules.Sales.Domain.Drafts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class LogisticsInstructionSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Sales_invoice_commit_rereads_real_Logistics_weighing_and_rolls_back_a_stale_source_before_same_key_retry()
    {
        await using var run = await Run.StartAsync();
        var owners = Owners.For(CompanyA);
        Guid instructionId;
        var weighingId = Guid.NewGuid();
        Guid orderLineId;
        Guid productId;
        await using (var scope = run.Store(CompanyA))
        {
            var created = await scope.Store.StoreInstructionAsync(run.Envelope("invoice-guard-create"), Hash("invoice-guard-create"),
                Day, () => Instruction(owners));
            instructionId = created.Instruction.Instruction.Id;
            var line = Assert.Single(created.Instruction.Instruction.Lines);
            orderLineId = line.Source.LineId;
            productId = line.ProductId;
            await scope.Store.MutateInstructionAsync(run.Envelope("invoice-guard-issue"), Hash("invoice-guard-issue"), LogisticsCommands.IssueV1,
                instructionId, (value, now) => value.Issue(2, now), Issued);
            await scope.Store.MutateInstructionAsync(run.Envelope("invoice-guard-load"), Hash("invoice-guard-load"), LogisticsCommands.RecordLoadV1,
                instructionId, (value, now) => value.RecordLoad(3, Guid.NewGuid(), line.Id, Kg(4), MeasurementKind.Measured, now), Loaded);
            await scope.Store.MutateInstructionAsync(run.Envelope("invoice-guard-weigh"), Hash("invoice-guard-weigh"), LogisticsCommands.RecordWeighingV1,
                instructionId, (value, now) => value.RecordWeighing(4, weighingId, WeighingDirection.TareFirst, Kg(100), Kg(104), run.Actor, now), Weighed);
        }
        var company = new CompanyContext(); company.Set(CompanyA);
        var audit = new AuditEventCollector();
        await using var context = new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>()
            .UseSqlServer(run.Factory.DatabaseConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company)).Options, company);
        var store = new SalesStore(context, company, audit, TimeProvider.System, new SalesSubcontractWeighingCommitReadHost());
        var envelope = new SalesDraftWriteEnvelope(run.Actor, SalesPermissionManifestV1.InvoicesWrite, [], run.Correlation,
            null, "invoice-guard-key", "Web", null, null);
        SalesSubcontractServiceInvoice Build(string number, long instructionVersion)
        {
            // Tax/price inputs are synthetic: this fact proves real Logistics-to-Sales source fencing, not tax acceptance.
            var tax = new SalesLineTaxSnapshot(productId, Day, "Normal", "Service", "Resolved", null, "Record",
                Guid.NewGuid(), 1, "Resolved", 20m, "NotRequired", null, null, "NotApplied", null, "NotRequired", null, null, null, 0, "Normal", false);
            return SalesSubcontractServiceInvoice.Create(Guid.NewGuid(), CompanyA, number,
                new(owners.Party, owners.Account, Guid.NewGuid(), Guid.NewGuid(), "1", 2, Day),
                new(owners.Order, instructionId, weighingId, 1, instructionVersion, "4"),
                [new(Guid.NewGuid(), orderLineId, 1, productId, KgUnit, 4m, "4", new CommercialDraftFields("2.50", KgUnit, null), tax)],
                4m, run.Actor, TimeProvider.System.GetUtcNow());
        }
        var stale = await Assert.ThrowsAsync<SalesDraftStoreException>(() => store.StoreSubcontractServiceInvoiceAsync(
            envelope, Hash("invoice-guard-content"), number => Build(number, 4)));
        Assert.Equal(SalesDraftStoreCodes.ConcurrentChange, stale.Code);
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractServiceInvoices"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractServiceInvoiceAttempts"));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityType=N'Sales.SubcontractServiceInvoice'"));
        var retry = await store.StoreSubcontractServiceInvoiceAsync(envelope, Hash("invoice-guard-content"), number => Build(number, 5));
        Assert.Equal(SalesDraftWriteOutcome.Recorded, retry.Outcome);
        Assert.Equal((1L, 5L, "4"), ((long)retry.Invoice.Source.WeighingRevision, retry.Invoice.Source.InstructionVersion, retry.Invoice.Source.NetKgText));
        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using var claim = await SalesSubcontractInvoiceClaimReadSession.EnlistAsync(connection, transaction, company);
        var active = await claim.LockAndReadAsync(instructionId, weighingId);
        Assert.True(active.HasActiveInvoice);
        Assert.Equal(retry.Invoice.Id, Assert.Single(active.ActiveInvoiceIds!));
        await transaction.RollbackAsync();
    }
}
