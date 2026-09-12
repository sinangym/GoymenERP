using System.Text.Json;
using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Idempotency.Domain.Executions;

// Immutable terminal data from the Idempotency owner. Not an HTTP request DTO.
public sealed record GroupSharedCommandTerminal
{
    private GroupSharedCommandTerminal(GroupSharedCommandBinding binding, int statusCode,
        string responseJson, string originalCorrelationId, int schemaVersion)
    {
        Binding = binding;
        StatusCode = statusCode;
        ResponseJson = responseJson;
        OriginalCorrelationId = originalCorrelationId;
        SchemaVersion = schemaVersion;
    }

    public GroupSharedCommandBinding Binding { get; }
    public int StatusCode { get; }
    public string ResponseJson { get; }
    public string OriginalCorrelationId { get; }
    public int SchemaVersion { get; }

    public static GroupSharedCommandTerminal Create(GroupSharedCommandBinding binding, int statusCode,
        string responseJson, string originalCorrelationId, int schemaVersion)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (statusCode is < 200 or > 599) throw new ArgumentOutOfRangeException(nameof(statusCode));
        if (schemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        if (string.IsNullOrWhiteSpace(originalCorrelationId) || originalCorrelationId.Length > 128
            || originalCorrelationId != originalCorrelationId.Trim() || originalCorrelationId.Any(char.IsControl))
            throw new ArgumentException("A canonical correlation ID is required.", nameof(originalCorrelationId));
        ArgumentNullException.ThrowIfNull(responseJson);
        using var document = JsonDocument.Parse(responseJson);
        // Preserve the exact stored response bytes represented by the string.
        return new(binding, statusCode, responseJson, originalCorrelationId, schemaVersion);
    }
}

// Privileged owner-side persistence port, NOT the Product consumer replay API.
// No concrete store/DI/schema is introduced here. Product consumes the resolver below.
public interface IGroupSharedCommandTerminalReader
{
    Task<GroupSharedCommandTerminal?> ReadAsync(GroupSharedCommandKey key,
        CancellationToken cancellationToken = default);
}

public enum GroupSharedCommandReplayKind
{
    AuthorizedMissing,
    MatchingReplay,
    Conflict,
}

public sealed record GroupSharedCommandReplayResult
{
    internal GroupSharedCommandReplayResult(GroupSharedCommandReplayKind kind,
        GroupSharedMasterCommandContext context, GroupSharedCommandTerminal? terminal = null)
    {
        Kind = kind;
        Context = context;
        Terminal = terminal;
    }

    public GroupSharedCommandReplayKind Kind { get; }
    // Always the current actor/origin, never evidence from a conflicting old command.
    public GroupSharedMasterCommandContext Context { get; }
    public GroupSharedCommandTerminal? Terminal { get; }
}

public interface IGroupSharedCommandReplayResolver
{
    Task<GroupSharedCommandReplayResult> ResolveAsync(GroupSharedMasterOperation operation,
        string keyHash, string requestHash, CancellationToken cancellationToken = default);
}

// This orchestration is executable with reviewed owner adapters. It is not a
// database transaction, eligibility provider, persistent store or DI registration.
public sealed class GroupSharedCommandReplayResolver(
    IGroupSharedMasterAuthorizationService authority,
    IGroupSharedCommandTerminalReader reader) : IGroupSharedCommandReplayResolver
{
    public async Task<GroupSharedCommandReplayResult> ResolveAsync(GroupSharedMasterOperation operation,
        string keyHash, string requestHash, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var before = GroupSharedMasterCommandContext.FromServerDecision(
            await authority.GetSnapshotAsync(GroupSharedMasterKind.Product, operation, cancellationToken), operation);
        var intent = GroupSharedCommandBinding.Create(before, keyHash, requestHash);
        var stored = await reader.ReadAsync(intent.Key, cancellationToken);
        // Recheck after the awaited lookup as well: revocation or context/revision
        // drift during a read must not release an old response or permit a new write.
        var current = GroupSharedMasterCommandContext.FromServerDecision(
            await authority.GetSnapshotAsync(GroupSharedMasterKind.Product, operation, cancellationToken), operation);
        cancellationToken.ThrowIfCancellationRequested();
        if (before.LocalActorId != current.LocalActorId || before.OriginCompanyId != current.OriginCompanyId
            || before.AuthorityRevision != current.AuthorityRevision)
            throw new UnauthorizedAccessException("Global authority changed during the command lookup.");
        if (stored is null)
            return new(GroupSharedCommandReplayKind.AuthorizedMissing, current);
        if (!stored.Binding.MatchesReauthorizedIntent(current, keyHash, requestHash))
            return new(GroupSharedCommandReplayKind.Conflict, current);
        if (stored.SchemaVersion != 1)
            throw new InvalidOperationException("Unsupported global command response schema.");
        return new(GroupSharedCommandReplayKind.MatchingReplay, current, stored);
    }
}
