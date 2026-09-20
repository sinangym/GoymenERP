namespace GoymenERP.Modules.Finance.Contracts;

// FS0: the Finance bank account master (card #66 5731563923). It moves no money.
//
// K2 (Sinan #66 5748258299) decided the card's fields: bank name and account type are MANDATORY;
// branch name/code, IBAN and account number are HOLDABLE. Göymen's own treasury card stays separate
// from Cari's counterparty IBAN — this type is the treasury side and names no counterparty.
//
// NOT here, and deliberately (see the PR body and #66 5748390243): the "usable for payment or
// collection" declaration and the IBAN requirement that attaches to it. Today IsUsableForReceipt
// derives from approval alone, so a declaration saying "not transfer-capable" would be contradicted
// by the behaviour, and a field whose meaning the system does not honour is worse than no field.
// The declaration and the derivation are one change and land together.

/// <summary>
/// Propose: the company comes from the server context, never from the request.
/// <para>
/// K2's mandatory pair is <paramref name="BankName"/> and <paramref name="AccountType"/>. The rest are
/// holdable: a special credit or term account with no IBAN is definable, which is what K2 requires, and
/// an IBAN that IS given must be structurally valid (<see cref="FinanceIban"/>) — a valid IBAN is
/// still not proof of ownership, and no bank data is derived from it.
/// </para>
/// </summary>
public sealed record BankAccountProposeRequest(
    string DisplayName,
    Guid CurrencyId,
    string BankName,
    string AccountType,
    string? BranchName = null,
    string? BranchCode = null,
    string? Iban = null,
    string? AccountNumber = null);

// Approve: Ö4 (#66 5722637822) — a single authorized person other than the one who proposed it.
public sealed record BankAccountApproveRequest(Guid AccountId, string ExpectedVersion);

public sealed record BankAccountCommandResult(CommandOutcome Outcome, BankAccountView? Resource,
    bool IsReplay, string OriginalCorrelationId, IReadOnlyList<ValidationIssue> Issues);

// FIN-UI-1 HTTP: the approval body. The account is the route's; only the expected version travels.
public sealed record BankAccountApprovalBody(string ExpectedVersion);

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
    string? AccountNumber);

// F-04's list (FIN-UI-1): each account with whether the caller proposed it, so a screen can offer the
// approval only to someone else (Ö4). The server refuses a self-approval regardless.
public sealed record BankAccountMasterItem(BankAccountView Account, bool IsOwnProposal, BankAccountCard Card);
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

    // Under the read authority: the company's accounts for F-04, proposals included.
    Task<ReadResult<BankAccountMasterPage>> ListMasterAsync(FinanceContext context, CancellationToken cancellationToken);

    // Under the propose authority: the currencies a proposal may name, never a default.
    Task<ReadResult<BankAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken);
}
