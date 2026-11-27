using System.Data;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.AiKnowledge.Domain;
using GoymenERP.Modules.AiKnowledge.Infrastructure;
using GoymenERP.Modules.AiKnowledge.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Application.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace GoymenERP.Api.IntegrationTests;

public sealed partial class AiKnowledgeSqlServerRelationalAcceptanceTests
{
    private static readonly DateTimeOffset AnswerAt = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private static AiAnswerReceiptV1 AnswerReceipt() => new(TestCompanyIds.CompanyA, Guid.NewGuid(), new string('a', 64),
        new string('b', 64), "Panel", "SENTETIK-model", "7", AnswerAt, AnswerAt.AddDays(90));
    private static AiAnswerReceiptIdentityV1 AnswerIdentity(AiAnswerReceiptV1 receipt) =>
        new(receipt.CompanyId, receipt.AnswerId, Approver.ToString("D"), "SENTETIK-answer-correlation");

    [SqlServerFact]
    public async Task Answer_receipt_and_separate_identity_survive_restart_and_a_collision_never_overwrites_either()
    {
        await WithDatabaseAsync(async connection =>
        {
            var receipt = AnswerReceipt(); var identity = AnswerIdentity(receipt);
            await using (var db = Db(connection, receipt.CompanyId))
                Assert.True(await AnswerStore(db, receipt.CompanyId).TryAddAsync(receipt, identity, _ => Task.CompletedTask));
            await using (var restarted = Db(connection, receipt.CompanyId))
            {
                var store = AnswerStore(restarted, receipt.CompanyId);
                Assert.Equal(receipt, await store.GetOwnAsync(receipt.CompanyId, receipt.AnswerId, identity.ActorId, _ => Task.CompletedTask));
                Assert.False(await store.TryAddAsync(receipt with { AnswerSha256 = new string('c', 64) }, identity, _ => Task.CompletedTask));
                Assert.Equal(receipt, await store.GetOwnAsync(receipt.CompanyId, receipt.AnswerId, identity.ActorId, _ => Task.CompletedTask));
                Assert.Single(await restarted.AnswerReceipts.ToArrayAsync()); Assert.Single(await restarted.AnswerReceiptIdentities.ToArrayAsync());
                Assert.Null(await store.GetOwnAsync(receipt.CompanyId, receipt.AnswerId, Guid.NewGuid().ToString("D"), _ => throw new InvalidOperationException("Must not read")));
                Assert.Null(await new AiAnswerReceiptPersistenceV1(restarted, AnswerCompany(receipt.CompanyId), new SqlUser(), new ReceiptAudit(),
                    new AnswerClock(receipt.RetainUntilUtc)).GetOwnAsync(receipt.CompanyId, receipt.AnswerId, identity.ActorId, _ => Task.CompletedTask));
            }
            await using var foreign = Db(connection, TestCompanyIds.CompanyB);
            Assert.Empty(await foreign.AnswerReceipts.ToArrayAsync()); Assert.Empty(await foreign.AnswerReceiptIdentities.ToArrayAsync());
            Assert.Null(await AnswerStore(foreign, TestCompanyIds.CompanyB).GetOwnAsync(receipt.CompanyId, receipt.AnswerId, identity.ActorId, _ => throw new InvalidOperationException("Must not read")));
        });
    }

    [SqlServerFact]
    public async Task Answer_receipt_actual_post_save_authority_loss_rolls_back_both_rows_and_same_id_can_then_execute()
    {
        await WithDatabaseAsync(async connection =>
        {
            var receipt = AnswerReceipt(); var company = AnswerCompany(receipt.CompanyId); var probe = new ReceiptSaveProbe();
            await using var db = new AiKnowledgeDbContext(new DbContextOptionsBuilder<AiKnowledgeDbContext>()
                .UseSqlServer(connection).AddInterceptors(probe).Options, company);
            var writer = new ReceiptAudit();
            var store = new AiAnswerReceiptPersistenceV1(db, company, new SqlUser(), writer, new AnswerClock(AnswerAt));
            await Assert.ThrowsAsync<AiAnswerReceiptAuthorityLostException>(() => store.TryAddAsync(receipt, AnswerIdentity(receipt),
                _ => probe.Saved ? throw new AiAnswerReceiptAuthorityLostException() : Task.CompletedTask));
            Assert.Equal(1, probe.Saves); Assert.Empty(db.ChangeTracker.Entries()); Assert.False(writer.Discarded);
            await using (var check = Db(connection, receipt.CompanyId))
            { Assert.Empty(await check.AnswerReceipts.ToArrayAsync()); Assert.Empty(await check.AnswerReceiptIdentities.ToArrayAsync()); }
            Assert.True(await store.TryAddAsync(receipt, AnswerIdentity(receipt), _ => Task.CompletedTask));
        });
    }

