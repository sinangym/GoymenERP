using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Domain.Credentials;

namespace GoymenERP.Modules.Identity.Infrastructure.LocalAccount;

internal static class LocalTwoFactorProofVerifierV1
{
    internal static bool Consume(LocalTwoFactorSecret secret, string? code, ILocalTwoFactorProtectionV1 protection, DateTimeOffset now)
    {
        if (!secret.IsActive || string.IsNullOrWhiteSpace(code)) return false;
        if (code.Length == 6 && code.All(char.IsAsciiDigit))
        {
            var step = LocalTotpV1.Match(protection.Unprotect(secret.UserId, "authenticator", secret.ProtectedSecret), code, now, secret.LastAcceptedStep);
            if (step is null) return false;
            secret.AcceptStep(step.Value);
            return true;
        }
        return secret.ConsumeRecoveryCode(code);
    }
}
