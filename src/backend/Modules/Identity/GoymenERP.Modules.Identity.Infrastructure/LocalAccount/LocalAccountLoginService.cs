using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security.Events;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;
using Microsoft.Extensions.Options;

namespace GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

/// <summary>
/// ID-1 local account login (spec "Durum geçişleri", KS-8). Every attempt runs exactly one password verification,
/// also for an unknown user, a locked or an inactive account, so the response time does not tell them apart.
/// Every refusal is the same result for the caller; only the security event carries the reason code. Until E8 is
/// decided the failure event has an anonymous actor and no user name or user name hash.
/// </summary>
internal sealed class LocalAccountLoginService(
    ILocalCredentialStore store,
    ILocalPasswordHasher passwordHasher,
    ISecurityEventWriterV1 securityEventWriter,
    ICorrelationContext correlationContext,
    TimeProvider timeProvider,
    IOptions<LocalAccountOptions> options,
    ILocalTwoFactorLoginV1? twoFactor = null)
    : ILocalAccountLoginService
{
    public Task<LocalAccountLoginResult> LoginAsync(string? userName, string? password, CancellationToken cancellationToken) =>
        LoginAsync(userName, password, null, cancellationToken);

    public async Task<LocalAccountLoginResult> LoginAsync(
        string? userName,
        string? password,
        string? secondFactorCode,
        CancellationToken cancellationToken)
    {
        var suppliedPassword = password ?? string.Empty;
        var normalizedUserName = LocalUserName.TryNormalize(userName);
        var state = normalizedUserName is null || suppliedPassword.Length == 0
            ? null
            : await store.FindForLoginAsync(normalizedUserName, cancellationToken);
        var nowUtc = timeProvider.GetUtcNow();

        if (state is null)
        {
            passwordHasher.VerifyAgainstDummy(suppliedPassword);
            return await RefuseAsync(SecurityEventReasonCodesV1.CredentialsInvalid, nowUtc, cancellationToken);
        }

        if (LocalCredentialLockout.IsLockedOut(state.LockoutEndUtc, nowUtc))
        {
            // KS-8: while locked even the right password is refused; the counter and the lock end stay as they are.
            _ = passwordHasher.Verify(state.PasswordHash, suppliedPassword);
            return await RefuseAsync(SecurityEventReasonCodesV1.PolicyRefused, nowUtc, cancellationToken);
        }

        if (!state.UserIsActive)
        {
            _ = passwordHasher.Verify(state.PasswordHash, suppliedPassword);
            return await RefuseAsync(SecurityEventReasonCodesV1.AccountInactive, nowUtc, cancellationToken);
        }

        var verification = passwordHasher.Verify(state.PasswordHash, suppliedPassword);
        if (verification == LocalPasswordVerification.Failed)
        {
            // A false result means the row changed after the read (another attempt locked it, or the password or
            // stamp changed); the attempt is refused either way and the stored state is not overwritten.
            _ = await store.TryRecordFailedAccessAsync(
                state,
                options.Value.ToLockoutPolicy(),
                nowUtc,
                cancellationToken);
            return await RefuseAsync(SecurityEventReasonCodesV1.CredentialsInvalid, nowUtc, cancellationToken);
        }

        var rehashedPasswordHash = verification == LocalPasswordVerification.SuccessRehashNeeded
            ? passwordHasher.Hash(suppliedPassword)
            : null;
        var rehashedFormatVersion = rehashedPasswordHash is null ? (int?)null : passwordHasher.FormatVersion;

        if (state.TwoFactorEnabled)
        {
            // KS-11: no session, counter reset, rehash or one-time temporary-password consumption before a factor
            // succeeds. Recovery-code/TOTP consumption and the conditional credential update share one SQL transaction.
            var usable = state.TemporaryPasswordIssuedAtUtc is null || LocalTemporaryPasswordRules.IsUsable(
                state.TemporaryPasswordExpiresAtUtc, state.TemporaryPasswordConsumedAtUtc, nowUtc);
            if (usable && twoFactor is null) throw new LocalTwoFactorUnavailableException();
            var completed = usable && twoFactor is not null && await twoFactor.TryCompleteAsync(state, secondFactorCode,
                () => state.TemporaryPasswordIssuedAtUtc is null
                    ? store.TryRecordSuccessfulAccessAsync(state, rehashedPasswordHash, rehashedFormatVersion, cancellationToken)
                    : store.TryConsumeTemporaryPasswordAsync(state, rehashedPasswordHash, rehashedFormatVersion, nowUtc, cancellationToken),
                cancellationToken);
            if (!completed)
            {
                _ = await store.TryRecordFailedAccessAsync(state, options.Value.ToLockoutPolicy(), nowUtc, cancellationToken);
                return await RefuseAsync(SecurityEventReasonCodesV1.CredentialsInvalid, nowUtc, cancellationToken);
            }
            return await SucceedAsync(state, nowUtc, cancellationToken);
        }

        if (state.TemporaryPasswordIssuedAtUtc is not null)
        {
            // KIM-D23/KIM-D43 (ID-2r): a temporary password opens exactly one session, and only before the stored
            // expiry. A consumed or expired one, or one without a stored expiry, is a failed attempt like a wrong
            // password: the counter rises and the caller sees the same generic refusal. The fresh session is fenced
            // by MustChangePassword (only the password change and logout).
            if (!LocalTemporaryPasswordRules.IsUsable(
                    state.TemporaryPasswordExpiresAtUtc,
                    state.TemporaryPasswordConsumedAtUtc,
                    nowUtc))
            {
                _ = await store.TryRecordFailedAccessAsync(
                    state,
                    options.Value.ToLockoutPolicy(),
                    nowUtc,
                    cancellationToken);
                return await RefuseAsync(SecurityEventReasonCodesV1.CredentialsInvalid, nowUtc, cancellationToken);
            }

            var consumed = await store.TryConsumeTemporaryPasswordAsync(
                state,
                rehashedPasswordHash,
                rehashedFormatVersion,
                nowUtc,
                cancellationToken);
            if (!consumed)
            {
                return await RefuseAsync(SecurityEventReasonCodesV1.CredentialsInvalid, nowUtc, cancellationToken);
            }

            return await SucceedAsync(state, nowUtc, cancellationToken);
        }

        var recorded = await store.TryRecordSuccessfulAccessAsync(
            state,
            rehashedPasswordHash,
            rehashedFormatVersion,
            cancellationToken);
        if (!recorded)
        {
            return await RefuseAsync(SecurityEventReasonCodesV1.CredentialsInvalid, nowUtc, cancellationToken);
        }

        return await SucceedAsync(state, nowUtc, cancellationToken);
    }

    private async Task<LocalAccountLoginResult> SucceedAsync(
        LocalCredentialLoginState state,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        await securityEventWriter.WriteAsync(
            NewEvent(nowUtc) with
            {
                ScopeKind = SecurityEventScopeKindsV1.Platform,
                ActorType = SecurityEventActorTypesV1.User,
                ActorId = state.UserId.ToString("D"),
                EventType = SecurityEventTypesV1.LoginSucceeded,
                Outcome = SecurityEventOutcomesV1.Allowed,
                Severity = SecurityEventSeveritiesV1.Info,
            },
            cancellationToken);
        return LocalAccountLoginResult.Success(state.UserId, state.SecurityStamp);
    }

    private async Task<LocalAccountLoginResult> RefuseAsync(
        string reasonCode,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        // auth.login.failed is fail-open with alarm: a lost event never changes the refusal.
        await securityEventWriter.WriteAsync(
            NewEvent(nowUtc) with
            {
                ScopeKind = SecurityEventScopeKindsV1.Platform,
                ActorType = SecurityEventActorTypesV1.Anonymous,
                EventType = SecurityEventTypesV1.LoginFailed,
                Outcome = SecurityEventOutcomesV1.Denied,
                ReasonCode = reasonCode,
                Severity = SecurityEventSeveritiesV1.Warning,
            },
            cancellationToken);
        return LocalAccountLoginResult.Refused;
    }

    private SecurityEventV1 NewEvent(DateTimeOffset nowUtc) => new()
    {
        EventId = Guid.NewGuid(),
        OccurredAtUtc = nowUtc.ToUniversalTime(),
        ScopeKind = SecurityEventScopeKindsV1.Platform,
        ActorType = SecurityEventActorTypesV1.Anonymous,
        EventType = SecurityEventTypesV1.LoginFailed,
        Outcome = SecurityEventOutcomesV1.Denied,
        Severity = SecurityEventSeveritiesV1.Warning,
        CorrelationId = correlationContext.CorrelationId,
        CausationId = correlationContext.CausationId,
        SourceChannel = correlationContext.SourceChannel,
    };
}
