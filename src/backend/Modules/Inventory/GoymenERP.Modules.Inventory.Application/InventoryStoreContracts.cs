using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;

namespace GoymenERP.Modules.Inventory.Application;

/// <summary>
/// Who asked, under which permission and in which conversation — the ADR-009 audit envelope of
/// one write. Every value comes from the caller's server-side context (a later command surface,
/// INV-3); this module never makes one up. The company is not here: it is always the
/// server-resolved company context, and a record of any other company is refused.
/// </summary>
public sealed record InventoryCommandMetadata(
    string ActorId,
    string PermissionCode,
    IReadOnlyCollection<string> RoleCodes,
    string CorrelationId,
    string? CausationId,
    string IdempotencyKey,
    string SourceChannel,
    string? ClientIpAddress = null,
    string? UserAgent = null);

/// <summary>What a write did: a new record, or the same request again with no second effect (R3).</summary>
public enum InventoryWriteOutcome
{
    Recorded = 1,
    Replayed = 2,
}

/// <summary>The stable refusal codes of the Inventory store, beside the domain's own rule codes.</summary>
public static class InventoryStoreCodes
{
    /// <summary>The record's company is not the server-resolved company context (or there is none).</summary>
    public const string CompanyContextMismatch = "inventory_company_context_mismatch";

    /// <summary>A movement names a label this company has not registered.</summary>
    public const string LabelNotRegistered = "inventory_label_not_registered";

    /// <summary>The same label id was offered with different identity facts, or a history that does not extend the stored one.</summary>
    public const string LabelConflict = "inventory_label_conflict";

    /// <summary>Another label of this company already carries that printed code.</summary>
    public const string LabelCodeTaken = "inventory_label_code_taken";

    /// <summary>The same movement id was offered again with a different content.</summary>
    public const string MovementIdConflict = "inventory_movement_id_conflict";

    /// <summary>This source (or source line) already caused a movement of this kind (ORG #281 P3-1).</summary>
    public const string DuplicateSource = "inventory_duplicate_source";

    /// <summary>
    /// Another write changed the same label's stock first. Nothing of this write was stored; the
    /// caller may read again and retry with the same movement id.
    /// </summary>
    public const string ConcurrentChange = "inventory_concurrent_change";

    /// <summary>A text or amount does not fit its stored column; it is refused, never cut.</summary>
    public const string ValueDoesNotFit = "inventory_value_does_not_fit";
}

/// <summary>A stored stock fact could not be written; <see cref="Code"/> says why. Nothing was stored.</summary>
public sealed class InventoryStoreException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// The persistent label store of the server-resolved company. A label's identity facts never
/// change; its prints and document attachments only ever grow.
/// </summary>
public interface IInventoryLabelStore
{
    /// <summary>
    /// Stores a new label, or the prints and attachments added to a stored one since it was last
    /// saved. The same label again with nothing new is a replay.
    /// </summary>
    Task<InventoryWriteOutcome> SaveLabelAsync(MaterialLabel label, InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>The company's stored label, or null (another company's label is null too).</summary>
    Task<MaterialLabel?> FindLabelAsync(Guid labelId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The persistent movement store of the server-resolved company: a movement and the balances it
/// changes are stored together or not at all.
/// </summary>
public interface IInventoryMovementStore
{
    /// <summary>
    /// Applies a movement against the stored balances and stores it with them. The same movement
    /// id with the same content is a replay with no second effect; with another content it is
    /// refused.
    /// </summary>
    Task<InventoryWriteOutcome> RecordMovementAsync(StockMovement movement, InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>The stored non-zero balances of one label.</summary>
    Task<IReadOnlyList<StockBalance>> ReadBalancesAsync(Guid labelId, CancellationToken cancellationToken = default);

    /// <summary>Every stored movement that touched one label, in the order they were recorded.</summary>
    Task<IReadOnlyList<StockMovement>> ReadMovementsAsync(Guid labelId, CancellationToken cancellationToken = default);
}
