using System.Data.Common;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AiKnowledgeSqlServerRelationalAcceptanceTests
{
    [SqlServerFact]
    public async Task Feedback_reverted_requires_the_current_accepted_version_and_old_accepted_receipts_still_replay()
    {
        await WithDatabaseAsync(async connection =>
        {
            var answer = AnswerReceipt(); await StoreFeedbackAnswer(connection, answer);
            await using var db = Db(connection, answer.CompanyId); var store = FeedbackStore(db, answer.CompanyId);
            Assert.Equal(AiFeedbackWriteStatusV1.Invalid, (await store.AppendAsync(FeedbackCommand(answer, 0, AiFeedbackKindV1.Reverted,
                AiFeedbackReasonsV1.Other, "SENTETIK-no-prior"), _ => Task.CompletedTask)).Status);
            Assert.Empty(await db.FeedbackEvents.ToArrayAsync()); Assert.Empty(await db.FeedbackIdentities.ToArrayAsync());
            var firstReverted = $"INSERT INTO ai.FeedbackEvents(CompanyId,AnswerId,Version,PriorVersion,Kind,ReasonCode,CreatedAtUtc,RetainUntilUtc) SELECT CompanyId,AnswerId,1,NULL,4,'other',CreatedAtUtc,RetainUntilUtc FROM ai.AnswerReceipts WHERE CompanyId='{answer.CompanyId:D}' AND AnswerId='{answer.AnswerId:D}'";
            Assert.Equal(547, Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connection, firstReverted))).Number);
            var accepted = FeedbackCommand(answer);
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await store.AppendAsync(accepted, _ => Task.CompletedTask)).Status);
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await store.AppendAsync(FeedbackCommand(answer, 1, AiFeedbackKindV1.Rejected,
                AiFeedbackReasonsV1.Other, "SENTETIK-rejected"), _ => Task.CompletedTask)).Status);
            Assert.Equal(AiFeedbackWriteStatusV1.Invalid, (await store.AppendAsync(FeedbackCommand(answer, 2, AiFeedbackKindV1.Reverted,
                AiFeedbackReasonsV1.Other, "SENTETIK-revert-rejected"), _ => Task.CompletedTask)).Status);
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await store.AppendAsync(FeedbackCommand(answer, 2, AiFeedbackKindV1.Corrected,
                AiFeedbackReasonsV1.Other, "SENTETIK-corrected"), _ => Task.CompletedTask)).Status);
            Assert.Equal(AiFeedbackWriteStatusV1.Invalid, (await store.AppendAsync(FeedbackCommand(answer, 3, AiFeedbackKindV1.Reverted,
                AiFeedbackReasonsV1.Other, "SENTETIK-revert-corrected"), _ => Task.CompletedTask)).Status);
            Assert.Equal(3, await db.FeedbackEvents.CountAsync()); Assert.Equal(3, await db.FeedbackIdentities.CountAsync());
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await store.AppendAsync(FeedbackCommand(answer, 3, AiFeedbackKindV1.Accepted,
                null, "SENTETIK-accept-again"), _ => Task.CompletedTask)).Status);
            var reverted = await store.AppendAsync(FeedbackCommand(answer, 4, AiFeedbackKindV1.Reverted, AiFeedbackReasonsV1.Other,
                "SENTETIK-valid-revert"), _ => Task.CompletedTask);
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, reverted.Status); Assert.Equal(5, reverted.Event!.Version);
            var replay = await store.AppendAsync(accepted with { CorrelationId = "SENTETIK-later-retry" }, _ => Task.CompletedTask);
            Assert.Equal(AiFeedbackWriteStatusV1.Replayed, replay.Status); Assert.Equal(1, replay.Event!.Version);
            Assert.Equal(accepted.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(5, await db.FeedbackEvents.CountAsync()); Assert.Equal(5, await db.FeedbackIdentities.CountAsync());
        });
    }
    [SqlServerFact]
    public async Task Feedback_owned_post_save_audit_draft_is_discarded_after_rollback_and_does_not_poison_a_new_request()
    {
        await WithDatabaseAsync(async connection =>
        {
            var answer = AnswerReceipt(); await StoreFeedbackAnswer(connection, answer); var company = AnswerCompany(answer.CompanyId);
            using var services = FeedbackAuditServices(connection, company); using var scope = services.CreateScope();
            var collector = scope.ServiceProvider.GetRequiredService<AuditEventCollector>();
            var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
            var probe = new FeedbackOwnedAuditProbe(() => collector.Enqueue(FeedbackOwnedDraft(answer)));
            await using var db = new AiKnowledgeDbContext(new DbContextOptionsBuilder<AiKnowledgeDbContext>().UseSqlServer(connection).AddInterceptors(probe).Options, company);
            var store = new AiFeedbackPersistenceV1(db, company, new SqlUser(), writer, new AnswerClock(AnswerAt.AddHours(1)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendAsync(FeedbackCommand(answer), _ => Task.CompletedTask));
            Assert.Equal(1, probe.Saves); Assert.False(writer.HasPendingDrafts); Assert.Empty(db.ChangeTracker.Entries());
            await using (var check = Db(connection, answer.CompanyId))
            { Assert.Empty(await check.FeedbackEvents.ToArrayAsync()); Assert.Empty(await check.FeedbackIdentities.ToArrayAsync()); Assert.Single(await check.AnswerReceipts.ToArrayAsync()); }
            probe.Enabled = false;
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await store.AppendAsync(FeedbackCommand(answer), _ => Task.CompletedTask)).Status);
            Assert.False(writer.HasPendingDrafts);
        });
    }
    [SqlServerFact]
    public async Task Feedback_owned_rollback_or_dispose_fault_still_clears_its_audit_and_never_replays_or_retries_the_mutation()
    {
        await WithDatabaseAsync(async connection =>
        {
            foreach (var failure in new[] { (Rollback: true, Dispose: false), (Rollback: false, Dispose: true), (Rollback: true, Dispose: true) })
            {
                var answer = AnswerReceipt(); await StoreFeedbackAnswer(connection, answer); var company = AnswerCompany(answer.CompanyId);
                using var services = FeedbackAuditServices(connection, company); using var scope = services.CreateScope();
                var collector = scope.ServiceProvider.GetRequiredService<AuditEventCollector>();
                var writer = scope.ServiceProvider.GetRequiredService<IAuditTransactionWriterV1>();
                var fault = new FeedbackCleanupProbe(failure.Rollback, failure.Dispose);
                var save = new FeedbackOwnedAuditProbe(() => { collector.Enqueue(FeedbackOwnedDraft(answer)); fault.Armed = true; });
                var options = new DbContextOptionsBuilder<AiKnowledgeDbContext>().UseSqlServer(connection).AddInterceptors(save, fault);
                fault.Configure(options);
                await using var db = new AiKnowledgeDbContext(options.Options, company);
                var store = new AiFeedbackPersistenceV1(db, company, new SqlUser(), writer, new AnswerClock(AnswerAt.AddHours(1)));
                var error = await Assert.ThrowsAsync<AggregateException>(() => store.AppendAsync(FeedbackCommand(answer), _ => Task.CompletedTask));
                Assert.NotEmpty(error.InnerExceptions); Assert.Equal(1, fault.Starts); Assert.Equal(1, save.Saves);
                Assert.Equal(1, fault.RollbackAttempts); Assert.Equal(1, fault.DisposeEvents);
                Assert.Equal(failure.Rollback, fault.RollbackFailed); Assert.Equal(failure.Dispose, fault.DisposeFailed);
                Assert.False(writer.HasPendingDrafts); Assert.Empty(db.ChangeTracker.Entries());
                await using var check = Db(connection, answer.CompanyId);
                Assert.Empty(await check.FeedbackEvents.Where(row => row.AnswerId == answer.AnswerId).ToArrayAsync());
                Assert.Empty(await check.FeedbackIdentities.Where(row => row.AnswerId == answer.AnswerId).ToArrayAsync());
                Assert.NotNull(await check.AnswerReceipts.SingleOrDefaultAsync(row => row.AnswerId == answer.AnswerId));
            }
        });
    }
    private static ServiceProvider FeedbackAuditServices(string connection, ICompanyContext company)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DB_CONNECTION_STRING"] = connection }).Build();
        var services = new ServiceCollection(); services.AddScoped<ICompanyContext>(_ => company); services.AddAuditModule(configuration);
        return services.BuildServiceProvider();
    }
    private static AuditEventDraft FeedbackOwnedDraft(AiAnswerReceiptV1 answer) => new(answer.CompanyId, "ai.feedback.test.v1", "Ai.Feedback",
        answer.AnswerId.ToString("D"), "Record", 0, 1, "User", Approver.ToString("D"), ErpPermissions.AiPanelUse, [], "{}", "{}",
        "SENTETIK owned feedback fault", null, "None", null, "Web", null, null, "SENTETIK-correlation", null, "SENTETIK-owned-key");
    private sealed class FeedbackOwnedAuditProbe(Action stage) : SaveChangesInterceptor
    {
        public bool Enabled = true; public int Saves;
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { if (Enabled) { Saves++; stage(); } return ValueTask.FromResult(result); }
    }
    // Reuses the existing Inventory native fixture pattern: EF10 has rollback interception and a post-real-disposal diagnostic.
    private sealed class FeedbackCleanupProbe(bool failRollback, bool failDispose) : DbTransactionInterceptor
    {
        public bool Armed; public int Starts; public int RollbackAttempts; public int DisposeEvents; public bool RollbackFailed; public bool DisposeFailed;
        public void Configure(DbContextOptionsBuilder<AiKnowledgeDbContext> options) => options.LogTo((id, _) => id == RelationalEventId.TransactionDisposed, _ =>
        {
            if (!Armed) return; DisposeEvents++;
            if (failDispose && !DisposeFailed) { DisposeFailed = true; throw new InvalidOperationException("SENTETIK feedback disposal fault"); }
        });
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection, TransactionEndEventData data, DbTransaction result, CancellationToken token = default)
        { Starts++; return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData data, InterceptionResult result, CancellationToken token = default)
        {
            RollbackAttempts++;
            if (Armed && failRollback && !RollbackFailed) { RollbackFailed = true; throw new InvalidOperationException("SENTETIK feedback rollback fault"); }
            return ValueTask.FromResult(result);
        }
    }
}
