namespace GoymenERP.Modules.Idempotency.Domain.Retention;

/// <summary>
/// Append-only evidence of one retention purge batch for one company (OPEN-021 / NS-12): which cutoff, which configured
/// period and policy reference, and how many idempotency records were disposed of. The disposed records themselves
/// are not copied; the evidence row is written in the same transaction as the delete.
/// </summary>
public sealed class IdempotencyRetentionPurgeRecord
{
    public const int PolicyRefMaxLength = 64;

    private IdempotencyRetentionPurgeRecord()
    {
        RetentionPeriod = string.Empty;
        PolicyRef = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid RunId { get; private set; }

    public Guid CompanyId { get; private set; }

    public DateTimeOffset CutoffUtc { get; private set; }

    public string RetentionPeriod { get; private set; }

    public string PolicyRef { get; private set; }

    public int DeletedCount { get; private set; }

    public DateTimeOffset RecordedAtUtc { get; private set; }

    public static IdempotencyRetentionPurgeRecord Record(
        Guid id,
        Guid runId,
        Guid companyId,
        DateTimeOffset cutoffUtc,
        IdempotencyRetentionPeriod period,
        string policyRef,
        int deletedCount,
        DateTimeOffset recordedAtUtc)
    {
        if (id == Guid.Empty || runId == Guid.Empty)
        {
            throw new ArgumentException("Saklama imha kaydı ve çalışma kimliği boş olamaz.");
        }

        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Saklama imha kaydı bir şirkete bağlı olmalıdır.", nameof(companyId));
        }

        if (!period.IsDefined)
        {
            throw new ArgumentException("Saklama imha kaydı tanımlı bir saklama süresi gerektirir.", nameof(period));
        }

        if (string.IsNullOrWhiteSpace(policyRef) || policyRef.Trim().Length > PolicyRefMaxLength)
        {
            throw new ArgumentException("Saklama imha kaydının politika referansı eksik veya çok uzun.", nameof(policyRef));
        }

        if (deletedCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deletedCount), "Saklama imha kaydı yalnız silinen kayıt varsa yazılır.");
        }

        if (cutoffUtc.Offset != TimeSpan.Zero || recordedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Saklama imha zamanları UTC olmalıdır.");
        }

        if (cutoffUtc >= recordedAtUtc)
        {
            throw new ArgumentException("Saklama kesim anı kayıt anından önce olmalıdır.", nameof(cutoffUtc));
        }

        return new IdempotencyRetentionPurgeRecord
        {
            Id = id,
            RunId = runId,
            CompanyId = companyId,
            CutoffUtc = cutoffUtc,
            RetentionPeriod = period.ToString(),
            PolicyRef = policyRef.Trim(),
            DeletedCount = deletedCount,
            RecordedAtUtc = recordedAtUtc,
        };
    }
}
