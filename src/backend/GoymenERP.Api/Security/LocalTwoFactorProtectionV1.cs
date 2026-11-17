using GoymenERP.Modules.Identity.Application.Contracts;
using Microsoft.AspNetCore.DataProtection;

namespace GoymenERP.Api.Security;

/// <summary>Uses the host's existing Data Protection key ring; no key, secret, provider or endpoint is created here.</summary>
internal sealed class LocalTwoFactorProtectionV1(IDataProtectionProvider provider) : ILocalTwoFactorProtectionV1
{
    public string Protect(Guid userId, string purpose, string plaintext) => Protector(userId, purpose).Protect(plaintext);
    public string Unprotect(Guid userId, string purpose, string protectedText) => Protector(userId, purpose).Unprotect(protectedText);
    private IDataProtector Protector(Guid userId, string purpose)
    {
        if (userId == Guid.Empty || purpose is not ("enrollment" or "authenticator")) throw new ArgumentException("Two factor protection purpose is invalid.");
        return provider.CreateProtector("GoymenERP.Identity.TwoFactor.V1").CreateProtector(userId.ToString("N")).CreateProtector(purpose);
    }
}
