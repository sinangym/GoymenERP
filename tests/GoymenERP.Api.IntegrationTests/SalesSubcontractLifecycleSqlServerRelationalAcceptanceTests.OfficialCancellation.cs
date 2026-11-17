using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace GoymenERP.Api.IntegrationTests;
public sealed partial class SalesSubcontractServiceInvoiceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Subcontract_official_cancellation_releases_actual_weighing_claim_retains_official_identity_and_original_immutable_finalization()
    {
        await using var run=await Run.StartAsync();var invoice=await CreateLifecycleAsync(run,Weighing,"official-fason-create");
        await MoveLifecycleAsync(run,invoice.Id,"official-fason-submit",SalesSubcontractInvoiceLifecycleCommandsV1.Submit,(x,n)=>x.Submit(1,"SENTETIK-submitter",n),"SENTETIK-submitter");
        await MoveLifecycleAsync(run,invoice.Id,"official-fason-approve",SalesSubcontractInvoiceLifecycleCommandsV1.Approve,(x,n)=>x.Approve(2,"SENTETIK-checker",n),"SENTETIK-checker");
        var official=LifecycleOfficial(invoice,Guid.NewGuid().ToString("D"));
        await MoveLifecycleAsync(run,invoice.Id,"official-fason-match",SalesSubcontractInvoiceLifecycleCommandsV1.Match,(x,n)=>x.RecordMatch(3,"SENTETIK-matcher",official,Currency,null,0.05m,false,null,n),"SENTETIK-matcher");
        var finalized=await MoveLifecycleAsync(run,invoice.Id,"official-fason-final",SalesSubcontractInvoiceLifecycleCommandsV1.Finalize,(x,n)=>x.Finalize(4,"SENTETIK-finalizer",Guid.NewGuid,n),"SENTETIK-finalizer");
        var quantityBefore = await ReadQuantityAsync();
        Assert.Equal(SalesOrderInvoicedQuantityReadOutcomeV1.Available, quantityBefore.Outcome);
        Assert.Equal(invoice.Lines.Select(line => (line.Input.SalesOrderLineId, line.Input.UnitId, line.Input.Quantity)),
            quantityBefore.Lines.Select(line => (line.SalesOrderLineId, line.UnitId, line.Quantity)));
        Assert.All(quantityBefore.Lines, line => Assert.Equal("Subcontract", line.InvoiceKind));
        var actor=Guid.NewGuid().ToString("D");var proof=new SalesOfficialInvoiceCancellationEvidenceV1(CompanyA,invoice.Id,official.OfficialNo,official.Ettn,"SENTETIK-status","SENTETIK-record","1",new string('a',64),DateTimeOffset.UtcNow.AddSeconds(-1));
        await using(var scope=run.OfficialFasonStore(CompanyA))
        {
            var answer=await scope.Store.RecordAsync(run.Envelope("official-fason-cancel",actor,SalesPermissionManifestV1.InvoicesFinalize),Hash('d'),SalesOfficialInvoiceKindV1.Subcontract,invoice.Id,5,"SENTETIK",proof,_=>Task.CompletedTask);
            Assert.Equal("6",answer.Cancellation.InvoiceVersion);
            var restored=(await scope.Store.FindSubcontractServiceInvoiceAsync(invoice.Id))!;
            Assert.Equal(SalesSubcontractServiceInvoiceState.OfficiallyCancelled,restored.State);
            Assert.Equal(finalized.Invoice.Finalization!.AdmissionId,restored.Finalization!.AdmissionId);
            Assert.Equal(finalized.Invoice.Finalization.Plan,restored.Finalization.Plan);
            Assert.Equal(invoice.Id,await scope.Store.FindOfficialClaimAsync(official.OfficialNo,official.Ettn));
        }
        Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.SubcontractServiceInvoices WHERE Id=@id AND State IN (1,2,3,6)",("@id",invoice.Id)));
        // Same actual order/weighing can be billed again only as a NEW invoice; old official identity remains owned.
        var replacement=await CreateLifecycleAsync(run,Weighing,"official-fason-replacement");Assert.NotEqual(invoice.Id,replacement.Id);
        var quantityAfter = await ReadQuantityAsync();
        Assert.Equal(SalesOrderInvoicedQuantityReadOutcomeV1.Available, quantityAfter.Outcome);
        Assert.Empty(quantityAfter.Lines); // Official cancellation and its new Draft replacement are not finalized quantities.
        await using var foreign=run.OfficialFasonStore(CompanyB);Assert.Null(await foreign.Store.ReadForInvoiceAsync(SalesOfficialInvoiceKindV1.Subcontract,invoice.Id,_=>Task.CompletedTask));

        async Task<SalesOrderInvoicedQuantityReadV1> ReadQuantityAsync()
        {
            await using var scope = run.Factory.Services.CreateAsyncScope();
            var company = scope.ServiceProvider.GetRequiredService<ICompanyContext>(); company.Set(CompanyA);
            return await scope.ServiceProvider.GetRequiredService<ISalesOrderInvoicedQuantityReadPortV1>()
                .ReadAsync(CompanyA, invoice.Source.SalesOrderId, _ => Task.CompletedTask);
        }
    }
    private sealed partial class Run
    {
        public OfficialFasonScope OfficialFasonStore(Guid companyId)
        {
            var scope=Factory.Services.CreateScope();var company=scope.ServiceProvider.GetRequiredService<ICompanyContext>();company.Set(companyId);
            var db=new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(Factory.DatabaseConnectionString,sql=>sql.MigrationsHistoryTable("__EFMigrationsHistory",SalesDbContext.Schema)).Options,company);
            return new(db,new SalesStore(db,company,scope.ServiceProvider.GetRequiredService<IAuditEventCollector>(),TimeProvider.System,new SyntheticCommitWeighingRead(),scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>()),scope);
        }
    }
    private sealed class OfficialFasonScope(SalesDbContext db,SalesStore store,IServiceScope scope):IAsyncDisposable
    {public SalesStore Store=>store;public async ValueTask DisposeAsync(){await db.DisposeAsync();scope.Dispose();}}
}
