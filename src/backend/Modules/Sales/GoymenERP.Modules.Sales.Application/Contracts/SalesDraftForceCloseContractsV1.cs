using GoymenERP.Modules.Sales.Domain.Drafts;

namespace GoymenERP.Modules.Sales.Application.Contracts;

// ---- SINAN_KARARLARI_2026-10-21 No 30 / SAT-D12: force-close ("zorla kapatma") of an approved order ----------------------
//
// No 30 (verbatim): "Zorla kapatma var: `sales.orders.force-close` (`sales.orders.*` ailesi, `sales.orders.cancel`'dan ayrı ad),
// gerekçe zorunlu, audit. DEC-SAL-005 aynen uygulanır: kapatma öncesi etki önizlemesi (aktif tahsis, bağlı satın alma/üretim,
// irsaliyesiz yükleme, faturalanmamış irsaliye, eşleşmemiş avans/dekont) zorunlu; herhangi bir kategori açık/okunamaz ise komut
// reddedilir; Satış başka modülün kaydını serbest bırakmaz … Önizleme temizse kalan 'iptal edilen kalan' olarak kaydedilir
// (DEC-SAL-001 ayrı eksen); stok/para olayı yok."
// SAT-D12 (Sinan, 2026-10-25, newer, overrides No 30 on this point): an approved dispatch note that is not yet invoiced does NOT
// block; it is shown as a warning (open invoicing work) and the financial-closing axis stays open.
// The six categories, their owners and ports are the cancellation's (SalesCancellationImpactSourcesV1 / StatusesV1); each
// source additionally says whether it BLOCKS the closure. "irsaliyesiz yükleme" is read for the closure as OPEN loading: a line
// whose instructed total is greater than its shipped total (fail-closed reading, README "Force-close"); fully shipped
// instruction lines are the delivered/shipped part and do not block. The cancellation keeps its stricter reading.

/// <summary>One category of the force-close preview: its stable source name, its status and whether it blocks the closure.</summary>
public sealed record SalesDraftForceCloseImpactSourceV1(string Source, string Status, bool Blocks);

/// <summary>
/// One order line in the force-close preview: the cancellation preview's owner members (allocations from Inventory, loading
/// totals from Logistics in the line's order unit; NULL when the owner could not be read — never zero) and
/// <c>closedRemainder</c>, the remainder a closure WOULD record (ordered − shipped, exact; null when the loading owner is
/// unreadable).
/// </summary>
public sealed record SalesDraftForceCloseImpactLineV1(
    Guid LineId,
    int LineNo,
    Guid OrderUnitId,
    string Ordered,
    int? ActiveAllocations,
    string? AllocatedKg,
    int? LoadingInstructionLines,
    string? Instructed,
    string? Loaded,
    string? Shipped,
    string? Delivered,
    string? ClosedRemainder);

/// <summary>
/// No 30: GET …/{id}/force-close-impact — the force-close preview under the force-close permission, read afresh from the owners
/// on every request. <c>closable</c> is true only when the order is approved, its lifecycle is Draft, no remainder change is
/// pending and no source blocks. <c>impactToken</c> identifies exactly this preview; the command must carry it.
/// </summary>
public sealed record SalesOrderDraftForceCloseImpactResponse(
    string DraftVersion,
    string Revision,
    bool Closable,
    string ImpactToken,
    IReadOnlyList<SalesDraftForceCloseImpactSourceV1> Sources,
    IReadOnlyList<SalesDraftForceCloseImpactLineV1> Lines);

/// <summary>No 30: POST …/{id}/force-close — the expected version (text), the reason (required, ≤ 4000) and the preview token.</summary>
public sealed record SalesDraftForceCloseRequest(string ExpectedVersion, string Reason, string ImpactToken);

/// <summary>One line as closed: the ordered quantity text, the shipped total and the closed ("iptal edilen") remainder.</summary>
public sealed record SalesDraftForceClosureLineV1(Guid LineId, int LineNo, string Ordered, string Shipped, string ClosedRemainder);

/// <summary>
/// The recorded force-closure — the revision closed, who, when, why, the preview it was decided on (its token and each
/// category's status and blocking flag then) and the closed remainder of every line. The line quantities, approval facts,
/// revisions and schedules are read as before.
/// </summary>
public sealed record SalesDraftForceClosureV1(
    string Revision,
    string ClosedBy,
    DateTimeOffset ClosedAtUtc,
    string Reason,
    string ImpactToken,
    IReadOnlyList<SalesDraftForceCloseImpactSourceV1> Sources,
    IReadOnlyList<SalesDraftForceClosureLineV1> Lines);

/// <summary>GET …/{id}/force-close — the recorded force-closure, or null while the order is not closed (under the draft read).</summary>
public sealed record SalesOrderDraftForceClosureResponse(SalesDraftForceClosureV1? ForceClosure);

/// <summary>
/// What the caller's last step hands the store for a force-close: the impact the user confirmed (read afresh, unchanged) and
/// every line's shipped total from the delivery owner, from which the domain derives the closed remainder.
/// </summary>
public sealed record SalesForceClosurePlan(ForceClosureImpactSnapshot Impact, IReadOnlyList<ForceClosureLineInput> Lines);
