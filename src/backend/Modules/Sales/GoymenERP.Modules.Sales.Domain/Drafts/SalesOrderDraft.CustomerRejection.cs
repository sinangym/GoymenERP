namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// SAT-D115 (EK14): the customer's recorded rejection of one internally approved commercial revision — which revision, who
/// recorded it, when and why (a required reason). Append-only history: a rejected revision is never deleted and never accepted
/// afterwards; a later revision leaves the fact attached to the revision it was about.
/// </summary>
public sealed record CustomerRejectionFacts(int Revision, string RejectedBy, DateTimeOffset RejectedAtUtc, string Reason);

public sealed partial class SalesOrderDraft
{
    /// <summary>The technical bound of a customer-rejection reason: the revision reason's (the note's).</summary>
    public const int MaximumCustomerRejectionReasonLength = MaximumRevisionReasonLength;

    private readonly List<CustomerRejectionFacts> customerRejections = [];

    /// <summary>SAT-D115: every recorded customer rejection, oldest first; empty until a customer rejected a revision.</summary>
    public IReadOnlyList<CustomerRejectionFacts> CustomerRejections => customerRejections.AsReadOnly();

    /// <summary>Whether the customer rejected <paramref name="revision"/>.</summary>
    public bool IsCustomerRejected(int revision) => customerRejections.Any(rejection => rejection.Revision == revision);

    /// <summary>The rejection of the CURRENT revision, or null.</summary>
    public CustomerRejectionFacts? CurrentRevisionCustomerRejection =>
        customerRejections.SingleOrDefault(rejection => rejection.Revision == Revision);

    /// <summary>
    /// SAT-D115 (EK14): "sevk ve fatura, müşterinin kabul ettiği son revizyonun miktar ve fiyatıyla devam eder". The archived
    /// revision the customer last ACCEPTED, when the LAST internally approved revision (the current one when it is approved, else
    /// the last archived one) was rejected by the customer and an earlier accepted revision exists; else null. The shipping line
    /// port, the invoicing gate, the realization and the open-order report fall back to it. A last approved revision that is
    /// neither accepted nor rejected (still awaiting the customer) has no fallback: nothing ships or is invoiced (SAT-D113/D114).
    /// </summary>
    public ApprovedRevisionSnapshot? CustomerAcceptedFallback
    {
        get
        {
            if (!CustomerAcceptanceRequired || CustomerAcceptance is not { } accepted)
            {
                return null;
            }

            int? lastApproved = ApprovalState == InternalApprovalState.Approved
                ? Revision
                : archivedRevisions.Count > 0 ? archivedRevisions[^1].Revision : null;
            if (lastApproved is not { } last || accepted.Revision >= last || !IsCustomerRejected(last))
            {
                return null;
            }

            return archivedRevisions.FirstOrDefault(archived => archived.Revision == accepted.Revision);
        }
    }

    /// <summary>
    /// Whether the fallback revision names another customer (party or account) or currency than the order's header: the line
    /// port then ships nothing (fail-closed, as <see cref="CustomerOrCurrencyRevisionPending"/>) — nothing is shipped under a
    /// header the customer did not accept. An archive without its header is "different".
    /// </summary>
    public static bool HeaderDiffersFrom(SalesDraftHeader header, ApprovedRevisionSnapshot basis) =>
        basis.Header is not { } accepted
        || accepted.PartyId != header.PartyId || accepted.CompanyAccountId != header.CompanyAccountId
        || accepted.CurrencyId != header.CurrencyId;

    /// <summary>
    /// SAT-D115: records the customer's rejection of the current internally approved revision that awaits the customer's
    /// acceptance, with a required reason. Nothing is deleted or reverted: the lines keep the rejected revision's values, the
    /// rejected revision stays recorded as rejected, and every downstream reader continues on the last revision the customer
    /// accepted (<see cref="CustomerAcceptedFallback"/>). A new revision (price, quantity or header) archives the rejected one
    /// as usual. Refused (<see cref="SalesRuleCodes.StateConflict"/>): a cancelled or closed order, a revision not internally
    /// approved, an order without the acceptance gate (pre-Q9), a revision already accepted or already rejected, and an order the
    /// customer never accepted (Rev.0's behaviour is unchanged, SAT-D113; there is no earlier revision to continue on).
    /// </summary>
    public void RecordCustomerRejection(long expectedVersion, string actorId, string? reason, DateTimeOffset now)
    {
        RequireVersion(expectedVersion);
        var actor = RequireActor(actorId);
        RequireNotCancelled("İptal edilmiş ya da kapatılmış sipariş için müşteri reddi kaydedilmez.");
        if (ApprovalState != InternalApprovalState.Approved)
        {
            throw StateConflict("Müşteri reddi yalnız iç onaylı revizyona kaydedilir.");
        }

        if (!CustomerAcceptanceRequired)
        {
            throw StateConflict("Bu geçiş öncesi sipariş için müşteri kabulü ya da reddi aranmaz.");
        }

        if (IsCustomerAccepted)
        {
            throw StateConflict("Siparişin güncel revizyonu müşteri tarafından kabul edilmiş; reddedilemez.");
        }

        if (IsCustomerRejected(Revision))
        {
            throw StateConflict("Siparişin güncel revizyonu zaten müşteri tarafından reddedilmiş.");
        }

        if (CustomerAcceptance is not { } accepted || accepted.Revision >= Revision
            || archivedRevisions.All(archived => archived.Revision != accepted.Revision))
        {
            throw StateConflict("Müşterinin daha önce kabul ettiği bir revizyon yok; sipariş reddedilmez, iptal edilir.");
        }

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is null)
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionReasonRequired, "Müşteri reddinin gerekçesi zorunludur.");
        }

        if (trimmedReason.Length > MaximumCustomerRejectionReasonLength)
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionReasonTooLong,
                $"Müşteri reddinin gerekçesi en fazla {MaximumCustomerRejectionReasonLength} karakterdir.");
        }

        customerRejections.Add(new CustomerRejectionFacts(Revision, actor, now.ToUniversalTime(), trimmedReason));
        Touch(now);
    }

    /// <summary>
    /// The store's rebuild of one recorded customer rejection, oldest first, AFTER the archived revisions and the customer
    /// acceptance and BEFORE the approval axis (its version floor counts one step per rejection). A stored rejection that does
    /// not fit (a revision above the current one, a repeat, a rejected revision that is the accepted one, blank facts) is
    /// refused, not repaired.
    /// </summary>
    public void RestoreCustomerRejection(CustomerRejectionFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        RequireApprovalNotRestored();
        if (!CustomerAcceptanceRequired || facts.Revision < 1 || facts.Revision > Revision
            || (customerRejections.Count > 0 && customerRejections[^1].Revision >= facts.Revision)
            || CustomerAcceptance?.Revision == facts.Revision
            || string.IsNullOrWhiteSpace(facts.RejectedBy) || facts.RejectedBy.Length > MaximumActorLength
            || string.IsNullOrWhiteSpace(facts.Reason) || facts.Reason.Length > MaximumCustomerRejectionReasonLength)
        {
            throw new InvalidOperationException("Kayıtlı müşteri reddi sipariş revizyonlarıyla uyuşmuyor.");
        }

        customerRejections.Add(facts with { RejectedBy = facts.RejectedBy.Trim(), RejectedAtUtc = facts.RejectedAtUtc.ToUniversalTime() });
    }
}
