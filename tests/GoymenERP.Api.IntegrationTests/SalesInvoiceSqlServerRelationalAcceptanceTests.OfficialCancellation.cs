using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Sales.Application.Contracts;
using GoymenERP.Modules.Sales.Domain.Invoices;
using GoymenERP.Modules.Sales.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class SalesInvoiceSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Official_cancellation_Down_51919_passes_empty_and_preserves_fact_attempt_delivery_and_released_shares_when_used()
    {
        await using var run = await Run.StartAsync();
        var guard = Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(
            new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.AddSalesOfficialInvoiceCancellation().DownOperations[0]).Sql;
        // Empty-store DDL is genuinely executed, then rolled back so the original guards remain installed for this fact.
        await using (var connection = new SqlConnection(run.Factory.DatabaseConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await using var command = new SqlCommand(guard, connection, transaction);
            await command.ExecuteNonQueryAsync();
            await transaction.RollbackAsync();
        }
        var actor = Guid.NewGuid().ToString("D");
        var invoice = await FinalizedAsync(run, "official-down", "SENTETIK-OC-DOWN", Guid.NewGuid().ToString("D"), "SENTETIK-checker", actor);
        SalesOfficialInvoiceCancellationResultV1 result;
        await using (var scope = run.OfficialStore(CompanyA))
        {
            result = await scope.Store.RecordAsync(run.Envelope("official-down-key", actor: actor, permission: SalesPermissionManifestV1.InvoicesFinalize),
                Hash('f'), SalesOfficialInvoiceKindV1.Normal, invoice.Id, invoice.Version, "SENTETIK", OfficialProof(invoice), _ => Task.CompletedTask);
            await scope.Store.RecordDeliveryAsync(run.Envelope("official-down-delivery", actor: actor, permission: SalesPermissionManifestV1.InvoicesFinalize),
                Hash('b'), result.Cancellation.CancellationId, 0, false, new("Pending", "SENTETIK-pending"), _ => Task.CompletedTask);
        }
        var refused = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync(guard));
        Assert.Equal(51919, refused.Number);
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.OfficialInvoiceCancellations WHERE CancellationId=@id", ("@id", result.Cancellation.CancellationId)));
        Assert.Equal(2, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.OfficialInvoiceCancellationAttempts WHERE CancellationId=@id", ("@id", result.Cancellation.CancellationId)));
        Assert.Equal(1, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.OfficialInvoiceCancellationDeliveries WHERE CancellationId=@id", ("@id", result.Cancellation.CancellationId)));
        Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceLines WHERE InvoiceId=@id AND ReleasedAtUtc IS NULL", ("@id", invoice.Id)));
        var immutable = await Assert.ThrowsAsync<SqlException>(() => run.ExecuteAsync("UPDATE sales.OfficialInvoiceCancellationDeliveries SET ActorId=N'SENTETIK-tamper' WHERE CancellationId=@id", ("@id", result.Cancellation.CancellationId)));
        Assert.Equal(51918, immutable.Number);
    }

    [SqlServerFact]
    public async Task Official_cancel_fact_header_shares_audit_event_and_original_replay_commit_together_without_rewriting_finalization()
    {
        await using var run=await Run.StartAsync();var actor="aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";
        var invoice=await FinalizedAsync(run,"official-cancel","SENTETIK-OC-1",Guid.NewGuid().ToString("D"),"SENTETIK-checker",actor);
        var original=await run.ScalarAsync<string>("SELECT PlanJson FROM sales.InvoiceFinalizations WHERE InvoiceId=@id",("@id",invoice.Id));
        // Execute each generated direction independently, twice: Down is a safe forward repair,
        // so both repeated Up and repeated Down must preserve rows and the exact-actor boundary.
        var repair = new GoymenERP.Modules.Sales.Infrastructure.Persistence.Migrations.RepairSalesOfficialInvoiceCancellationActorCollation();
        foreach (var operation in repair.UpOperations.Concat(repair.UpOperations).Concat(repair.DownOperations).Concat(repair.DownOperations))
        {
            await run.ExecuteAsync(Assert.IsType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>(operation).Sql);
            foreach (var factActor in new[] { actor, actor.ToUpperInvariant() })
            {
                var headerActor = factActor == actor ? actor.ToUpperInvariant() : actor;
                var differentCaseActor = await Assert.ThrowsAsync<SqlException>(() => ProbeCancellationAsync(factActor, headerActor));
                Assert.Equal(51223, differentCaseActor.Number);
                // A blanket refusal or a broken trigger must not masquerade as an actor check.
                // The same SQL succeeds with the exact actor and releases shares in that transaction.
                Assert.Equal(1, await ProbeCancellationAsync(factActor, factActor));
                Assert.Equal(6, await run.ScalarAsync<int>("SELECT State FROM sales.Invoices WHERE CompanyId=@company AND Id=@id", ("@company", CompanyA), ("@id", invoice.Id)));
                Assert.Equal(invoice.Version, await run.ScalarAsync<long>("SELECT Version FROM sales.Invoices WHERE CompanyId=@company AND Id=@id", ("@company", CompanyA), ("@id", invoice.Id)));
                Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.OfficialInvoiceCancellations WHERE CompanyId=@company AND InvoiceId=@id", ("@company", CompanyA), ("@id", invoice.Id)));
                Assert.Equal(0, await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceLines WHERE CompanyId=@company AND InvoiceId=@id AND ReleasedAtUtc IS NOT NULL", ("@company", CompanyA), ("@id", invoice.Id)));
                Assert.Equal(original, await run.ScalarAsync<string>("SELECT PlanJson FROM sales.InvoiceFinalizations WHERE CompanyId=@company AND InvoiceId=@id", ("@company", CompanyA), ("@id", invoice.Id)));
            }
        }

        // Every probe rolls back both the successful and refused transition. The real store
        // workflow below must still commit once, preserving its original audit/outbox/replay checks.
        Task<int> ProbeCancellationAsync(string factActor, string headerActor) => run.ScalarAsync<int>("""
            DECLARE @proved int;
            BEGIN TRY
                BEGIN TRANSACTION;
                INSERT INTO sales.OfficialInvoiceCancellations
                    (CompanyId, CancellationId, InvoiceId, Kind, NormalInvoiceId, SubcontractInvoiceId,
                     FinalizedVersion, InvoiceVersion, AdmissionId, ActorId, RecordedAtUtc, CorrelationId, ViewJson, AdmissionJson)
                VALUES (@company, @cancellation, @invoice, 1, @invoice, NULL,
                        @version, @version + 1, @admission, @actor, @at, N'SENTETIK', N'{"reason":"SENTETIK"}', N'{}');
                UPDATE sales.Invoices SET State=7, Version=Version+1, CancelledBy=@headerActor,
                    CancelledAtUtc=@at, CancellationReason=N'SENTETIK'
                    WHERE CompanyId=@company AND Id=@invoice;
                SET @proved = CASE WHEN EXISTS
                    (SELECT 1 FROM sales.Invoices WHERE CompanyId=@company AND Id=@invoice AND State=7 AND Version=@version+1)
                    AND EXISTS (SELECT 1 FROM sales.InvoiceLines WHERE CompanyId=@company AND InvoiceId=@invoice)
                    AND NOT EXISTS
                    (SELECT 1 FROM sales.InvoiceLines WHERE CompanyId=@company AND InvoiceId=@invoice AND ReleasedAtUtc IS NULL)
                    THEN 1 ELSE 0 END;
                ROLLBACK TRANSACTION;
                SELECT @proved;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                THROW;
            END CATCH;
            """, ("@company", CompanyA), ("@cancellation", Guid.NewGuid()), ("@invoice", invoice.Id),
            ("@version", invoice.Version), ("@admission", Guid.NewGuid()), ("@actor", factActor),
            ("@headerActor", headerActor), ("@at", DateTimeOffset.UtcNow));
        var proof=OfficialProof(invoice);SalesOfficialInvoiceCancellationResultV1 first;
        await using(var scope=run.OfficialStore(CompanyA))
        {
            first=await scope.Store.RecordAsync(run.Envelope("official-key",actor:actor,permission:SalesPermissionManifestV1.InvoicesFinalize),Hash('a'),SalesOfficialInvoiceKindV1.Normal,invoice.Id,invoice.Version,"SENTETIK",proof,_=>Task.CompletedTask);
            Assert.False(first.IsReplay);Assert.Equal("Pending",first.Cancellation.FinanceStatus);
        }
        Assert.Equal(7,await run.ScalarAsync<int>("SELECT State FROM sales.Invoices WHERE Id=@id",("@id",invoice.Id)));
        Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceLines WHERE InvoiceId=@id AND ReleasedAtUtc IS NULL",("@id",invoice.Id)));
        Assert.Equal(original,await run.ScalarAsync<string>("SELECT PlanJson FROM sales.InvoiceFinalizations WHERE InvoiceId=@id",("@id",invoice.Id)));
        Assert.Equal(1,await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'sales.invoice.official-cancellation.recorded.v1'",("@id",invoice.Id.ToString("D"))));
        await using(var scope=run.OfficialStore(CompanyA))
        {
            var delivered=await scope.Store.RecordDeliveryAsync(run.Envelope("official-delivery",actor:actor,permission:SalesPermissionManifestV1.InvoicesFinalize),Hash('b'),first.Cancellation.CancellationId,0,false,new("Cancelled"),_=>Task.CompletedTask);
            Assert.Equal("Cancelled",delivered.Cancellation.FinanceStatus);
            var replay=await scope.Store.FindReplayAsync(run.Envelope("official-key",actor:actor,permission:SalesPermissionManifestV1.InvoicesFinalize),Hash('a'),SalesOfficialInvoiceCancellationCommandsV1.Record,_=>Task.CompletedTask);
            Assert.True(replay!.IsReplay);Assert.Equal(first.Cancellation,replay.Cancellation);Assert.Equal("Pending",replay.Cancellation.FinanceStatus);
            Assert.Equal("Cancelled",(await scope.Store.ReadDeliveryAsync(first.Cancellation.CancellationId,_=>Task.CompletedTask))!.Cancellation.FinanceStatus);
        }
        Assert.Equal(1,await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.OutboxMessages WHERE Id=@id AND EventType=N'sales.invoice.cancelled.v1'",("@id",first.Cancellation.CancellationId)));
        await using var foreign=run.OfficialStore(CompanyB);Assert.Null(await foreign.Store.ReadDeliveryAsync(first.Cancellation.CancellationId,_=>Task.CompletedTask));
    }
    [SqlServerFact]
    public async Task Official_cancel_audit_owner_failure_after_actual_append_rolls_back_fact_share_release_header_attempt_and_outbox()
    {
        await using var run=await Run.StartAsync();var actor=Guid.NewGuid().ToString("D");
        var invoice=await FinalizedAsync(run,"official-rollback","SENTETIK-OC-2",Guid.NewGuid().ToString("D"),"SENTETIK-checker",actor);
        await using(var scope=run.OfficialStore(CompanyA,writer=>new FailingOfficialAudit(writer)))
            await Assert.ThrowsAsync<InvalidOperationException>(()=>scope.Store.RecordAsync(run.Envelope("official-rollback-key",actor:actor,permission:SalesPermissionManifestV1.InvoicesFinalize),Hash('c'),SalesOfficialInvoiceKindV1.Normal,invoice.Id,invoice.Version,"SENTETIK",OfficialProof(invoice),_=>Task.CompletedTask));
        Assert.Equal(6,await run.ScalarAsync<int>("SELECT State FROM sales.Invoices WHERE Id=@id",("@id",invoice.Id)));
        Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.OfficialInvoiceCancellations WHERE InvoiceId=@id",("@id",invoice.Id)));
        Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.InvoiceLines WHERE InvoiceId=@id AND ReleasedAtUtc IS NOT NULL",("@id",invoice.Id)));
        Assert.Equal(0,await run.ScalarAsync<int>("SELECT COUNT(*) FROM audit.AuditEvents WHERE EntityId=@id AND EventType=N'sales.invoice.official-cancellation.recorded.v1'",("@id",invoice.Id.ToString("D"))));
        await using var retry=run.OfficialStore(CompanyA);Assert.Null(await retry.Store.FindReplayAsync(run.Envelope("official-rollback-key",actor:actor,permission:SalesPermissionManifestV1.InvoicesFinalize),Hash('c'),SalesOfficialInvoiceCancellationCommandsV1.Record,_=>Task.CompletedTask));
    }
    [SqlServerFact]
    public async Task Official_cancel_precommit_authority_loss_rolls_back_and_immutable_sql_history_cannot_be_changed()
    {
        await using var run=await Run.StartAsync();var actor=Guid.NewGuid().ToString("D");
        var invoice=await FinalizedAsync(run,"official-drift","SENTETIK-OC-3",Guid.NewGuid().ToString("D"),"SENTETIK-checker",actor);
        await using(var scope=run.OfficialStore(CompanyA))
            await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>scope.Store.RecordAsync(run.Envelope("official-drift-key",actor:actor,permission:SalesPermissionManifestV1.InvoicesFinalize),Hash('d'),SalesOfficialInvoiceKindV1.Normal,invoice.Id,invoice.Version,"SENTETIK",OfficialProof(invoice),_=>scope.Context.OutboxMessages.Local.Any()?throw new UnauthorizedAccessException():Task.CompletedTask));
        Assert.Equal(6,await run.ScalarAsync<int>("SELECT State FROM sales.Invoices WHERE Id=@id",("@id",invoice.Id)));
        await using(var scope=run.OfficialStore(CompanyA))await scope.Store.RecordAsync(run.Envelope("official-drift-key",actor:actor,permission:SalesPermissionManifestV1.InvoicesFinalize),Hash('d'),SalesOfficialInvoiceKindV1.Normal,invoice.Id,invoice.Version,"SENTETIK",OfficialProof(invoice),_=>Task.CompletedTask);
        foreach(var (table,code) in new[]{("OfficialInvoiceCancellations",51916),("OfficialInvoiceCancellationAttempts",51917)})
        {var exception=await Assert.ThrowsAsync<SqlException>(()=>run.ExecuteAsync($"UPDATE sales.[{table}] SET ActorId=N'SENTETIK-tamper' WHERE CompanyId=@company",("@company",CompanyA)));Assert.Equal(code,exception.Number);}
    }
    [SqlServerFact]
    public async Task Concurrent_official_cancellations_commit_one_fact_and_same_key_retry_reads_the_original_answer()
    {
        await using var run=await Run.StartAsync();var actor=Guid.NewGuid().ToString("D");
        var invoice=await FinalizedAsync(run,"official-race","SENTETIK-OC-4",Guid.NewGuid().ToString("D"),"SENTETIK-checker",actor);
        var proof=OfficialProof(invoice);var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<SalesOfficialInvoiceCancellationResultV1?> Send()
        {await start.Task;await using var scope=run.OfficialStore(CompanyA);try{return await scope.Store.RecordAsync(run.Envelope("official-race-key",actor:actor,permission:SalesPermissionManifestV1.InvoicesFinalize),Hash('e'),SalesOfficialInvoiceKindV1.Normal,invoice.Id,invoice.Version,"SENTETIK",proof,_=>Task.CompletedTask);}catch(SalesDraftStoreException ex)when(ex.Code==SalesDraftStoreCodes.ConcurrentChange){return null;}}
        var a=Send();var b=Send();start.SetResult();var answers=await Task.WhenAll(a,b).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Contains(answers,x=>x is not null);Assert.Equal(1,await run.ScalarAsync<int>("SELECT COUNT(*) FROM sales.OfficialInvoiceCancellations WHERE InvoiceId=@id",("@id",invoice.Id)));
        await using var again=run.OfficialStore(CompanyA);var replay=await again.Store.FindReplayAsync(run.Envelope("official-race-key",actor:actor,permission:SalesPermissionManifestV1.InvoicesFinalize),Hash('e'),SalesOfficialInvoiceCancellationCommandsV1.Record,_=>Task.CompletedTask);
        Assert.True(replay!.IsReplay);Assert.All(answers.Where(x=>x is not null),x=>Assert.Equal(x!.Cancellation,replay.Cancellation));
    }

    private static SalesOfficialInvoiceCancellationEvidenceV1 OfficialProof(SalesInvoice invoice)=>new(invoice.CompanyId,invoice.Id,invoice.Match!.OfficialNo,invoice.Match.Ettn,"SENTETIK-status","SENTETIK-record","1",new string('a',64),DateTimeOffset.UtcNow.AddSeconds(-1));
    private sealed class FailingOfficialAudit(IAuditTransactionWriterV1 actual):IAuditTransactionWriterV1
    {public bool HasPendingDrafts=>actual.HasPendingDrafts;public void DiscardPending()=>actual.DiscardPending();public async Task WritePendingAsync(System.Data.IDbTransaction tx,CancellationToken ct=default){await actual.WritePendingAsync(tx,ct);throw new InvalidOperationException("SENTETIK audit fault after actual append");}}
    private sealed partial class Run
    {
        public OfficialStoreScope OfficialStore(Guid companyId,Func<IAuditTransactionWriterV1,IAuditTransactionWriterV1>? wrap=null)
        {
            var scope=Factory.Services.CreateScope();var company=scope.ServiceProvider.GetRequiredService<ICompanyContext>();company.Set(companyId);
            var collector=scope.ServiceProvider.GetRequiredService<IAuditEventCollector>();var writer=scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var db=new SalesDbContext(new DbContextOptionsBuilder<SalesDbContext>().UseSqlServer(Factory.DatabaseConnectionString,
                sql=>sql.MigrationsHistoryTable("__EFMigrationsHistory",SalesDbContext.Schema)).Options,company);
            return new(db,new SalesStore(db,company,collector,TimeProvider.System,officialCancellationAuditWriter:wrap?.Invoke(writer)??writer),scope);
        }
    }
    private sealed class OfficialStoreScope(SalesDbContext db,SalesStore store,IServiceScope scope):IAsyncDisposable
    {public SalesDbContext Context=>db;public SalesStore Store=>store;public async ValueTask DisposeAsync(){await db.DisposeAsync();scope.Dispose();}}
}
