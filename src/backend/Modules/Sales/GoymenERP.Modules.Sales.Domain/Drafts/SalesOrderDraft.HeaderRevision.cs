namespace GoymenERP.Modules.Sales.Domain.Drafts;

/// <summary>
/// API-04: the header values a header revision sets — every member is the NEW value (a full replacement; an unchanged member
/// repeats the current one). <see cref="Money"/> is the Settings money profile of <see cref="CurrencyId"/> on the order's
/// business date, given exactly when the currency changes. A null reference / owner / terms clears it.
/// </summary>
public sealed record SalesHeaderRevisionInput(
    Guid PartyId,
    Guid CompanyAccountId,
    Guid CurrencyId,
    MoneyProfileSnapshot? Money,
    OwnerReference? DeliveryAddress,
    OwnerReference? Contact,
    Guid? SalesOwnerUserId,
    SalesDueTerms? DueTerms);

public sealed partial class SalesOrderDraft
{
    /// <summary>SAT-D152 (EK21): the Turkish text of the quote-customer lock (the screen shows its own copy of it).</summary>
    public const string QuoteCustomerLockedMessage =
        "Tekliften dönen siparişin carisi değiştirilemez (SAT-D152); başka müşteri için sipariş iptal edilip yeni teklif ya da sipariş açılır.";

    /// <summary>
    /// No 162 / SAT-D152: the quote this order was converted from (null for an order created directly). Set once, by the
    /// conversion or the store's rebuild; never changed. An order converted from a quote keeps the quote's customer.
    /// </summary>
    public Guid? SourceQuoteId { get; private set; }

    /// <summary>The conversion's / the store's one-time binding of the source quote (not a transition; no version moves).</summary>
    public void RestoreSourceQuote(Guid quoteId)
    {
        if (SourceQuoteId is not null)
        {
            throw new InvalidOperationException("Kaynak teklif yalnız bir kez bağlanır.");
        }

        SourceQuoteId = Identity.Require(quoteId, nameof(quoteId));
    }

