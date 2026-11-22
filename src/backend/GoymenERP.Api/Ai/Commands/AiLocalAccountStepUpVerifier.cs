using GoymenERP.Modules.Identity.Application.Contracts;

namespace GoymenERP.Api.Ai.Commands;

/// <summary>
/// YOL-D27 step-up = the existing LocalAccount password verification (Identity owner port), with the login lockout
/// counter and policy. Only the signed-in second approver's own account is checked; the password is not kept.
/// </summary>
public sealed class AiLocalAccountStepUpVerifier(ILocalAccountReauthenticationServiceV1 reauthentication) : IAiStepUpVerifierV1
{
    public async Task<bool> IsVerifiedAsync(AiStepUpChallengeV1 challenge, string? password, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        return Guid.TryParse(challenge.ActorId, out var userId)
            && userId != Guid.Empty
            && !string.IsNullOrEmpty(password)
            && await reauthentication.VerifyAsync(userId, password, cancellationToken) == LocalAccountReauthenticationStatusV1.Verified;
    }
}
