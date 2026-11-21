using System.Security.Cryptography;
using GoymenERP.Modules.Identity.Application.Contracts;
using GoymenERP.Modules.Identity.Infrastructure.LocalAccount;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace GoymenERP.Api.Security;

/// <summary>
/// KIM-D12 option A: the framework <see cref="PasswordHasher{TUser}"/> in its V3 format (PBKDF2, random salt,
/// iteration count inside the hash). The fixed-time comparison is the framework's. A stored hash with a lower
/// iteration count verifies as <see cref="LocalPasswordVerification.SuccessRehashNeeded"/>, so raising
/// <c>Identity:LocalAccount:PasswordHashing:Pbkdf2Iterations</c> upgrades hashes at the next successful login.
/// The password is never logged or kept; the type only sees it for the duration of one call.
/// </summary>
internal sealed class LocalAccountPasswordHasher : ILocalPasswordHasher
{
    /// <summary>
    /// <c>LocalCredentials.HashFormatVersion</c> for a framework <c>PasswordHasher</c> V3 hash. A later format
    /// (for example Argon2id, KIM-D12) gets a new number and keeps verifying this one.
    /// </summary>
    public const int FrameworkV3FormatVersion = 3;

    private static readonly HashSubject Subject = new();
    private readonly PasswordHasher<HashSubject> hasher;
    private readonly string dummyHash;

    public LocalAccountPasswordHasher(IOptions<LocalAccountOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Value.PasswordHashing;
        if (!string.Equals(settings.Algorithm, LocalAccountPasswordHashingSettings.Pbkdf2Algorithm, StringComparison.Ordinal)
            || settings.Pbkdf2Iterations is not { } iterations
            || iterations < LocalAccountOptions.MinimumPbkdf2Iterations)
        {
            throw new InvalidOperationException(
                "Yerel hesap parola özeti ayarları eksik veya geçersiz (Identity:LocalAccount:PasswordHashing).");
        }

        hasher = new PasswordHasher<HashSubject>(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = iterations,
        }));

        // A throw-away hash of random bytes, made with the same cost as real hashes; nobody knows its password.
        dummyHash = hasher.HashPassword(Subject, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    public int FormatVersion => FrameworkV3FormatVersion;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return hasher.HashPassword(Subject, password);
    }

    public LocalPasswordVerification Verify(string passwordHash, string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(passwordHash);
        ArgumentNullException.ThrowIfNull(password);

        PasswordVerificationResult result;
        try
        {
            result = hasher.VerifyHashedPassword(Subject, passwordHash, password);
        }
        catch (FormatException)
        {
            // A damaged stored hash never opens a session.
            return LocalPasswordVerification.Failed;
        }

        return result switch
        {
            PasswordVerificationResult.Success => LocalPasswordVerification.Success,
            PasswordVerificationResult.SuccessRehashNeeded => LocalPasswordVerification.SuccessRehashNeeded,
            _ => LocalPasswordVerification.Failed,
        };
    }

    public void VerifyAgainstDummy(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        _ = hasher.VerifyHashedPassword(Subject, dummyHash, password);
    }

    /// <summary>The framework hasher is generic over a user type it never reads.</summary>
    private sealed class HashSubject;
}
