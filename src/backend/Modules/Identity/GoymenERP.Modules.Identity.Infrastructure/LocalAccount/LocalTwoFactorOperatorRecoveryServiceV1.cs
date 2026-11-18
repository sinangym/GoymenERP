using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using GoymenERP.Modules.Identity.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

/// <summary>KIM-D27 explicit server operator recovery. No company, membership, current-session or role is invented.
/// Each invocation owns fresh contexts; neither an existing scope's tracker nor another command's transaction is reused.</summary>
internal sealed class LocalTwoFactorOperatorRecoveryServiceV1(DbContextOptions<IdentityDbContext> options,
    ILocalTwoFactorOperatorRecoveryAuditV1 audit, ILocalTwoFactorOperatorRecoverySecurityWriterV1 security,
    ISecurityEventWriterV1 refusalWriter, ICorrelationContext correlation, TimeProvider clock) : ILocalTwoFactorOperatorRecoveryServiceV1
{
    private sealed record RequestProof(Guid TargetId, string TargetInputHash, string OperatorHash, string Reason,
        long UserVersion, long CredentialVersion, long SecretVersion, bool Enabled);

    public async Task<LocalTwoFactorOperatorRecoveryResultV1> RunAsync(bool invocationValid,
        ILocalTwoFactorOperatorRecoveryConsoleV1 console, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(console);
        if (!invocationValid) return await RefuseAsync(LocalTwoFactorOperatorRecoveryStatusV1.InvalidInvocation, cancellationToken);
        if (!console.IsInteractive) return await RefuseAsync(LocalTwoFactorOperatorRecoveryStatusV1.NotInteractive, cancellationToken);
        var normalizedTarget = LocalUserName.TryNormalize(console.ReadTargetUserName() ?? string.Empty);
        var key = console.ReadIdempotencyKey(); var reason = console.ReadReason(); var op = console.ReadOperatorDeclaration();
        if (normalizedTarget is null || !Canonical(key, 128) || !Canonical(reason, 1000) || op is null
            || !Canonical(op.OperatingSystemUser, 256) || !Canonical(op.MachineName, 256)
            || !correlation.IsResolved || correlation.SourceChannel != SourceChannels.System)
            return await RefuseAsync(LocalTwoFactorOperatorRecoveryStatusV1.InvalidRequest, cancellationToken);
        var keyHash = Hash(key!); var targetHash = Hash(normalizedTarget);
        var operatorHash = Hash(JsonSerializer.Serialize(op));
        try
        {
            await using var reader = new IdentityDbContext(options);
            if (!reader.Database.IsRelational()) return await RefuseAsync(LocalTwoFactorOperatorRecoveryStatusV1.SourceUnavailable, cancellationToken);
            var recorded = await reader.LocalTwoFactorOperatorRecoveryAttempts.AsNoTracking().SingleOrDefaultAsync(row => row.KeyHash == keyHash, cancellationToken);
            if (recorded is not null && !Matches(recorded, targetHash, operatorHash, reason!))
                return await RefuseAsync(LocalTwoFactorOperatorRecoveryStatusV1.IdempotencyConflict, cancellationToken);
            var credential = recorded is null
                ? await reader.LocalCredentials.AsNoTracking().SingleOrDefaultAsync(row => row.NormalizedUserName == normalizedTarget, cancellationToken)
                : await reader.LocalCredentials.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == recorded.TargetUserId, cancellationToken);
            if (credential is null) return await RefuseAsync(LocalTwoFactorOperatorRecoveryStatusV1.TargetNotFound, cancellationToken);
            var target = await reader.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == credential.UserId, cancellationToken);
            var secret = await reader.LocalTwoFactorSecrets.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == credential.UserId, cancellationToken);
            if (target is null || recorded is null && credential.TwoFactorEnabled != (secret is { IsActive: true }))
                return await RefuseAsync(LocalTwoFactorOperatorRecoveryStatusV1.SourceUnavailable, cancellationToken);
            var confirmed = recorded is null
                ? new LocalTwoFactorRecoveryVersionAuditV1(target.Version, credential.Version, secret?.Version ?? 0, credential.TwoFactorEnabled)
                : new LocalTwoFactorRecoveryVersionAuditV1(recorded.ConfirmedUserVersion, recorded.ConfirmedCredentialVersion, recorded.ConfirmedSecretVersion, recorded.ConfirmedTwoFactorEnabled);
            var connection = reader.Database.GetDbConnection();
            if (!console.Confirm(new LocalTwoFactorOperatorRecoveryConfirmationV1(target.Id, credential.UserName, target.IsActive, confirmed.UserVersion, confirmed.CredentialVersion,
                    confirmed.SecretVersion, confirmed.TwoFactorEnabled, recorded is not null, key!, reason!, connection.DataSource, connection.Database, op)))
                return await RefuseAsync(LocalTwoFactorOperatorRecoveryStatusV1.NotConfirmed, cancellationToken);
            // Confirmation is outside a transaction. Everything affecting the mutation is re-read under Serializable.
            await using var owner = new IdentityDbContext(options);
            await using var transaction = await owner.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var winner = await owner.LocalTwoFactorOperatorRecoveryAttempts.AsNoTracking().SingleOrDefaultAsync(row => row.KeyHash == keyHash, cancellationToken);
            if (winner is not null)
            {
                if (!Matches(winner, targetHash, operatorHash, reason!))
                    return await RefuseAsync(LocalTwoFactorOperatorRecoveryStatusV1.IdempotencyConflict, cancellationToken);
                // The original answer is authoritative: do not inspect or disable a newly enrolled factor.
                return Result(winner, replay: true);
            }
            if (recorded is not null) return new LocalTwoFactorOperatorRecoveryResultV1(LocalTwoFactorOperatorRecoveryStatusV1.SourceUnavailable);
            var freshUser = await owner.Users.AsNoTracking().SingleOrDefaultAsync(row => row.Id == target.Id, cancellationToken);
            var freshCredential = await owner.LocalCredentials.SingleOrDefaultAsync(row => row.UserId == target.Id, cancellationToken);
            var freshSecret = await owner.LocalTwoFactorSecrets.SingleOrDefaultAsync(row => row.UserId == target.Id, cancellationToken);
            if (freshUser is null || freshCredential is null || freshCredential.TwoFactorEnabled != (freshSecret is { IsActive: true }))
                return new LocalTwoFactorOperatorRecoveryResultV1(LocalTwoFactorOperatorRecoveryStatusV1.SourceUnavailable);
            var before = new LocalTwoFactorRecoveryVersionAuditV1(freshUser.Version, freshCredential.Version, freshSecret?.Version ?? 0, freshCredential.TwoFactorEnabled);
            var outcome = before != confirmed ? LocalTwoFactorOperatorRecoveryOutcome.ConcurrentChange
                : !before.TwoFactorEnabled ? LocalTwoFactorOperatorRecoveryOutcome.AlreadyDisabled : LocalTwoFactorOperatorRecoveryOutcome.Recovered;
            if (outcome == LocalTwoFactorOperatorRecoveryOutcome.Recovered)
            {
                if (freshCredential.Version == long.MaxValue || freshSecret!.Version == long.MaxValue)
                    return new LocalTwoFactorOperatorRecoveryResultV1(LocalTwoFactorOperatorRecoveryStatusV1.SourceUnavailable);
                freshSecret.Deactivate();
                freshCredential.ChangeTwoFactor(false, Guid.NewGuid());
            }
            var after = new LocalTwoFactorRecoveryVersionAuditV1(freshUser.Version, freshCredential.Version, freshSecret?.Version ?? 0, freshCredential.TwoFactorEnabled);
            var id = Guid.NewGuid(); var now = clock.GetUtcNow().ToUniversalTime();
            var attempt = new LocalTwoFactorOperatorRecoveryAttempt(id, target.Id, keyHash,
                Fingerprint(target.Id, targetHash, operatorHash, reason!, confirmed), targetHash, operatorHash, reason!, confirmed.UserVersion,
                confirmed.CredentialVersion, confirmed.SecretVersion, confirmed.TwoFactorEnabled, outcome,
                after.CredentialVersion, after.SecretVersion, now, correlation.CorrelationId);
            owner.LocalTwoFactorOperatorRecoveryAttempts.Add(attempt);
            await owner.SaveChangesAsync(cancellationToken);
            try
            {
                await audit.AppendAsync(transaction.GetDbTransaction(), new LocalTwoFactorOperatorRecoveryAuditV1(id, target.Id, new LocalTwoFactorRecoveryOperatorAuditV1(op.OperatingSystemUser, op.MachineName),
                    confirmed, before, after, (LocalTwoFactorOperatorRecoveryAuditOutcomeV1)outcome, reason!, now,
                    correlation.CorrelationId, correlation.CausationId, keyHash), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return new LocalTwoFactorOperatorRecoveryResultV1(LocalTwoFactorOperatorRecoveryStatusV1.AuditUnavailable); }
            try
            {
                await security.AppendAsync(transaction.GetDbTransaction(), new LocalTwoFactorOperatorRecoverySecurityV1(id, outcome == LocalTwoFactorOperatorRecoveryOutcome.Recovered,
                    now, correlation.CorrelationId, correlation.CausationId), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception) { return new LocalTwoFactorOperatorRecoveryResultV1(LocalTwoFactorOperatorRecoveryStatusV1.SecurityEventUnavailable); }
            await transaction.CommitAsync(cancellationToken);
            return Result(attempt, replay: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        // A lost/uncertain commit never reports success. The same key safely discovers its original result next run.
        catch (Exception error) when (error is SqlException or DbUpdateException or InvalidOperationException or ArgumentException or JsonException)
        { return new LocalTwoFactorOperatorRecoveryResultV1(LocalTwoFactorOperatorRecoveryStatusV1.SourceUnavailable); }
    }

    private async Task<LocalTwoFactorOperatorRecoveryResultV1> RefuseAsync(LocalTwoFactorOperatorRecoveryStatusV1 status, CancellationToken ct)
    {
        if (correlation.IsResolved)
            _ = await refusalWriter.WriteAsync(new LocalTwoFactorOperatorRecoverySecurityV1(Guid.NewGuid(), false,
                clock.GetUtcNow().ToUniversalTime(), correlation.CorrelationId, correlation.CausationId).ToEvent(), ct);
        return new LocalTwoFactorOperatorRecoveryResultV1(status);
    }
    private static bool Canonical(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum
        && value.Trim() == value && !value.Any(char.IsControl);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Fingerprint(Guid target, string input, string op, string reason, LocalTwoFactorRecoveryVersionAuditV1 confirmed) =>
        Hash(JsonSerializer.Serialize(new RequestProof(target, input, op, reason, confirmed.UserVersion, confirmed.CredentialVersion, confirmed.SecretVersion, confirmed.TwoFactorEnabled)));
    private static bool Matches(LocalTwoFactorOperatorRecoveryAttempt row, string target, string op, string reason)
    {
        // Revalidate persisted evidence before an immutable replay; malformed rows never become success.
        _ = new LocalTwoFactorOperatorRecoveryAttempt(row.Id, row.TargetUserId, row.KeyHash, row.RequestHash, row.TargetInputHash, row.OperatorHash,
            row.Reason, row.ConfirmedUserVersion, row.ConfirmedCredentialVersion, row.ConfirmedSecretVersion, row.ConfirmedTwoFactorEnabled,
            row.Outcome, row.ResultCredentialVersion, row.ResultSecretVersion, row.RecordedAtUtc, row.OriginalCorrelationId);
        var original = new LocalTwoFactorRecoveryVersionAuditV1(row.ConfirmedUserVersion, row.ConfirmedCredentialVersion, row.ConfirmedSecretVersion, row.ConfirmedTwoFactorEnabled);
        return row.TargetInputHash == target && row.OperatorHash == op && row.Reason == reason
            && row.RequestHash == Fingerprint(row.TargetUserId, target, op, reason, original);
    }
    private static LocalTwoFactorOperatorRecoveryResultV1 Result(LocalTwoFactorOperatorRecoveryAttempt row, bool replay) =>
        new LocalTwoFactorOperatorRecoveryResultV1((LocalTwoFactorOperatorRecoveryStatusV1)row.Outcome, row.TargetUserId, row.ResultCredentialVersion, replay, row.OriginalCorrelationId, row.Id);
}
