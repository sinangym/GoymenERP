using System.Globalization;
using GoymenERP.Modules.Sales.Domain.Quantities;

namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SINAN_KARARLARI_2026-10-21 No 30 (KİLİTLİ): "Zorla kapatma var: <c>sales.orders.force-close</c> …, gerekçe zorunlu, audit.
/// DEC-SAL-005 aynen uygulanır: kapatma öncesi etki önizlemesi … zorunlu; herhangi bir kategori açık/okunamaz ise komut
/// reddedilir; Satış başka modülün kaydını serbest bırakmaz … Önizleme temizse kalan 'iptal edilen kalan' olarak kaydedilir
/// (DEC-SAL-001 ayrı eksen); stok/para olayı yok." SAT-D12 (2026-10-25, newer, overrides No 30 on this point): an approved,
/// not-yet-invoiced dispatch note does NOT block — it is a warning (open invoicing work); the financial-closing axis stays open.
/// The domain half, the cancellation's shape:
/// <list type="bullet">
/// <item><b>Only Approved:</b> the lifecycle moves <c>Draft → Closed</c> for an APPROVED order only, with a required reason; an
/// order in re-approval has no approved revision in force and is refused (<see cref="SalesRuleCodes.StateConflict"/>), as the
/// cancellation refuses it. A pending remainder change is decided first (its decision entry refuses a terminal order, so it
/// would be stranded) — a <see cref="SalesRuleCodes.StateConflict"/> too.</item>
/// <item><b>What blocks is the caller's reading:</b> the owners' records (allocations, loading, dispatch notes) are not this
/// aggregate's knowledge; the caller reads them through the owners' ports and refuses a blocked closure BEFORE this runs. The
/// domain only refuses to record a closure over a source the caller itself marked as blocking.</item>
/// <item><b>The closed remainder</b> of every line is ordered − shipped (the delivery owner's shipped total, in the line's own
/// order unit, exact, never rounded). The line quantities do NOT move; nothing of another module is released or reversed.</item>
/// <item><b>History stays:</b> the approval facts, the revision, the lines, the archived revisions and the schedules stay; only
/// the lifecycle state, the closure facts and the version (one step) move. Terminal: every mutation entry refuses it.</item>
/// </list>
/// </summary>
public sealed partial class SalesOrderDraft
{
    /// <summary>The technical bound of a force-close reason: the note's, as every other reason.</summary>
    public const int MaximumForceCloseReasonLength = MaximumNoteLength;

    /// <summary>No 30 / SAT-D12: the recorded force-closure (who, when, why, which revision, the closed remainders); null while open.</summary>
    public ForceClosureFacts? ForceClosure { get; private set; }

    /// <summary>
    /// Force-closes an approved order: the preconditions (<see cref="RequireForceClosable"/>), the impact the user confirmed
    /// (no source marked as blocking) and every line's shipped total; records the closed remainder per line and moves the
    /// lifecycle to <see cref="SalesDraftState.Closed"/> with one version step.
    /// </summary>
    public ForceClosureFacts ForceClose(long expectedVersion, string actorId, string? reason, ForceClosureImpactSnapshot impact,
        IReadOnlyList<ForceClosureLineInput> lineRemainders, DateTimeOffset now)
    {
        var actor = RequireActor(actorId);
        var trimmedReason = RequireForceClosable(expectedVersion, reason);
        if (!IsForceClosureImpact(impact))
        {
            throw new ArgumentException(
                "Zorla kapatma, kullanıcının gördüğü engelsiz etki önizlemesiyle (belirteç ve kaynaklar) kaydedilir.", nameof(impact));
        }

        var closedLines = ClosedRemainders(lineRemainders);
        var facts = new ForceClosureFacts(Revision, actor, now.ToUniversalTime(), trimmedReason, impact, closedLines);
        State = SalesDraftState.Closed;
        ForceClosure = facts;
        Touch(now);
        return facts;
    }

    /// <summary>
    /// The force-closure's own preconditions WITHOUT the transition — the expected version, the lifecycle (not cancelled, not
    /// closed), the approval state (Approved), no pending remainder change, and the reason — so the caller can refuse a stale or
    /// wrong-state command before it asks any owner for the impact. Returns the trimmed reason.
    /// </summary>
    public string RequireForceClosable(long expectedVersion, string? reason)
    {
        RequireVersion(expectedVersion);
        RequireNotCancelled("İptal edilmiş sipariş zorla kapatılmaz.");
        if (ApprovalState != InternalApprovalState.Approved)
        {
            throw StateConflict("Yalnız onaylı sipariş zorla kapatılır; onaylanmamış taslak geri çekilir.");
        }

        if (PendingRemainderChange is not null)
        {
            throw StateConflict("Siparişte karar bekleyen bir kalan değişikliği var; önce o karara bağlanır.");
        }

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is null)
        {
            throw new SalesRuleException(SalesRuleCodes.ForceCloseReasonRequired, "Zorla kapatma gerekçesi zorunludur.");
        }

        if (trimmedReason.Length > MaximumForceCloseReasonLength)
        {
            throw new SalesRuleException(SalesRuleCodes.ForceCloseReasonTooLong,
                $"Zorla kapatma gerekçesi en fazla {MaximumForceCloseReasonLength} karakterdir.");
        }

