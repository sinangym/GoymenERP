using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Approvals;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Bootstrap;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// ID-3 command skeleton on the in-memory provider: the permission fence, the metadata check, the request validation,
/// the expected version and the idempotent replay all answer BEFORE the company lock. The in-memory provider has no
/// transaction, so every test here would fail loudly if the path reached the locked body; the locked body (applock,
/// one save, last Admin, two-person rule under concurrency) is measured by the SqlServerFact suite. SENTETIK / NOT_ENV.
/// </summary>
public sealed class IdentityAccessAssignmentServiceTests
{
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CompanyB = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task An_unauthenticated_caller_is_refused_before_any_authority_is_asked()
    {
        var harness = new Harness(authenticated: false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => harness.Service().AssignAsync(ValidAssign(), harness.Metadata()));

        Assert.Empty(harness.Authorization.Asked);
        harness.AssertNothingRecordedOrStored();
    }

    [Fact]
    public async Task An_unresolved_company_context_is_refused_before_any_authority_is_asked()
    {
        var harness = new Harness(resolveCompany: false);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => harness.Service().AssignAsync(ValidAssign(), harness.Metadata()));

        Assert.Empty(harness.Authorization.Asked);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Without_the_company_wide_grant_the_command_is_forbidden_and_nothing_is_recorded(
        bool granted,
        bool companyWide)
    {
        var harness = new Harness();
        harness.Authorization.Scopes[IdentityPermissionManifestV1.RolesManage] = Scope(granted, companyWide);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => harness.Service().AssignAsync(ValidAssign(), harness.Metadata()));

