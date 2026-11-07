namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D32 (EK3): "Tüm eksenler tamamlansa da sipariş kendiliğinden kapanmaz; 'kapanmaya hazır' işareti çıkar ve kullanıcı
/// kapatır." SAT-D168 (EK24): the user closure is its own action (<c>sales.orders.close</c>); SAT-D215 (EK37): no reason is
/// required. The domain half:
/// <list type="bullet">
/// <item><b>Only Approved, only open:</b> the lifecycle moves <c>Draft → Closed</c> for an APPROVED order without a pending
/// remainder change, as the force-closure; a cancelled or already closed order is a <see cref="SalesRuleCodes.StateConflict"/>.</item>
/// <item><b>"Kapanmaya hazır" is the caller's reading:</b> the seven status axes come from the owners (Logistics shipped totals,
/// the Settings tolerance, the owner-less axes) and are not this aggregate's knowledge; the caller recomputes the readiness
/// inside the save and refuses a not-ready order BEFORE this runs.</item>
/// <item><b>History stays:</b> the approval facts, the revision, the lines, the archived revisions and the schedules stay; only
/// the lifecycle state, the user-closure facts and the version (one step) move. Terminal: every mutation entry refuses it.</item>
/// <item><b>Same state as a force-closure, another record</b> (reversible implementation detail, EK37 "Sınırlar"): an order
/// carries a user closure OR a force-closure, never both; the restore refuses both at once.</item>
/// </list>
/// </summary>
public sealed partial class SalesOrderDraft
{
    /// <summary>SAT-D32: the recorded user closure (who, when, which revision); null while open or when force-closed.</summary>
    public UserClosureFacts? UserClosure { get; private set; }

    /// <summary>
    /// Closes a "kapanmaya hazır" order on the user's action: the preconditions (<see cref="RequireUserClosable"/>) and the
    /// caller's readiness reading (already passed); records who closed and moves the lifecycle to
    /// <see cref="SalesDraftState.Closed"/> with one version step.
    /// </summary>
    public UserClosureFacts CloseByUser(long expectedVersion, string actorId, DateTimeOffset now)
    {
        var actor = RequireActor(actorId);
        RequireUserClosable(expectedVersion);
        var facts = new UserClosureFacts(Revision, actor, now.ToUniversalTime());
        State = SalesDraftState.Closed;
        UserClosure = facts;
        Touch(now);
        return facts;
    }

    /// <summary>
    /// The user closure's own preconditions WITHOUT the transition — the expected version, the lifecycle (not cancelled, not
    /// closed), the approval state (Approved) and no pending remainder change — so the caller can refuse a stale or wrong-state
    /// command before it asks any owner for the readiness.
    /// </summary>
    public void RequireUserClosable(long expectedVersion)
    {
        RequireVersion(expectedVersion);
        RequireNotCancelled("İptal edilmiş sipariş kapatılmaz.");
        if (ApprovalState != InternalApprovalState.Approved)
        {
            throw StateConflict("Yalnız onaylı sipariş kapatılır.");
        }

        if (PendingRemainderChange is not null)
        {
            throw StateConflict("Siparişte karar bekleyen bir kalan değişikliği var; önce o karara bağlanır.");
        }
    }

    /// <summary>
    /// The store's rebuild of the recorded user closure, after the lines, revisions, schedules and remainder changes and BEFORE
    /// the approval axis (whose version floor counts the closed lifecycle as one step). It must name the current revision, an
    /// actor and a time, and the order must carry no cancellation and no force-closure; a stored closure that does not fit is
    /// refused, not repaired.
    /// </summary>
    public void RestoreUserClosure(UserClosureFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        RequireApprovalNotRestored();
        if (State != SalesDraftState.Draft || Cancellation is not null || ForceClosure is not null || UserClosure is not null
            || facts.Revision != Revision || string.IsNullOrWhiteSpace(facts.ClosedBy) || facts.ClosedBy.Length > MaximumActorLength)
        {
            throw new InvalidOperationException("Kayıtlı kullanıcı kapatması olguları siparişle uyuşmuyor.");
        }

        State = SalesDraftState.Closed;
        UserClosure = facts;
    }
}
