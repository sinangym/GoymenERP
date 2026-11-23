namespace GoymenERP.Modules.Identity.Domain.Credentials;

/// <summary>
/// The idempotency record of a completed credential command (ID-2 <c>POST /session/password</c>). Only a success is
/// recorded, in the same transaction as the credential write; a replay with the same key and fingerprint returns
/// the recorded result without touching the credential again (ASSUMPTION-ID-IDEM-1 candidate). The fingerprint is
/// a SHA-256 over the key, the user and the expected version; it never contains a password.
/// </summary>
public sealed class LocalCredentialCommand
{
    public const int IdempotencyKeyMaxLength = 128;

    public const int OperationMaxLength = 100;

    public const int FingerprintLength = 32;

    public const string PasswordChangeOperation = "identity.local_account.password_change";

    private LocalCredentialCommand()
    {
    }

    public LocalCredentialCommand(
        Guid id,
        Guid userId,
        string operation,
        string idempotencyKey,
        byte[] requestFingerprint,
        long resultVersion,
        DateTimeOffset completedAtUtc,
        string? originalCorrelationId = null)
    {
        if (id == Guid.Empty || userId == Guid.Empty)
        {
            throw new ArgumentException("Komut kimlikleri boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(operation) || operation.Length > OperationMaxLength)
        {
            throw new ArgumentException("İşlem adı geçerli değil.", nameof(operation));
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > IdempotencyKeyMaxLength)
        {
            throw new ArgumentException("Tekrar anahtarı geçerli değil.", nameof(idempotencyKey));
        }

        ArgumentNullException.ThrowIfNull(requestFingerprint);
        if (requestFingerprint.Length != FingerprintLength)
        {
            throw new ArgumentException("İstek parmak izi 32 bayt olmalıdır.", nameof(requestFingerprint));
        }

        if (resultVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(resultVersion));
        }

        Id = id;
        UserId = userId;
        Operation = operation;
        IdempotencyKey = idempotencyKey;
        RequestFingerprint = requestFingerprint.ToArray();
        ResultVersion = resultVersion;
        CompletedAtUtc = completedAtUtc;
        if (originalCorrelationId is not null && (string.IsNullOrWhiteSpace(originalCorrelationId) || originalCorrelationId.Length > 128))
            throw new ArgumentException("Original correlation is invalid.", nameof(originalCorrelationId));
        OriginalCorrelationId = originalCorrelationId;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Operation { get; private set; } = string.Empty;

    public string IdempotencyKey { get; private set; } = string.Empty;

    public byte[] RequestFingerprint { get; private set; } = [];

    public long ResultVersion { get; private set; }

    public DateTimeOffset CompletedAtUtc { get; private set; }

    public string? OriginalCorrelationId { get; private set; }
}
