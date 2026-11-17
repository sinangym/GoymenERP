using System.Security.Cryptography;
using System.Text.Json;

namespace GoymenERP.Modules.Identity.Domain.Credentials;

/// <summary>The local credential's protected authenticator and one-use recovery hashes. Never a plaintext secret store.</summary>
public sealed class LocalTwoFactorSecret
{
    private LocalTwoFactorSecret() { }
    public LocalTwoFactorSecret(Guid userId, string protectedSecret, IReadOnlyList<string> recoveryHashes, long acceptedStep)
    {
        if (userId == Guid.Empty) throw new ArgumentException("User is required.");
        UserId = userId;
        Replace(protectedSecret, recoveryHashes, acceptedStep);
    }
    public Guid UserId { get; private set; }
    public string ProtectedSecret { get; private set; } = string.Empty;
    public string RecoveryHashesJson { get; private set; } = "[]";
    public bool IsActive { get; private set; }
    public long? LastAcceptedStep { get; private set; }
    public long Version { get; private set; }
    public int RecoveryCodeCount => Hashes().Count;
    public void Replace(string protectedSecret, IReadOnlyList<string> hashes, long acceptedStep)
    {
        if (string.IsNullOrWhiteSpace(protectedSecret) || protectedSecret.Length > 4000 || acceptedStep < 0) throw new ArgumentException("Protected authenticator is invalid.");
        ProtectedSecret = protectedSecret;
        SetHashes(hashes);
        LastAcceptedStep = acceptedStep;
        IsActive = true;
        Version++;
    }
    public void AcceptStep(long step)
    {
        if (!IsActive || step < 0 || LastAcceptedStep >= step) throw new InvalidOperationException("Authenticator step is already used.");
        LastAcceptedStep = step; Version++;
    }
    public bool ConsumeRecoveryCode(string code)
    {
        if (!IsActive) return false;
        var hash = LocalTotpV1.RecoveryHash(code);
        if (hash.Length == 0) return false;
        var hashes = Hashes();
        var matching = hashes.Where(value => CryptographicOperations.FixedTimeEquals(Convert.FromHexString(value), Convert.FromHexString(hash))).ToArray();
        if (matching.Length != 1) return false;
        SetHashes(hashes.Where(value => value != matching[0]).ToArray()); Version++;
        return true;
    }
    public void Regenerate(IReadOnlyList<string> hashes) { if (!IsActive) throw new InvalidOperationException("Two factor is disabled."); SetHashes(hashes); Version++; }
    public void Deactivate() { IsActive = false; Version++; }
    private IReadOnlyList<string> Hashes()
    {
        var hashes = JsonSerializer.Deserialize<string[]>(RecoveryHashesJson);
        if (!ValidHashes(hashes)) throw new InvalidOperationException("Recovery hashes are unreadable.");
        return hashes!;
    }
    private void SetHashes(IReadOnlyList<string> hashes)
    {
        if (!ValidHashes(hashes)) throw new ArgumentException("Recovery hashes are invalid.");
        RecoveryHashesJson = JsonSerializer.Serialize(hashes);
    }
    private static bool ValidHashes(IReadOnlyList<string>? hashes) => hashes is not null && hashes.Count <= 10
        && hashes.Distinct(StringComparer.Ordinal).Count() == hashes.Count
        && hashes.All(value => value is not null && value.Length == 64
            && value.All(character => char.IsAsciiDigit(character) || character is >= 'A' and <= 'F'));
    public override string ToString() => nameof(LocalTwoFactorSecret);
}
