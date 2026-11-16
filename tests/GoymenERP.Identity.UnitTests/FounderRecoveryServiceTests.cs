using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Bootstrap;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Domain.Roles;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.Bootstrap;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace GoymenERP.Identity.UnitTests;

/// <summary>
/// ID-2r <c>recover-founder</c> (KIM-D22 reactivate, KIM-D23/KIM-D43 reset-password, KIM-D16/KIM-D17 preconditions):
/// the refusal order (configuration and preconditions before any input), the single refused security event of every
/// refusal, the locks in <c>CompanyId</c> order and the written state. EF InMemory does not roll a transaction back
/// and has no application lock, so the rollback after a refused security event and the real lock are proven by the
/// SQL tests only. SENTETIK.
/// </summary>
public sealed class FounderRecoveryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_missing_subcommand_refuses_with_one_refused_event_and_reads_nothing()
    {
        using var harness = Harness.Create(founderIsActive: false);

        var result = await harness.RunAsync(subcommand: null);

        Assert.Equal(FounderRecoveryStatus.SubcommandRequired, result.Status);
        Assert.Equal("identity.founder_recovery.subcommand_required", result.RefusalCode);
        Assert.Equal(0, harness.Console.Reads);
        harness.AssertOneRefusedEventAndNothingChanged();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-01:00:00")]
    [InlineData("abc")]
    public async Task An_invalid_temporary_password_lifetime_refuses_reset_password_before_any_input(string lifetime)
    {
        using var harness = Harness.Create(temporaryPasswordLifetime: lifetime);

        var result = await harness.RunAsync(FounderRecoverySubcommand.ResetPassword);

        Assert.Equal(FounderRecoveryStatus.ConfigurationMissing, result.Status);
        Assert.Equal(0, harness.Console.Reads);
        harness.AssertOneRefusedEventAndNothingChanged();
    }

    [Fact]
    public async Task Reactivate_without_a_configured_or_known_admin_role_refuses_before_any_input()
    {
        using var empty = Harness.Create(founderIsActive: false, adminRoleCode: " ");
        Assert.Equal(FounderRecoveryStatus.AdminRoleNotConfigured, (await empty.RunAsync(FounderRecoverySubcommand.Reactivate)).Status);

        using var unknown = Harness.Create(founderIsActive: false, adminRoleCode: "BASKA-ROL");
        Assert.Equal(FounderRecoveryStatus.AdminRoleNotConfigured, (await unknown.RunAsync(FounderRecoverySubcommand.Reactivate)).Status);

        Assert.Equal(0, empty.Console.Reads + unknown.Console.Reads);
        empty.AssertOneRefusedEventAndNothingChanged();
        unknown.AssertOneRefusedEventAndNothingChanged();
    }

    [Fact]
    public async Task A_missing_marker_or_founder_row_refuses_before_any_input()
    {
        using var notBootstrapped = Harness.Create(bootstrapped: false);
        Assert.Equal(FounderRecoveryStatus.NotBootstrapped, (await notBootstrapped.RunAsync(FounderRecoverySubcommand.ResetPassword)).Status);

        using var missing = Harness.Create(withFounder: false);
        Assert.Equal(FounderRecoveryStatus.FounderNotFound, (await missing.RunAsync(FounderRecoverySubcommand.ResetPassword)).Status);

        Assert.Equal(0, notBootstrapped.Console.Reads + missing.Console.Reads);
        Assert.Single(notBootstrapped.Events.Written);
        Assert.Single(missing.Events.Written);
    }

    [Fact]
    public async Task Reactivate_refuses_an_active_founder_and_reset_password_refuses_an_inactive_one()
    {
        using var active = Harness.Create(founderIsActive: true);
        Assert.Equal(FounderRecoveryStatus.FounderActive, (await active.RunAsync(FounderRecoverySubcommand.Reactivate)).Status);

        using var inactive = Harness.Create(founderIsActive: false);
        Assert.Equal(FounderRecoveryStatus.FounderInactive, (await inactive.RunAsync(FounderRecoverySubcommand.ResetPassword)).Status);

        Assert.Equal(0, active.Console.Reads + inactive.Console.Reads);
        active.AssertOneRefusedEventAndNothingChanged();
        inactive.AssertOneRefusedEventAndNothingChanged();
    }

    [Fact]
    public async Task Reactivate_is_not_needed_while_every_company_has_another_effective_admin()
    {
        using var harness = Harness.Create(founderIsActive: false, companyCount: 2);
        foreach (var companyId in harness.CompanyIds)
        {
            harness.AddAssignment(harness.AddUser(), harness.AdminRoleId, companyId);
        }

        var result = await harness.RunAsync(FounderRecoverySubcommand.Reactivate);

        Assert.Equal(FounderRecoveryStatus.NotNeeded, result.Status);
        Assert.Equal(0, harness.Console.Reads);
        harness.AssertOneRefusedEventAndNothingChanged();
    }

    [Fact]
    public async Task An_inactive_admin_a_time_limited_assignment_or_a_site_scope_is_not_an_effective_admin()
    {
        using var harness = Harness.Create(founderIsActive: false, companyCount: 3);
        harness.AddAssignment(harness.AddUser(isActive: false), harness.AdminRoleId, harness.CompanyIds[0]);
        harness.AddAssignment(harness.AddUser(), harness.AdminRoleId, harness.CompanyIds[1], validUntilUtc: Now.AddDays(30));
        harness.AddAssignment(harness.AddUser(), harness.AdminRoleId, harness.CompanyIds[2], siteScopeOnly: true);

        var result = await harness.RunAsync(FounderRecoverySubcommand.Reactivate);

        Assert.Equal(FounderRecoveryStatus.Completed, result.Status);
        Assert.Equal(harness.CompanyIds.Order(), result.CompanyIds.Order());
    }

    [Fact]
    public async Task Reset_password_is_not_needed_while_another_active_user_may_manage_the_founder_in_every_membership_company()
    {
        using var harness = Harness.Create(companyCount: 2);
        var manager = harness.AddUser();
        foreach (var companyId in harness.CompanyIds)
        {
            harness.AddAssignment(harness.FounderUserId, harness.AdminRoleId, companyId);
            harness.AddAssignment(manager, harness.ManagerRoleId, companyId);
        }

        var result = await harness.RunAsync(FounderRecoverySubcommand.ResetPassword);

        Assert.Equal(FounderRecoveryStatus.NotNeeded, result.Status);
        Assert.Equal(0, harness.Console.Reads);
        harness.AssertOneRefusedEventAndNothingChanged();
    }

    [Fact]
    public async Task Reset_password_is_needed_when_the_other_manager_covers_only_some_membership_companies()
    {
        using var harness = Harness.Create(companyCount: 2);
        var manager = harness.AddUser();
        foreach (var companyId in harness.CompanyIds)
        {
            harness.AddAssignment(harness.FounderUserId, harness.AdminRoleId, companyId);
        }

        harness.AddAssignment(manager, harness.ManagerRoleId, harness.CompanyIds[0]);

        var result = await harness.RunAsync(FounderRecoverySubcommand.ResetPassword);

        Assert.Equal(FounderRecoveryStatus.Completed, result.Status);
        Assert.Equal(harness.CompanyIds.Order(), result.CompanyIds.Order());
    }

    [Fact]
    public async Task A_redirected_terminal_refuses_after_the_preconditions_and_before_any_password_is_read()
    {
        using var harness = Harness.Create();
        harness.Console.IsInteractive = false;

        var result = await harness.RunAsync(FounderRecoverySubcommand.ResetPassword);

        Assert.Equal(FounderRecoveryStatus.NotInteractive, result.Status);
        Assert.Equal(0, harness.Console.Reads);
        harness.AssertOneRefusedEventAndNothingChanged();
    }

    [Fact]
    public async Task Mismatched_passwords_refuse_without_hashing_or_asking_for_confirmation()
    {
        using var harness = Harness.Create();
        harness.Console.Confirmation = SyntheticPasswords.New();

        var result = await harness.RunAsync(FounderRecoverySubcommand.ResetPassword);

        Assert.Equal(FounderRecoveryStatus.PasswordMismatch, result.Status);
        Assert.Equal((0, 0), (harness.Hasher.Hashes, harness.Console.Confirmations.Count));
        harness.AssertOneRefusedEventAndNothingChanged();
    }

    [Fact]
    public async Task A_password_containing_a_company_name_violates_the_policy_and_is_never_hashed()
    {
        using var harness = Harness.Create(companyCount: 1);
        var password = "SentetikGıdaAş-" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(100_000, 1_000_000)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        harness.Console.Password = password;
        harness.Console.Confirmation = password;

        var result = await harness.RunAsync(FounderRecoverySubcommand.ResetPassword);

        Assert.Equal(FounderRecoveryStatus.PolicyViolation, result.Status);
        Assert.NotEmpty(result.PolicyViolations);
        Assert.Equal((0, 0), (harness.Hasher.Hashes, harness.Console.Confirmations.Count));
        harness.AssertOneRefusedEventAndNothingChanged();
    }

    [Fact]
    public async Task A_declined_confirmation_writes_nothing_and_shows_the_founder_and_every_company()
    {
        using var harness = Harness.Create(companyCount: 2);
        harness.Console.Answer = false;

        var result = await harness.RunAsync(FounderRecoverySubcommand.ResetPassword);

        Assert.Equal(FounderRecoveryStatus.NotConfirmed, result.Status);
        var shown = Assert.Single(harness.Console.Confirmations);
        Assert.Equal((FounderRecoverySubcommand.ResetPassword, Harness.UserName), (shown.Subcommand, shown.FounderUserName));
        Assert.Equal(harness.CompanyIds.Order(), shown.Companies.Select(company => company.CompanyId));
        Assert.Equal(0, harness.Hasher.Hashes);
        harness.AssertOneRefusedEventAndNothingChanged();
    }

    [Fact]
    public async Task A_busy_company_lock_refuses_with_concurrent_change_and_the_locks_are_taken_in_company_order()
    {
        using var harness = Harness.Create(companyCount: 3);
        harness.Lock.BusyCompanyId = harness.CompanyIds.Order().ElementAt(1);

        var result = await harness.RunAsync(FounderRecoverySubcommand.ResetPassword);

        Assert.Equal(FounderRecoveryStatus.ConcurrentChange, result.Status);
        Assert.Equal(harness.CompanyIds.Order().Take(2), harness.Lock.Requested);
        harness.AssertOneRefusedEventAndNothingChanged();
    }

    [Fact]
    public async Task A_completed_reactivation_activates_the_founder_with_the_typed_password_and_forces_a_change()
    {
        using var harness = Harness.Create(founderIsActive: false, companyCount: 2);
        harness.AddAssignment(harness.AddUser(), harness.AdminRoleId, harness.CompanyIds[0]);
        var stampBefore = harness.CredentialStamp;

        var result = await harness.RunAsync(FounderRecoverySubcommand.Reactivate);

        Assert.Equal(FounderRecoveryStatus.Completed, result.Status);
        Assert.Equal(harness.FounderUserId, result.FounderUserId);
        Assert.Equal([harness.CompanyIds[1]], result.CompanyIds);
        Assert.Null(result.TemporaryPasswordExpiresAtUtc);
        Assert.Equal(harness.CompanyIds.Order(), harness.Lock.Requested);

        harness.Context.ChangeTracker.Clear();
        var user = await harness.Context.Users.SingleAsync(candidate => candidate.Id == harness.FounderUserId);
        var credential = await harness.Context.LocalCredentials.SingleAsync(candidate => candidate.UserId == harness.FounderUserId);
        Assert.True(user.IsActive);
        Assert.Equal(FakePasswordHasher.HashWithCost(harness.Console.Password!, FakePasswordHasher.CurrentCost), credential.PasswordHash);
        Assert.True(credential.MustChangePassword);
        Assert.Null(credential.TemporaryPasswordExpiresAtUtc);
        Assert.NotEqual(stampBefore, credential.SecurityStamp);

        var written = Assert.Single(harness.Events.Written);
        Assert.Equal(SecurityEventTypesV1.FounderRecovered, written.EventType);
        Assert.Equal(SecurityEventOutcomesV1.Allowed, written.Outcome);
        Assert.Equal((SecurityEventScopeKindsV1.Platform, (Guid?)null), (written.ScopeKind, written.CompanyId));
        Assert.Equal((SecurityEventActorTypesV1.System, FounderRecoveryService.SystemActorId), (written.ActorType, written.ActorId));
        Assert.Empty(SecurityEventV1.Validate(written));
    }

    [Fact]
    public async Task A_completed_reset_issues_a_single_use_temporary_password_that_expires_after_the_default_72_hours()
    {
        using var harness = Harness.Create(companyCount: 1);
        harness.AddAssignment(harness.FounderUserId, harness.AdminRoleId, harness.CompanyIds[0]);

        var result = await harness.RunAsync(FounderRecoverySubcommand.ResetPassword);

        Assert.Equal(FounderRecoveryStatus.Completed, result.Status);
        Assert.Equal(Now + FounderRecoveryOptions.DefaultTemporaryPasswordLifetime, result.TemporaryPasswordExpiresAtUtc);
        Assert.Equal(TimeSpan.FromHours(72), FounderRecoveryOptions.DefaultTemporaryPasswordLifetime);
        Assert.Equal([harness.CompanyIds[0]], result.CompanyIds);

        harness.Context.ChangeTracker.Clear();
        var credential = await harness.Context.LocalCredentials.SingleAsync(candidate => candidate.UserId == harness.FounderUserId);
        Assert.Equal((Now, Now.AddHours(72), (DateTimeOffset?)null),
            (credential.TemporaryPasswordIssuedAtUtc, credential.TemporaryPasswordExpiresAtUtc, credential.TemporaryPasswordConsumedAtUtc));
        Assert.True(credential.MustChangePassword);
        Assert.Equal(FakePasswordHasher.HashWithCost(harness.Console.Password!, FakePasswordHasher.CurrentCost), credential.PasswordHash);

        var written = Assert.Single(harness.Events.Written);
        Assert.Equal(SecurityEventTypesV1.FounderPasswordReset, written.EventType);
        Assert.Empty(SecurityEventV1.Validate(written));
    }

    [Fact]
    public async Task A_founder_without_membership_can_be_reset_and_a_configured_lifetime_is_used()
    {
        using var harness = Harness.Create(temporaryPasswordLifetime: "1.00:00:00");

        var result = await harness.RunAsync(FounderRecoverySubcommand.ResetPassword);

        Assert.Equal(FounderRecoveryStatus.Completed, result.Status);
        Assert.Empty(result.CompanyIds);
        Assert.Equal(Now.AddDays(1), result.TemporaryPasswordExpiresAtUtc);
        Assert.Empty(Assert.Single(harness.Console.Confirmations).Companies);
    }

    [Fact]
    public async Task A_refused_success_event_reports_security_event_refused_and_records_the_refusal()
    {
        using var harness = Harness.Create(failClosedWriter: true);

        var result = await harness.RunAsync(FounderRecoverySubcommand.ResetPassword);

        Assert.Equal(FounderRecoveryStatus.SecurityEventRefused, result.Status);
        Assert.Equal("identity.founder_recovery.security_event_refused", result.RefusalCode);
        Assert.Equal(
            [SecurityEventTypesV1.FounderPasswordReset, SecurityEventTypesV1.FounderRecoveryRefused],
            harness.Events.Written.Select(written => written.EventType));
    }

    [Fact]
    public async Task Every_refused_event_is_a_valid_platform_policy_refusal_without_a_user_or_company()
    {
        using var harness = Harness.Create(founderIsActive: true);

        await harness.RunAsync(FounderRecoverySubcommand.Reactivate);

        var written = Assert.Single(harness.Events.Written);
        Assert.Equal(SecurityEventTypesV1.FounderRecoveryRefused, written.EventType);
        Assert.Equal((SecurityEventOutcomesV1.Denied, SecurityEventReasonCodesV1.PolicyRefused), (written.Outcome, written.ReasonCode));
        Assert.Equal((SecurityEventScopeKindsV1.Platform, (Guid?)null, (Guid?)null), (written.ScopeKind, written.CompanyId, written.RequestedCompanyId));
        Assert.Empty(SecurityEventV1.Validate(written));
    }

    internal sealed class Harness : IDisposable
    {
        public const string UserName = "kurucukullanici";
        private const string AdminRoleCode = "SENTETIK-YONETICI";
        private const string ManagerRoleCode = "SENTETIK-KULLANICI-YONETICI";

        private readonly string? adminRoleCode;
        private readonly string? temporaryPasswordLifetime;
        private readonly ISecurityEventWriterV1 writer;

        private Harness(string? adminRoleCode, string? temporaryPasswordLifetime, bool failClosedWriter)
        {
            this.adminRoleCode = adminRoleCode;
            this.temporaryPasswordLifetime = temporaryPasswordLifetime;
            writer = failClosedWriter ? new FailClosedOnSuccessWriter(Events) : Events;
            var options = new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase($"identity-founder-recovery-{Guid.NewGuid():N}")
                .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            Context = new IdentityDbContext(options);
            var password = SyntheticPasswords.New();
            Console.Password = password;
            Console.Confirmation = password;
        }

        public IdentityDbContext Context { get; }

        public Guid FounderUserId { get; } = Guid.NewGuid();

        public Guid AdminRoleId { get; } = Guid.NewGuid();

        public Guid ManagerRoleId { get; } = Guid.NewGuid();

        public Guid CredentialStamp { get; } = Guid.NewGuid();

        public List<Guid> CompanyIds { get; } = [];

        public FakeFounderRecoveryConsole Console { get; } = new();

        public FakeFounderRecoveryCompanyLock Lock { get; } = new();

        public FakePasswordHasher Hasher { get; } = new();

        public RecordingSecurityEventWriter Events { get; } = new();

        private string? StoredHash { get; set; }

        private long UserVersion { get; set; }

        private long CredentialVersion { get; set; }

        public static Harness Create(
            bool founderIsActive = true,
            int companyCount = 0,
            string? adminRoleCode = AdminRoleCode,
            string? temporaryPasswordLifetime = null,
            bool bootstrapped = true,
            bool withFounder = true,
            bool failClosedWriter = false)
        {
            var harness = new Harness(adminRoleCode, temporaryPasswordLifetime, failClosedWriter);
            var context = harness.Context;
            context.Roles.Add(new RoleDefinition(harness.AdminRoleId, AdminRoleCode, "Sentetik yönetici", isSystem: true));
            var managerRole = new RoleDefinition(harness.ManagerRoleId, ManagerRoleCode, "Sentetik kullanıcı yöneticisi", isSystem: false);
            managerRole.AddPermission(ErpPermissions.IdentityUsersManage);
            context.Roles.Add(managerRole);
            for (var index = 0; index < companyCount; index++)
            {
                var companyId = Guid.NewGuid();
                harness.CompanyIds.Add(companyId);
                context.Companies.Add(new IdentityCompanyReference
                {
                    Id = companyId,
                    Code = $"SNT{index}",
                    LegalName = "Sentetik Gıda A.Ş.",
                    IsActive = true,
                });
            }

            if (bootstrapped)
            {
                context.BootstrapStates.Add(new IdentityBootstrapState(harness.FounderUserId, Now.AddDays(-30)));
            }

            if (withFounder)
            {
                var founder = new UserAccount(harness.FounderUserId, "Sentetik Kurucu", email: null);
                if (!founderIsActive)
                {
                    typeof(UserAccount).GetProperty(nameof(UserAccount.IsActive))!.SetValue(founder, false);
                }

                context.Users.Add(founder);
                var credential = new LocalCredential(
                    harness.FounderUserId,
                    UserName,
                    FakePasswordHasher.HashWithCost(SyntheticPasswords.New(), FakePasswordHasher.CurrentCost),
                    FakePasswordHasher.CurrentFormatVersion,
                    mustChangePassword: false,
                    Now.AddDays(-30),
                    harness.CredentialStamp);
                context.LocalCredentials.Add(credential);
                harness.StoredHash = credential.PasswordHash;
            }

            context.SaveChanges();
            context.ChangeTracker.Clear();
            harness.UserVersion = context.Users.Where(user => user.Id == harness.FounderUserId).Select(user => user.Version).SingleOrDefault();
            harness.CredentialVersion = context.LocalCredentials.Where(row => row.UserId == harness.FounderUserId)
                .Select(row => row.Version).SingleOrDefault();
            return harness;
        }

        public Guid AddUser(bool isActive = true)
        {
            var user = new UserAccount(Guid.NewGuid(), "Sentetik Kullanıcı", email: null);
            if (!isActive)
            {
                typeof(UserAccount).GetProperty(nameof(UserAccount.IsActive))!.SetValue(user, false);
            }

            Context.Users.Add(user);
            Context.SaveChanges();
            Context.ChangeTracker.Clear();
            return user.Id;
        }

        public void AddAssignment(
            Guid userId,
            Guid roleId,
            Guid companyId,
            DateTimeOffset? validUntilUtc = null,
            bool siteScopeOnly = false)
        {
            var assignment = new RoleAssignment(Guid.NewGuid(), userId, roleId, companyId, Now.AddDays(-10), validUntilUtc, approvedByUserId: null);
            if (siteScopeOnly)
            {
                assignment.AddScope(AccessScopeType.Site, Guid.NewGuid());
            }
            else
            {
                assignment.AddScope(AccessScopeType.Company, companyId);
            }

            Context.RoleAssignments.Add(assignment);
            Context.SaveChanges();
            Context.ChangeTracker.Clear();
        }

        public Task<FounderRecoveryResult> RunAsync(FounderRecoverySubcommand? subcommand)
        {
            var service = new FounderRecoveryService(
                Context,
                Hasher,
                writer,
                new FixedCorrelationContext(),
                new ManualTimeProvider(Now),
                Options.Create(LocalAccountLoginServiceTests.ValidOptions()),
                Options.Create(new IdentityBootstrapOptions { AdminRoleCode = adminRoleCode }),
                Options.Create(new FounderRecoveryOptions { TemporaryPasswordLifetime = temporaryPasswordLifetime }),
                Lock,
                new IdentityUserManagementEvaluator(Context));
            return service.RunAsync(subcommand, Console, CancellationToken.None);
        }

        public void AssertOneRefusedEventAndNothingChanged()
        {
            var written = Assert.Single(Events.Written);
            Assert.Equal(SecurityEventTypesV1.FounderRecoveryRefused, written.EventType);
            Context.ChangeTracker.Clear();
            var user = Context.Users.SingleOrDefault(candidate => candidate.Id == FounderUserId);
            var credential = Context.LocalCredentials.SingleOrDefault(candidate => candidate.UserId == FounderUserId);
            Assert.Equal(UserVersion, user?.Version ?? 0);
            Assert.Equal(CredentialVersion, credential?.Version ?? 0);
            Assert.Equal(StoredHash, credential?.PasswordHash);
        }

        public void Dispose() => Context.Dispose();
    }

    /// <summary>Records every event; refuses the fail-closed success events like an unavailable store.</summary>
    private sealed class FailClosedOnSuccessWriter(RecordingSecurityEventWriter recorder) : ISecurityEventWriterV1
    {
        public async Task<SecurityEventWriteResultV1> WriteAsync(SecurityEventV1 securityEvent, CancellationToken cancellationToken = default)
        {
            await recorder.WriteAsync(securityEvent, cancellationToken);
            return securityEvent.EventType == SecurityEventTypesV1.FounderRecoveryRefused
                ? SecurityEventWriteResultV1.Written(SecurityEventWriteFailureMode.FailOpenWithAlarm)
                : SecurityEventWriteResultV1.NotWritten(
                    SecurityEventWriteFailureMode.FailClosed,
                    SecurityEventWriteFailureKindsV1.StoreUnavailable);
        }
    }
}

/// <summary>A scripted recovery terminal; counts every password read and records each confirmation shown.</summary>
internal sealed class FakeFounderRecoveryConsole : IFounderRecoveryConsole
{
    public bool IsInteractive { get; set; } = true;

    public string? Password { get; set; }

    public string? Confirmation { get; set; }

    public bool Answer { get; set; } = true;

    public int Reads { get; private set; }

    public List<FounderRecoveryConfirmation> Confirmations { get; } = [];

    public string? ReadNewPassword()
    {
        Reads++;
        return Password;
    }

    public string? ReadNewPasswordConfirmation()
    {
        Reads++;
        return Confirmation;
    }

    public bool Confirm(FounderRecoveryConfirmation confirmation)
    {
        Confirmations.Add(confirmation);
        return Answer;
    }
}

/// <summary>Records the lock order; one company can be made busy.</summary>
internal sealed class FakeFounderRecoveryCompanyLock : IFounderRecoveryCompanyLock
{
    public Guid? BusyCompanyId { get; set; }

    public List<Guid> Requested { get; } = [];

    public Task<bool> TryAcquireAsync(Guid companyId, CancellationToken cancellationToken)
    {
        Requested.Add(companyId);
        return Task.FromResult(companyId != BusyCompanyId);
    }
}
