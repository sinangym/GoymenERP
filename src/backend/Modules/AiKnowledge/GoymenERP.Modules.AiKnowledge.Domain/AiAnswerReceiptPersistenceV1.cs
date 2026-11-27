namespace GoymenERP.Modules.AiKnowledge.Domain;

/// <summary>K10/No159: content-free immutable answer evidence; identity is held separately. A digest is not anonymization.</summary>
public sealed record AiAnswerReceiptV1(Guid CompanyId, Guid AnswerId, string AnswerSha256, string CitationsSha256,
    string Feature, string ModelDeployment, string PolicyVersion, DateTimeOffset CreatedAtUtc, DateTimeOffset RetainUntilUtc);
public sealed record AiAnswerReceiptIdentityV1(Guid CompanyId, Guid AnswerId, string ActorId, string CorrelationId);

public static class AiAnswerReceiptRulesV1
{
    public const int RetentionDays = 90; // SINAN_KARARLARI_2026-10-21 K10/No159, fixed at record time; no purge is enabled.
    public const int MetadataMaximumLength = 128;
    public static bool IsValid(AiAnswerReceiptV1 value) => value.CompanyId != Guid.Empty && value.AnswerId != Guid.Empty
        && Hash(value.AnswerSha256) && Hash(value.CitationsSha256) && value.Feature == "Panel"
        && Token(value.ModelDeployment) && Token(value.PolicyVersion) && value.CreatedAtUtc != default
        && value.CreatedAtUtc.Offset == TimeSpan.Zero && value.CreatedAtUtc <= DateTimeOffset.MaxValue.AddDays(-RetentionDays)
        && value.RetainUntilUtc.Offset == TimeSpan.Zero && value.RetainUntilUtc == value.CreatedAtUtc.AddDays(RetentionDays);
    public static bool Token(string? value) => value is { Length: > 0 and <= MetadataMaximumLength }
        && value == value.Trim() && !value.Any(char.IsControl);
    public static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

/// <summary>Only the AI owner writes these rows; the callback revalidates the captured consumer before and after saving.</summary>
public interface IAiAnswerReceiptPersistenceV1
{
    Task<bool> TryAddAsync(AiAnswerReceiptV1 receipt, AiAnswerReceiptIdentityV1 identity,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
    Task<AiAnswerReceiptV1?> GetOwnAsync(Guid companyId, Guid answerId, string actorId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
public sealed class AiAnswerReceiptAuthorityLostException : Exception { }