        harness.AssertNothingRecordedOrStored();
    }

    [Fact]
    public async Task A_company_the_session_cannot_access_is_forbidden_without_asking_the_authority()
    {
        var harness = new Harness(sessionCompanies: [CompanyB]);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => harness.Service().AssignAsync(ValidAssign(), harness.Metadata()));

        Assert.Empty(harness.Authorization.Asked);
    }

    [Fact]
    public async Task Every_command_asks_its_manifest_name_in_the_context_company_only()
    {
        var harness = new Harness();
        var invalidGrant = new AssignIdentityRoleRequestV1(Guid.Empty, Guid.NewGuid(), null, null, null);
        var service = harness.Service();

        await service.AssignAsync(invalidGrant, harness.Metadata());
        await service.CreateGrantRequestAsync(
            new CreateAccessGrantRequestRequestV1(Guid.Empty, Guid.NewGuid(), null, null, null), harness.Metadata());
        await service.RevokeAsync(Guid.NewGuid(), new RevokeIdentityRoleAssignmentRequestV1("0", null), harness.Metadata());
        await service.ApproveAsync(Guid.NewGuid(), new ApproveAccessGrantRequestV1("x"), harness.Metadata());
        await service.RejectAsync(Guid.NewGuid(), new RejectAccessGrantRequestV1("x", "reason_code"), harness.Metadata());
        await service.WithdrawAsync(Guid.NewGuid(), new WithdrawAccessGrantRequestV1("x"), harness.Metadata());

        Assert.Equal(
            [
                (IdentityPermissionManifestV1.RolesManage, CompanyA),
                (IdentityPermissionManifestV1.RolesManage, CompanyA),
                (IdentityPermissionManifestV1.RolesManage, CompanyA),
                (IdentityPermissionManifestV1.AccessApprove, CompanyA),
                (IdentityPermissionManifestV1.AccessApprove, CompanyA),
                (IdentityPermissionManifestV1.RolesManage, CompanyA),
            ],
            harness.Authorization.Asked);
        Assert.Equal("identity.roles.manage", IdentityPermissionManifestV1.RolesManage);
        Assert.Equal("identity.access.approve", IdentityPermissionManifestV1.AccessApprove);
    }

    [Fact]
    public async Task A_metadata_envelope_of_another_company_or_actor_is_a_programming_fault()
    {
        var harness = new Harness();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service().AssignAsync(ValidAssign(), harness.Metadata() with { CompanyId = CompanyB }));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service().AssignAsync(
                ValidAssign(), harness.Metadata() with { ActorId = Guid.NewGuid().ToString("D") }));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service().AssignAsync(ValidAssign(), harness.Metadata() with { IdempotencyKey = " " }));

        harness.AssertNothingRecordedOrStored();
    }

    [Fact]
    public async Task An_invalid_grant_body_is_a_recorded_400_that_stores_no_answer_for_the_key()
    {
        var harness = new Harness();
        var request = new AssignIdentityRoleRequestV1(
            Guid.Empty, Guid.Empty, null, Now.AddDays(1), new string('x', AccessGrantRequest.ReasonMaxLength + 1));

        var result = await harness.Service().AssignAsync(request, harness.Metadata());

        Assert.Equal(400, result.Answer.StatusCode);
        Assert.Equal(IdentityAccessProblemCodesV1.RequestInvalid, result.Answer.Code);
        Assert.False(result.IsReplay);
        Assert.Equal(Harness.CorrelationId, result.OriginalCorrelationId);
        var attempt = Assert.Single(harness.RefusedAttempts.Standalone);
        Assert.Equal(RefusedAttemptClassesV1.Validation, attempt.RefusalClass);
        Assert.Equal(IdentityAccessCommandsV1.Assign, attempt.CommandName);
        Assert.Equal(IdentityPermissionManifestV1.RolesManage, attempt.PermissionCode);
        Assert.Equal(CompanyA, attempt.CompanyId);
        Assert.Equal(
            ["userId", "roleId", "validUntilUtc", "reason"],
            attempt.FieldErrors.Select(error => error.Field));
        Assert.Empty(harness.Database.CommandExecutions);
        Assert.Empty(harness.Revalidation.Asked);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData(" 1")]
    [InlineData("1.0")]
    [InlineData("99999999999999999999")]
    public async Task A_non_canonical_expected_version_is_a_recorded_400_on_expected_version(string version)
    {
        var harness = new Harness();

        var result = await harness.Service().RevokeAsync(
            Guid.NewGuid(), new RevokeIdentityRoleAssignmentRequestV1(version, null), harness.Metadata());

        Assert.Equal(400, result.Answer.StatusCode);
        Assert.Equal(IdentityAccessProblemCodesV1.RequestInvalid, result.Answer.Code);
        var attempt = Assert.Single(harness.RefusedAttempts.Standalone);
        Assert.Equal("expectedVersion", Assert.Single(attempt.FieldErrors).Field);
        Assert.Null(attempt.ExpectedVersion);
        Assert.Empty(harness.Database.CommandExecutions);
    }

    [Fact]
    public async Task A_non_canonical_reject_reason_code_is_a_recorded_400()
    {
        var harness = new Harness();

        var result = await harness.Service().RejectAsync(
            Guid.NewGuid(), new RejectAccessGrantRequestV1("1", "Serbest metin"), harness.Metadata());

        Assert.Equal(400, result.Answer.StatusCode);
        Assert.Equal(IdentityAccessProblemCodesV1.RequestInvalid, result.Answer.Code);
        Assert.Contains(Assert.Single(harness.RefusedAttempts.Standalone).FieldErrors, error => error.Field == "reasonCode");
    }

    [Fact]
    public async Task Without_the_configured_admin_role_a_revocation_fails_closed_and_stores_nothing()
    {
        var harness = new Harness(adminRoleCode: null);

        var result = await harness.Service().RevokeAsync(
            Guid.NewGuid(), new RevokeIdentityRoleAssignmentRequestV1("1", null), harness.Metadata());

        Assert.Equal(503, result.Answer.StatusCode);
        Assert.Null(result.Answer.Code);
        Assert.False(result.IsReplay);
        harness.AssertNothingRecordedOrStored();
    }

    [Fact]
    public async Task The_same_key_and_body_replays_the_stored_answer_with_the_original_correlation_after_a_fresh_fence()
    {
        var harness = new Harness();
        var request = ValidAssign();
        var stored = new IdentityAccessCommandAnswerV1(
            201, null, "Kaydedildi.", "Değişiklik kaydedildi.",
            new IdentityAccessCommandOutcomeV1(Guid.NewGuid(), "1", "Active", null));
        harness.StoreExecution(IdentityAccessCommandsV1.Assign, RefusedAttemptV1.NoTarget, request, stored, "Succeeded");

        var result = await harness.Service().AssignAsync(request, harness.Metadata("korelasyon-yeni"));

        Assert.True(result.IsReplay);
        Assert.Equal(Harness.CorrelationId, result.OriginalCorrelationId);
        Assert.Equal(stored, result.Answer);
        Assert.Equal([(IdentityPermissionManifestV1.RolesManage, CompanyA)], harness.Revalidation.Asked);
        Assert.Empty(harness.RefusedAttempts.Standalone);
        Assert.Empty(harness.RefusedAttempts.Replayed);
        Assert.Single(harness.Database.CommandExecutions);
    }

    [Fact]
    public async Task A_replayed_coded_refusal_is_only_counted_not_recorded_again()
    {
        var harness = new Harness();
        var request = ValidAssign();
        var stored = new IdentityAccessCommandAnswerV1(
            409, IdentityAccessProblemCodesV1.SecondApprovalRequired, "İkinci onay gerekli.", "Sentetik.");
        harness.StoreExecution(
            IdentityAccessCommandsV1.Assign, RefusedAttemptV1.NoTarget, request, stored,
            IdentityAccessProblemCodesV1.SecondApprovalRequired);

        var result = await harness.Service().AssignAsync(request, harness.Metadata());

        Assert.True(result.IsReplay);
        Assert.Equal(stored, result.Answer);
        Assert.Equal([RefusedAttemptClassesV1.BusinessRule], harness.RefusedAttempts.Replayed);
        Assert.Empty(harness.RefusedAttempts.Standalone);
    }

    [Fact]
    public async Task The_same_key_with_another_body_is_a_recorded_409_and_the_stored_answer_stays()
    {
        var harness = new Harness();
        var stored = new IdentityAccessCommandAnswerV1(201, null, "Kaydedildi.", "Değişiklik kaydedildi.");
        harness.StoreExecution(IdentityAccessCommandsV1.Assign, RefusedAttemptV1.NoTarget, ValidAssign(), stored, "Succeeded");

        var result = await harness.Service().AssignAsync(ValidAssign(), harness.Metadata());

        Assert.False(result.IsReplay);
        Assert.Equal(409, result.Answer.StatusCode);
        Assert.Equal(IdentityAccessProblemCodesV1.IdempotencyConflict, result.Answer.Code);
        Assert.Equal(RefusedAttemptClassesV1.Idempotency, Assert.Single(harness.RefusedAttempts.Standalone).RefusalClass);
        Assert.Single(harness.Database.CommandExecutions);
    }

    [Fact]
    public async Task The_same_key_and_body_from_another_actor_is_a_409_not_a_replay()
    {
        var harness = new Harness();
        var request = ValidAssign();
        var stored = new IdentityAccessCommandAnswerV1(201, null, "Kaydedildi.", "Değişiklik kaydedildi.");
        harness.StoreExecution(
            IdentityAccessCommandsV1.Assign, RefusedAttemptV1.NoTarget, request, stored, "Succeeded",
            actorId: Guid.NewGuid().ToString("D"));

        var result = await harness.Service().AssignAsync(request, harness.Metadata());

        Assert.False(result.IsReplay);
        Assert.Equal(IdentityAccessProblemCodesV1.IdempotencyConflict, result.Answer.Code);
    }

    [Fact]
    public async Task A_key_of_another_company_is_not_replayed_and_the_command_goes_on_to_the_lock()
    {
        var harness = new Harness();
        var request = ValidAssign();
        var stored = new IdentityAccessCommandAnswerV1(201, null, "Kaydedildi.", "Değişiklik kaydedildi.");
        harness.StoreExecution(
            IdentityAccessCommandsV1.Assign, RefusedAttemptV1.NoTarget, request, stored, "Succeeded", company: CompanyB);

        // The in-memory provider refuses the locked body's transaction: reaching it proves the other company's
        // stored answer was not taken as this key's answer.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => harness.Service().AssignAsync(request, harness.Metadata()));

        Assert.Empty(harness.Revalidation.Asked);
        Assert.Empty(harness.RefusedAttempts.Replayed);
        Assert.Empty(harness.RefusedAttempts.Standalone);
    }

    [Fact]
    public async Task Authority_lost_between_the_first_measure_and_the_replay_refuses_the_replay()
    {
        var harness = new Harness();
        var request = ValidAssign();
        var stored = new IdentityAccessCommandAnswerV1(201, null, "Kaydedildi.", "Değişiklik kaydedildi.");
        harness.StoreExecution(IdentityAccessCommandsV1.Assign, RefusedAttemptV1.NoTarget, request, stored, "Succeeded");
        harness.Revalidation.Scope = PermissionScope.Denied;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => harness.Service().AssignAsync(request, harness.Metadata()));
    }

    [Fact]
    public async Task The_pending_queue_is_nothing_without_approve_or_assignments_read_company_wide()
    {
        var harness = new Harness();
        harness.SeedQueue();
        harness.Authorization.Scopes.Clear();
        harness.Authorization.Scopes[IdentityPermissionManifestV1.RolesManage] = Scope(true, true);
        harness.Authorization.Scopes[IdentityPermissionManifestV1.AccessApprove] = Scope(true, false);

        Assert.Null(await harness.Service().GetPendingGrantRequestsAsync());
        Assert.Null(await new Harness(authenticated: false).Service().GetPendingGrantRequestsAsync());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task The_pending_queue_lists_only_the_context_companys_pending_requests_oldest_first(
        bool approve,
        bool read)
    {
        var harness = new Harness();
        var seeded = harness.SeedQueue();
        harness.Authorization.Scopes.Clear();
        if (approve)
        {
            harness.Authorization.Scopes[IdentityPermissionManifestV1.AccessApprove] = Scope(true, true);
        }

        if (read)
        {
            harness.Authorization.Scopes[IdentityPermissionManifestV1.AssignmentsRead] = Scope(true, true);
        }

        var queue = await harness.Service().GetPendingGrantRequestsAsync();

        Assert.NotNull(queue);
        Assert.Equal([seeded.Older, seeded.Newer], queue.Select(view => view.RequestId));
        var first = queue[0];
        Assert.Equal("Pending", first.Status);
        Assert.Equal("1", first.Version);
        Assert.Equal("Talep Eden", first.RequestedBy.DisplayName);
        Assert.Equal("Hedef Kişi", first.Target.DisplayName);
        Assert.Equal("KRITIK", first.RoleCode);
    }

    private static AssignIdentityRoleRequestV1 ValidAssign() =>
        new(Guid.NewGuid(), Guid.NewGuid(), null, null, "Sentetik gerekçe");

    private static PermissionScope Scope(bool granted, bool companyWide) => new(
        granted, companyWide, new HashSet<string>(StringComparer.Ordinal) { "PROBE" },
        companyWide ? new HashSet<Guid>() : new HashSet<Guid> { Guid.NewGuid() },
        new HashSet<Guid>(), new HashSet<Guid>());

    private sealed record QueueSeed(Guid Older, Guid Newer);

    private sealed class UnexpectedTransactionAuditWriter : IAuditTransactionWriterV1
    {
        public bool HasPendingDrafts => false;

        public Task WritePendingAsync(System.Data.IDbTransaction transaction, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("These in-memory gate tests must not enter a relational write.");

        public void DiscardPending() { }
    }

    private sealed class Harness
    {
        public const string CorrelationId = "korelasyon-ilk";
        public const string IdempotencyKey = "anahtar-0001";

        private readonly Guid actor = Guid.NewGuid();
        private readonly CompanyContext companyContext = new();
        private readonly TestUser user;
        private readonly string? adminRoleCode;

        public Harness(
            bool authenticated = true,
            bool resolveCompany = true,
            Guid[]? sessionCompanies = null,
            string? adminRoleCode = "ADMIN")
        {
            if (resolveCompany)
            {
                companyContext.Set(CompanyA);
            }

            user = new TestUser(actor.ToString("D"), authenticated, sessionCompanies ?? [CompanyA]);
            this.adminRoleCode = adminRoleCode;
            Database = new IdentityDbContext(
                new DbContextOptionsBuilder<IdentityDbContext>()
                    .UseInMemoryDatabase($"identity-access-assignment-{Guid.NewGuid():N}").Options);
            Authorization.Scopes[IdentityPermissionManifestV1.RolesManage] = Scope(true, true);
            Authorization.Scopes[IdentityPermissionManifestV1.AccessApprove] = Scope(true, true);
        }

        public IdentityDbContext Database { get; }

        public ProbeAuthorization Authorization { get; } = new();

        public ProbeRevalidation Revalidation { get; } = new();

        public RecordingRefusedAttempts RefusedAttempts { get; } = new();

        public RecordingAuditCollector Audits { get; } = new();

        public RecordingSecurityEventWriter SecurityEvents { get; } = new();

        public IdentityAccessAssignmentService Service() => new(
            Database,
            companyContext,
            user,
            Authorization,
            Revalidation,
            Audits,
            new UnexpectedTransactionAuditWriter(),
            RefusedAttempts,
            SecurityEvents,
            new ManualTimeProvider(Now),
            Options.Create(new IdentityBootstrapOptions { AdminRoleCode = adminRoleCode }),
            NullLogger<IdentityAccessAssignmentService>.Instance);

        public IdentityAccessCommandMetadataV1 Metadata(string correlationId = CorrelationId) => new(
            CompanyA,
            actor.ToString("D"),
            correlationId,
            null,
            "Web",
            null,
            null,
            IdempotencyKey);

        public void StoreExecution(
            string command,
            string target,
            object request,
            IdentityAccessCommandAnswerV1 answer,
            string outcome,
            string? actorId = null,
            Guid? company = null)
        {
            Database.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(
                Guid.NewGuid(),
                company ?? CompanyA,
                command,
                IdempotencyHash.ComputeKey(IdempotencyKey),
                IdempotencyHash.ComputePayload(new { Command = command, Target = target, Request = request }),
                actorId ?? actor.ToString("D"),
                outcome,
                answer.StatusCode,
                JsonSerializer.Serialize(answer, Web),
                CorrelationId,
                Now.AddMinutes(-5),
                1)));
            Database.SaveChanges();
            Database.ChangeTracker.Clear();
        }

        public QueueSeed SeedQueue()
        {
            var requester = new UserAccount(Guid.NewGuid(), "Talep Eden", null);
            var target = new UserAccount(Guid.NewGuid(), "Hedef Kişi", null);
            var role = new RoleDefinition(Guid.NewGuid(), "KRITIK", "Kritik rol", isSystem: false);
            role.AddPermission(ErpPermissions.IdentityRolesManage);
            Database.Users.AddRange(requester, target);
            Database.Roles.Add(role);
            var older = Request(requester.Id, target.Id, role.Id, CompanyA, Now.AddHours(-2));
            var newer = Request(requester.Id, target.Id, role.Id, CompanyA, Now.AddHours(-1));
            var decided = Request(requester.Id, target.Id, role.Id, CompanyA, Now.AddHours(-3));
            decided.Withdraw(requester.Id, Now.AddHours(-3));
            var otherCompany = Request(requester.Id, target.Id, role.Id, CompanyB, Now.AddHours(-4));
            Database.AccessGrantRequests.AddRange(newer, older, decided, otherCompany);
            Database.SaveChanges();
            Database.ChangeTracker.Clear();
            return new QueueSeed(older.Id, newer.Id);
        }

        public void AssertNothingRecordedOrStored()
        {
            Assert.Empty(RefusedAttempts.Standalone);
            Assert.Empty(RefusedAttempts.Recorded);
            Assert.Empty(Audits.Enqueued);
            Assert.Empty(SecurityEvents.Written);
            Assert.Empty(Database.CommandExecutions);
            Assert.Empty(Database.OutboxMessages);
        }

        private static AccessGrantRequest Request(Guid requester, Guid target, Guid role, Guid company, DateTimeOffset at) =>
            new(Guid.NewGuid(), requester, target, role, company, true, at, null, null, null);
    }

    private sealed class TestUser(string userId, bool authenticated, Guid[] companies) : ICurrentUser
    {
        public bool IsAuthenticated => authenticated;

        public string? UserId => authenticated ? userId : null;

        public IReadOnlySet<Guid> AllowedCompanyIds { get; } = companies.ToHashSet();

        public bool CanAccessCompany(Guid companyId) => authenticated && AllowedCompanyIds.Contains(companyId);
    }

    private sealed class ProbeAuthorization : IErpAuthorizationService
    {
        public Dictionary<string, PermissionScope> Scopes { get; } = new(StringComparer.Ordinal);

        public List<(string Permission, Guid Company)> Asked { get; } = [];

        public Task<AuthorizationSnapshot> GetAuthorizationSnapshotAsync(
            Guid companyId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(string permission, Guid companyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PermissionScope> GetPermissionScopeAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Asked.Add((permission, companyId));
            return Task.FromResult(Scopes.TryGetValue(permission, out var scope) ? scope : PermissionScope.Denied);
        }
    }

    private sealed class ProbeRevalidation : IErpAuthorityRevalidationService
    {
        public PermissionScope Scope { get; set; } = IdentityAccessAssignmentServiceTests.Scope(true, true);

        public List<(string Permission, Guid Company)> Asked { get; } = [];

        public Task<PermissionScope> RevalidatePermissionScopeAsync(
            string permission, Guid companyId, CancellationToken cancellationToken = default)
        {
            Asked.Add((permission, companyId));
            return Task.FromResult(Scope);
        }
    }

    private sealed class RecordingRefusedAttempts : IRefusedAttemptRecorderV1
    {
        public List<RefusedAttemptV1> Recorded { get; } = [];

        public List<RefusedAttemptV1> Standalone { get; } = [];

        public List<string> Replayed { get; } = [];

        public void Record(RefusedAttemptV1 attempt) => Recorded.Add(attempt);

        public Task<RefusedAttemptRecordResultV1> RecordStandaloneAsync(
            RefusedAttemptV1 attempt, CancellationToken cancellationToken = default)
        {
            Standalone.Add(attempt);
            return Task.FromResult(RefusedAttemptRecordResultV1.Written());
        }

        public void NoteReplayedRefusal(string refusalClass) => Replayed.Add(refusalClass);
    }

    private sealed class RecordingAuditCollector : IAuditEventCollector
    {
        public List<AuditEventDraft> Enqueued { get; } = [];

        public void Enqueue(AuditEventDraft auditEvent) => Enqueued.Add(auditEvent);
    }
}