    /// <summary>
    /// DEC-SAL-002 (KILITLI, K0, verbatim): "Cari, ürün, miktar, fiyat, iskonto, para, vade ve risk istisnası değişirse yeni
    /// revizyon ve yeniden onay gerekir." — "Rev.0 korunur". API-04 (SATIS_HAZIRLIK_PLANI_v1 §3.1): the header revision of the
    /// customer account, the currency, the due terms (DEC-SAL-008 "vade"), the sales owner and the delivery information
    /// (address, contact), with a required reason. The quantity revision's shape, step for step:
    /// <list type="bullet">
    /// <item>From <c>Preparing</c> or <c>Rejected</c> the header simply changes (nothing approved is touched; the revision number
    /// stays). From <c>Approved</c> the approved revision is archived FIRST — its approval facts, money profile, lines and now
    /// its HEADER as approved, with the reason — and only then does the draft advance to the next revision, back to
    /// <c>Preparing</c>; the existing submit/approve commands re-approve it (SAT-D103 machinery, unchanged). <c>PendingApproval</c>,
    /// <c>Withdrawn</c> and a cancelled or closed order are refused (<see cref="SalesRuleCodes.StateConflict"/>).</item>
    /// <item>SAT-D126 (EK18): a change of ONLY the sales owner and/or the delivery address / contact is recorded in place — no
    /// archive, no Rev+1, the approval axis and the shipping / invoicing gates untouched — with the required reason and the audit.
    /// A change of the due terms, the customer or the currency re-enters approval (DEC-SAL-002, SAT-D123).</item>
    /// <item>SAT-D152 (EK21): an order converted from a quote never changes its customer
    /// (<see cref="SalesRuleCodes.HeaderRevisionQuoteCustomer"/>); its currency follows SAT-D128 unchanged.</item>
    /// <item>Customer or currency (SAT-D128): refused while any line is priced (<see cref="SalesRuleCodes.HeaderRevisionPricedLines"/>)
    /// — a wrong customer or currency means cancelling the order and opening a new one; a currency change brings the new
    /// currency's money profile on the order's business date (the create's money rules). Whether shipping has started
    /// (allocations, loading, dispatch notes) is the caller's read of the owners, made BEFORE this runs.</item>
    /// <item>A revision that changes nothing is refused (<see cref="SalesRuleCodes.RevisionNoChange"/>): an approval is never
    /// discarded for nothing. Due terms compare by value (<see cref="SalesDueTerms.SameTerms"/>).</item>
    /// </list>
    /// Returns the archived snapshot when one was made (the store persists it in the same save), else null.
    /// </summary>
    public ApprovedRevisionSnapshot? ReviseHeader(long expectedVersion, SalesHeaderRevisionInput input, string actorId, string? reason,
        DateTimeOffset now)
    {
        var actor = RequireActor(actorId);
        var trimmedReason = RequireHeaderRevisable(expectedVersion, reason);
        ArgumentNullException.ThrowIfNull(input);
        Identity.Require(input.PartyId, nameof(input.PartyId));
        Identity.Require(input.CompanyAccountId, nameof(input.CompanyAccountId));
        Identity.Require(input.CurrencyId, nameof(input.CurrencyId));
        foreach (var reference in new[] { input.DeliveryAddress, input.Contact })
        {
            if (reference is not null && (reference.Id == Guid.Empty || string.IsNullOrWhiteSpace(reference.ExpectedVersion)))
            {
                throw new ArgumentException("Sahip referansı kimlik ve beklenen sürüm ister.", nameof(input));
            }
        }

        if (input.SalesOwnerUserId == Guid.Empty)
        {
            throw new ArgumentException("Satış sorumlusu boş kimlik olamaz.", nameof(input));
        }

        var customerChanged = input.PartyId != Header.PartyId || input.CompanyAccountId != Header.CompanyAccountId;
        var currencyChanged = input.CurrencyId != Header.CurrencyId;
        if (customerChanged && SourceQuoteId is not null)
        {
            throw new SalesRuleException(SalesRuleCodes.HeaderRevisionQuoteCustomer, QuoteCustomerLockedMessage);
        }

        if ((customerChanged || currencyChanged) && lines.Any(line => line.Commercial.UnitPrice is not null))
        {
            throw new SalesRuleException(SalesRuleCodes.HeaderRevisionPricedLines,
                "Fiyatlı satırı olan siparişin carisi ya da para birimi değiştirilmez (SAT-D128); yanlışsa sipariş iptal edilip yeni sipariş açılır.");
        }

        var money = Money;
        if (currencyChanged)
        {
            money = RequireRevisedMoney(input.Money ?? throw new ArgumentException("Para birimi değişince yeni para profili gerekir.", nameof(input)),
                input.CurrencyId);
        }
        else if (input.Money is not null)
        {
            throw new ArgumentException("Para birimi değişmeden para profili verilmez.", nameof(input));
        }

        var unchanged = !customerChanged && !currencyChanged
            && input.DeliveryAddress == Header.DeliveryAddress && input.Contact == Header.Contact
            && input.SalesOwnerUserId == Header.SalesOwnerUserId
            && SalesDueTerms.SameTerms(input.DueTerms, Header.DueTerms);
        if (unchanged)
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionNoChange,
                "Başlık revizyonu hiçbir değeri değiştirmiyor; onay boşuna düşürülmez.");
        }

        // SAT-D126 (EK18): a change of ONLY the sales owner and/or the delivery address / contact does not re-enter approval and
        // opens no commercial revision — the order keeps its revision and approval axis; the reason and the audit record it. Any
        // change of the due terms, the customer or the currency re-enters approval (DEC-SAL-002).
        var commercialChange = customerChanged || currencyChanged || !SalesDueTerms.SameTerms(input.DueTerms, Header.DueTerms);
        ApprovedRevisionSnapshot? archived = null;
        if (commercialChange && ApprovalState == InternalApprovalState.Approved)
        {
            // The approved revision as it stands — its header included — archived BEFORE anything of it changes.
            archived = new ApprovedRevisionSnapshot(Revision, Approval, Money.ProfileId, Money.ProfileVersion, Money.Scale,
                now.ToUniversalTime(), actor,
                [.. lines.Select(line => new RevisionLineSnapshot(line.Id, line.LineNo, line.Product.ProductId, line.Product.ProductVersion,
                    line.OrderUnit.UnitId, line.QuantityText, line.Commercial))],
                trimmedReason, CurrentHeaderSnapshot());
            archivedRevisions.Add(archived);
            Revision += 1;
            ApprovalState = InternalApprovalState.Preparing;
            Approval = ApprovalFacts.None;
        }

        // A value-equal due terms keeps the stored spelling (as a value-equal quantity text keeps its text).
        var dueTerms = SalesDueTerms.SameTerms(input.DueTerms, Header.DueTerms) ? Header.DueTerms : input.DueTerms;
        Header = Header with
        {
            PartyId = input.PartyId,
            CompanyAccountId = input.CompanyAccountId,
            CurrencyId = input.CurrencyId,
            DeliveryAddress = input.DeliveryAddress,
            Contact = input.Contact,
            SalesOwnerUserId = input.SalesOwnerUserId,
            DueTerms = dueTerms,
        };
        Money = money;
        Touch(now);
        return archived;
    }

    /// <summary>
    /// The header revision's own preconditions WITHOUT the change — the expected version, the lifecycle and approval state and
    /// the reason — so the caller can refuse a stale or wrong-state command before it asks any owner. Returns the trimmed
    /// reason. <see cref="ReviseHeader"/> runs the same checks again.
    /// </summary>
    public string RequireHeaderRevisable(long expectedVersion, string? reason)
    {
        RequireVersion(expectedVersion);
        RequireNotCancelled("İptal edilmiş siparişin başlığı değiştirilmez.");
        if (State != SalesDraftState.Draft)
        {
            throw new SalesRuleException(SalesRuleCodes.DraftNotDraft, "Yalnız taslak değiştirilebilir.");
        }

        if (ApprovalState == InternalApprovalState.PendingApproval)
        {
            throw StateConflict("Onay bekleyen siparişin başlığı değiştirilmez; önce karar verilir.");
        }

        if (ApprovalState == InternalApprovalState.Withdrawn)
        {
            throw StateConflict("Geri çekilmiş taslak değiştirilmez.");
        }

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason is null)
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionReasonRequired, "Revizyon gerekçesi zorunludur.");
        }

        if (trimmedReason.Length > MaximumRevisionReasonLength)
        {
            throw new SalesRuleException(SalesRuleCodes.RevisionReasonTooLong,
                $"Revizyon gerekçesi en fazla {MaximumRevisionReasonLength} karakterdir.");
        }

        return trimmedReason;
    }

    /// <summary>
    /// SAT-D103 does not cover a currency change ("Para biriminin değişmesi bu satıra dahil değildir") and no decision lets a
    /// customer change ship under the old customer: while the current (not yet approved) revision names another customer
    /// (party or account) or another currency than the LAST APPROVED revision, nothing of the order is shipped (the line port
    /// answers not approved) and the approval re-reads the owner records (fail-closed). False when no approved revision is
    /// archived, when the current revision is approved, or for a cancelled / closed order. SAT-D115 (EK14): when the customer
    /// rejected the last approved revision, the comparison base is the revision the customer last ACCEPTED (the one the order
    /// ships on, <see cref="CustomerAcceptedFallback"/>), never the rejected one — a newer revision that returns to the accepted
    /// customer and currency is no customer/currency change (SAT-D103: shipping goes on under the accepted revision).
    /// </summary>
    public bool CustomerOrCurrencyRevisionPending =>
        State == SalesDraftState.Draft
        && ApprovalState is InternalApprovalState.Preparing or InternalApprovalState.PendingApproval or InternalApprovalState.Rejected
        && archivedRevisions.Count > 0
        && HeaderDiffersFrom(Header, CustomerAcceptedFallback ?? archivedRevisions[^1]);

    /// <summary>The header as it stands, for an archive (API-04).</summary>
    private RevisionHeaderSnapshot CurrentHeaderSnapshot() =>
        new(Header.PartyId, Header.CompanyAccountId, Header.CurrencyId, Header.DeliveryAddress, Header.Contact,
            Header.SalesOwnerUserId, Header.DueTerms);

    /// <summary>The create's money-profile rules for the revised currency on the order's business date.</summary>
    private MoneyProfileSnapshot RequireRevisedMoney(MoneyProfileSnapshot money, Guid currencyId)
    {
        if (money.CompanyId != CompanyId)
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotCompanyMismatch, "Para profili başka şirketin.");
        }

        if (money.CurrencyId != currencyId || money.ProfileId == Guid.Empty || string.IsNullOrWhiteSpace(money.ProfileVersion))
        {
            throw new ArgumentException("Para profili siparişin yeni para birimini adlandırmalı ve tam olmalıdır.", nameof(money));
        }

        if (money.EffectiveOn != Header.BusinessDate)
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotDateMismatch, "Para profili siparişin iş tarihi için alınmamış.");
        }

        if (!string.Equals(money.Operation, MoneyOperation, StringComparison.Ordinal))
        {
            throw new SalesRuleException(SalesRuleCodes.SnapshotOperationMismatch,
                "Para profili siparişin operasyonu için değil başka bir operasyon için alınmış.");
        }

        if (money.Scale < 0 || (money.EffectiveThrough is { } moneyThrough && moneyThrough < money.EffectiveFrom))
        {
            throw new ArgumentException("Para profili snapshot'ı kendiyle çelişiyor.", nameof(money));
        }

        if (!money.IsActive || Header.BusinessDate < money.EffectiveFrom
            || (money.EffectiveThrough is { } until && Header.BusinessDate > until))
        {
            throw new SalesRuleException(SalesRuleCodes.MoneyProfileNotInEffect, "Para profili iş tarihinde etkin değil.");
        }

        return money;
    }
}
