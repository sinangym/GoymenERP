using System.Data;
using System.Data.Common;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using GoymenERP.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Identity.Infrastructure.Bootstrap;

/// <summary>
/// ID-2r founder recovery (spec "Kurucu kurtarma komutu", KIM-D22 <c>reactivate</c>, KIM-D23/D43 <c>reset-password</c>).
/// Only the <c>IdentityBootstrapState.FounderUserId</c> account is ever targeted. Fixed order, no interaction under a
/// lock:
/// <list type="number">
/// <item>configuration (<c>configuration_missing</c> before any password is read) and a lock-free pre-read of the
/// marker, the founder's user and credential rows (their <c>Version</c> tokens included), the companies and the
/// precondition; this is the state the operator confirms;</item>
/// <item>outside any transaction: the terminal check, the new password twice without echo, the password policy and an
/// explicit confirmation of the environment, database, founder user name and companies (no <c>--yes</c>);</item>
/// <item>one SERIALIZABLE transaction: the KIM-D17 company locks of EVERY company in <c>CompanyId</c> order, then the
/// founder's rows, then the precondition again with locked reads;</item>
/// <item>the write, guarded by the <c>Version</c> values read in step 1 ("the confirmed state is the written state"):
/// a change in between refuses with <c>concurrent_change</c>.</item>
/// </list>
/// The success security event (<c>identity.founder.recovered</c> / <c>identity.founder.password_reset</c>, Platform, no
/// company) is written fail-closed before the commit. Every refusal, the missing sub-command included, writes exactly
/// one <c>identity.founder.recovery_refused</c> Platform record after the account transaction is rolled back (fail-open
/// with alarm). The company Audit rows use the separate typed System contract, join this exact transaction, and
/// contain the confirmed operator declaration. Audit failure rolls back account and credential changes.
/// </summary>
internal sealed class FounderRecoveryService(
    IdentityDbContext dbContext,
    ILocalPasswordHasher passwordHasher,
    ISecurityEventWriterV1 securityEventWriter,
    ICorrelationContext correlationContext,
    TimeProvider timeProvider,
    IOptions<LocalAccountOptions> localAccountOptions,
    IOptions<IdentityBootstrapOptions> bootstrapOptions,
    IOptions<FounderRecoveryOptions> recoveryOptions,
    IFounderRecoveryCompanyLock companyLock,
    IdentityUserManagementEvaluator userManagementEvaluator,
    IFounderRecoveryAuditParticipant recoveryAudit)
    : IFounderRecoveryService
{
    /// <summary>The system actor token of the command (never an OS user or machine name).</summary>
    internal const string SystemActorId = "identity.recover-founder";

    private static readonly string[] DatabaseServerKeys = ["Data Source", "Server", "Address", "Addr", "Network Address"];

    private static readonly string[] DatabaseCatalogKeys = ["Initial Catalog", "Database"];

    public async Task<FounderRecoveryResult> RunAsync(
        FounderRecoverySubcommand? subcommand,
        IFounderRecoveryConsole console,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(console);

        var result = await RunCoreAsync(subcommand, console, cancellationToken);
        if (result.Status != FounderRecoveryStatus.Completed)
        {
            // Fail-open with alarm (authz.denied pattern): the refusal stands whether or not the channel records it.
            await securityEventWriter.WriteAsync(NewRefusedEvent(), cancellationToken);
        }

        return result;
    }

    private async Task<FounderRecoveryResult> RunCoreAsync(
        FounderRecoverySubcommand? subcommand,
        IFounderRecoveryConsole console,
        CancellationToken cancellationToken)
    {
        if (subcommand is not { } chosen || !Enum.IsDefined(chosen))
        {
            return FounderRecoveryResult.Of(FounderRecoveryStatus.SubcommandRequired);
        }

        var localAccount = localAccountOptions.Value;
        localAccount.EnsureValid();

        // Step 1: configuration first, so a misconfigured reset never asks for a password.
        TimeSpan? lifetime = null;
        if (chosen == FounderRecoverySubcommand.ResetPassword)
        {
            lifetime = recoveryOptions.Value.ResolveTemporaryPasswordLifetime();
            if (lifetime is null)
            {
                return FounderRecoveryResult.Of(FounderRecoveryStatus.ConfigurationMissing);
            }
        }

        var adminRoleCode = bootstrapOptions.Value.AdminRoleCode?.Trim();
        if (chosen == FounderRecoverySubcommand.Reactivate && string.IsNullOrEmpty(adminRoleCode))
        {
            return FounderRecoveryResult.Of(FounderRecoveryStatus.AdminRoleNotConfigured);
        }

        var preRead = await PreReadAsync(cancellationToken);
        if (preRead.Refusal is { } preReadRefusal)
        {
            return FounderRecoveryResult.Of(preReadRefusal);
        }

        var founder = preRead.Founder!;
        var precondition = await EvaluatePreconditionAsync(
            chosen,
            founder.UserId,
            founder.IsActive,
            adminRoleCode,
            preRead.Companies.Select(company => company.CompanyId).ToList(),
            timeProvider.GetUtcNow(),
            cancellationToken);
        if (precondition.Refusal is { } preconditionRefusal)
        {
            return FounderRecoveryResult.Of(preconditionRefusal);
        }

        // Step 2: interaction, outside any transaction and lock.
        if (!console.IsInteractive)
        {
            return FounderRecoveryResult.Of(FounderRecoveryStatus.NotInteractive);
        }

        var declaration = console.ReadOperatorDeclaration();
        if (declaration is null || string.IsNullOrWhiteSpace(declaration.OperatingSystemUser) || declaration.OperatingSystemUser.Trim().Length > 256
            || string.IsNullOrWhiteSpace(declaration.MachineName) || declaration.MachineName.Trim().Length > 256)
            return FounderRecoveryResult.Of(FounderRecoveryStatus.OperatorDeclarationMissing);
        var password = console.ReadNewPassword() ?? string.Empty;
        var confirmation = console.ReadNewPasswordConfirmation() ?? string.Empty;
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            return FounderRecoveryResult.Of(FounderRecoveryStatus.PasswordMismatch);
        }

        var contextTerms = new[] { founder.UserName, founder.DisplayName }
            .Concat(preRead.Companies.SelectMany(company => new[] { company.Code, company.LegalName }))
            .Where(term => !string.IsNullOrWhiteSpace(term))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var violations = localAccount.ToPasswordPolicy().Evaluate(password, contextTerms);
        if (violations.Count > 0)
        {
            return new FounderRecoveryResult(FounderRecoveryStatus.PolicyViolation, null, [], violations, null);
        }

        var (databaseServer, databaseCatalog) = DescribeDatabase();
        if (!console.Confirm(new FounderRecoveryConfirmation(
                chosen,
                founder.UserName,
                databaseServer,
                databaseCatalog,
                preRead.Companies, declaration)))
        {
            return FounderRecoveryResult.Of(FounderRecoveryStatus.NotConfirmed);
        }

        var passwordHash = passwordHasher.Hash(password);
        return await WriteAsync(chosen, founder, adminRoleCode, passwordHash, declaration, Guid.NewGuid().ToString("N"), cancellationToken);
    }

    private async Task<PreRead> PreReadAsync(CancellationToken cancellationToken)
    {
        var founderUserId = await dbContext.BootstrapStates.AsNoTracking()
            .Select(state => (Guid?)state.FounderUserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (founderUserId is not { } userId)
        {
            return PreRead.Refused(FounderRecoveryStatus.NotBootstrapped);
        }

        var user = await dbContext.Users.AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new { candidate.IsActive, candidate.DisplayName, candidate.Version })
            .SingleOrDefaultAsync(cancellationToken);
        var credential = await dbContext.LocalCredentials.AsNoTracking()
            .Where(candidate => candidate.UserId == userId)
            .Select(candidate => new { candidate.UserName, candidate.Version })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null || credential is null)
        {
            return PreRead.Refused(FounderRecoveryStatus.FounderNotFound);
        }

        var companies = await dbContext.Companies.AsNoTracking()
            .OrderBy(company => company.Id)
            .Select(company => new FounderRecoveryCompany(company.Id, company.Code, company.LegalName))
            .ToListAsync(cancellationToken);

        return new PreRead(
            null,
            new FounderSnapshot(userId, user.IsActive, user.DisplayName, user.Version, credential.UserName, credential.Version),
            companies);
    }

    /// <summary>
    /// The shared precondition of step 1 and step 3 (the same code answers both, so a locked re-read can only refuse what
    /// the pre-read would also refuse). Returns the companies that meet it.
    /// </summary>
    private async Task<Precondition> EvaluatePreconditionAsync(
        FounderRecoverySubcommand subcommand,
        Guid founderUserId,
        bool founderIsActive,
        string? adminRoleCode,
        IReadOnlyList<Guid> companyIds,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (subcommand == FounderRecoverySubcommand.Reactivate)
        {
            if (founderIsActive)
            {
                return Precondition.Refused(FounderRecoveryStatus.FounderActive);
            }

            var adminRole = await dbContext.Roles.AsNoTracking()
                .Where(role => role.Code == adminRoleCode)
                .Select(role => new { role.Id, role.IsActive })
                .SingleOrDefaultAsync(cancellationToken);
            if (adminRole is null)
            {
                // The configured code names no role: the KIM-D17 question cannot be answered (fail-closed).
                return Precondition.Refused(FounderRecoveryStatus.AdminRoleNotConfigured);
            }

            var companiesWithAdmin = adminRole.IsActive
                ? await CompaniesWithEffectiveAdminAsync(adminRole.Id, nowUtc, cancellationToken)
                : new HashSet<Guid>();
            var adminless = companyIds.Where(companyId => !companiesWithAdmin.Contains(companyId)).ToList();
            return adminless.Count == 0
                ? Precondition.Refused(FounderRecoveryStatus.NotNeeded)
                : new Precondition(null, adminless);
        }

        if (!founderIsActive)
        {
            return Precondition.Refused(FounderRecoveryStatus.FounderInactive);
        }

        // KIM-D19: without any valid assignment the screen cannot reset the founder; this branch runs before the evaluator.
        var membership = await userManagementEvaluator.GetMembershipCompanyIdsAsync(founderUserId, nowUtc, cancellationToken);
        if (membership.Count == 0)
        {
            return new Precondition(null, []);
        }

        return await userManagementEvaluator.AnyOtherActiveUserMayManageAsync(founderUserId, membership, nowUtc, cancellationToken)
            ? Precondition.Refused(FounderRecoveryStatus.NotNeeded)
            : new Precondition(null, membership);
    }

    /// <summary>
    /// KIM-D17 effective Admin: an active user with an active, started, open-ended (<c>ValidUntilUtc</c> null) assignment
    /// of the active Admin role carrying the company-wide scope of that company.
    /// </summary>
    private async Task<HashSet<Guid>> CompaniesWithEffectiveAdminAsync(
        Guid adminRoleId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var companyIds = await dbContext.RoleAssignments.AsNoTracking()
            .Where(assignment => assignment.RoleId == adminRoleId
                && assignment.Status == RoleAssignmentStatus.Active
                && assignment.ValidFromUtc <= nowUtc
                && assignment.ValidUntilUtc == null
                && dbContext.Users.Any(user => user.Id == assignment.UserId && user.IsActive)
                && assignment.Scopes.Any(scope =>
                    scope.ScopeType == AccessScopeType.Company && scope.ScopeId == assignment.CompanyId))
            .Select(assignment => assignment.CompanyId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return companyIds.ToHashSet();
    }

    private async Task<FounderRecoveryResult> WriteAsync(
        FounderRecoverySubcommand subcommand,
        FounderSnapshot confirmed,
        string? adminRoleCode,
        string passwordHash,
        FounderRecoveryOperatorDeclaration declaration,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        // Step 3: every company lock in CompanyId order, then the founder's rows, then the precondition again.
        var companyIds = await dbContext.Companies.AsNoTracking()
            .OrderBy(company => company.Id)
            .Select(company => company.Id)
            .ToListAsync(cancellationToken);
        foreach (var companyId in companyIds)
        {
            if (!await companyLock.TryAcquireAsync(companyId, cancellationToken))
            {
                return await RollbackAsync(transaction, FounderRecoveryStatus.ConcurrentChange, cancellationToken);
            }
        }

        var lockedFounderUserId = await dbContext.BootstrapStates
            .Select(state => (Guid?)state.FounderUserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (lockedFounderUserId != confirmed.UserId)
        {
            return await RollbackAsync(transaction, FounderRecoveryStatus.ConcurrentChange, cancellationToken);
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == confirmed.UserId, cancellationToken);
        var credential = await dbContext.LocalCredentials
            .SingleOrDefaultAsync(candidate => candidate.UserId == confirmed.UserId, cancellationToken);
        if (user is null || credential is null)
        {
            return await RollbackAsync(transaction, FounderRecoveryStatus.FounderNotFound, cancellationToken);
        }

        var nowUtc = timeProvider.GetUtcNow();
        var precondition = await EvaluatePreconditionAsync(
            subcommand,
            user.Id,
            user.IsActive,
            adminRoleCode,
            companyIds,
            nowUtc,
            cancellationToken);
        if (precondition.Refusal is { } refusal)
        {
            return await RollbackAsync(transaction, refusal, cancellationToken);
        }

        TimeSpan? lifetime = null;
        if (subcommand == FounderRecoverySubcommand.ResetPassword)
        {
            lifetime = recoveryOptions.Value.ResolveTemporaryPasswordLifetime();
            if (lifetime is null)
            {
                return await RollbackAsync(transaction, FounderRecoveryStatus.ConfigurationMissing, cancellationToken);
            }
        }

        // Step 4: the expected Version is the one the operator confirmed, not the one read under the lock.
        if (user.Version != confirmed.UserVersion || credential.Version != confirmed.CredentialVersion)
        {
            return await RollbackAsync(transaction, FounderRecoveryStatus.ConcurrentChange, cancellationToken);
        }

        dbContext.Entry(user).Property(candidate => candidate.Version).OriginalValue = confirmed.UserVersion;
        dbContext.Entry(credential).Property(candidate => candidate.Version).OriginalValue = confirmed.CredentialVersion;

        DateTimeOffset? temporaryPasswordExpiresAtUtc = null;
        string successEventType;
        if (subcommand == FounderRecoverySubcommand.Reactivate)
        {
            user.Reactivate(nowUtc);
            credential.ApplyFounderReactivation(passwordHash, passwordHasher.FormatVersion, nowUtc, Guid.NewGuid());
            successEventType = SecurityEventTypesV1.FounderRecovered;
        }
        else
        {
            temporaryPasswordExpiresAtUtc = nowUtc + lifetime!.Value;
            credential.IssueTemporaryPassword(
                passwordHash,
                passwordHasher.FormatVersion,
                nowUtc,
                temporaryPasswordExpiresAtUtc.Value,
                Guid.NewGuid());
            successEventType = SecurityEventTypesV1.FounderPasswordReset;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            return await RollbackAsync(transaction, FounderRecoveryStatus.ConcurrentChange, cancellationToken);
        }

        try
        {
            var restored = subcommand == FounderRecoverySubcommand.Reactivate
                ? await dbContext.RoleAssignments.AsNoTracking()
                    .Where(assignment => assignment.UserId == user.Id && assignment.Status == RoleAssignmentStatus.Active
                        && assignment.ValidFromUtc <= nowUtc && (assignment.ValidUntilUtc == null || assignment.ValidUntilUtc > nowUtc)
                        && dbContext.Roles.Any(role => role.Id == assignment.RoleId && role.IsActive))
                    .Select(assignment => new RestoredAssignment(assignment.CompanyId, assignment.Id)).ToArrayAsync(cancellationToken)
                : [];
            var auditCompanies = precondition.CompanyIds.Concat(restored.Select(assignment => assignment.CompanyId)).Distinct().Order().ToArray();
            if (auditCompanies.Any(company => !companyIds.Contains(company))) throw new InvalidOperationException("An effective assignment has no known company.");
            if (auditCompanies.Length > 0)
            {
                var auditRows = auditCompanies.Select(company => new FounderRecoveryCompanyAuditV1(company, user.Id,
                    subcommand == FounderRecoverySubcommand.Reactivate ? FounderRecoverySystemAuditOperationV1.Reactivate : FounderRecoverySystemAuditOperationV1.ResetPassword,
                    new(declaration.OperatingSystemUser, declaration.MachineName), precondition.CompanyIds.Order().ToArray(),
                    restored.Where(assignment => assignment.CompanyId == company).Select(assignment => assignment.AssignmentId).Order().ToArray(),
                    confirmed.UserVersion, user.Version, nowUtc, correlationContext.CorrelationId, correlationContext.CausationId, idempotencyKey)).ToArray();
                await recoveryAudit.AppendAsync(auditRows, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            dbContext.ChangeTracker.Clear();
            return await RollbackAsync(transaction, FounderRecoveryStatus.AuditRefused, cancellationToken);
        }

        var written = await securityEventWriter.WriteAsync(NewSuccessEvent(successEventType, nowUtc), cancellationToken);
        if (!written.CallerMayContinue)
        {
            dbContext.ChangeTracker.Clear();
            return await RollbackAsync(transaction, FounderRecoveryStatus.SecurityEventRefused, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new FounderRecoveryResult(
            FounderRecoveryStatus.Completed,
            user.Id,
            precondition.CompanyIds,
            [],
            temporaryPasswordExpiresAtUtc);
    }

    private SecurityEventV1 NewSuccessEvent(string eventType, DateTimeOffset nowUtc) => new()
    {
        EventId = Guid.NewGuid(),
        OccurredAtUtc = nowUtc.ToUniversalTime(),
        ScopeKind = SecurityEventScopeKindsV1.Platform,
        ActorType = SecurityEventActorTypesV1.System,
        ActorId = SystemActorId,
        EventType = eventType,
        Outcome = SecurityEventOutcomesV1.Allowed,
        Severity = SecurityEventSeveritiesV1.High,
        CorrelationId = correlationContext.CorrelationId,
        CausationId = correlationContext.CausationId,
        SourceChannel = correlationContext.SourceChannel,
    };

    /// <summary>One record per refused run; it carries the closed reason only, never the user or the detailed code.</summary>
    private SecurityEventV1 NewRefusedEvent() => new()
    {
        EventId = Guid.NewGuid(),
        OccurredAtUtc = timeProvider.GetUtcNow().ToUniversalTime(),
        ScopeKind = SecurityEventScopeKindsV1.Platform,
        ActorType = SecurityEventActorTypesV1.System,
        ActorId = SystemActorId,
        EventType = SecurityEventTypesV1.FounderRecoveryRefused,
        Outcome = SecurityEventOutcomesV1.Denied,
        ReasonCode = SecurityEventReasonCodesV1.PolicyRefused,
        Severity = SecurityEventSeveritiesV1.High,
        CorrelationId = correlationContext.CorrelationId,
        CausationId = correlationContext.CausationId,
        SourceChannel = correlationContext.SourceChannel,
    };

    /// <summary>The server and catalog the operator confirms; any password or other key of the connection string is never read.</summary>
    private (string? Server, string? Catalog) DescribeDatabase()
    {
        if (!dbContext.Database.IsRelational())
        {
            return (null, null);
        }

        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return (null, null);
        }

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return (FirstValue(builder, DatabaseServerKeys), FirstValue(builder, DatabaseCatalogKeys));
    }

    private static string? FirstValue(DbConnectionStringBuilder builder, IReadOnlyList<string> keys)
    {
        foreach (var key in keys)
        {
            if (builder.TryGetValue(key, out var value) && value?.ToString() is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    private static async Task<FounderRecoveryResult> RollbackAsync(
        IDbContextTransaction transaction,
        FounderRecoveryStatus status,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return FounderRecoveryResult.Of(status);
    }

    private sealed record RestoredAssignment(Guid CompanyId, Guid AssignmentId);

    private sealed record FounderSnapshot(
        Guid UserId,
        bool IsActive,
        string DisplayName,
        long UserVersion,
        string UserName,
        long CredentialVersion);

    private sealed record PreRead(
        FounderRecoveryStatus? Refusal,
        FounderSnapshot? Founder,
        IReadOnlyList<FounderRecoveryCompany> Companies)
    {
        public static PreRead Refused(FounderRecoveryStatus status) => new(status, null, []);
    }

    private sealed record Precondition(FounderRecoveryStatus? Refusal, IReadOnlyList<Guid> CompanyIds)
    {
        public static Precondition Refused(FounderRecoveryStatus status) => new(status, []);
    }
}
