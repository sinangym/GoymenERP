namespace GoymenERP.Modules.Finance.Infrastructure.Persistence;

/// <summary>A row of the server-resolved company (ADR-002): its company is set once and never changes.</summary>
internal interface IFinanceTenantRow
{
    Guid CompanyId { get; }
}

/// <summary>
/// A manual bank receipt DRAFT (FS1). It is preparation only: no money source, no bank balance,
/// no open-item or accounting effect. Every save replaces the whole preparation; <see cref="Version"/>
/// is the optimistic concurrency token and the lossless version the contract shows. The amount is
/// the exact text that passed the Settings profile's scale; no database decimal is a money scale.
/// </summary>
internal sealed class ReceiptDraftRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid PartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid TreasuryAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public string AmountText { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileVersion { get; set; } = string.Empty;
    public DateOnly OccurredOn { get; set; }
    public DateOnly? ValueOn { get; set; }
    public string? BankReference { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;

    /// <summary>
    /// FIN-T05a: <see cref="ReceiptStages.Draft"/> until one Record makes it <see cref="ReceiptStages.Recorded"/>;
    /// a recorded source is never edited again (its correction is a separate reversal, FIN-T06).
    /// </summary>
    public int Stage { get; set; }
    public DateTimeOffset? RecordedAtUtc { get; set; }
    public string? RecordedBy { get; set; }

    /// <summary>
    /// DEC-FIN-014: a customer POS collection's facts, all three NULL (a bank transfer) or all three set
    /// (<c>CK_ReceiptDrafts_Pos</c>). <see cref="AmountText"/> is then the GROSS card amount — the customer's debt closes
    /// at it — and the commission and the bank net are kept apart from it, never subtracted from the customer credit.
    /// The net is derived by the server (gross − commission), exact text; the bearer is <see cref="PosCommissionBearers"/>.
    /// </summary>
    public string? PosCommissionAmountText { get; set; }
    public string? PosBankNetAmountText { get; set; }
    public int? PosCommissionBearer { get; set; }
}

/// <summary>The stored stage of a manual receipt source.</summary>
internal static class ReceiptStages
{
    public const int Draft = 0;
    public const int Recorded = 1;
}

/// <summary>
/// FIN-T05a: THE money event of a recorded manual bank receipt — one per source, by (company, source, kind), so a
/// second event for the same source is impossible even for a lost race. The bank inflow and the customer credit are
/// two READS of this one row, never two rows (card #66 5743678389 §2–§3); a distribution is not a money event.
/// Append-only. Its facts are kept apart as Ö5 keeps them: the transaction date, the value date and the recording
/// instant. The amount is the exact text that passed the Settings profile's scale; no database decimal is a scale.
/// </summary>
internal sealed class MoneyEventRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SourceId { get; set; }
    public int Kind { get; set; }
    public Guid PartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid TreasuryAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public string AmountText { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileVersion { get; set; } = string.Empty;
    public DateOnly OccurredOn { get; set; }
    public DateOnly? ValueOn { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// DEC-FIN-014: the POS facts of the source, copied into its ONE event (all NULL for a bank transfer). The customer
    /// credit reads <see cref="AmountText"/> (gross); a bank inflow read takes <see cref="PosBankNetAmountText"/> when
    /// set; the commission is its own fact. Still one row: a POS collection is not three money events.
    /// </summary>
    public string? PosCommissionAmountText { get; set; }
    public string? PosBankNetAmountText { get; set; }
    public int? PosCommissionBearer { get; set; }
}

/// <summary>The stored values of <c>PosCommissionBearer</c> (the contract enum's own values).</summary>
internal static class PosCommissionBearers
{
    public const int Company = 1;
    public const int Customer = 2;
}

/// <summary>The distinct recorded and reversing manual bank receipt money-event kinds.</summary>
internal static class MoneyEventKinds
{
    public const int ManualBankReceiptRecorded = 1;

    /// <summary>
    /// FIN-T06: an immutable counter-event. Amount stays exact-positive text;
    /// the kind, source link and proposal establish reversal semantics.
    /// </summary>
    public const int ManualBankReceiptReversed = 2;
}

/// <summary>R47 manual outgoing bank payment, from preparation through a separately confirmed record.</summary>
internal sealed class ManualBankPaymentRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public long Version { get; set; }
    public Guid PartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid TreasuryAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public string AmountText { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileVersion { get; set; } = string.Empty;
    public DateOnly OccurredOn { get; set; }
    public DateOnly? ValueOn { get; set; }
    public string? BankReference { get; set; }
    public string? Description { get; set; }
    public Guid? LinkedDocumentId { get; set; }
    public string? LinkedDocumentVersion { get; set; }
    public int Status { get; set; }
    public string PreparedBy { get; set; } = string.Empty;
    public DateTimeOffset PreparedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string? RecordedBy { get; set; }
    public DateTimeOffset? RecordedAtUtc { get; set; }
    public bool BankResultConfirmed { get; set; }
}

internal static class ManualBankPaymentStatuses
{
    public const int Draft = 0;
    public const int Approved = 1;
    public const int Recorded = 2;
}

/// <summary>Exactly one append-only outgoing money event for a recorded R47 payment.</summary>
internal sealed class OutgoingMoneyEventRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid PaymentId { get; set; }
    public Guid PartyId { get; set; }
    public Guid CompanyAccountId { get; set; }
    public Guid TreasuryAccountId { get; set; }
    public Guid CurrencyId { get; set; }
    public string AmountText { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileVersion { get; set; } = string.Empty;
    public DateOnly OccurredOn { get; set; }
    public DateOnly? ValueOn { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
}

/// <summary>
/// A Finance bank account (FS0, card #66 5731563923): exactly the fields of the reviewed
/// <c>BankAccountView</c>. Proposed (1) is neither active nor usable; Approved (2) is both, and
/// only a user other than the proposer approves it (Ö4, #66 5722637822). The display name and
/// currency never change here; nothing is deleted. It holds no balance and moves no money.
/// </summary>
internal sealed class BankAccountRow : IFinanceTenantRow
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CurrencyId { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    // K2 (Sinan #66 5748258299): the card. BankName and AccountType are MANDATORY for a card written
    // from now on — enforced where the card is written, not by the column, because the column also has
    // to hold the rows that already exist. Those predate K2 and carry NULL, and B3 (#66 5750700247)
    // now says what they mean: nothing here backfills, defaults or reinterprets them.
    public string? BankName { get; set; }
    public string? AccountType { get; set; }
    public string? BranchName { get; set; }
    public string? BranchCode { get; set; }

    // Normalised (no spaces, upper case) when present, so the stored value is the validated value.
    public string? Iban { get; set; }
    public string? AccountNumber { get; set; }

    /// <summary>
    /// B3: the card's own statement that it may move money. NULL is the LEGACY row and means UNDECLARED,
    /// which is refused on the money path — "Banka adı+tür+IBAN'ın dolu olması bu açık beyanın yerine
    /// geçmez". The column is nullable for exactly one reason: to carry rows written before the
    /// declaration existed, without inventing a value for them. It is required of every card written
    /// from now on, enforced where the card is written.
    /// </summary>
    public bool? MoneyMovementEligible { get; set; }

    /// <summary>
    /// D4: an IBAN change proposed on an APPROVED account, waiting for its own Ö4 approval. It lives
    /// beside the row and never in <see cref="Iban"/> until approved, so the approved value the money
    /// path reads does not move while a change is pending. All four are NULL together or set together.
    /// </summary>
    public string? PendingIban { get; set; }
    public string? PendingIbanProposedBy { get; set; }
    public DateTimeOffset? PendingIbanProposedAtUtc { get; set; }
    public string? PendingIbanReason { get; set; }

    public int Status { get; set; }
    public long Version { get; set; }
    public string ProposedBy { get; set; } = string.Empty;
    public DateTimeOffset ProposedAtUtc { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

/// <summary>
/// One accepted command attempt (ADR-011): (company, command, key hash) is its key. The raw
/// Idempotency-Key is never stored, only its upper-case SHA-256 (§2). The actor, the canonical
/// payload hash, the answered result and the first correlation are kept, so the same key with the
/// same payload and actor replays that result, and another payload or actor is refused (§3–§4).
/// It names exactly one target: a draft or a bank account. Append-only.
/// </summary>
internal sealed class FinanceCommandRow : IFinanceTenantRow
{
    public Guid CompanyId { get; set; }
    public string CommandName { get; set; } = string.Empty;
    public string IdempotencyKeyHash { get; set; } = string.Empty;
    public string ActorId { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public Guid? DraftId { get; set; }
    public Guid? BankAccountId { get; set; }
    public Guid? ReversalProposalId { get; set; }
    public Guid? ManualBankPaymentId { get; set; }
    public Guid? FinanceAllocationId { get; set; }
    public string ResultJson { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>Organization's company, referenced by identity only (the table is Organization's, not migrated here).</summary>
internal sealed class OrganizationCompanyReference
{
    public Guid Id { get; set; }
}
