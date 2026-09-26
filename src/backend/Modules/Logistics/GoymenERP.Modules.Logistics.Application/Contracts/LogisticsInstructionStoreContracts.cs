using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.Modules.Logistics.Domain.Loading;

namespace GoymenERP.Modules.Logistics.Application.Contracts;

/// <summary>
/// The ADR-009 audit envelope of one loading-instruction write (the Sales / Inventory shape): who
/// asked, under which permission, in which conversation, with which client key. Every value is the
/// caller's server-side context; the company is always the server-resolved one.
/// </summary>
public sealed record LogisticsWriteEnvelope(
    string ActorId,
    string PermissionCode,
    IReadOnlyCollection<string> RoleCodes,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    string SourceChannel,
    string? ClientIpAddress = null,
    string? UserAgent = null);

public enum LogisticsWriteOutcome
{
    Recorded = 1,
    Replayed = 2,
}

/// <summary>The versioned command names the attempt record is keyed by (ADR-011 §2; LOG-4 D5).</summary>
public static class LogisticsCommands
{
    public const string CreateV1 = "logistics.loading-instruction.create.v1";
    public const string IssueV1 = "logistics.loading-instruction.issue.v1";
    public const string RecordLoadV1 = "logistics.loading-instruction.record-load.v1";
    public const string RecordShipmentV1 = "logistics.loading-instruction.record-shipment.v1";
    public const string RecordDeliveryV1 = "logistics.loading-instruction.record-delivery.v1";

    public static bool IsMutation(string commandName) => commandName is IssueV1 or RecordLoadV1 or RecordShipmentV1 or RecordDeliveryV1;

    public static bool IsKnown(string commandName) => commandName == CreateV1 || IsMutation(commandName);
}

/// <summary>The stable refusal codes of the store, beside the domain's rule codes.</summary>
public static class LogisticsStoreCodes
{
    public const string CompanyContextMismatch = "logistics_company_context_mismatch";
    public const string SameKeyPayloadMismatch = "logistics_same_key_payload_mismatch";
    public const string ConcurrentChange = "logistics_concurrent_change";
    public const string ValueDoesNotFit = "logistics_value_does_not_fit";
    public const string InvalidCursor = "logistics_invalid_cursor";
}

public sealed class LogisticsStoreException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>A stored instruction with the business date its unit profiles were read for (kept beside the domain record, not in it).</summary>
public sealed record StoredLoadingInstruction(LoadingInstruction Instruction, DateOnly EffectiveOn);

public sealed record LogisticsStoreResult(StoredLoadingInstruction Instruction, LogisticsWriteOutcome Outcome, string OriginalCorrelationId);

/// <summary>ADR-011 §7: a terminal refusal recorded under the attempt key (stale version, cap, block, state), answered again on the same key.</summary>
public sealed record LogisticsTerminalRefusal(int Status, string Code, IReadOnlyList<LogisticsFieldError> Errors);

/// <summary>What the key holds: the stored instruction or the recorded refusal, never both.</summary>
public sealed record LogisticsReplay(StoredLoadingInstruction? Instruction, LogisticsTerminalRefusal? Refusal, string OriginalCorrelationId);

public sealed record LogisticsInstructionPage(IReadOnlyList<StoredLoadingInstruction> Items, string? NextCursor);

/// <summary>The audit facts of one mutation (issue / record-load): the versioned event type, the operation and the reason.</summary>
public sealed record LogisticsMutationAudit(string EventType, string Operation, string Reason);

/// <summary>
/// The persistent instruction store of the server-resolved company (LOG-4a). One call is one save:
/// the instruction, its lines and shares, the attempt record and the audit event commit together or
/// not at all. Replay identity (ADR-011, R3): (company, command, SHA-256 of the trimmed key); the raw
/// key is never stored; the same key with the same content and actor answers the stored result, another
/// content or actor is one 409; a key of another company is invisible.
/// </summary>
public interface ILogisticsInstructionStore
{
    /// <summary>Stores a new instruction the caller built (Draft, ≥1 line), or answers the same attempt again.</summary>
    Task<LogisticsStoreResult> StoreInstructionAsync(LogisticsWriteEnvelope envelope, string requestHash, DateOnly effectiveOn,
        Func<LoadingInstruction> build, CancellationToken cancellationToken = default);

    Task<LogisticsReplay?> FindReplayAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName,
        CancellationToken cancellationToken = default);

    Task RecordRefusalAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName, LogisticsTerminalRefusal refusal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One mutation (issue / record-load) of the company's stored instruction in ONE save: the domain
    /// transition, the attempt record and the audit event. The caller answers the domain's refusals
    /// (terminal ones through <see cref="RecordRefusalAsync"/>); another company's instruction, or none,
    /// is null (never 403); a same-key attempt already stored answers Replayed; a lost race is
    /// <see cref="LogisticsStoreCodes.ConcurrentChange"/> with nothing stored.
    /// </summary>
    Task<LogisticsStoreResult?> MutateInstructionAsync(LogisticsWriteEnvelope envelope, string requestHash, string commandName,
        Guid instructionId, Action<LoadingInstruction, DateTimeOffset> mutate, LogisticsMutationAudit audit,
        CancellationToken cancellationToken = default);

    Task<StoredLoadingInstruction?> FindInstructionAsync(Guid instructionId, CancellationToken cancellationToken = default);

    /// <summary>The company's instructions, newest first, by opaque company-bound cursor; <paramref name="status"/> narrows to one status.</summary>
    Task<LogisticsInstructionPage> ListInstructionsAsync(int pageSize, string? cursor, LoadingInstructionStatus? status = null,
        CancellationToken cancellationToken = default);
}

/// <summary>The canonical content of a command for the replay identity (R3): the strict wire shape, serialised and hashed.</summary>
public static class LogisticsRequestHash
{
    public static string Compute(CreateLoadingInstructionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new { command = LogisticsCommands.CreateV1, request }, LogisticsJson.Options));
    }

    public static string Compute(string commandName, Guid instructionId, object request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
        ArgumentNullException.ThrowIfNull(request);
        return Hash(JsonSerializer.Serialize(new { command = commandName, instructionId, request }, LogisticsJson.Options));
    }

    private static string Hash(string canonical) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}
