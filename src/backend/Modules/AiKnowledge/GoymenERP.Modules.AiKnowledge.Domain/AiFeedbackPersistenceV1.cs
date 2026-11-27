using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GoymenERP.Modules.AiKnowledge.Domain;

/// <summary>Learning observations only. Reverted never invokes an ERP reversal or changes a business rule.</summary>
public enum AiFeedbackKindV1 { Accepted = 1, Corrected = 2, Rejected = 3, Reverted = 4 }
public static class AiFeedbackReasonsV1
{
    public const string IncorrectInformation = "incorrect_information";
    public const string IncorrectOrMissingSource = "incorrect_or_missing_source";
    public const string IncompleteAnswer = "incomplete_answer";
    public const string OutdatedInformation = "outdated_information";
    public const string Other = "other";
    public static bool Valid(AiFeedbackKindV1 kind, string? reason) => Enum.IsDefined(kind)
        && (reason is null ? kind == AiFeedbackKindV1.Accepted : reason is IncorrectInformation or IncorrectOrMissingSource
            or IncompleteAnswer or OutdatedInformation or Other);
}
public sealed record AiFeedbackAppendV1(Guid CompanyId, Guid AnswerId, Guid ActorId, long ExpectedVersion,
    AiFeedbackKindV1 Kind, string? ReasonCode, string IdempotencyKey, string CorrelationId);
public sealed record AiFeedbackEventV1(Guid CompanyId, Guid AnswerId, long Version, long? PriorVersion,
    AiFeedbackKindV1 Kind, string? ReasonCode, DateTimeOffset CreatedAtUtc, DateTimeOffset RetainUntilUtc);
public sealed record AiFeedbackCurrentV1(Guid AnswerId, long Version, AiFeedbackKindV1? Kind, string? ReasonCode);
public enum AiFeedbackWriteStatusV1 { Recorded, Replayed, Invalid, Refused, NotFound, VersionConflict, IdempotencyConflict, Unavailable }
public sealed record AiFeedbackWriteResultV1(AiFeedbackWriteStatusV1 Status, AiFeedbackEventV1? Event = null, string? OriginalCorrelationId = null);
public sealed record AiFeedbackCountWindowV1(DateTimeOffset StartUtc, DateTimeOffset ExclusiveEndUtc, DateTimeOffset AsOfUtc);
public sealed record AiFeedbackMonthCountV1(string Feature, int Accepted, int Corrected, int Rejected, int Reverted, int Unreviewed, DateTimeOffset AsOfUtc);
public static class AiFeedbackRulesV1
{
    public static bool Valid(AiFeedbackAppendV1 value) => value.CompanyId != Guid.Empty && value.AnswerId != Guid.Empty
        && value.ActorId != Guid.Empty && value.ExpectedVersion is >= 0 and < long.MaxValue
        && AiFeedbackReasonsV1.Valid(value.Kind, value.ReasonCode) && AiAnswerReceiptRulesV1.Token(value.IdempotencyKey)
        && AiAnswerReceiptRulesV1.Token(value.CorrelationId);
    public static bool Valid(AiFeedbackEventV1 value) => value.CompanyId != Guid.Empty && value.AnswerId != Guid.Empty
        && value.Version > 0 && (value.Version == 1 ? value.PriorVersion is null : value.PriorVersion == value.Version - 1)
        && AiFeedbackReasonsV1.Valid(value.Kind, value.ReasonCode) && value.CreatedAtUtc != default
        && value.CreatedAtUtc.Offset == TimeSpan.Zero && value.CreatedAtUtc <= DateTimeOffset.MaxValue.AddDays(-AiAnswerReceiptRulesV1.RetentionDays)
        && value.RetainUntilUtc.Offset == TimeSpan.Zero && value.RetainUntilUtc == value.CreatedAtUtc.AddDays(AiAnswerReceiptRulesV1.RetentionDays);
    public static string KeyHash(string key) => Hash(Encoding.UTF8.GetBytes(key));
    // Fixed ordered typed fields, no arbitrary dictionary or free text. Actor/key scope is checked separately.
    public static string Fingerprint(AiFeedbackAppendV1 value) => Hash(JsonSerializer.SerializeToUtf8Bytes(new
        { value.CompanyId, value.AnswerId, value.ActorId, value.ExpectedVersion, value.Kind, value.ReasonCode }));
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
}
public interface IAiFeedbackPersistenceV1
{
    Task<AiFeedbackWriteResultV1> AppendAsync(AiFeedbackAppendV1 request, Func<CancellationToken, Task> freshConsumerFence,
        CancellationToken cancellationToken = default);
    Task<AiFeedbackCurrentV1?> GetOwnCurrentAsync(Guid companyId, Guid answerId, Guid actorId,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiFeedbackMonthCountV1>> GetMonthCountsAsync(Guid companyId, Guid actorId, AiFeedbackCountWindowV1 window,
        Func<CancellationToken, Task> freshConsumerFence, CancellationToken cancellationToken = default);
}
public sealed class AiFeedbackAuthorityLostException : Exception { }
