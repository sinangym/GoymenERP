using System.Data;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Bootstrap;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Domain.Outbox;
using GoymenERP.Modules.Identity.Domain.Users;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Identity.Infrastructure.Bootstrap;

/// <summary>
/// ID-2 founder bootstrap (spec "Kurucu bootstrap komutu", KS-5; KIM-D5, KIM-D18). Order: terminal check (nothing
/// is read before it), configuration and preconditions without a lock, the interactive input outside the
/// transaction, then one serializable transaction that re-checks the preconditions and writes the user, the local
/// credential (<c>MustChangePassword=true</c>), the local external identity link, one indefinite Admin assignment
/// per company, the <c>IdentityBootstrapState</c> marker and the <c>identity.founder.bootstrapped.v1</c> outbox
/// message. A second run fails on the marker's singleton key even when two runs race. The fail-closed
/// <c>identity.permission.changed</c> security events are written before the commit; a refused event rolls the
/// transaction back. Every refusal writes nothing. The Audit row needs a platform Audit contract change (System
/// actor + operator declaration, spec) and is not written here (reported).
/// </summary>
internal sealed class FounderBootstrapService(
    IdentityDbContext dbContext,
    ILocalPasswordHasher passwordHasher,
    ISecurityEventWriterV1 securityEventWriter,
    ICorrelationContext correlationContext,
    TimeProvider timeProvider,
    IOptions<LocalAccountOptions> localAccountOptions,
    IOptions<IdentityBootstrapOptions> bootstrapOptions)
    : IFounderBootstrapService
{
    /// <summary>The system actor token of the command (no user exists before it; never an OS user or machine name).</summary>
    internal const string SystemActorId = "identity.bootstrap-founder";

    internal const string SourceOperation = "identity.founder.bootstrap";

    internal const string AggregateType = "identity.user";

    private const int SqlUniqueIndexViolation = 2601;
    private const int SqlUniqueConstraintViolation = 2627;

    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<FounderBootstrapResult> RunAsync(IFounderBootstrapConsole console, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(console);
        if (!console.IsInteractive)
        {
            return FounderBootstrapResult.Of(FounderBootstrapStatus.NotInteractive);
        }

        var options = localAccountOptions.Value;
        options.EnsureValid();
        var adminRoleCode = bootstrapOptions.Value.AdminRoleCode?.Trim();
        if (string.IsNullOrEmpty(adminRoleCode))
        {
            return FounderBootstrapResult.Of(FounderBootstrapStatus.AdminRoleNotConfigured);
        }

        var nowUtc = timeProvider.GetUtcNow();
        var precondition = await CheckPreconditionsAsync(adminRoleCode, nowUtc, cancellationToken);
        if (precondition.Refusal is { } refusal)
        {
            return FounderBootstrapResult.Of(refusal);
        }

        var userName = console.ReadUserName()?.Trim();
        var normalizedUserName = LocalUserName.TryNormalize(userName);
        if (normalizedUserName is null)
        {
            return FounderBootstrapResult.Of(FounderBootstrapStatus.UserNameInvalid);
        }

        if (await dbContext.LocalCredentials.AsNoTracking()
                .AnyAsync(credential => credential.NormalizedUserName == normalizedUserName, cancellationToken))
        {
            return FounderBootstrapResult.Of(FounderBootstrapStatus.UserNameTaken);
        }

        var displayName = console.ReadDisplayName()?.Trim();
        if (string.IsNullOrEmpty(displayName) || displayName.Length > UserAccount.DisplayNameMaxLength)
        {
            return FounderBootstrapResult.Of(FounderBootstrapStatus.DisplayNameInvalid);
        }

        var password = console.ReadNewPassword() ?? string.Empty;
        var confirmation = console.ReadNewPasswordConfirmation() ?? string.Empty;
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            return FounderBootstrapResult.Of(FounderBootstrapStatus.PasswordMismatch);
        }

        var violations = options.ToPasswordPolicy()
            .Evaluate(password, [userName!, displayName, .. precondition.CompanyTerms]);
        if (violations.Count > 0)
        {
            return new FounderBootstrapResult(FounderBootstrapStatus.PolicyViolation, null, [], violations);
        }

        var passwordHash = passwordHasher.Hash(password);
        return await WriteAsync(userName!, displayName, passwordHash, adminRoleCode, cancellationToken);
    }

    private async Task<Precondition> CheckPreconditionsAsync(
        string adminRoleCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (await dbContext.BootstrapStates.AsNoTracking().AnyAsync(cancellationToken))
        {
            return Precondition.Refused(FounderBootstrapStatus.AlreadyBootstrapped);
        }

        var role = await dbContext.Roles.AsNoTracking()
            .Include(candidate => candidate.Permissions)
            .SingleOrDefaultAsync(candidate => candidate.Code == adminRoleCode, cancellationToken);
        if (role is null || !role.IsActive || !role.Permissions.Any(permission => ErpPermissions.IsCritical(permission.PermissionCode)))
        {
            return Precondition.Refused(FounderBootstrapStatus.AdminRoleInvalid);
        }

        var companies = await dbContext.Companies.AsNoTracking()
            .OrderBy(company => company.Id)
            .Select(company => new { company.Id, company.Code, company.LegalName })
            .ToListAsync(cancellationToken);
        if (companies.Count == 0)
        {
            return Precondition.Refused(FounderBootstrapStatus.NoCompanies);
        }

        if (await AnyUnexpiredAdminAssignmentAsync(role.Id, nowUtc, cancellationToken))
        {
            return Precondition.Refused(FounderBootstrapStatus.AdminAlreadyAssigned);
        }

        return new Precondition(
            null,
            companies.SelectMany(company => new[] { company.Code, company.LegalName })
                .Where(term => !string.IsNullOrWhiteSpace(term))
                .ToArray());
    }

    /// <summary>
    /// Stricter than "effective" (KIM-D17): any unrevoked, unexpired Admin assignment refuses, whatever its scopes or
    /// its user's state, so the one-shot command never runs next to an existing Admin (fail-closed).
    /// </summary>
    private Task<bool> AnyUnexpiredAdminAssignmentAsync(Guid adminRoleId, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        dbContext.RoleAssignments.AsNoTracking()
            .AnyAsync(
                assignment => assignment.RoleId == adminRoleId
                    && assignment.Status == RoleAssignmentStatus.Active
                    && (assignment.ValidUntilUtc == null || assignment.ValidUntilUtc > nowUtc),
                cancellationToken);

    private async Task<FounderBootstrapResult> WriteAsync(
        string userName,
        string displayName,
        string passwordHash,
        string adminRoleCode,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        // The preconditions are read again inside the transaction; the input above was taken without a lock.
        var nowUtc = timeProvider.GetUtcNow();
        if (await dbContext.BootstrapStates.AnyAsync(cancellationToken))
        {
            return await RollbackAsync(transaction, FounderBootstrapStatus.AlreadyBootstrapped, cancellationToken);
        }

        var role = await dbContext.Roles.AsNoTracking()
            .Include(candidate => candidate.Permissions)
            .SingleOrDefaultAsync(candidate => candidate.Code == adminRoleCode, cancellationToken);
        if (role is null || !role.IsActive || !role.Permissions.Any(permission => ErpPermissions.IsCritical(permission.PermissionCode)))
        {
            return await RollbackAsync(transaction, FounderBootstrapStatus.AdminRoleInvalid, cancellationToken);
        }

        var companyIds = await dbContext.Companies.AsNoTracking()
            .OrderBy(company => company.Id)
            .Select(company => company.Id)
            .ToListAsync(cancellationToken);
        if (companyIds.Count == 0)
        {
            return await RollbackAsync(transaction, FounderBootstrapStatus.NoCompanies, cancellationToken);
        }

        if (await AnyUnexpiredAdminAssignmentAsync(role.Id, nowUtc, cancellationToken))
        {
            return await RollbackAsync(transaction, FounderBootstrapStatus.AdminAlreadyAssigned, cancellationToken);
        }

        var normalizedUserName = LocalUserName.TryNormalize(userName)!;
        if (await dbContext.LocalCredentials.AnyAsync(
                credential => credential.NormalizedUserName == normalizedUserName,
                cancellationToken))
        {
            return await RollbackAsync(transaction, FounderBootstrapStatus.UserNameTaken, cancellationToken);
        }

        var userId = Guid.NewGuid();
        dbContext.Users.Add(new UserAccount(userId, displayName, email: null));
        dbContext.LocalCredentials.Add(new LocalCredential(
            userId,
            userName,
            passwordHash,
            passwordHasher.FormatVersion,
            mustChangePassword: true,
            nowUtc,
            Guid.NewGuid()));
        dbContext.ExternalIdentities.Add(new ExternalIdentityLink(
            Guid.NewGuid(),
            userId,
            LocalAccountIdentity.Issuer,
            LocalAccountIdentity.SubjectOf(userId)));
        foreach (var companyId in companyIds)
        {
            // KIM-D5 FounderDirect: the founder's first Admin needs no second approval; indefinite (KIM-D18).
            var assignment = new RoleAssignment(
                Guid.NewGuid(),
                userId,
                role.Id,
                companyId,
                nowUtc,
                validUntilUtc: null,
                approvedByUserId: null);
            assignment.AddScope(AccessScopeType.Company, companyId);
            dbContext.RoleAssignments.Add(assignment);
        }

        dbContext.BootstrapStates.Add(new IdentityBootstrapState(userId, nowUtc));
        dbContext.OutboxMessages.Add(new IdentityOutboxMessage(
            Guid.NewGuid(),
            companyId: null,
            IdentityFounderBootstrappedV1.EventType,
            IdentityFounderBootstrappedV1.SchemaVersion,
            SourceOperation,
            AggregateType,
            userId,
            aggregateVersion: 1,
            JsonSerializer.Serialize(new IdentityFounderBootstrappedV1(userId, nowUtc), PayloadJsonOptions),
            nowUtc,
            correlationContext.CorrelationId,
            correlationContext.CausationId));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            return await RollbackAsync(transaction, FounderBootstrapStatus.AlreadyBootstrapped, cancellationToken);
        }

        foreach (var companyId in companyIds)
        {
            var written = await securityEventWriter.WriteAsync(NewPermissionChangedEvent(companyId, nowUtc), cancellationToken);
            if (!written.CallerMayContinue)
            {
                dbContext.ChangeTracker.Clear();
                return await RollbackAsync(transaction, FounderBootstrapStatus.SecurityEventRefused, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new FounderBootstrapResult(FounderBootstrapStatus.Completed, userId, companyIds, []);
    }

    private SecurityEventV1 NewPermissionChangedEvent(Guid companyId, DateTimeOffset nowUtc) => new()
    {
        EventId = Guid.NewGuid(),
        OccurredAtUtc = nowUtc.ToUniversalTime(),
        ScopeKind = SecurityEventScopeKindsV1.Company,
        CompanyId = companyId,
        ActorType = SecurityEventActorTypesV1.System,
        ActorId = SystemActorId,
        EventType = SecurityEventTypesV1.PermissionChanged,
        Outcome = SecurityEventOutcomesV1.Allowed,
        Severity = SecurityEventSeveritiesV1.High,
        CorrelationId = correlationContext.CorrelationId,
        CausationId = correlationContext.CausationId,
        SourceChannel = correlationContext.SourceChannel,
    };

    private static async Task<FounderBootstrapResult> RollbackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        FounderBootstrapStatus status,
        CancellationToken cancellationToken)
    {
        await transaction.RollbackAsync(cancellationToken);
        return FounderBootstrapResult.Of(status);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: SqlUniqueIndexViolation or SqlUniqueConstraintViolation };

    private sealed record Precondition(FounderBootstrapStatus? Refusal, IReadOnlyList<string> CompanyTerms)
    {
        public static Precondition Refused(FounderBootstrapStatus status) => new(status, []);
    }
}
