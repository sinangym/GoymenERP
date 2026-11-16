using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

/// <summary>
/// ID-2 password change (spec "Durum geçişleri": <c>MustChangePassword=true</c> → change → <c>false</c>). Order:
/// guarded read of the session's credential, idempotent replay, expected version, current password, policy, one
/// conditional write together with the idempotency record. A wrong current password is refused without touching
/// the lockout counter (the session already proves the account; KS-8 counts login attempts only). Every refusal
/// writes nothing.
/// </summary>
internal sealed class LocalAccountPasswordChangeService(
    ILocalCredentialCommandStore store,
    ILocalPasswordHasher passwordHasher,
    ILocalPasswordContextTermSource contextTermSource,
    TimeProvider timeProvider,
    IOptions<LocalAccountOptions> options)
    : ILocalAccountPasswordChangeService
{
    public async Task<LocalAccountPasswordChangeResult> ChangeAsync(
        LocalAccountPasswordChangeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.IdempotencyKey);
        var fingerprint = ComputeFingerprint(command.IdempotencyKey, command.UserId, command.ExpectedVersion);

        var state = await store.FindForCommandAsync(command.UserId, cancellationToken);
        if (state is null || !state.UserIsActive || state.SecurityStamp != command.SessionSecurityStamp)
        {
            // A replay is only answered to the session the success re-issued (its stamp is the stored one).
            return LocalAccountPasswordChangeResult.Of(LocalAccountPasswordChangeStatus.SessionNotCurrent);
        }

        var recorded = await store.FindCommandAsync(
            command.UserId,
            LocalCredentialCommand.PasswordChangeOperation,
            command.IdempotencyKey,
            cancellationToken);
        if (recorded is not null)
        {
            return Replay(recorded, fingerprint);
        }

        if (LocalTemporaryPasswordRules.BlocksSession(
                state.TemporaryPasswordIssuedAtUtc,
                state.TemporaryPasswordConsumedAtUtc))
        {
            // KIM-D23: an unconsumed temporary password has no session yet. Once a login consumed it, the fenced
            // session may change the password; the change clears the three temporary fields.
            return LocalAccountPasswordChangeResult.Of(LocalAccountPasswordChangeStatus.SessionNotCurrent);
        }

        if (state.Version != command.ExpectedVersion)
        {
            return new LocalAccountPasswordChangeResult(
                LocalAccountPasswordChangeStatus.VersionConflict,
                null,
                state.Version,
                []);
        }

        if (passwordHasher.Verify(state.PasswordHash, command.CurrentPassword ?? string.Empty)
            == LocalPasswordVerification.Failed)
        {
            return LocalAccountPasswordChangeResult.Of(LocalAccountPasswordChangeStatus.CurrentPasswordInvalid);
        }

        var newPassword = command.NewPassword ?? string.Empty;
        var companyTerms = await contextTermSource.GetCompanyTermsAsync(cancellationToken);
        var violations = options.Value.ToPasswordPolicy().Evaluate(newPassword, [state.UserName, .. companyTerms]);
        if (violations.Count > 0)
        {
            return new LocalAccountPasswordChangeResult(
                LocalAccountPasswordChangeStatus.PolicyViolation,
                null,
                null,
                violations);
        }

        var nowUtc = timeProvider.GetUtcNow();
        var newStamp = Guid.NewGuid();
        var resultVersion = state.Version + 1;
        var record = new LocalCredentialCommand(
            Guid.NewGuid(),
            state.UserId,
            LocalCredentialCommand.PasswordChangeOperation,
            command.IdempotencyKey,
            fingerprint,
            resultVersion,
            nowUtc);
        bool changed;
        try
        {
            changed = await store.TryChangePasswordAsync(
                state,
                passwordHasher.Hash(newPassword),
                passwordHasher.FormatVersion,
                newStamp,
                nowUtc,
                record,
                cancellationToken);
        }
        catch (LocalCredentialCommandKeyTakenException)
        {
            // A concurrent request with the same key won; nothing of this request was written. Its session stamp
            // is no longer current either, so the caller re-authenticates and replays with the re-issued session.
            var winner = await store.FindCommandAsync(
                command.UserId,
                LocalCredentialCommand.PasswordChangeOperation,
                command.IdempotencyKey,
                cancellationToken);
            return winner is null
                ? LocalAccountPasswordChangeResult.Of(LocalAccountPasswordChangeStatus.IdempotencyConflict)
                : CryptographicOperations.FixedTimeEquals(winner.RequestFingerprint, fingerprint)
                    ? LocalAccountPasswordChangeResult.Of(LocalAccountPasswordChangeStatus.SessionNotCurrent)
                    : LocalAccountPasswordChangeResult.Of(LocalAccountPasswordChangeStatus.IdempotencyConflict);
        }

        if (!changed)
        {
            // The row moved after the read: another change, a logout (stamp) or a login rehash. The session
            // check decides on the next request; this one reports the conflict with the version now stored.
            var current = await store.FindForCommandAsync(command.UserId, cancellationToken);
            return current is null || current.SecurityStamp != command.SessionSecurityStamp
                ? LocalAccountPasswordChangeResult.Of(LocalAccountPasswordChangeStatus.SessionNotCurrent)
                : new LocalAccountPasswordChangeResult(
                    LocalAccountPasswordChangeStatus.VersionConflict,
                    null,
                    current.Version,
                    []);
        }

        return new LocalAccountPasswordChangeResult(
            LocalAccountPasswordChangeStatus.Changed,
            newStamp,
            resultVersion,
            []);
    }

    internal static byte[] ComputeFingerprint(string idempotencyKey, Guid userId, long expectedVersion)
    {
        var material = string.Join(
            '|',
            LocalCredentialCommand.PasswordChangeOperation,
            idempotencyKey,
            userId.ToString("D", CultureInfo.InvariantCulture),
            expectedVersion.ToString(CultureInfo.InvariantCulture));
        return SHA256.HashData(Encoding.UTF8.GetBytes(material));
    }

    private static LocalAccountPasswordChangeResult Replay(LocalCredentialCommandRecord recorded, byte[] fingerprint) =>
        CryptographicOperations.FixedTimeEquals(recorded.RequestFingerprint, fingerprint)
            ? new LocalAccountPasswordChangeResult(
                LocalAccountPasswordChangeStatus.Replayed,
                null,
                recorded.ResultVersion,
                [])
            : LocalAccountPasswordChangeResult.Of(LocalAccountPasswordChangeStatus.IdempotencyConflict);
}
