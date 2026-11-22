using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Bootstrap;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Bootstrap;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// ID-2 founder bootstrap (KIM-D5, KIM-D18): the refusal order and the single write of the user, the credential with
/// <c>MustChangePassword</c>, the local identity link, one indefinite Admin assignment per company, the marker and the
/// <c>identity.founder.bootstrapped.v1</c> outbox message. EF InMemory does not roll a transaction back, so the
/// rollback of a refused security event and the concurrent second run are proven by the SQL tests only. SENTETIK.
/// </summary>
public sealed class FounderBootstrapServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_redirected_stdin_is_refused_before_anything_is_read()
    {
        using var harness = new Harness();
        harness.Console.IsInteractive = false;

        var result = await harness.RunAsync();

        Assert.Equal(FounderBootstrapStatus.NotInteractive, result.Status);
        Assert.Equal(0, harness.Console.Reads);
        harness.AssertNothingWritten();
    }

    [Fact]
    public async Task A_missing_admin_role_code_is_refused()
    {
        using var harness = new Harness(adminRoleCode: "  ");

        var result = await harness.RunAsync();

        Assert.Equal(FounderBootstrapStatus.AdminRoleNotConfigured, result.Status);
        Assert.Equal(0, harness.Console.Reads);
        harness.AssertNothingWritten();
    }

    [Fact]
    public async Task An_existing_marker_refuses_before_the_input_is_read()
    {
        using var harness = new Harness();
        harness.Context.BootstrapStates.Add(new IdentityBootstrapState(Guid.NewGuid(), Now.AddDays(-1)));
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        var result = await harness.RunAsync();

        Assert.Equal(FounderBootstrapStatus.AlreadyBootstrapped, result.Status);
        Assert.Equal(0, harness.Console.Reads);
        Assert.Empty(await harness.Context.Users.ToListAsync());
    }

    [Fact]
    public async Task A_role_without_a_critical_permission_or_an_unknown_role_is_refused()
    {
        Assert.False(ErpPermissions.IsCritical(ErpPermissions.OrganizationLocationsRead));
        using var harness = new Harness(adminPermission: ErpPermissions.OrganizationLocationsRead);

        Assert.Equal(FounderBootstrapStatus.AdminRoleInvalid, (await harness.RunAsync()).Status);

        using var unknown = new Harness(adminRoleCode: "BASKA-ROL");
        Assert.Equal(FounderBootstrapStatus.AdminRoleInvalid, (await unknown.RunAsync()).Status);
        Assert.Equal(0, harness.Console.Reads + unknown.Console.Reads);
        harness.AssertNothingWritten();
    }

    [Fact]
    public async Task No_company_refuses()
    {
        using var harness = new Harness(companyCount: 0);

        var result = await harness.RunAsync();

        Assert.Equal(FounderBootstrapStatus.NoCompanies, result.Status);
        Assert.Equal(0, harness.Console.Reads);
        harness.AssertNothingWritten();
    }

    [Fact]
    public async Task An_unexpired_admin_assignment_refuses_and_an_expired_one_does_not()
    {
        using var harness = new Harness();
        var assignment = new RoleAssignment(
            Guid.NewGuid(), Guid.NewGuid(), harness.AdminRoleId, harness.CompanyIds[0], Now.AddDays(-2), null, null);
        assignment.AddScope(AccessScopeType.Company, harness.CompanyIds[0]);
        harness.Context.RoleAssignments.Add(assignment);
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();

        var result = await harness.RunAsync();

        Assert.Equal(FounderBootstrapStatus.AdminAlreadyAssigned, result.Status);
        Assert.Equal(0, harness.Console.Reads);

        using var expired = new Harness();
        var old = new RoleAssignment(
            Guid.NewGuid(), Guid.NewGuid(), expired.AdminRoleId, expired.CompanyIds[0], Now.AddDays(-2), Now.AddDays(-1), null);
        old.AddScope(AccessScopeType.Company, expired.CompanyIds[0]);
        expired.Context.RoleAssignments.Add(old);
        await expired.Context.SaveChangesAsync();
        expired.Context.ChangeTracker.Clear();

        Assert.Equal(FounderBootstrapStatus.Completed, (await expired.RunAsync()).Status);
    }

    [Fact]
    public async Task An_empty_user_name_is_refused()
    {
        using var harness = new Harness();
        harness.Console.UserName = "   ";

        var result = await harness.RunAsync();

        Assert.Equal(FounderBootstrapStatus.UserNameInvalid, result.Status);
        harness.AssertNothingWritten();
    }

    [Fact]
    public async Task A_taken_user_name_is_refused_case_insensitively()
    {
        using var harness = new Harness();
        var existingUserId = Guid.NewGuid();
        harness.Context.Users.Add(new UserAccount(existingUserId, "Var Olan", email: null));
        harness.Context.LocalCredentials.Add(new LocalCredential(
            existingUserId,
            Harness.UserName,
            FakePasswordHasher.HashWithCost(SyntheticPasswords.New(), FakePasswordHasher.CurrentCost),
            FakePasswordHasher.CurrentFormatVersion,
            mustChangePassword: false,
            Now.AddDays(-1),
            Guid.NewGuid()));
        await harness.Context.SaveChangesAsync();
        harness.Context.ChangeTracker.Clear();
        harness.Console.UserName = Harness.UserName.ToUpperInvariant();

        var result = await harness.RunAsync();

        Assert.Equal(FounderBootstrapStatus.UserNameTaken, result.Status);
        Assert.Single(await harness.Context.Users.ToListAsync());
        Assert.Empty(await harness.Context.RoleAssignments.ToListAsync());
    }

    [Fact]
    public async Task An_empty_or_too_long_display_name_is_refused()
    {
        using var empty = new Harness();
        empty.Console.DisplayName = " ";
        Assert.Equal(FounderBootstrapStatus.DisplayNameInvalid, (await empty.RunAsync()).Status);

        using var tooLong = new Harness();
        tooLong.Console.DisplayName = new string('k', UserAccount.DisplayNameMaxLength + 1);
        Assert.Equal(FounderBootstrapStatus.DisplayNameInvalid, (await tooLong.RunAsync()).Status);
        empty.AssertNothingWritten();
        tooLong.AssertNothingWritten();
    }

    [Fact]
    public async Task A_mismatched_confirmation_is_refused_without_hashing()
    {
        using var harness = new Harness();
        harness.Console.Confirmation = SyntheticPasswords.New();

        var result = await harness.RunAsync();

        Assert.Equal(FounderBootstrapStatus.PasswordMismatch, result.Status);
        Assert.Equal(0, harness.Hasher.Hashes);
        harness.AssertNothingWritten();
    }

    [Fact]
    public async Task A_password_derived_from_a_company_name_reports_the_violation_without_hashing()
    {
        using var harness = new Harness();
        var password = "SentetikGıdaAş-" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(100_000, 1_000_000)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        harness.Console.Password = password;
        harness.Console.Confirmation = password;

        var result = await harness.RunAsync();

        Assert.Equal(FounderBootstrapStatus.PolicyViolation, result.Status);
        Assert.Equal([LocalPasswordPolicyViolationCodes.Blocklisted], result.PolicyViolations);
        Assert.Equal(0, harness.Hasher.Hashes);
        harness.AssertNothingWritten();
    }

    [Fact]
    public async Task A_completed_bootstrap_writes_the_founder_with_one_indefinite_admin_assignment_per_company()
    {
        using var harness = new Harness(companyCount: 2);

        var result = await harness.RunAsync();

        Assert.Equal(FounderBootstrapStatus.Completed, result.Status);
        var userId = Assert.IsType<Guid>(result.FounderUserId);
        Assert.Equal(harness.CompanyIds.Order(), result.CompanyIds.Order());
        Assert.Empty(result.PolicyViolations);
        var context = harness.Context;
        context.ChangeTracker.Clear();

        var user = Assert.Single(await context.Users.ToListAsync());
        Assert.Equal(userId, user.Id);
        Assert.Equal(Harness.DisplayName, user.DisplayName);
        Assert.Null(user.Email);

        var credential = Assert.Single(await context.LocalCredentials.ToListAsync());
        Assert.Equal(userId, credential.UserId);
        Assert.Equal(Harness.UserName, credential.UserName);
        Assert.True(credential.MustChangePassword);
        Assert.Equal(FakePasswordHasher.HashWithCost(harness.Console.Password!, FakePasswordHasher.CurrentCost), credential.PasswordHash);
        Assert.Equal(FakePasswordHasher.CurrentFormatVersion, credential.HashFormatVersion);
        Assert.Equal(1, harness.Hasher.Hashes);

        var link = Assert.Single(await context.ExternalIdentities.ToListAsync());
        Assert.Equal(userId, link.UserId);
        Assert.Equal(LocalAccountIdentity.Issuer, link.Issuer);
        Assert.Equal(LocalAccountIdentity.SubjectOf(userId), link.Subject);

        var assignments = await context.RoleAssignments.Include(assignment => assignment.Scopes).ToListAsync();
        Assert.Equal(harness.CompanyIds.Order(), assignments.Select(assignment => assignment.CompanyId).Order());
        Assert.All(assignments, assignment =>
        {
            Assert.Equal(userId, assignment.UserId);
            Assert.Equal(harness.AdminRoleId, assignment.RoleId);
            Assert.Equal(RoleAssignmentStatus.Active, assignment.Status);
            Assert.Equal(Now, assignment.ValidFromUtc);
            Assert.Null(assignment.ValidUntilUtc);
            Assert.Null(assignment.ApprovedByUserId);
            var scope = Assert.Single(assignment.Scopes);
            Assert.Equal(AccessScopeType.Company, scope.ScopeType);
            Assert.Equal(assignment.CompanyId, scope.ScopeId);
        });

        var marker = Assert.Single(await context.BootstrapStates.ToListAsync());
        Assert.Equal(IdentityBootstrapState.SingletonId, marker.Id);
        Assert.Equal(userId, marker.FounderUserId);
        Assert.Equal(Now, marker.CompletedAtUtc);

        var message = Assert.Single(await context.OutboxMessages.ToListAsync());
        Assert.Null(message.CompanyId);
        Assert.Equal(IdentityFounderBootstrappedV1.EventType, message.EventType);
        Assert.Equal(IdentityFounderBootstrappedV1.SchemaVersion, message.SchemaVersion);
        Assert.Equal(FounderBootstrapService.SourceOperation, message.SourceOperation);
        Assert.Equal(FounderBootstrapService.AggregateType, message.AggregateType);
        Assert.Equal(userId, message.AggregateId);
        Assert.Equal(1, message.AggregateVersion);
        Assert.Equal(Now, message.OccurredAtUtc);
        Assert.Equal(new FixedCorrelationContext().CorrelationId, message.CorrelationId);
        using (var payload = JsonDocument.Parse(message.PayloadJson))
        {
            Assert.Equal(["userId", "occurredAt"], payload.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.Equal(userId, payload.RootElement.GetProperty("userId").GetGuid());
            Assert.Equal(Now, payload.RootElement.GetProperty("occurredAt").GetDateTimeOffset());
        }

        Assert.Equal(harness.CompanyIds.Order(), harness.SecurityEvents.Written.Select(written => written.CompanyId!.Value).Order());
        Assert.All(harness.SecurityEvents.Written, written =>
        {
            Assert.Equal(SecurityEventTypesV1.PermissionChanged, written.EventType);
            Assert.Equal(SecurityEventOutcomesV1.Allowed, written.Outcome);
            Assert.Equal(SecurityEventScopeKindsV1.Company, written.ScopeKind);
            Assert.Equal(SecurityEventActorTypesV1.System, written.ActorType);
            Assert.Equal(FounderBootstrapService.SystemActorId, written.ActorId);
        });
    }

    [Fact]
    public async Task A_second_run_after_completion_is_refused_before_the_input_is_read()
    {
        using var harness = new Harness();
        Assert.Equal(FounderBootstrapStatus.Completed, (await harness.RunAsync()).Status);
        var readsAfterFirstRun = harness.Console.Reads;
        harness.Context.ChangeTracker.Clear();

        var second = await harness.RunAsync();

        Assert.Equal(FounderBootstrapStatus.AlreadyBootstrapped, second.Status);
        Assert.Equal(readsAfterFirstRun, harness.Console.Reads);
        Assert.Single(await harness.Context.Users.ToListAsync());
        Assert.Single(await harness.Context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task A_refused_fail_closed_security_event_reports_the_refusal()
    {
        using var harness = new Harness(securityEventWriter: new FailClosedSecurityEventWriter());

        var result = await harness.RunAsync();

        // The rollback itself is proven against SQL Server; InMemory keeps the saved rows.
        Assert.Equal(FounderBootstrapStatus.SecurityEventRefused, result.Status);
        Assert.Null(result.FounderUserId);
        Assert.Empty(result.CompanyIds);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", "SENTETIK-machine")]
    [InlineData("SENTETIK-os", "")]
    public async Task A_missing_operator_declaration_is_refused_before_the_account_input_is_read(string? osUser, string? machine)
    {
        using var harness = new Harness();
        harness.Console.Declaration = osUser is null ? null : new(osUser, machine!);

        var result = await harness.RunAsync();

        Assert.Equal(FounderBootstrapStatus.OperatorDeclarationMissing, result.Status);
        Assert.Equal(1, harness.Console.Reads);
        Assert.Equal(0, harness.Hasher.Hashes);
        harness.AssertNothingWritten();
    }

    [Fact]
    public async Task A_completed_bootstrap_hands_one_audit_row_per_company_with_its_admin_assignment_and_the_operator()
    {
        using var harness = new Harness(companyCount: 2);

        var result = await harness.RunAsync();

        Assert.Equal(FounderBootstrapStatus.Completed, result.Status);
        var userId = Assert.IsType<Guid>(result.FounderUserId);
        harness.Context.ChangeTracker.Clear();
        var assignments = await harness.Context.RoleAssignments.ToListAsync();
        Assert.Equal(harness.CompanyIds.Order(), harness.Recorded.Rows.Select(row => row.CompanyId).Order());
        Assert.All(harness.Recorded.Rows, row =>
        {
            Assert.Equal(userId, row.FounderUserId);
            Assert.Equal(assignments.Single(assignment => assignment.CompanyId == row.CompanyId).Id, row.AdminAssignmentId);
            Assert.Equal(new FounderRecoveryOperatorDeclarationV1("SENTETIK-os", "SENTETIK-machine"), row.Operator);
            Assert.Equal(harness.CompanyIds.Order(), row.CompanyIds.Order());
            Assert.Equal(Now, row.OccurredAtUtc);
            Assert.Equal(new FixedCorrelationContext().CorrelationId, row.CorrelationId);
        });
        Assert.Single(harness.Recorded.Rows.Select(row => row.IdempotencyKey).Distinct());
    }

    [Fact]
    public async Task A_refused_audit_row_reports_the_refusal_before_any_security_event()
    {
        using var harness = new Harness(audit: new RecordingBootstrapAudit { Refuse = true });

        var result = await harness.RunAsync();

        // The rollback itself is proven against SQL Server; InMemory keeps the saved rows.
        Assert.Equal(FounderBootstrapStatus.AuditRefused, result.Status);
        Assert.Null(result.FounderUserId);
        Assert.Empty(harness.SecurityEvents.Written);
    }

    private sealed class Harness : IDisposable
    {
        public const string UserName = "kurucukullanici";
        public const string DisplayName = "Sentetik Kurucu";
        private const string AdminRoleCode = "SENTETIK-YONETICI";

        private readonly ISecurityEventWriterV1 securityEventWriter;
        private readonly string? adminRoleCode;

        public Harness(
            string? adminRoleCode = AdminRoleCode,
            string? adminPermission = null,
            int companyCount = 1,
            ISecurityEventWriterV1? securityEventWriter = null,
            IFounderBootstrapAuditParticipant? audit = null)
        {
            Audit = audit ?? Recorded;
            this.adminRoleCode = adminRoleCode;
            this.securityEventWriter = securityEventWriter ?? SecurityEvents;
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase($"identity-founder-bootstrap-{Guid.NewGuid():N}")
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            Context = new IdentityDbContext(options);

            var permission = adminPermission ?? ErpPermissions.CounterpartyPartiesTaxIdentityApprove;
            var role = new RoleDefinition(AdminRoleId, AdminRoleCode, "Sentetik yönetici", isSystem: true);
            role.AddPermission(permission);
            Context.Roles.Add(role);
            for (var index = 0; index < companyCount; index++)
            {
                var companyId = Guid.NewGuid();
                CompanyIds.Add(companyId);
                Context.Companies.Add(new IdentityCompanyReference
                {
                    Id = companyId,
                    Code = $"SNT{index}",
                    LegalName = "Sentetik Gıda A.Ş.",
                    IsActive = true,
                });
            }

            Context.SaveChanges();
            Context.ChangeTracker.Clear();
            var password = SyntheticPasswords.New();
            Console.Password = password;
            Console.Confirmation = password;
        }

        public IdentityDbContext Context { get; }

        public Guid AdminRoleId { get; } = Guid.NewGuid();

        public List<Guid> CompanyIds { get; } = [];

        public FakeFounderBootstrapConsole Console { get; } = new() { UserName = UserName, DisplayName = DisplayName };

        public FakePasswordHasher Hasher { get; } = new();

        public RecordingSecurityEventWriter SecurityEvents { get; } = new();

        public RecordingBootstrapAudit Recorded { get; } = new();

        private IFounderBootstrapAuditParticipant Audit { get; }

        public Task<FounderBootstrapResult> RunAsync()
        {
            var service = new FounderBootstrapService(
                Context,
                Hasher,
                securityEventWriter,
                new FixedCorrelationContext(),
                new ManualTimeProvider(Now),
                Options.Create(LocalAccountLoginServiceTests.ValidOptions()),
                Options.Create(new IdentityBootstrapOptions { AdminRoleCode = adminRoleCode }),
                Audit);
            return service.RunAsync(Console, CancellationToken.None);
        }

        public void AssertNothingWritten()
        {
            Context.ChangeTracker.Clear();
            Assert.Empty(Context.Users.ToList());
            Assert.Empty(Context.LocalCredentials.ToList());
            Assert.Empty(Context.ExternalIdentities.ToList());
            Assert.Empty(Context.RoleAssignments.ToList());
            Assert.Empty(Context.BootstrapStates.ToList());
            Assert.Empty(Context.OutboxMessages.ToList());
            Assert.Empty(SecurityEvents.Written);
            Assert.Empty(Recorded.Rows);
        }

        public void Dispose() => Context.Dispose();
    }

    internal sealed class RecordingBootstrapAudit : IFounderBootstrapAuditParticipant
    {
        public List<FounderBootstrapCompanyAuditV1> Rows { get; } = [];

        public bool Refuse { get; init; }

        public Task AppendAsync(IReadOnlyList<FounderBootstrapCompanyAuditV1> records, CancellationToken cancellationToken)
        {
            if (Refuse) throw new InvalidOperationException("SENTETIK audit refusal.");
            Rows.AddRange(records);
            return Task.CompletedTask;
        }
    }

    private sealed class FailClosedSecurityEventWriter : ISecurityEventWriterV1
    {
        public Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecurityEventWriteResultV1.NotWritten(
                SecurityEventWriteFailureMode.FailClosed,
                SecurityEventWriteFailureKindsV1.StoreUnavailable));
    }
}

/// <summary>A scripted operator terminal; counts every read so a refusal before the input can be proven.</summary>
internal sealed class FakeFounderBootstrapConsole : IFounderBootstrapConsole
{
    public bool IsInteractive { get; set; } = true;

    public string? UserName { get; set; }

    public string? DisplayName { get; set; }

    public string? Password { get; set; }

    public string? Confirmation { get; set; }

    public int Reads { get; private set; }

    public string? ReadUserName() => Read(UserName);

    public string? ReadDisplayName() => Read(DisplayName);

    public string? ReadNewPassword() => Read(Password);

    public string? ReadNewPasswordConfirmation() => Read(Confirmation);

    public FounderRecoveryOperatorDeclaration? Declaration { get; set; } = new("SENTETIK-os", "SENTETIK-machine");

    public FounderRecoveryOperatorDeclaration? ReadOperatorDeclaration()
    {
        Reads++;
        return Declaration;
    }

    private string? Read(string? value)
    {
        Reads++;
        return value;
    }
}
