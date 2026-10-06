using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Opening;

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

    /// <summary>
    /// A label names a lot this company has not stored (DEC-PRD-003: a label belongs to a real lot
    /// record). The schema holds the same rule: a label row without its company's lot row is refused.
    /// </summary>
    public const string LotNotFound = "inventory_lot_not_found";

    /// <summary>
    /// The same lot id was offered with different facts (product, heat or coil), or the id is held by
    /// another company (invisible here; a retry gets the same answer, #324 P3-1).
    /// </summary>
    public const string LotConflict = "inventory_lot_conflict";

    /// <summary>A label and its lot name different products: a lot is of one product.</summary>
    public const string LotProductMismatch = "inventory_lot_product_mismatch";

    /// <summary>A movement names a label this company has not registered.</summary>
    public const string LabelNotRegistered = "inventory_label_not_registered";

    /// <summary>
    /// The same label id was offered with different identity facts, or a history that does not extend the
    /// stored one, or the id is held by another company (invisible here; a retry gets the same answer).
    /// </summary>
    public const string LabelConflict = "inventory_label_conflict";

    /// <summary>
    /// That printed code is already stored in this company, as the code column's collation compares
    /// it: by another label, or by the same label under another spelling (INV-3, #293 SİPARİŞ P3-1).
    /// </summary>
    public const string LabelCodeTaken = "inventory_label_code_taken";

    /// <summary>The same movement id was offered again with a different content.</summary>
    public const string MovementIdConflict = "inventory_movement_id_conflict";

    /// <summary>This source (or source line) already caused a movement of this kind (ORG #281 P3-1).</summary>
    public const string DuplicateSource = "inventory_duplicate_source";

    /// <summary>
    /// This source was already answered at the other grain for this kind (whole vs line); both would
    /// count the same quantity twice (#293 ORG P3-1).
    /// </summary>
    public const string SourceGrainMismatch = "inventory_source_grain_mismatch";

    /// <summary>
    /// Another write changed the same label's stock first. Nothing of this write was stored; the
    /// caller may read again and retry with the same movement id.
    /// </summary>
    public const string ConcurrentChange = "inventory_concurrent_change";

    /// <summary>A text or amount does not fit its stored column; it is refused, never cut.</summary>
    public const string ValueDoesNotFit = "inventory_value_does_not_fit";

    /// <summary>The same opening count id was offered with another content (INV-OPEN-1, R3).</summary>
    public const string OpeningConflict = "inventory_opening_conflict";
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
    /// saved. The same label again with nothing new is a replay. A new label's lot must already be
    /// stored in this company, of the same product (<see cref="InventoryStoreCodes.LotNotFound"/>,
    /// <see cref="InventoryStoreCodes.LotProductMismatch"/>); a first arrival saves both through
    /// <see cref="IInventoryLotStore.SaveLotAsync"/>.
    /// </summary>
    Task<InventoryWriteOutcome> SaveLabelAsync(MaterialLabel label, InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default);

    /// <summary>The company's stored label, or null (another company's label is null too).</summary>
    Task<MaterialLabel?> FindLabelAsync(Guid labelId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The persistent lot store of the server-resolved company (DEC-PRD-003, Ö3). A lot's facts never
/// change in this slice: there is no update path.
/// </summary>
public interface IInventoryLotStore
{
    /// <summary>
    /// Stores a lot and, in the same save, the given labels of it (a first arrival: the lot, its
    /// labels and their audit events commit together or not at all). The same lot id with the same
    /// facts is a replay of the lot; each label is then saved as <see cref="IInventoryLabelStore.SaveLabelAsync"/>
    /// would (new, extended or replayed). Every label must name this lot and its product.
    /// </summary>
    Task<InventoryWriteOutcome> SaveLotAsync(MaterialLot lot, IReadOnlyList<MaterialLabel> labels,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>The company's stored lot, or null (another company's lot is null too).</summary>
    Task<MaterialLot?> FindLotAsync(Guid lotId, CancellationToken cancellationToken = default);
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

/// <summary>
/// The opening stock of the server-resolved company, recorded from a physical count (INV-OPEN-1,
/// decision A #66 5738113623 §2). Whether each counted product may be stocked is the caller's question
/// (Product's stock eligibility), asked before this store is reached.
/// </summary>
public interface IInventoryOpeningStore
{
    /// <summary>
    /// Stores the count and, in ONE save, every counted piece's lot (new or the same stored facts), its new
    /// label, its one receipt and the resulting balance, with their audit events. The count id is the R3
    /// identity: the same count again is a replay with no second effect, another content under the same id
    /// is <see cref="InventoryStoreCodes.OpeningConflict"/>. A piece whose label is already stored is refused
    /// whole: with stock <c>inventory_label_already_in_stock</c> (opening is never an adjustment), without
    /// it <see cref="InventoryStoreCodes.LabelConflict"/> (an opening registers new pieces only).
    /// </summary>
    Task<InventoryWriteOutcome> RecordAsync(OpeningCount count, InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default, InventoryOpeningOwnerProofV1? ownerProof = null);

    /// <summary>A stored count of this company, or null.</summary>
    Task<OpeningCount?> ReadCountAsync(Guid countId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fresh Counterparty validation provenance retained in the opening audit. It is not a stock-position dimension.
/// </summary>
public sealed record InventoryOpeningOwnerProofV1(
    Guid CompanyPartyAccountId,
    string PartyVersion,
    string AccountVersion);
