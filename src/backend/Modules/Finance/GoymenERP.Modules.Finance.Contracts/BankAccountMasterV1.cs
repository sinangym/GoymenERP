using System.Text.Json.Serialization;

namespace GoymenERP.Modules.Finance.Contracts;

// FS0: the Finance bank account master (card #66 5731563923). It moves no money.
//
// K2 (Sinan #66 5748258299) decided the card's fields: bank name and account type are MANDATORY;
// branch name/code, IBAN and account number are HOLDABLE. Göymen's own treasury card stays separate
// from Cari's counterparty IBAN — this type is the treasury side and names no counterparty.
//
// B3 (Sinan #66 5750700247) answers what this comment used to hold open. The declaration and the
// derivation land together here, as the earlier version of this file said they must: the card now
// DECLARES whether it is eligible for money movement, and IsUsableForReceipt is derived from that
// declaration instead of from approval alone.
//
// The answer is FAIL-CLOSED on the legacy rows, in Sinan's own words: "Yeni `para hareketine uygun`
// beyanı eski satırlarda NULL ise hesap ödeme/tahsilata uygun sayılmaz. Banka adı+tür+IBAN'ın dolu
// olması bu açık beyanın yerine geçmez." So a row that predates this change is NOT money-eligible,
// and completeness of its fields does not make it so — eligibility comes from the declaration, never
// from data. Nothing is deleted and nothing is backfilled; the card opens as a definition and says
// which state it is in.

/// <summary>
/// Propose: the company comes from the server context, never from the request.
/// <para>
/// K2's mandatory pair is <paramref name="BankName"/> and <paramref name="AccountType"/>. The rest are
/// holdable: a special credit or term account with no IBAN is definable, which is what K2 requires, and
/// an IBAN that IS given must be structurally valid (<see cref="FinanceIban"/>) — a valid IBAN is
/// still not proof of ownership, and no bank data is derived from it.
/// </para>
/// <para>
/// B3: <paramref name="MoneyMovementEligible"/> is REQUIRED on a card written from now on, and it is a
/// declaration rather than a derivation — the caller says what the card is for. Declaring <c>true</c>
/// brings K2's IBAN rule with it ("Ödeme/tahsilatta kullanılacak hesapta IBAN zorunlu"), so a
/// money-eligible proposal without a valid IBAN is refused. Declaring <c>false</c> is how K2's IBAN-less
/// special credit or term account is defined, and it stays out of the money path by its own statement.
/// </para>
/// </summary>
public sealed record BankAccountProposeRequest(
    string DisplayName,
    Guid CurrencyId,
    string BankName,
    string AccountType,
    bool MoneyMovementEligible,
    string? BranchName = null,
    string? BranchCode = null,
    string? Iban = null,
    string? AccountNumber = null);

// Approve: Ö4 (#66 5722637822) — a single authorized person other than the one who proposed it.
public sealed record BankAccountApproveRequest(Guid AccountId, string ExpectedVersion);

/// <summary>
/// D4: propose a new IBAN for an already-approved account. The value does not take effect here — it
/// waits for a different authorized actor's approval, which is what "yeni değer Ö4 öneren≠onaylayan
/// sürecine girer" asks for.
/// <para>
/// <paramref name="Reason"/> is required because Sinan's acceptance says the audit must show "eski/yeni,
/// sebep ve aktör": a reason that may be omitted is a reason the audit cannot show. Its length bound
/// follows the existing approval-reason precedent rather than inventing a new limit class.
/// </para>
/// </summary>
public sealed record BankAccountIbanChangeProposeRequest(Guid AccountId, string ExpectedVersion, string Iban, string Reason);

/// <summary>D4: approve the waiting IBAN. Ö4 — never the actor who proposed the change.</summary>
public sealed record BankAccountIbanChangeApproveRequest(Guid AccountId, string ExpectedVersion);

public sealed record BankAccountCommandResult(CommandOutcome Outcome, BankAccountView? Resource,
    bool IsReplay, string OriginalCorrelationId, IReadOnlyList<ValidationIssue> Issues);

// FIN-UI-1 HTTP: the approval body. The account is the route's; only the expected version travels.
// D4 reuses it for the IBAN change's approval: that decision carries no payload of its own either —
// the value being approved is the one already waiting on the row, not one the approver supplies.
public sealed record BankAccountApprovalBody(string ExpectedVersion);

/// <summary>D4 HTTP: proposing the change. The account is the route's; the new IBAN and its reason travel.</summary>
public sealed record BankAccountIbanChangeBody(string ExpectedVersion, string Iban, string Reason);