    [SqlServerFact]
    public async Task Answer_receipt_post_save_company_or_actor_drift_is_rejected_by_the_owner_even_when_the_consumer_callback_returns()
    {
        await WithDatabaseAsync(async connection =>
        {
            foreach (var boundary in new[] { "company", "actor" })
            {
                var receipt = AnswerReceipt(); var company = new AnswerMutableCompany(receipt.CompanyId);
                var actor = new AnswerMutableUser(); var probe = new ReceiptSaveProbe();
                await using var db = new AiKnowledgeDbContext(new DbContextOptionsBuilder<AiKnowledgeDbContext>()
                    .UseSqlServer(connection).AddInterceptors(probe).Options, company);
                var store = new AiAnswerReceiptPersistenceV1(db, company, actor, new ReceiptAudit(), new AnswerClock(AnswerAt));
                await Assert.ThrowsAsync<AiAnswerReceiptAuthorityLostException>(() => store.TryAddAsync(receipt, AnswerIdentity(receipt), _ =>
                {
                    if (probe.Saved)
                    {
                        if (boundary == "company") company.Set(TestCompanyIds.CompanyB);
                        else actor.ActorId = Guid.NewGuid();
                    }
                    return Task.CompletedTask;
                }));
                Assert.Equal(1, probe.Saves); Assert.Empty(db.ChangeTracker.Entries());
                await using var check = Db(connection, receipt.CompanyId);
                Assert.Empty(await check.AnswerReceipts.ToArrayAsync()); Assert.Empty(await check.AnswerReceiptIdentities.ToArrayAsync());
            }
        });
    }

    [SqlServerFact]
    public async Task Answer_receipt_owned_entry_refuses_borrowed_dirty_ambient_or_pending_audit_without_touching_the_caller()
    {
        await WithDatabaseAsync(async connection =>
        {
            foreach (var boundary in new[] { "borrowed", "dirty", "ambient", "audit" })
            {
                var receipt = AnswerReceipt(); var company = AnswerCompany(receipt.CompanyId); var audit = new ReceiptAudit { HasPendingDrafts = boundary == "audit" };
                await using var db = Db(connection, receipt.CompanyId);
                await using var borrowed = boundary == "borrowed" ? await db.Database.BeginTransactionAsync() : null;
                AiAnswerReceiptRow? caller = boundary == "dirty" ? new() { CompanyId = receipt.CompanyId, AnswerId = Guid.NewGuid() } : null;
                if (caller is not null) db.AnswerReceipts.Add(caller);
                using var ambient = boundary == "ambient" ? new TransactionScope(TransactionScopeAsyncFlowOption.Enabled) : null;
                var calls = 0;
                Assert.False(await new AiAnswerReceiptPersistenceV1(db, company, new SqlUser(), audit, new AnswerClock(AnswerAt))
                    .TryAddAsync(receipt, AnswerIdentity(receipt), _ => { calls++; return Task.CompletedTask; }));
                Assert.Equal(0, calls); Assert.Same(borrowed, db.Database.CurrentTransaction);
                Assert.Equal(boundary == "dirty", db.ChangeTracker.HasChanges());
                Assert.Equal(boundary == "audit", audit.HasPendingDrafts); Assert.False(audit.Discarded);
                if (caller is not null) Assert.Equal(EntityState.Added, db.Entry(caller).State);
            }
        });
    }

    [SqlServerFact]
    public async Task Answer_receipts_and_identity_bindings_are_immutable_and_sql_enforces_company_bound_identity_links()
    {
        await WithDatabaseAsync(async connection =>
        {
            var receipt = AnswerReceipt();
            await using var db = Db(connection, receipt.CompanyId);
            Assert.True(await AnswerStore(db, receipt.CompanyId).TryAddAsync(receipt, AnswerIdentity(receipt), _ => Task.CompletedTask));
            var row = await db.AnswerReceipts.SingleAsync(); row.AnswerSha256 = new string('d', 64);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            var identity = await db.AnswerReceiptIdentities.SingleAsync(); identity.ActorId = Guid.NewGuid().ToString("D");
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            var sql = $"INSERT INTO ai.AnswerReceiptIdentities(CompanyId,AnswerId,ActorId,CorrelationId) VALUES('{TestCompanyIds.CompanyB:D}','{receipt.AnswerId:D}','SENTETIK','SENTETIK')";
            Assert.Equal(547, Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connection, sql))).Number);
        });
    }

    private static ICompanyContext AnswerCompany(Guid id) { var company = new CompanyContext(); company.Set(id); return company; }
    private static AiAnswerReceiptPersistenceV1 AnswerStore(AiKnowledgeDbContext db, Guid company) =>
        new(db, AnswerCompany(company), new SqlUser(), new ReceiptAudit(), new AnswerClock(AnswerAt));
    private sealed class AnswerMutableCompany(Guid id) : ICompanyContext
    {
        public bool IsResolved => true;
        public Guid CompanyId { get; private set; } = id;
        public void Set(Guid companyId) => CompanyId = companyId;
    }
    private sealed class AnswerMutableUser : ICurrentUser
    {
        public Guid ActorId = Approver;
        public bool IsAuthenticated => true;
        public string? UserId => ActorId.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([TestCompanyIds.CompanyA]);
        public bool CanAccessCompany(Guid companyId) => companyId == TestCompanyIds.CompanyA;
    }
    private sealed class AnswerClock(DateTimeOffset time) : TimeProvider { public override DateTimeOffset GetUtcNow() => time; }
    private sealed class ReceiptAudit : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts { get; set; } public bool Discarded;
        public Task WritePendingAsync(IDbTransaction transaction, CancellationToken token = default) => throw new InvalidOperationException("No receipt audit drafts");
        public void DiscardPending() => Discarded = true;
    }
    private sealed class ReceiptSaveProbe : SaveChangesInterceptor
    {
        public bool Saved; public int Saves;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default)
        { Saves++; return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken token = default)
        { Saved = true; return ValueTask.FromResult(result); }
    }
}
