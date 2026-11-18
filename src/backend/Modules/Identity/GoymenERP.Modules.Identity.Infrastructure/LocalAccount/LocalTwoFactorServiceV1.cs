using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Assignments;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

/// <summary>KIM-D3/D27 optional self enrollment, factor removal and one-time recovery-code delivery.
/// The actor is the cookie's actual local user, never a target from the body. No administrative permission is invented.</summary>
internal sealed class LocalTwoFactorServiceV1(IdentityDbContext database, ICurrentUser user, ICompanyContext company,
    ILocalPasswordHasher hasher, ILocalTwoFactorProtectionV1 protection, ILocalCredentialSelfServiceAuditWriterV1 audit,
    TimeProvider clock) : ILocalTwoFactorServiceV1
{
    private sealed record Enrollment(Guid UserId, Guid Stamp, long Version, string Secret, IReadOnlyList<string> RecoveryHashes)
    { public override string ToString() => nameof(Enrollment); }

    public async Task<LocalTwoFactorStateV1?> ReadAsync(Guid userId, Guid stamp, CancellationToken ct)
    {
        var credential = await RequireSessionAsync(userId, stamp, ct);
        if (credential is null) return null;
        var secret = await database.LocalTwoFactorSecrets.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == userId, ct);
        if (credential.TwoFactorEnabled != (secret is { IsActive: true })) throw new InvalidOperationException("Two factor owner state is unavailable.");
        if (await RequireSessionAsync(userId, stamp, ct) is null) return null;
        return new LocalTwoFactorStateV1(Text(credential.Version), credential.TwoFactorEnabled, credential.TwoFactorEnabled ? secret!.RecoveryCodeCount : 0);
    }

    public async Task<LocalTwoFactorEnrollmentV1?> BeginEnrollmentAsync(Guid userId, Guid stamp, LocalTwoFactorProofV1 request, CancellationToken ct)
    {
        var credential = await RequireSessionAsync(userId, stamp, ct);
        if (credential is null) throw new LocalTwoFactorSessionNotCurrentException();
        if (credential.TwoFactorEnabled || request.ExpectedVersion != Text(credential.Version)
            || hasher.Verify(credential.PasswordHash, request.CurrentPassword ?? string.Empty) == LocalPasswordVerification.Failed) return null;
        var secret = LocalTotpV1.NewSecret(); var codes = LocalTotpV1.NewRecoveryCodes();
        var token = protection.Protect(userId, "enrollment", JsonSerializer.Serialize(new Enrollment(userId, stamp,
            credential.Version, secret, codes.Select(LocalTotpV1.RecoveryHash).ToArray())));
        if ((await RequireSessionAsync(userId, stamp, ct))?.Version != credential.Version) return null;
        var uri = $"otpauth://totp/{Uri.EscapeDataString("Göymen ERP:" + credential.UserName)}?secret={secret}&issuer={Uri.EscapeDataString("Göymen ERP")}&algorithm=SHA1&digits=6&period=30";
        // Read-only preparation: the token is encrypted and bound to the current stamp/version. No seed/code is
        // persisted until the user proves possession with a TOTP code. The response is NoStore and never logged.
        return new LocalTwoFactorEnrollmentV1(Text(credential.Version), secret, uri, codes, token);
    }

    public async Task<LocalTwoFactorCommandResultV1> ChangeAsync(Guid userId, Guid stamp, string action,
        LocalTwoFactorProofV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken ct)
    {
        if (database.ChangeTracker.HasChanges() || database.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Self credential command requires its own clean unit of work.");
        try { return await ChangeCoreAsync(userId, stamp, action, request, metadata, ct); }
        finally { database.ChangeTracker.Clear(); }
    }

    private async Task<LocalTwoFactorCommandResultV1> ChangeCoreAsync(Guid userId, Guid stamp, string action,
        LocalTwoFactorProofV1 request, IdentityAccessCommandMetadataV1 metadata, CancellationToken ct)
    {
        if (action is not ("enable" or "disable" or "recovery-codes") || !long.TryParse(request.ExpectedVersion, NumberStyles.None,
            CultureInfo.InvariantCulture, out var expected) || expected < 1 || request.ExpectedVersion != Text(expected)
            || string.IsNullOrWhiteSpace(request.CurrentPassword)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > LocalCredentialCommand.IdempotencyKeyMaxLength)
            return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.InvalidRequest);
        var credential = await RequireSessionAsync(userId, stamp, ct);
        if (credential is null) return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.SessionNotCurrent);
        if (metadata.CompanyId != company.CompanyId || metadata.ActorId != user.UserId || string.IsNullOrWhiteSpace(metadata.CorrelationId))
            throw new UnauthorizedAccessException();
        if (hasher.Verify(credential.PasswordHash, request.CurrentPassword) == LocalPasswordVerification.Failed)
            return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.InvalidProof);
        var operation = "identity.local_account.two_factor." + action;
        var fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes($"{operation}|{metadata.IdempotencyKey}|{userId:D}|{expected.ToString(CultureInfo.InvariantCulture)}"));
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var fresh = await RequireSessionAsync(userId, stamp, ct);
        if (fresh is null || fresh.PasswordHash != credential.PasswordHash) return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.SessionNotCurrent);
        var recorded = await database.LocalCredentialCommands.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == userId
            && row.Operation == operation && row.IdempotencyKey == metadata.IdempotencyKey, ct);
        if (recorded is not null)
        {
            if (string.IsNullOrWhiteSpace(recorded.OriginalCorrelationId)) throw new InvalidOperationException("Two factor replay evidence is incomplete.");
            return CryptographicOperations.FixedTimeEquals(recorded.RequestFingerprint, fingerprint)
                ? new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.Replayed, Text(recorded.ResultVersion), null, action != "disable", null, recorded.OriginalCorrelationId)
                : new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.IdempotencyConflict);
        }
        if (fresh.Version != expected) return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.VersionConflict);
        var tracked = await database.LocalCredentials.SingleAsync(row => row.UserId == userId, ct);
        if (tracked.Version != expected || tracked.SecurityStamp != stamp) return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.VersionConflict);
        var secretRow = await database.LocalTwoFactorSecrets.SingleOrDefaultAsync(row => row.UserId == userId, ct);
        IReadOnlyList<string>? newCodes = null;
        LocalCredentialSelfServiceOperationV1 auditOperation;
        if (action == "enable")
        {
            if (tracked.TwoFactorEnabled) return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.StateConflict);
            Enrollment? enrollment;
            try { enrollment = JsonSerializer.Deserialize<Enrollment>(protection.Unprotect(userId, "enrollment", request.EnrollmentToken ?? string.Empty)); }
            catch (Exception error) when (error is CryptographicException or JsonException or ArgumentException) { return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.InvalidProof); }
            if (enrollment is null || enrollment.UserId != userId || enrollment.Stamp != stamp || enrollment.Version != expected)
                return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.InvalidProof);
            var step = LocalTotpV1.Match(enrollment.Secret, request.Code, clock.GetUtcNow(), null);
            if (step is null) return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.InvalidProof);
            var protectedSecret = protection.Protect(userId, "authenticator", enrollment.Secret);
            if (secretRow is null) database.LocalTwoFactorSecrets.Add(new LocalTwoFactorSecret(userId, protectedSecret, enrollment.RecoveryHashes, step.Value));
            else secretRow.Replace(protectedSecret, enrollment.RecoveryHashes, step.Value);
            tracked.ChangeTwoFactor(true, Guid.NewGuid()); auditOperation = LocalCredentialSelfServiceOperationV1.EnableTwoFactor;
        }
        else
        {
            if (!tracked.TwoFactorEnabled || secretRow is not { IsActive: true }) return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.StateConflict);
            if (!LocalTwoFactorProofVerifierV1.Consume(secretRow, request.Code, protection, clock.GetUtcNow())) return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.InvalidProof);
            if (action == "disable")
            {
                secretRow.Deactivate(); tracked.ChangeTwoFactor(false, Guid.NewGuid());
                auditOperation = LocalCredentialSelfServiceOperationV1.DisableTwoFactor;
            }
            else
            {
                newCodes = LocalTotpV1.NewRecoveryCodes(); secretRow.Regenerate(newCodes.Select(LocalTotpV1.RecoveryHash).ToArray());
                tracked.ChangeTwoFactor(true, Guid.NewGuid()); auditOperation = LocalCredentialSelfServiceOperationV1.RegenerateRecoveryCodes;
            }
        }
        // Snapshot only evidence of the authenticated operation; never the proof, seed, protected token or code hashes.
        database.LocalCredentialCommands.Add(new LocalCredentialCommand(Guid.NewGuid(), userId, operation, metadata.IdempotencyKey,
            fingerprint, tracked.Version, clock.GetUtcNow(), metadata.CorrelationId));
        if ((await RequireSessionAsync(userId, stamp, ct))?.Version != expected) return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.SessionNotCurrent);
        try
        {
            await database.SaveChangesAsync(ct);
            await audit.AppendAsync(transaction.GetDbTransaction(), new LocalCredentialSelfServiceAuditV1(company.CompanyId, userId, userId, auditOperation, expected,
                tracked.Version, "TOTP", clock.GetUtcNow(), AuditReason(action), metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey), ct);
            if (await RequireSessionAsync(userId, tracked.SecurityStamp, ct) is null) throw new UnauthorizedAccessException();
            await transaction.CommitAsync(ct);
            return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.Applied, Text(tracked.Version), tracked.SecurityStamp, tracked.TwoFactorEnabled, newCodes, metadata.CorrelationId);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct); database.ChangeTracker.Clear(); return new LocalTwoFactorCommandResultV1(LocalTwoFactorCommandStatusV1.VersionConflict);
        }
    }

    private async Task<LocalCredential?> RequireSessionAsync(Guid id, Guid stamp, CancellationToken ct)
    {
        if (!user.IsAuthenticated || user.UserId != id.ToString("D")) return null;
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.CanAccessCompany(company.CompanyId)) throw new UnauthorizedAccessException();
        var actor = user.UserId; var companyId = company.CompanyId; var now = clock.GetUtcNow();
        var credential = await database.LocalCredentials.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == id && row.SecurityStamp == stamp
            && !row.MustChangePassword && database.Users.Any(account => account.Id == id && account.IsActive), ct);
        if (credential is null) return null;
        if (!await database.RoleAssignments.AsNoTracking().AnyAsync(assignment => assignment.CompanyId == companyId && assignment.UserId == id
            && assignment.Status == RoleAssignmentStatus.Active && assignment.ValidFromUtc <= now && (assignment.ValidUntilUtc == null || assignment.ValidUntilUtc > now)
            && assignment.Scopes.Any(scope => scope.CompanyId == companyId)
            && database.Roles.Any(role => role.Id == assignment.RoleId && role.IsActive), ct)) throw new UnauthorizedAccessException();
        if (!user.IsAuthenticated || user.UserId != actor) return null;
        if (company.CompanyId != companyId || !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException();
        return credential;
    }
    private static string Text(long version) => version.ToString(CultureInfo.InvariantCulture);
    private static string AuditReason(string action) => action switch
    {
        "enable" => "Kullanıcı kendi TOTP doğrulayıcısını etkinleştirdi.",
        "disable" => "Kullanıcı kendi iki aşamalı doğrulamasını kapattı.",
        _ => "Kullanıcı kendi tek kullanımlık kurtarma kodlarını yeniledi.",
    };
}