/// <summary>
/// K2's card fields, carried ONLY on the master (F-04). They are deliberately NOT on
/// <see cref="BankAccountView"/>, which is what the receipt draft and Record paths read: those paths
/// need to know that an account exists, is approved and is in the right currency — they have no use
/// for an IBAN, and a field that travels where it is not needed is a field that leaks. K2's own
/// acceptance asks that IBAN and account-number data not reach someone who should not see it; the
/// narrowest way to satisfy that is not to put it on the widely-read view at all.
/// <para>
/// Reading the master already requires the narrow <c>finance.bank-account.read</c> name, so an actor
/// without it — a Sevkiyatçı, for instance — receives no account at all, and therefore no IBAN.
/// </para>
/// </summary>
public sealed record BankAccountCard(
    string BankName,
    string AccountType,
    string? BranchName,
    string? BranchCode,
    string? Iban,
    string? AccountNumber,
    BankAccountMoneyEligibility MoneyEligibility,
    BankAccountPendingIbanChange? PendingIbanChange);

/// <summary>
/// B3: what the card SAYS about money movement. Three states and never null, because the third one is
/// the whole point of the answer: a row that predates the declaration is not "false", it is UNDECLARED,
/// and Sinan asks for that state to be shown specifically ("para hareketine uygunluk beyanı
/// tamamlanmamış") rather than folded into a no. A nullable bool would make the legacy row and a
/// deliberate "not for money" indistinguishable — the same argument that made
/// <c>canOpenLocationDetail</c> required (#377) and the price disclosure a never-null marker (#382).
/// <para>
/// Only <see cref="Eligible"/> reaches the money path. <see cref="NotEligible"/> and
/// <see cref="Undeclared"/> are both refused there, for different reasons that the user is told apart.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BankAccountMoneyEligibility>))]
public enum BankAccountMoneyEligibility { Eligible, NotEligible, Undeclared }

/// <summary>
/// D4: an IBAN change waiting for its own Ö4 approval. NULL here means there is no pending change —
/// an absence, not a withheld answer, because reading this card already requires the narrow
/// <c>finance.bank-account.read</c> name and nothing about this member is per-field authorised.
/// <para>
/// The proposed value lives HERE and never in the account's own IBAN until it is approved, which is
/// how "eski onaylı değer korunur" and "değişen IBAN onaylanmadan yeni para işleminde kullanılamaz"
/// both hold without a second refusal site: money reads the approved column, which has not moved.
/// </para>
/// </summary>
public sealed record BankAccountPendingIbanChange(
    string Iban,
    string ProposedBy,
    DateTimeOffset ProposedAtUtc,
    string Reason);

// F-04's list (FIN-UI-1): each account with whether the caller proposed it, so a screen can offer the
// approval only to someone else (Ö4). The server refuses a self-approval regardless.
// D4 adds the same fact for a pending IBAN change, for the same reason and with the same caveat.
public sealed record BankAccountMasterItem(BankAccountView Account, bool IsOwnProposal, BankAccountCard Card,
    bool IsOwnPendingIbanProposal);
public sealed record BankAccountMasterPage(Guid CompanyId, IReadOnlyList<BankAccountMasterItem> Items);

// The currencies a proposal may name: Settings' money currency candidates of the server-resolved
// company, passed through as the owner gave them. Order is undefined, no row is a default, and an
// empty list means "no candidates", never a made-up currency (Business #66 5702138790).
public sealed record BankAccountCurrencyCandidate(Guid CurrencyId, string Code, string DisplayLabel);
public sealed record BankAccountCurrencyCandidates(Guid CompanyId, IReadOnlyList<BankAccountCurrencyCandidate> Items);

// Every command checks fresh company/action authority, including a replay.
public interface IFinanceBankAccountMaster
{
    Task<BankAccountCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        BankAccountProposeRequest request, CancellationToken cancellationToken);

    Task<BankAccountCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        BankAccountApproveRequest request, CancellationToken cancellationToken);

    // D4: the IBAN change's own Ö4 pair. Separate actions with their own idempotency records and audit
    // events, on the EXISTING propose/approve permission names — the same dual control, applied to a change.
    Task<BankAccountCommandResult> ProposeIbanChangeAsync(FinanceContext context, CommandMetadata metadata,
        BankAccountIbanChangeProposeRequest request, CancellationToken cancellationToken);

    Task<BankAccountCommandResult> ApproveIbanChangeAsync(FinanceContext context, CommandMetadata metadata,
        BankAccountIbanChangeApproveRequest request, CancellationToken cancellationToken);

    // Under the read authority: the company's accounts for F-04, proposals included.
    Task<ReadResult<BankAccountMasterPage>> ListMasterAsync(FinanceContext context, CancellationToken cancellationToken);

    // Under the propose authority: the currencies a proposal may name, never a default.
    Task<ReadResult<BankAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken);
}
