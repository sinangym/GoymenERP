using GoymenERP.BuildingBlocks.Security;

namespace GoymenERP.Modules.Idempotency.Domain.Executions;

// Persistence must uniquely constrain this complete key in an owner-supported GLOBAL store.
// Actor/origin are intentionally absent: changing either must not mint a second terminal effect.
public sealed record GroupSharedCommandKey
{
    private GroupSharedCommandKey(string commandName, string keyHash)
    {
        CommandName = commandName;
        IdempotencyKeyHash = keyHash;
    }

    public string Subject => "Product";
    public string CommandName { get; }
    public string IdempotencyKeyHash { get; }

    public static GroupSharedCommandKey Create(
        GroupSharedMasterCommandContext context, string keyHash)
    {
        ArgumentNullException.ThrowIfNull(context);
        RequireHash(keyHash);
        var commandName = context.Operation switch
        {
            GroupSharedMasterOperation.DraftCreate => "product.draft.create.v1",
            GroupSharedMasterOperation.DraftBasicEdit => "product.draft.basic-edit.v1",
            // #329: the Product owner's command name (ProductCommandIntent.ActivateOperation).
            GroupSharedMasterOperation.Activate => "product.card.activate.v1",
            _ => throw new ArgumentException("Unsupported shared command operation.", nameof(context)),
        };
        return new(commandName, keyHash);
    }

    internal static void RequireHash(string hash)
    {
        // Same uppercase SHA-256 representation as the existing Idempotency owner.
        if (hash is null || hash.Length != 64
            || hash.Any(character => character is not (>= '0' and <= '9' or >= 'A' and <= 'F')))
            throw new ArgumentException("A canonical uppercase SHA-256 hash is required.", nameof(hash));
    }

    internal static GroupSharedCommandKey RestoreStored(string subject, string commandName, string keyHash)
    {
        RequireHash(keyHash);
        if (subject != "Product" || commandName is not
            ("product.draft.create.v1" or "product.draft.basic-edit.v1" or "product.card.activate.v1"))
            throw new ArgumentException("The stored shared command identity is not supported.");
        return new(commandName, keyHash);
    }
}

public sealed record GroupSharedCommandBinding
{
    private GroupSharedCommandBinding(GroupSharedMasterCommandContext context,
        GroupSharedCommandKey key, string requestHash)
        : this(key, context.LocalActorId, context.OriginCompanyId, requestHash)
    {
    }

    private GroupSharedCommandBinding(GroupSharedCommandKey key,
        Guid localActorId, Guid originCompanyId, string requestHash)
    {
        Key = key;
        LocalActorId = localActorId;
        OriginCompanyId = originCompanyId;
        RequestHash = requestHash;
    }

    public GroupSharedCommandKey Key { get; }
    public Guid LocalActorId { get; }
    public Guid OriginCompanyId { get; }
    public string RequestHash { get; }

    public static GroupSharedCommandBinding Create(
        GroupSharedMasterCommandContext context, string keyHash, string requestHash)
    {
        GroupSharedCommandKey.RequireHash(requestHash);
        return new(context, GroupSharedCommandKey.Create(context, keyHash), requestHash);
    }

    // Data rehydration only. This does not create an authorization snapshot/context or
    // authorize disclosure. Only the fresh-authority resolver may release a stored result.
    public static GroupSharedCommandBinding RestoreStored(string subject, string commandName,
        string keyHash, Guid localActorId, Guid originCompanyId, string requestHash)
    {
        GroupSharedCommandKey.RequireHash(requestHash);
        if (localActorId == Guid.Empty || originCompanyId == Guid.Empty)
            throw new ArgumentException("Stored actor and origin identities must be non-empty.");
        return new(GroupSharedCommandKey.RestoreStored(subject, commandName, keyHash),
            localActorId, originCompanyId, requestHash);
    }

    // Intent matching is NOT authorization and does not read a stored response.
    // The caller must first obtain a new server decision for this request/replay.
    // False must not disclose an old response, origin, actor or original correlation.
    public bool MatchesReauthorizedIntent(
        GroupSharedMasterCommandContext currentContext, string keyHash, string requestHash)
    {
        var candidate = Create(currentContext, keyHash, requestHash);
        return Key == candidate.Key && LocalActorId == candidate.LocalActorId
            && OriginCompanyId == candidate.OriginCompanyId
            && RequestHash == candidate.RequestHash;
    }
}
