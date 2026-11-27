using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AiKnowledgeSqlServerRelationalAcceptanceTests
{
    private static AiFeedbackAppendV1 FeedbackCommand(AiAnswerReceiptV1 answer, long version = 0,
        AiFeedbackKindV1 kind = AiFeedbackKindV1.Accepted, string? reason = null, string key = "SENTETIK-feedback") =>
        new(answer.CompanyId, answer.AnswerId, Approver, version, kind, reason, key, "SENTETIK-feedback-original");
    private static AiFeedbackPersistenceV1 FeedbackStore(AiKnowledgeDbContext db, Guid company, DateTimeOffset? at = null) =>
        new(db, AnswerCompany(company), new SqlUser(), new ReceiptAudit(), new AnswerClock(at ?? AnswerAt.AddHours(1)));
    private static async Task StoreFeedbackAnswer(string connection, AiAnswerReceiptV1 answer)
    {
        await using var db = Db(connection, answer.CompanyId);
        Assert.True(await new AiAnswerReceiptPersistenceV1(db, AnswerCompany(answer.CompanyId), new SqlUser(), new ReceiptAudit(),
            new AnswerClock(answer.CreatedAtUtc)).TryAddAsync(answer, AnswerIdentity(answer), _ => Task.CompletedTask));
    }
    [SqlServerFact]
    public async Task Feedback_chain_survives_restart_and_old_key_replays_original_version_and_correlation_without_double_writing()
    {
        await WithDatabaseAsync(async connection =>
        {
            var answer = AnswerReceipt(); await StoreFeedbackAnswer(connection, answer); var command = FeedbackCommand(answer);
            await using (var first = Db(connection, answer.CompanyId))
                Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await FeedbackStore(first, answer.CompanyId).AppendAsync(command, _ => Task.CompletedTask)).Status);
            await using var db = Db(connection, answer.CompanyId); var store = FeedbackStore(db, answer.CompanyId);
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await store.AppendAsync(FeedbackCommand(answer, 1, AiFeedbackKindV1.Rejected,
                AiFeedbackReasonsV1.IncorrectInformation, "SENTETIK-reject"), _ => Task.CompletedTask)).Status);
            var replay = await store.AppendAsync(command with { CorrelationId = "SENTETIK-retry" }, _ => Task.CompletedTask);
            Assert.Equal(AiFeedbackWriteStatusV1.Replayed, replay.Status); Assert.Equal(1, replay.Event!.Version);
            Assert.Equal(command.CorrelationId, replay.OriginalCorrelationId);
            Assert.Equal(AiFeedbackWriteStatusV1.IdempotencyConflict, (await store.AppendAsync(command with { ReasonCode = AiFeedbackReasonsV1.Other }, _ => Task.CompletedTask)).Status);
            Assert.Equal(AiFeedbackWriteStatusV1.VersionConflict, (await store.AppendAsync(command with { IdempotencyKey = "SENTETIK-new-stale" }, _ => Task.CompletedTask)).Status);
            var current = await store.GetOwnCurrentAsync(answer.CompanyId, answer.AnswerId, Approver, _ => Task.CompletedTask);
            Assert.Equal(2L, current!.Version); Assert.Equal(AiFeedbackKindV1.Rejected, current.Kind);
            Assert.Equal(2, await db.FeedbackEvents.CountAsync()); Assert.Equal(2, await db.FeedbackIdentities.CountAsync());
            Assert.Equal(1, await db.AnswerReceipts.CountAsync());
        });
    }
    [SqlServerFact]
    public async Task Feedback_same_version_concurrency_records_one_event_and_one_identity_without_mutation_retry()
    {
        await WithDatabaseAsync(async connection =>
        {
            var answer = AnswerReceipt(); await StoreFeedbackAnswer(connection, answer);
            await using var first = Db(connection, answer.CompanyId); await using var second = Db(connection, answer.CompanyId);
            var results = await Task.WhenAll(FeedbackStore(first, answer.CompanyId).AppendAsync(FeedbackCommand(answer), _ => Task.CompletedTask),
                FeedbackStore(second, answer.CompanyId).AppendAsync(FeedbackCommand(answer) with { IdempotencyKey = "SENTETIK-concurrent" }, _ => Task.CompletedTask));
            Assert.Single(results, result => result.Status == AiFeedbackWriteStatusV1.Recorded);
            Assert.Single(results, result => result.Status == AiFeedbackWriteStatusV1.VersionConflict);
            await using var check = Db(connection, answer.CompanyId);
            Assert.Equal(1, await check.FeedbackEvents.CountAsync()); Assert.Equal(1, await check.FeedbackIdentities.CountAsync());
        });
    }
    [SqlServerFact]
    public async Task Feedback_post_save_authority_loss_rolls_back_its_event_and_identity_preserving_the_answer_and_same_key_retry()
    {
        await WithDatabaseAsync(async connection =>
        {
            var answer = AnswerReceipt(); await StoreFeedbackAnswer(connection, answer);
            var probe = new ReceiptSaveProbe(); var company = AnswerCompany(answer.CompanyId); var audit = new ReceiptAudit();
            await using var db = new AiKnowledgeDbContext(new DbContextOptionsBuilder<AiKnowledgeDbContext>().UseSqlServer(connection).AddInterceptors(probe).Options, company);
            var store = new AiFeedbackPersistenceV1(db, company, new SqlUser(), audit, new AnswerClock(AnswerAt.AddHours(1)));
            await Assert.ThrowsAsync<AiFeedbackAuthorityLostException>(() => store.AppendAsync(FeedbackCommand(answer),
                _ => probe.Saved ? throw new AiFeedbackAuthorityLostException() : Task.CompletedTask));
            Assert.Equal(1, probe.Saves); Assert.Empty(db.ChangeTracker.Entries()); Assert.False(audit.Discarded);
            await using (var check = Db(connection, answer.CompanyId))
            { Assert.Empty(await check.FeedbackEvents.ToArrayAsync()); Assert.Empty(await check.FeedbackIdentities.ToArrayAsync()); Assert.Single(await check.AnswerReceipts.ToArrayAsync()); }
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await store.AppendAsync(FeedbackCommand(answer), _ => Task.CompletedTask)).Status);
        });
    }
    [SqlServerFact]
    public async Task Feedback_entry_refusal_preserves_borrowed_dirty_ambient_and_pending_audit_callers()
    {
        await WithDatabaseAsync(async connection =>
        {
            var answer = AnswerReceipt(); await StoreFeedbackAnswer(connection, answer);
            foreach (var boundary in new[] { "borrowed", "dirty", "ambient", "audit" })
            {
                await using var db = Db(connection, answer.CompanyId); var company = AnswerCompany(answer.CompanyId);
                var audit = new ReceiptAudit { HasPendingDrafts = boundary == "audit" };
                await using var borrowed = boundary == "borrowed" ? await db.Database.BeginTransactionAsync() : null;
                AiFeedbackEventRow? caller = boundary == "dirty" ? new() { CompanyId = answer.CompanyId, AnswerId = answer.AnswerId, Version = 1 } : null;
                if (caller is not null) db.FeedbackEvents.Add(caller);
                using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
                var calls = 0;
                var result = await new AiFeedbackPersistenceV1(db, company, new SqlUser(), audit, new AnswerClock(AnswerAt.AddHours(1)))
                    .AppendAsync(FeedbackCommand(answer), _ => { calls++; return Task.CompletedTask; });
                Assert.Equal(AiFeedbackWriteStatusV1.Unavailable, result.Status); Assert.Equal(0, calls);
                Assert.Same(borrowed, db.Database.CurrentTransaction); Assert.Equal(boundary == "dirty", db.ChangeTracker.HasChanges());
                Assert.Equal(boundary == "audit", audit.HasPendingDrafts); Assert.False(audit.Discarded);
                if (caller is not null) Assert.Equal(EntityState.Added, db.Entry(caller).State);
            }
        });
    }
    [SqlServerFact]
    public async Task Feedback_requires_the_actual_answer_creator_company_and_unexpired_receipt()
    {
        await WithDatabaseAsync(async connection =>
        {
            var answer = AnswerReceipt(); await StoreFeedbackAnswer(connection, answer);
            await using var db = Db(connection, answer.CompanyId);
            var actor = new AnswerMutableUser { ActorId = Guid.NewGuid() };
            var foreignActor = new AiFeedbackPersistenceV1(db, AnswerCompany(answer.CompanyId), actor, new ReceiptAudit(), new AnswerClock(AnswerAt.AddHours(1)));
            Assert.Equal(AiFeedbackWriteStatusV1.NotFound, (await foreignActor.AppendAsync(FeedbackCommand(answer) with { ActorId = actor.ActorId }, _ => Task.CompletedTask)).Status);
            Assert.Equal(AiFeedbackWriteStatusV1.NotFound, (await FeedbackStore(db, answer.CompanyId).AppendAsync(FeedbackCommand(answer) with { AnswerId = Guid.NewGuid() }, _ => Task.CompletedTask)).Status);
            Assert.Equal(AiFeedbackWriteStatusV1.NotFound, (await FeedbackStore(db, answer.CompanyId, answer.RetainUntilUtc).AppendAsync(FeedbackCommand(answer), _ => Task.CompletedTask)).Status);
            await using var foreign = Db(connection, TestCompanyIds.CompanyB);
            Assert.Equal(AiFeedbackWriteStatusV1.Refused, (await FeedbackStore(foreign, TestCompanyIds.CompanyB).AppendAsync(FeedbackCommand(answer), _ => Task.CompletedTask)).Status);
            Assert.Empty(await db.FeedbackEvents.ToArrayAsync()); Assert.Empty(await db.FeedbackIdentities.ToArrayAsync());
        });
    }
    [SqlServerFact]
    public async Task Feedback_month_counts_use_the_answer_cohort_and_last_category_at_the_month_fence_including_unreviewed()
    {
        await WithDatabaseAsync(async connection =>
        {
            var created = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
            var answers = Enumerable.Range(0, 3).Select(_ => AnswerReceipt() with { CreatedAtUtc = created, RetainUntilUtc = created.AddDays(90) }).ToArray();
            foreach (var answer in answers) await StoreFeedbackAnswer(connection, answer);
            await using var db = Db(connection, TestCompanyIds.CompanyA);
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await FeedbackStore(db, TestCompanyIds.CompanyA, created.AddDays(1)).AppendAsync(FeedbackCommand(answers[0]), _ => Task.CompletedTask)).Status);
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await FeedbackStore(db, TestCompanyIds.CompanyA, created.AddDays(2)).AppendAsync(FeedbackCommand(answers[1], 0, AiFeedbackKindV1.Corrected, AiFeedbackReasonsV1.IncompleteAnswer, "SENTETIK-correct"), _ => Task.CompletedTask)).Status);
            var december = new DateTimeOffset(2026, 12, 2, 9, 0, 0, TimeSpan.Zero);
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await FeedbackStore(db, TestCompanyIds.CompanyA, december).AppendAsync(FeedbackCommand(answers[0], 1, AiFeedbackKindV1.Reverted, AiFeedbackReasonsV1.Other, "SENTETIK-revert"), _ => Task.CompletedTask)).Status);
            var end = new DateTimeOffset(2026, 11, 30, 21, 0, 0, TimeSpan.Zero);
            var window = new AiFeedbackCountWindowV1(new(2026, 10, 31, 21, 0, 0, TimeSpan.Zero), end, end);
            var counts = Assert.Single(await FeedbackStore(db, TestCompanyIds.CompanyA, december).GetMonthCountsAsync(TestCompanyIds.CompanyA, Approver, window, _ => Task.CompletedTask));
            Assert.Equal((1, 1, 0, 0, 1, end), (counts.Accepted, counts.Corrected, counts.Rejected, counts.Reverted, counts.Unreviewed, counts.AsOfUtc));
            var decemberCreated = new DateTimeOffset(2026, 12, 1, 9, 0, 0, TimeSpan.Zero);
            var decemberAnswer = AnswerReceipt() with { CreatedAtUtc = decemberCreated, RetainUntilUtc = decemberCreated.AddDays(90) };
            await StoreFeedbackAnswer(connection, decemberAnswer);
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await FeedbackStore(db, TestCompanyIds.CompanyA, december).AppendAsync(
                FeedbackCommand(decemberAnswer) with { IdempotencyKey = "SENTETIK-december" }, _ => Task.CompletedTask)).Status);
            var currentWindow = new AiFeedbackCountWindowV1(end, new(2026, 12, 31, 21, 0, 0, TimeSpan.Zero), december);
            var current = Assert.Single(await FeedbackStore(db, TestCompanyIds.CompanyA, december).GetMonthCountsAsync(TestCompanyIds.CompanyA, Approver, currentWindow, _ => Task.CompletedTask));
            Assert.Equal((1, 0, 0, 0, 0), (current.Accepted, current.Corrected, current.Rejected, current.Reverted, current.Unreviewed));
            await Assert.ThrowsAsync<InvalidOperationException>(() => FeedbackStore(db, TestCompanyIds.CompanyA, december.AddDays(100))
                .GetMonthCountsAsync(TestCompanyIds.CompanyA, Approver, window, _ => Task.CompletedTask));
        });
    }
    [SqlServerFact]
    public async Task Feedback_rows_are_immutable_and_sql_rejects_a_missing_reason_or_broken_history_link()
    {
        await WithDatabaseAsync(async connection =>
        {
            var answer = AnswerReceipt(); await StoreFeedbackAnswer(connection, answer); await using var db = Db(connection, answer.CompanyId);
            Assert.Equal(AiFeedbackWriteStatusV1.Recorded, (await FeedbackStore(db, answer.CompanyId).AppendAsync(FeedbackCommand(answer), _ => Task.CompletedTask)).Status);
            var row = await db.FeedbackEvents.SingleAsync(); row.Kind = (int)AiFeedbackKindV1.Rejected;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            var identity = await db.FeedbackIdentities.SingleAsync(); identity.CorrelationId = "SENTETIK-changed";
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            var invalidReason = $"UPDATE ai.FeedbackEvents SET Kind=3,ReasonCode=NULL WHERE CompanyId='{answer.CompanyId:D}' AND AnswerId='{answer.AnswerId:D}'";
            Assert.Equal(547, Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connection, invalidReason))).Number);
            var invalidChain = $"INSERT INTO ai.FeedbackEvents(CompanyId,AnswerId,Version,PriorVersion,Kind,ReasonCode,CreatedAtUtc,RetainUntilUtc) SELECT CompanyId,AnswerId,2,NULL,1,NULL,CreatedAtUtc,RetainUntilUtc FROM ai.FeedbackEvents WHERE CompanyId='{answer.CompanyId:D}' AND AnswerId='{answer.AnswerId:D}' AND Version=1";
            Assert.Equal(547, Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connection, invalidChain))).Number);
            Assert.Equal(1, await db.FeedbackEvents.CountAsync()); Assert.Equal(1, await db.FeedbackIdentities.CountAsync());
        });
    }
}