        return trimmedReason;
    }

    /// <summary>
    /// The closed remainder a closure WOULD record for one line given the owner's shipped total: ordered − shipped in the line's
    /// own unit (exact, never rounded), or null when the shipped total is not a valid value for the line (negative, over Ö2's
    /// scale, above the ordered quantity) — the caller then treats the owner's answer as unreadable.
    /// </summary>
    public static string? ClosedRemainderText(SalesOrderDraftLine line, decimal shipped)
    {
        ArgumentNullException.ThrowIfNull(line);
        return TryClosedRemainder(line, shipped, out _, out var remainder) ? Text(remainder) : null;
    }

    /// <summary>
    /// The store's rebuild of the recorded force-closure, after the lines, revisions, schedules and remainder changes and BEFORE
    /// the approval axis (whose version floor counts it as one step). It must name the current revision, an actor, a time, a
    /// reason within the bound, a non-blocking impact and EVERY line exactly once with its current ordered text, a shipped total
    /// within it and the remainder that is their difference; a stored closure that does not fit is refused, not repaired.
    /// </summary>
    public void RestoreForceClosure(ForceClosureFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        RequireApprovalNotRestored();
        if (State != SalesDraftState.Draft || Cancellation is not null || ForceClosure is not null || facts.Revision != Revision
            || string.IsNullOrWhiteSpace(facts.ClosedBy) || facts.ClosedBy.Length > MaximumActorLength
            || string.IsNullOrWhiteSpace(facts.Reason) || facts.Reason.Length > MaximumForceCloseReasonLength
            || !IsForceClosureImpact(facts.Impact) || !FitsLines(facts.Lines))
        {
            throw new InvalidOperationException("Kayıtlı zorla kapatma olguları siparişle uyuşmuyor.");
        }

        State = SalesDraftState.Closed;
        ForceClosure = facts;
    }

    /// <summary>Every line named exactly once; per line ordered − shipped as recorded snapshots, in line order.</summary>
    private IReadOnlyList<ForceClosureLineSnapshot> ClosedRemainders(IReadOnlyList<ForceClosureLineInput> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count != lines.Count || inputs.Any(input => input is null)
            || inputs.Select(input => input.LineId).Distinct().Count() != inputs.Count
            || inputs.Any(input => lines.All(line => line.Id != input.LineId)))
        {
            throw new ArgumentException("Zorla kapatma siparişin her satırının sevk edilen toplamını tam bir kez taşır.", nameof(inputs));
        }

        var result = new List<ForceClosureLineSnapshot>(lines.Count);
        foreach (var line in lines.OrderBy(candidate => candidate.LineNo))
        {
            var input = inputs.Single(candidate => candidate.LineId == line.Id);
            if (!TryClosedRemainder(line, input.Shipped, out var shipped, out var remainder))
            {
                throw new ArgumentException("Sevk edilen toplam satırın biriminde geçerli değil ya da sipariş miktarını aşıyor.", nameof(inputs));
            }

            result.Add(new ForceClosureLineSnapshot(line.Id, line.LineNo, line.QuantityText, Text(shipped), Text(remainder)));
        }

        return result.AsReadOnly();
    }

    private static bool TryClosedRemainder(SalesOrderDraftLine line, decimal shippedValue, out Quantity shipped, out Quantity remainder)
    {
        shipped = default;
        remainder = default;
        try
        {
            shipped = Quantity.Of(shippedValue, line.Quantity.Unit);
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (shipped.IsGreaterThan(line.Quantity))
        {
            return false;
        }

        remainder = line.Quantity.Subtract(shipped);
        return true;
    }

    /// <summary>The stored lines must be exactly the order's lines, each with its current ordered text and a consistent remainder.</summary>
    private bool FitsLines(IReadOnlyList<ForceClosureLineSnapshot>? stored)
    {
        if (stored is null || stored.Count != lines.Count)
        {
            return false;
        }

        foreach (var line in lines)
        {
            var entry = stored.SingleOrDefault(candidate => candidate?.LineId == line.Id);
            if (entry is null || entry.LineNo != line.LineNo
                || !string.Equals(entry.Ordered, line.QuantityText, StringComparison.Ordinal)
                || !decimal.TryParse(entry.Shipped, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var shippedValue)
                || !TryClosedRemainder(line, shippedValue, out var shipped, out var remainder)
                || !string.Equals(entry.Shipped, Text(shipped), StringComparison.Ordinal)
                || !string.Equals(entry.ClosedRemainder, Text(remainder), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A token, at least one source, each named once with a status, and none marked as blocking.</summary>
    private static bool IsForceClosureImpact(ForceClosureImpactSnapshot? impact) =>
        impact is not null && !string.IsNullOrWhiteSpace(impact.Token) && impact.Token.Length <= 128
        && impact.Sources is { Count: > 0 }
        && impact.Sources.All(entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Source)
            && !string.IsNullOrWhiteSpace(entry.Status) && !entry.Blocks)
        && impact.Sources.Select(entry => entry.Source).Distinct(StringComparer.Ordinal).Count() == impact.Sources.Count;

    /// <summary>Plain invariant text of an exact quantity (trailing zeros dropped; equal values print equally).</summary>
    private static string Text(Quantity quantity) =>
        Quantity.Of(quantity.Value, quantity.Unit).Value.ToString(CultureInfo.InvariantCulture);
}
