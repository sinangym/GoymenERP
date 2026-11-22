using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Work.Domain;
using GoymenERP.Modules.Work.Infrastructure.Collaboration;
using GoymenERP.Modules.Work.Infrastructure.Persistence;
using GoymenERP.Modules.Work.Infrastructure.Persistence.Migrations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace GoymenERP.Api.IntegrationTests;

// W1-C real-SQL evidence for the collaboration tables. Owner answers (record access, directory, authority) are SENTETIK test
// doubles: this is storage, constraint, atomicity and forward-fix evidence, not the production owner composition.
public sealed partial class WorkOutboxDispatchSqlServerRelationalAcceptanceTests
{
    private static readonly Guid CollaborationWriter = Guid.Parse("b1000000-0000-4000-8000-000000000001");
    private static readonly Guid CollaborationReader = Guid.Parse("b1000000-0000-4000-8000-000000000002");
    private static readonly Guid CollaborationRecord = Guid.Parse("b1000000-0000-4000-8000-0000000000aa");

    [SqlServerFact]
    public async Task Collaboration_Down_allows_an_empty_store_and_refuses_with_52090_while_a_note_exists()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var guard = Assert.IsType<SqlOperation>(new AddWorkCollaboration().DownOperations[0]).Sql;
            await ExecAsync(connectionString, guard);
            var service = CollaborationService(connectionString, WorkPermissionManifestV1.NotesWrite);
            await service.AddNoteAsync("sales-order-draft", CollaborationRecord, new("SENTETIK not", null, null), new("down-1", "corr-down", null));
            var refusal = await Assert.ThrowsAsync<SqlException>(() => ExecAsync(connectionString, guard));
            Assert.Equal(52090, refusal.Number);
            Assert.Equal(1, await CountAsync(connectionString, "RecordNotes", TestCompanyIds.CompanyA));
            Assert.Equal(1, await CountAsync(connectionString, "CollaborationAttempts", TestCompanyIds.CompanyA));
        });
    }

    [SqlServerFact]
    public async Task Collaboration_note_mention_notification_and_attempt_are_one_save_and_a_same_key_race_leaves_one_note()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var request = new AddWorkNoteRequestV1("SENTETIK @okur", [CollaborationReader], null);
            var first = CollaborationService(connectionString, WorkPermissionManifestV1.NotesWrite)
                .AddNoteAsync("sales-order-draft", CollaborationRecord, request, new("race-1", "corr-a", null));
            var second = CollaborationService(connectionString, WorkPermissionManifestV1.NotesWrite)
                .AddNoteAsync("sales-order-draft", CollaborationRecord, request, new("race-1", "corr-b", null));
            var answers = await Task.WhenAll(first, second);
            Assert.Single(answers.Select(answer => answer.Note.Id).Distinct());
            Assert.Equal(1, await CountAsync(connectionString, "RecordNotes", TestCompanyIds.CompanyA));
            Assert.Equal(1, await CountAsync(connectionString, "RecordNoteMentions", TestCompanyIds.CompanyA));
            Assert.Equal(1, await CountAsync(connectionString, "InboxNotifications", TestCompanyIds.CompanyA));
            Assert.Equal(1, await CountAsync(connectionString, "CollaborationAttempts", TestCompanyIds.CompanyA));
            Assert.Equal(0, await CountAsync(connectionString, "RecordNotes", TestCompanyIds.CompanyB));
            await using var other = Db(connectionString, TestCompanyIds.CompanyB);
            Assert.Empty(await other.RecordNotes.ToListAsync());
        });
    }

    [SqlServerFact]
    public async Task Collaboration_checks_dedup_key_and_one_step_task_state_refuse_bad_rows_in_real_SQL()
    {
        await WithDatabaseAsync(async connectionString =>
        {
            var company = TestCompanyIds.CompanyA;
            var notification = Guid.NewGuid();
            var source = Guid.NewGuid();
            await ExecAsync(connectionString, $"""
                INSERT INTO [work].[InboxNotifications] ([CompanyId],[Id],[RecipientUserId],[Kind],[Class],[SourceId],[RecordType],[RecordId],[ActorUserId],[RecordedAtUtc],[CorrelationId],[CausationId])
                VALUES ('{company}','{notification}','{CollaborationReader}',1,1,'{source}',1,'{CollaborationRecord}','{CollaborationWriter}',SYSUTCDATETIME(),'c',NULL);
                """);
            var duplicate = Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString, $"""
                INSERT INTO [work].[InboxNotifications] ([CompanyId],[Id],[RecipientUserId],[Kind],[Class],[SourceId],[RecordType],[RecordId],[ActorUserId],[RecordedAtUtc],[CorrelationId],[CausationId])
                VALUES ('{company}','{Guid.NewGuid()}','{CollaborationReader}',1,1,'{source}',1,'{CollaborationRecord}','{CollaborationWriter}',SYSUTCDATETIME(),'c',NULL);
                """)));
            Assert.Contains(duplicate.Number, new[] { 2601, 2627 });
            var wrongClass = Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString, $"""
                INSERT INTO [work].[InboxNotifications] ([CompanyId],[Id],[RecipientUserId],[Kind],[Class],[SourceId],[RecordType],[RecordId],[ActorUserId],[RecordedAtUtc],[CorrelationId],[CausationId])
                VALUES ('{company}','{Guid.NewGuid()}','{CollaborationReader}',1,2,'{Guid.NewGuid()}',1,'{CollaborationRecord}','{CollaborationWriter}',SYSUTCDATETIME(),'c',NULL);
                """)));
            Assert.Equal(547, wrongClass.Number);
            var closedWithoutStep = Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString, $"""
                INSERT INTO [work].[Tasks] ([CompanyId],[Id],[RecordType],[RecordId],[Title],[Details],[AssignerUserId],[AssigneeUserId],[DueAtUtc],[Status],[Version],[CreatedAtUtc],[ClosedAtUtc],[ClosedByUserId],[CancelReason])
                VALUES ('{company}','{Guid.NewGuid()}',NULL,NULL,'t',NULL,'{CollaborationWriter}','{CollaborationReader}',SYSUTCDATETIME(),2,1,SYSUTCDATETIME(),SYSUTCDATETIME(),'{CollaborationReader}',NULL);
                """)));
            Assert.Equal(547, closedWithoutStep.Number);
            var emptyNote = Sql(await Assert.ThrowsAnyAsync<Exception>(() => ExecAsync(connectionString, $"""
                INSERT INTO [work].[RecordNotes] ([CompanyId],[Id],[RecordType],[RecordId],[AuthorUserId],[Body],[CorrectsNoteId],[CreatedAtUtc],[CorrelationId],[CausationId])
                VALUES ('{company}','{Guid.NewGuid()}',1,'{CollaborationRecord}','{CollaborationWriter}','',NULL,SYSUTCDATETIME(),'c',NULL);
                """)));
            Assert.Equal(547, emptyNote.Number);
            Assert.Equal(1, await CountAsync(connectionString, "InboxNotifications", company));
            Assert.Equal(0, await CountAsync(connectionString, "Tasks", company));
        });
    }

    private static WorkCollaborationServiceV1 CollaborationService(string connectionString, string permission)
    {
        var company = new CompanyContext();
        company.Set(TestCompanyIds.CompanyA);
        var database = new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", WorkDbContext.Schema)).Options, company);
        return new WorkCollaborationServiceV1(database, company, new CollaborationUser(), new CollaborationAuthority(permission),
            new CollaborationRecords(), new CollaborationPeople(), new CollaborationAudit(), new CollaborationCorrelation(), TimeProvider.System);
    }

    private sealed class CollaborationUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public string? UserId => CollaborationWriter.ToString("D");
        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = new HashSet<Guid>([TestCompanyIds.CompanyA]);
        public bool CanAccessCompany(Guid companyId) => companyId == TestCompanyIds.CompanyA;
    }

    private sealed class CollaborationAuthority(string granted) : IErpAuthorityRevalidationService
    {
        public Task<PermissionScope> RevalidatePermissionScopeAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            Task.FromResult(permission == granted && companyId == TestCompanyIds.CompanyA
                ? new PermissionScope(true, true, new HashSet<string>(["SENTETIK_ROLE"]), new HashSet<Guid>(), new HashSet<Guid>(), new HashSet<Guid>())
                : PermissionScope.Denied);
    }

    private sealed class CollaborationRecords : IWorkRecordAccessPortV1
    {
        public Task<WorkRecordAccessOutcomeV1> CheckCurrentActorAsync(Guid companyId, WorkRecordRefV1 record, CancellationToken cancellationToken = default) =>
            Task.FromResult(record.RecordId == CollaborationRecord ? WorkRecordAccessOutcomeV1.Readable : WorkRecordAccessOutcomeV1.NotReadable);

        public Task<WorkRecordAccessOutcomeV1> CheckUserAsync(Guid companyId, WorkRecordRefV1 record, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(userId == CollaborationReader ? WorkRecordAccessOutcomeV1.Readable : WorkRecordAccessOutcomeV1.NotReadable);
    }

    private sealed class CollaborationPeople : IWorkPersonDirectoryV1
    {
        public Task<WorkPeopleAnswerV1> ResolveAsync(Guid companyId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkPeopleAnswerV1(WorkPersonOutcomeV1.Found, userIds.Select(id => new WorkPersonV1(id, "SENTETIK", true)).ToArray(), false));

        public Task<WorkPeopleAnswerV1> SearchAsync(Guid companyId, string? query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkPeopleAnswerV1(WorkPersonOutcomeV1.Found, [], false));

        public Task<WorkPersonOutcomeV1> IsActiveMemberAsync(Guid companyId, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(userId == CollaborationWriter || userId == CollaborationReader ? WorkPersonOutcomeV1.Found : WorkPersonOutcomeV1.Denied);
    }

    private sealed class CollaborationAudit : IAuditEventCollector
    {
        public void Enqueue(AuditEventDraft auditEvent) { }
    }

    private sealed class CollaborationCorrelation : ICorrelationContext
    {
        public bool IsResolved => true;
        public string CorrelationId => "corr-sql";
        public string? CausationId => null;
        public string SourceChannel => "api";
        public string? ClientIpAddress => null;
        public string? UserAgent => null;
        public void Set(string correlationId, string? causationId, string sourceChannel, string? clientIpAddress, string? userAgent) { }
    }
}
