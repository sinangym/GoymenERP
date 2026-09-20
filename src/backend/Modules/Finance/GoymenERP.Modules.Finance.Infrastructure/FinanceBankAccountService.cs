using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FS0 (card #66 5731563923): the Finance bank account master and its authorized reader, the
/// real source behind <see cref="IFinanceBankAccountReader"/>. It holds no balance and moves no money.
/// <list type="bullet">
/// <item><b>Fields (K2, Sinan #66 5748258299):</b> <see cref="BankAccountView"/>'s, plus the card —
/// bank name and account type MANDATORY, branch name/code, IBAN and account number holdable. The card
/// travels on <see cref="BankAccountCard"/>, which the master carries and the receipt path does not:
/// a draft or a Record needs an account to exist, be approved and match the currency, never its IBAN.
/// A given IBAN must be structurally valid (<see cref="FinanceIban"/>), which is arithmetic and NOT
/// proof of ownership — that is Ö4's approval — and no bank datum is derived from it. A currency
/// change and closing are still not decided and not built.</item>
/// <item><b>Money eligibility (B3, Sinan #66 5750700247):</b> the card DECLARES whether it may move
/// money, and <c>IsUsableForReceipt</c> derives from that declaration — approval alone no longer makes
/// an account usable. The declaration has THREE states because the third is the answer's point: a row
/// written before it exists is <c>Undeclared</c>, not <c>false</c>, and is refused on the money path
/// while being shown as "beyan tamamlanmamış". Completeness of bank name, type and IBAN does NOT stand
/// in for the declaration — "Banka adı+tür+IBAN'ın dolu olması bu açık beyanın yerine geçmez" — so
/// there is no field-completeness route to eligibility anywhere in this file. Declaring eligible brings
/// K2's IBAN rule with it: such a card without a valid IBAN is refused where it is written.</item>
/// <item><b>IBAN change (D4, #66 5749046401):</b> changing an approved account's IBAN re-enters Ö4.
/// The proposed value waits in <c>PendingIban*</c> BESIDE the row and never in <c>Iban</c> until a
/// different authorized actor approves it, so the approved value the money path reads does not move —
/// "eski onaylı değer korunur" and "değişen IBAN onaylanmadan yeni para işleminde kullanılamaz" both
/// hold with no second refusal site. The account is NOT frozen for money while a change is pending:
/// Sinan's sentence restricts the CHANGED value, not the account, and freezing it would be a
/// restriction nobody decided.</item>
/// <item><b>Lifecycle (Ö4, #66 5722637822):</b> a proposal is neither active nor usable; one approval
/// by an authorized user OTHER than the proposer makes it both. The database refuses an approval by
/// the proposer too (<c>CK_BankAccounts_Approval</c>).</item>
/// <item><b>Currency:</b> must be one of Settings' money currency candidates of the server-resolved
/// company, checked on propose and again on approve. Being listed proves existence only; money
/// validity is still the profile's, at draft save. An unavailable Settings source refuses, fail-closed.</item>
/// <item><b>Authority:</b> server company/actor must be the request's; the action is checked before
/// anything is read and again after every owner await, replay included.</item>
/// <item><b>Visibility (least privilege, coordinator's technical default on #325):</b> a pending
/// proposal is shown only to a caller who may propose or approve; a read-only caller sees approved
/// accounts only, in the lists and by id alike. Business may decide otherwise in a later slice.</item>
/// <item><b>Idempotency and concurrency (ADR-011):</b> the shared command record keyed by (company,
/// command, key hash) with the actor and the canonical payload hash; the same key, payload and actor
/// replays, another payload or actor conflicts (§2–§4). An authorized terminal refusal — the account
/// is not the company's, or the expected version is stale — is recorded and replayed too (§7). The
/// loser of a same-key race answers the winner's record (§8).
/// Approve needs the expected version. One save covers the account, the command and the audit event.</item>
/// <item><b>Audit (ADR-009):</b> each write is audited with the roles that granted its own permission
/// (<see cref="FinanceBankAccountEnvelopes"/>).</item>
/// </list>
/// </summary>
public sealed class FinanceBankAccountService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    ISettingsMoneyCandidateReader currencies,
    IAuditEventCollector auditEvents,
    FinanceBankAccountEnvelopes envelopes,
    TimeProvider timeProvider) : IFinanceBankAccountMaster, IFinanceBankAccountReader
{
    public const string ProposeCommand = "finance.bank-account.propose";
    public const string ApproveCommand = "finance.bank-account.approve";
    // D4: the IBAN change is its own command name, so its idempotency records and replays never
    // collide with the card's own propose/approve under the same key.
    public const string ProposeIbanChangeCommand = "finance.bank-account.iban-change.propose";
    public const string ApproveIbanChangeCommand = "finance.bank-account.iban-change.approve";
    private const string EntityType = "Finance.BankAccount";
    private const int Proposed = 1;
    private const int Approved = 2;
    private const int IdentityMaximumLength = 128;
    private const int DisplayNameMaximumLength = 200;

    /// <summary>K2's shorter card fields: account type, branch code and account number (column widths).</summary>
    private const int AccountTypeMaximumLength = 64;
    // D4's reason, bounded like the existing approval reason rather than by a new limit class.
    private const int ReasonMaximumLength = 500;

    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<BankAccountCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        BankAccountProposeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (await StartAsync(context, metadata, FinanceAction.ProposeBankAccount, null, cancellationToken) is { } refused)
        {
            return refused;
        }

        // A null text cannot be the contract's JSON; refused before it is hashed.
        if (request.DisplayName is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("displayName", "display_name_invalid"));
        }

        // K2's card, judged here for the same reason: its mandatory fields are non-nullable in the
        // contract, so a null one cannot be serialised and the hash below would throw rather than
        // refuse. Nothing is read or recorded for a card that is not a card.
        if (CardRefusal(request) is { } cardRefused)
        {
            return Refused(context, CommandOutcome.ValidationFailed, cardRefused);
        }

        var payloadHash = PayloadHash(ProposeCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ProposeBankAccount, ProposeCommand, payloadHash, cancellationToken)
            is { } prior)
        {
            return prior;
        }

        if (!IsCanonical(request.DisplayName))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("displayName", "display_name_invalid"));
        }

        if (request.DisplayName.Length > DisplayNameMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("displayName", "finance_value_does_not_fit"));
        }

        if (request.CurrencyId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("currencyId", "identity_required"));
        }

        if (await CurrencyRefusalAsync(context, FinanceAction.ProposeBankAccount, null, request.CurrencyId, cancellationToken)
            is { } currencyRefused)
        {
            return currencyRefused;
        }

        var now = timeProvider.GetUtcNow();
        var row = new BankAccountRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            CurrencyId = request.CurrencyId,
            DisplayName = request.DisplayName,
            BankName = request.BankName,
            AccountType = request.AccountType,
            BranchName = Held(request.BranchName),
            BranchCode = Held(request.BranchCode),
            // Stored NORMALISED, so the value kept is the value validated — see CardRefusal.
            Iban = request.Iban is null ? null : FinanceIban.Normalize(request.Iban),
            AccountNumber = Held(request.AccountNumber),
            // B3: a card written from now on always states this, so it is never NULL here. NULL is
            // reserved for the rows that predate the declaration, and this path cannot produce one.
            MoneyMovementEligible = request.MoneyMovementEligible,
            Status = Proposed,
            Version = 1,
            ProposedBy = context.ActorId,
            ProposedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.BankAccounts.Add(row);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, ProposeCommand, payloadHash, row.Id, result, now);
        Audit(context, metadata, envelopes.Propose, row.Id, "finance.bank-account.proposed.v1", "Propose", versionBefore: null,
            row.Version, "{}", AccountAudit(row), approvalContextJson: null, "Finans banka hesabı önerisi (para etkisi yok)");
        return await SaveAsync(context, metadata, FinanceAction.ProposeBankAccount, ProposeCommand, payloadHash, result,
            cancellationToken);
    }

    public async Task<BankAccountCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        BankAccountApproveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (await StartAsync(context, metadata, FinanceAction.ApproveBankAccount, request.AccountId, cancellationToken)
            is { } refused)
        {
            return refused;
        }

        if (request.ExpectedVersion is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
        }

        // Malformed identities are refused before anything is read (never stored, so never a replay).
        if (request.AccountId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("accountId", "identity_required"));
        }

        if (!IsCanonical(request.ExpectedVersion))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
        }

        var payloadHash = PayloadHash(ApproveCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ApproveBankAccount, ApproveCommand, payloadHash, cancellationToken)
            is { } prior)
        {
            return prior;
        }

        var row = await dbContext.BankAccounts.SingleOrDefaultAsync(candidate => candidate.Id == request.AccountId, cancellationToken);
        if (row is null)
        {
            // Another company's account is not visible either: the same answer, nothing leaks. Terminal
            // (ADR-011 §7): the key keeps this answer, with no target to point at.
            return await TerminalAsync(context, metadata, FinanceAction.ApproveBankAccount, ApproveCommand, payloadHash,
                accountId: null, Refused(context, CommandOutcome.ValidationFailed, Issue("accountId", "bank_account_not_found")),
                cancellationToken);
        }

        if (!string.Equals(VersionText(row.Version), request.ExpectedVersion, StringComparison.Ordinal))
        {
            // Terminal (§7): a retry of this key answers the same conflict even after the account moves on.
            return await TerminalAsync(context, metadata, FinanceAction.ApproveBankAccount, ApproveCommand, payloadHash,
                row.Id, Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                cancellationToken);
        }

        if (row.Status != Proposed)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("accountId", "bank_account_not_proposed"));
        }

        // Ö4: a single authorized person other than the one who proposed it.
        if (string.Equals(row.ProposedBy, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.Denied, Issue("approval", "finance_approver_is_proposer"));
        }

        if (await CurrencyRefusalAsync(context, FinanceAction.ApproveBankAccount, row.Id, row.CurrencyId, cancellationToken)
            is { } currencyRefused)
        {
            return currencyRefused;
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = row.Version;
        var before = AccountAudit(row);
        row.Status = Approved;
        row.ApprovedBy = context.ActorId;
        row.ApprovedAtUtc = now;
        row.UpdatedAtUtc = now;
        row.Version = checked(row.Version + 1);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, ApproveCommand, payloadHash, row.Id, result, now);
        var approval = JsonSerializer.Serialize(new { rule = "O4", proposedBy = row.ProposedBy, approvedBy = context.ActorId },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Audit(context, metadata, envelopes.Approve, row.Id, "finance.bank-account.approved.v1", "Approve", versionBefore,
            row.Version, before, AccountAudit(row), approval,
            "Finans banka hesabı onayı (hazırlayandan farklı tek onay; para etkisi yok)");
        return await SaveAsync(context, metadata, FinanceAction.ApproveBankAccount, ApproveCommand, payloadHash, result,
            cancellationToken);
    }

    /// <summary>
    /// D4 (Sinan #66 5749046401): propose a new IBAN for an APPROVED account. The value is written
    /// BESIDE the row, never into <see cref="BankAccountRow.Iban"/>, so the approved value the money
    /// path reads does not move — that is how "eski onaylı değer korunur" and "değişen IBAN
    /// onaylanmadan yeni para işleminde kullanılamaz" both hold without a second refusal site.
    /// <para>
    /// <b>The account is NOT frozen for money while this waits.</b> Sinan's sentence restricts the
    /// CHANGED value, not the account, and the old approved value is stated to be preserved. Freezing
    /// would be a restriction nobody decided — and inventing a restriction is as much fabrication as
    /// inventing a permission, with the extra hazard that withholding reads as prudence.
    /// </para>
    /// <para>
    /// Replacing a change that is already waiting is allowed and is not a second transition: the
    /// pending slot holds one proposal, the newest one, and ADR-011 still makes the same key replay.
    /// </para>
    /// </summary>
    public async Task<BankAccountCommandResult> ProposeIbanChangeAsync(FinanceContext context, CommandMetadata metadata,
        BankAccountIbanChangeProposeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (await StartAsync(context, metadata, FinanceAction.ProposeBankAccountIbanChange, request.AccountId, cancellationToken)
            is { } refused)
        {
            return refused;
        }

        if (request.AccountId == Guid.Empty || !IsCanonical(request.ExpectedVersion))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
        }

        if (!IsCanonical(request.Reason))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("reason", "reason_required"));
        }

        if (request.Reason.Length > ReasonMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("reason", "finance_value_does_not_fit"));
        }

        var proposedIban = FinanceIban.Normalize(request.Iban ?? string.Empty);
        if (!FinanceIban.IsStructurallyValid(proposedIban))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("iban", "iban_invalid"));
        }

        var payloadHash = PayloadHash(ProposeIbanChangeCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ProposeBankAccountIbanChange, ProposeIbanChangeCommand,
            payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        var row = await dbContext.BankAccounts.SingleOrDefaultAsync(candidate => candidate.Id == request.AccountId, cancellationToken);
        if (row is null)
        {
            return await TerminalAsync(context, metadata, FinanceAction.ProposeBankAccountIbanChange, ProposeIbanChangeCommand,
                payloadHash, accountId: null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("accountId", "bank_account_not_found")),
                cancellationToken);
        }

        if (!string.Equals(VersionText(row.Version), request.ExpectedVersion, StringComparison.Ordinal))
        {
            return await TerminalAsync(context, metadata, FinanceAction.ProposeBankAccountIbanChange, ProposeIbanChangeCommand,
                payloadHash, row.Id, Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                cancellationToken);
        }

        // An unapproved card has no approved value to preserve: its IBAN is still part of the proposal
        // its own Ö4 covers, so changing it here would be a second process over the same decision.
        if (row.Status != Approved)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("accountId", "bank_account_not_approved"));
        }

        // A change to the value already in force is not a change; recording one would put an approval
        // in front of a human for a difference that does not exist.
        if (string.Equals(row.Iban, proposedIban, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("iban", "iban_unchanged"));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = row.Version;
        var before = AccountAudit(row);
        row.PendingIban = proposedIban;
        row.PendingIbanProposedBy = context.ActorId;
        row.PendingIbanProposedAtUtc = now;
        row.PendingIbanReason = request.Reason;
        row.UpdatedAtUtc = now;
        row.Version = checked(row.Version + 1);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, ProposeIbanChangeCommand, payloadHash, row.Id, result, now);
        Audit(context, metadata, envelopes.Propose, row.Id, "finance.bank-account.iban-change-proposed.v1",
            "ProposeIbanChange", versionBefore, row.Version, before, AccountAudit(row), approvalContextJson: null,
            "Finans banka hesabı IBAN değişikliği önerisi (onaya kadar para yolunda kullanılmaz)");
        return await SaveAsync(context, metadata, FinanceAction.ProposeBankAccountIbanChange, ProposeIbanChangeCommand, payloadHash,
            result, cancellationToken);
    }

    /// <summary>
    /// D4: approve the waiting IBAN, by a single authorized actor OTHER than the one who proposed the
    /// change (Ö4). Only here does the new value enter <see cref="BankAccountRow.Iban"/>, and the audit
    /// carries the old value, the new one, the reason and both actors — Sinan's "audit'te eski/yeni,
    /// sebep ve aktör görünür".
    /// <para>
    /// Ö4 compares against the CHANGE's proposer, not the card's original proposer: the decision being
    /// double-checked is this change. Someone who proposed the card years ago and did not propose this
    /// change is a legitimate approver of it.
    /// </para>
    /// </summary>
    public async Task<BankAccountCommandResult> ApproveIbanChangeAsync(FinanceContext context, CommandMetadata metadata,
        BankAccountIbanChangeApproveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (await StartAsync(context, metadata, FinanceAction.ApproveBankAccountIbanChange, request.AccountId, cancellationToken)
            is { } refused)
        {
            return refused;
        }

        if (request.AccountId == Guid.Empty || !IsCanonical(request.ExpectedVersion))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
        }

        var payloadHash = PayloadHash(ApproveIbanChangeCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ApproveBankAccountIbanChange, ApproveIbanChangeCommand,
            payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        var row = await dbContext.BankAccounts.SingleOrDefaultAsync(candidate => candidate.Id == request.AccountId, cancellationToken);
        if (row is null)
        {
            return await TerminalAsync(context, metadata, FinanceAction.ApproveBankAccountIbanChange, ApproveIbanChangeCommand,
                payloadHash, accountId: null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("accountId", "bank_account_not_found")),
                cancellationToken);
        }

        if (!string.Equals(VersionText(row.Version), request.ExpectedVersion, StringComparison.Ordinal))
        {
            return await TerminalAsync(context, metadata, FinanceAction.ApproveBankAccountIbanChange, ApproveIbanChangeCommand,
                payloadHash, row.Id, Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                cancellationToken);
        }

        if (row.PendingIban is not { } pendingIban || row.PendingIbanProposedBy is not { } pendingBy
            || row.PendingIbanReason is not { } pendingReason)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("accountId", "iban_change_not_proposed"));
        }

        if (string.Equals(pendingBy, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.Denied, Issue("approval", "finance_approver_is_proposer"));
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = row.Version;
        var before = AccountAudit(row);
        var previousIban = row.Iban;
        row.Iban = pendingIban;
        row.PendingIban = null;
        row.PendingIbanProposedBy = null;
        row.PendingIbanProposedAtUtc = null;
        row.PendingIbanReason = null;
        row.UpdatedAtUtc = now;
        row.Version = checked(row.Version + 1);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, ApproveIbanChangeCommand, payloadHash, row.Id, result, now);
        var approval = JsonSerializer.Serialize(
            new { rule = "O4", proposedBy = pendingBy, approvedBy = context.ActorId, previousIban, newIban = pendingIban, reason = pendingReason },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Audit(context, metadata, envelopes.Approve, row.Id, "finance.bank-account.iban-change-approved.v1",
            "ApproveIbanChange", versionBefore, row.Version, before, AccountAudit(row), approval,
            "Finans banka hesabı IBAN değişikliği onayı (öneren≠onaylayan; eski değer audit'te korunur)");
        return await SaveAsync(context, metadata, FinanceAction.ApproveBankAccountIbanChange, ApproveIbanChangeCommand, payloadHash,
            result, cancellationToken);
    }

    /// <summary>
    /// F-04's list: every account of the company, proposals included, each with whether the caller
    /// proposed it. Read authority as for the picker; the order is the picker's.
    /// </summary>
    public async Task<ReadResult<BankAccountMasterPage>> ListMasterAsync(FinanceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadFenceAsync(context, null, cancellationToken) is { } outcome)
        {
            return new ReadResult<BankAccountMasterPage>(outcome, null);
        }

        var rows = await VisibleAsync(context, cancellationToken);
        if (await ReadFenceAsync(context, null, cancellationToken) is { } again)
        {
            return new ReadResult<BankAccountMasterPage>(again, null);
        }

        return new ReadResult<BankAccountMasterPage>(ReadOutcome.Found, new BankAccountMasterPage(context.CompanyId,
            [.. rows.Select(row => new BankAccountMasterItem(View(row),
                string.Equals(row.ProposedBy, context.ActorId, StringComparison.Ordinal),
                Card(row),
                // D4, mirroring IsOwnProposal: a screen may offer the pending change's approval to
                // someone else. The server refuses the proposer's own approval regardless.
                row.PendingIbanProposedBy is { } pendingBy
                && string.Equals(pendingBy, context.ActorId, StringComparison.Ordinal)))]));
    }

    /// <summary>
    /// The currencies a proposal may name: Settings' candidates under the PROPOSE authority, passed
    /// through as given (shape-checked, never filtered, reordered or defaulted). A source with no
    /// candidates is an empty list; an unreachable or malformed one is unavailable, a forbidden one denied.
    /// </summary>
    public async Task<ReadResult<BankAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await FenceAsync(context, FinanceAction.ProposeBankAccount, null, cancellationToken) is { } refused)
        {
            return new ReadResult<BankAccountCurrencyCandidates>(ReadOutcomeOf(refused.Outcome), null);
        }

        IReadOnlyList<MoneyCurrencyCandidateV1> candidates;
        try
        {
            candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(
                await currencies.GetMoneyCurrencyCandidatesAsync(cancellationToken));
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceAsync(context, FinanceAction.ProposeBankAccount, null, cancellationToken) is { } fenced)
            {
                return new ReadResult<BankAccountCurrencyCandidates>(ReadOutcomeOf(fenced.Outcome), null);
            }

            return new ReadResult<BankAccountCurrencyCandidates>(
                exception.Code == "settings_access_denied" ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }

        if (await FenceAsync(context, FinanceAction.ProposeBankAccount, null, cancellationToken) is { } afterSettings)
        {
            return new ReadResult<BankAccountCurrencyCandidates>(ReadOutcomeOf(afterSettings.Outcome), null);
        }

        return new ReadResult<BankAccountCurrencyCandidates>(ReadOutcome.Found, new BankAccountCurrencyCandidates(
            context.CompanyId,
            [.. candidates.Select(candidate => new BankAccountCurrencyCandidate(candidate.CurrencyId, candidate.Code,
                candidate.DisplayLabel))]));
    }

    public async Task<ReadResult<BankAccountPage>> ListAuthorizedAsync(FinanceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadFenceAsync(context, null, cancellationToken) is { } outcome)
        {
            return new ReadResult<BankAccountPage>(outcome, null);
        }

        var rows = await VisibleAsync(context, cancellationToken);
        if (await ReadFenceAsync(context, null, cancellationToken) is { } again)
        {
            return new ReadResult<BankAccountPage>(again, null);
        }

        return new ReadResult<BankAccountPage>(ReadOutcome.Found,
            new BankAccountPage(context.CompanyId, [.. rows.Select(View)]));
    }

    public async Task<ReadResult<BankAccountView>> GetAuthorizedAsync(FinanceContext context, Guid accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadFenceAsync(context, accountId, cancellationToken) is { } outcome)
        {
            return new ReadResult<BankAccountView>(outcome, null);
        }

        var row = await dbContext.BankAccounts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == accountId, cancellationToken);
        if (row is { Status: Proposed } && !await SeesProposalsAsync(context, cancellationToken))
        {
            // A pending proposal is not disclosed to a read-only caller: the same answer as no account.
            row = null;
        }

        if (await ReadFenceAsync(context, accountId, cancellationToken) is { } again)
        {
            return new ReadResult<BankAccountView>(again, null);
        }

        return row is null
            ? new ReadResult<BankAccountView>(ReadOutcome.NotFound, null)
            : new ReadResult<BankAccountView>(ReadOutcome.Found, View(row));
    }

    /// <summary>The canonical content of a command: its name and the exact contract JSON of the request.</summary>
    internal static string PayloadHash<T>(string command, T request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command + "|" + JsonSerializer.Serialize(request, Json))))
            .ToLowerInvariant();

    internal static string VersionText(long version) => version.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Approved is the only ACTIVE state; a proposal is never selectable. Usable for money is narrower
    /// still, and B3 is what narrows it: approval says a human accepted the card, the DECLARATION says
    /// the card is for money movement, and both are required. This is the ONE site that decides
    /// usability — the money paths (<c>ManualReceiptV1</c>'s Record and <c>DraftReadContext</c>) already
    /// refuse on <c>!IsUsableForReceipt</c>, so narrowing here reaches them without a new refusal point.
    /// <para>
    /// <b>Note what is NOT in this expression:</b> no bank name, no account type, no IBAN. Eligibility
    /// comes from the declaration alone. Reading it from field completeness would be the option Sinan
    /// rejected — "Banka adı+tür+IBAN'ın dolu olması bu açık beyanın yerine geçmez" — arriving by the
    /// back door, and a card whose declaration is NULL stays unusable however complete it looks.
    /// </para>
    /// </summary>
    private static BankAccountView View(BankAccountRow row) =>
        new BankAccountView(row.Id, row.CompanyId, row.CurrencyId, row.DisplayName,
            IsActive: row.Status == Approved,
            IsUsableForReceipt: row.Status == Approved && row.MoneyMovementEligible == true,
            VersionText(row.Version));

    /// <summary>
    /// K2's card as stored. A row written before K2 carries NULL where K2 calls a value mandatory; that
    /// is shown as it is, never as a blank or a placeholder — B3 decided what those rows MEAN for the
    /// money path, and it deliberately did not invent values for their fields.
    /// </summary>
    private static BankAccountCard Card(BankAccountRow row) =>
        new BankAccountCard(row.BankName ?? string.Empty, row.AccountType ?? string.Empty,
            row.BranchName, row.BranchCode, row.Iban, row.AccountNumber, Eligibility(row), PendingIbanChange(row));

    /// <summary>
    /// B3's three states. NULL is <see cref="BankAccountMoneyEligibility.Undeclared"/> and never
    /// <see cref="BankAccountMoneyEligibility.NotEligible"/>: the first is a card nobody has answered
    /// for, the second is a card someone answered "no" for, and Sinan asks for the first to be shown
    /// specifically. Both are refused on the money path; only the reason differs, and the user is told
    /// which one applies.
    /// </summary>
    private static BankAccountMoneyEligibility Eligibility(BankAccountRow row) => row.MoneyMovementEligible switch
    {
        true => BankAccountMoneyEligibility.Eligible,
        false => BankAccountMoneyEligibility.NotEligible,
        null => BankAccountMoneyEligibility.Undeclared,
    };

    /// <summary>D4: the waiting change, or null when there is none. The four members move together.</summary>
    private static BankAccountPendingIbanChange? PendingIbanChange(BankAccountRow row) =>
        row.PendingIban is null || row.PendingIbanProposedBy is null || row.PendingIbanProposedAtUtc is null
        || row.PendingIbanReason is null
            ? null
            : new BankAccountPendingIbanChange(row.PendingIban, row.PendingIbanProposedBy,
                row.PendingIbanProposedAtUtc.Value, row.PendingIbanReason);

    /// <summary>A holdable field: absent and blank are the same thing, and neither is invented.</summary>
    private static string? Held(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// K2 (Sinan #66 5748258299) on a card being WRITTEN: bank name and account type are mandatory;
    /// branch name/code, IBAN and account number are holdable. An IBAN that IS given must be
    /// structurally valid — and a valid one is still not proof of ownership (Ö4's approval is), nor is
    /// any bank datum derived from it.
    /// <para>
    /// The account TYPE is stored as the owner's text and no list of types is enforced here: which
    /// types exist is a business list nobody has decided, and inventing one would close a question K2
    /// left open. What K2 does fix is that the field cannot be empty.
    /// </para>
    /// </summary>
    private static ValidationIssue? CardRefusal(BankAccountProposeRequest request)
    {
        if (!IsCanonical(request.BankName))
        {
            return Issue("bankName", "bank_name_required");
        }

        if (request.BankName.Length > DisplayNameMaximumLength)
        {
            return Issue("bankName", "finance_value_does_not_fit");
        }

        if (!IsCanonical(request.AccountType))
        {
            return Issue("accountType", "account_type_required");
        }

        if (request.AccountType.Length > AccountTypeMaximumLength)
        {
            return Issue("accountType", "finance_value_does_not_fit");
        }

        if (Held(request.BranchName) is { Length: > DisplayNameMaximumLength })
        {
            return Issue("branchName", "finance_value_does_not_fit");
        }

        if (Held(request.BranchCode) is { Length: > AccountTypeMaximumLength })
        {
            return Issue("branchCode", "finance_value_does_not_fit");
        }

        if (Held(request.AccountNumber) is { Length: > AccountTypeMaximumLength })
        {
            return Issue("accountNumber", "finance_value_does_not_fit");
        }

        // Absent is allowed ONLY for a card that does not declare itself money-eligible: K2 requires an
        // IBAN-less special credit or term account to be definable, and equally requires that "ödeme/
        // tahsilatta kullanılacak hesapta IBAN zorunlu". B3 is what lets both be true at once — the
        // declaration says which kind of card this is, so the rule can attach to the declaration instead
        // of guessing from the data.
        if (request.Iban is null)
        {
            return request.MoneyMovementEligible ? Issue("iban", "iban_required_for_money_movement") : null;
        }

        var normalized = FinanceIban.Normalize(request.Iban);
        return FinanceIban.IsStructurallyValid(normalized) ? null : Issue("iban", "iban_invalid");
    }

    /// <summary>Context, the action's fresh authority, and the key and context storage limits.</summary>
    private async Task<BankAccountCommandResult?> StartAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, Guid? accountId, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, action, accountId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var key = metadata.IdempotencyKey;
        if (!IsCanonical(key) || key.Length > IdentityMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("idempotencyKey", "idempotency_key_invalid"));
        }

        return context.ActorId.Length > IdentityMaximumLength || context.CorrelationId.Length > IdentityMaximumLength
            ? Refused(context, CommandOutcome.ValidationFailed, Issue("context", "finance_value_does_not_fit"))
            : null;
    }

    /// <summary>
    /// ADR-011: the same (command, key hash) again answers its stored result — a success or a recorded
    /// terminal refusal — as a replay with its first correlation; another payload or actor is a conflict
    /// that discloses nothing of the earlier result. The action is measured afresh before the replay.
    /// </summary>
    private async Task<BankAccountCommandResult?> PriorAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, CancellationToken cancellationToken)
    {
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var prior = await dbContext.Commands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        if (prior.PayloadHash != payloadHash || !string.Equals(prior.ActorId, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.IdempotencyConflict);
        }

        if (await FenceAsync(context, action, prior.BankAccountId, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        var stored = JsonSerializer.Deserialize<BankAccountCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı komut sonucu okunamadı.");
        return stored with { IsReplay = true };
    }

    /// <summary>The currency must be one of Settings' candidates now; an unavailable source refuses.</summary>
    private async Task<BankAccountCommandResult?> CurrencyRefusalAsync(FinanceContext context, FinanceAction action,
        Guid? accountId, Guid currencyId, CancellationToken cancellationToken)
    {
        IReadOnlyList<MoneyCurrencyCandidateV1> candidates;
        try
        {
            candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(
                await currencies.GetMoneyCurrencyCandidatesAsync(cancellationToken));
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceAsync(context, action, accountId, cancellationToken) is { } fenced)
            {
                return fenced;
            }

            return exception.Code == "settings_access_denied"
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable, Issue("currencyId", exception.Code));
        }

        if (await FenceAsync(context, action, accountId, cancellationToken) is { } afterSettings)
        {
            return afterSettings;
        }

        return candidates.Any(candidate => candidate.CurrencyId == currencyId)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("currencyId", "currency_not_a_candidate"));
    }

    /// <summary>Company/actor still the request's, then a fresh decision on the action.</summary>
    private async Task<BankAccountCommandResult?> FenceAsync(FinanceContext context, FinanceAction action, Guid? accountId,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }

        var decision = await permissions.CheckAsync(context, action, accountId, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable, Issue("permission", "finance_permission_mapping_unavailable"));
        }

        return SameContext(context) ? null : Refused(context, CommandOutcome.Denied);
    }

    /// <summary>The company's accounts the caller may see, in the picker's order: proposals only for a proposer or approver.</summary>
    private async Task<List<BankAccountRow>> VisibleAsync(FinanceContext context, CancellationToken cancellationToken)
    {
        var query = dbContext.BankAccounts.AsNoTracking();
        if (!await SeesProposalsAsync(context, cancellationToken))
        {
            query = query.Where(row => row.Status == Approved);
        }

        return await query.OrderBy(row => row.DisplayName).ThenBy(row => row.Id).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Whether pending proposals are the caller's business: a fresh grant to propose or to approve.
    /// Anything else (denied, unmapped) is no — least privilege, never a leak.
    /// </summary>
    private async Task<bool> SeesProposalsAsync(FinanceContext context, CancellationToken cancellationToken) =>
        await permissions.CheckAsync(context, FinanceAction.ProposeBankAccount, null, cancellationToken) == PermissionDecision.Allowed
        || await permissions.CheckAsync(context, FinanceAction.ApproveBankAccount, null, cancellationToken) == PermissionDecision.Allowed;

    private async Task<ReadOutcome?> ReadFenceAsync(FinanceContext context, Guid? accountId, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return ReadOutcome.Denied;
        }

        var decision = await permissions.CheckAsync(context, FinanceAction.ReadBankAccounts, accountId, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable;
        }

        return SameContext(context) ? null : ReadOutcome.Denied;
    }

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && string.Equals(currentUser.UserId, context.ActorId, StringComparison.Ordinal)
        && companyContext.IsResolved && companyContext.CompanyId != Guid.Empty
        && companyContext.CompanyId == context.CompanyId && currentUser.CanAccessCompany(context.CompanyId);

    private void AddCommand(FinanceContext context, CommandMetadata metadata, string command, string payloadHash,
        Guid? accountId, BankAccountCommandResult result, DateTimeOffset now) =>
        dbContext.Commands.Add(new FinanceCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            BankAccountId = accountId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    /// <summary>
    /// One save. If it loses a race (a unique key or the row version), nothing of it stays tracked and
    /// the key is read afresh: another caller of the same key that won is this answer too (ADR-011 §8,
    /// through <see cref="PriorAsync"/> and its fence); a race that left this key empty is a
    /// <c>concurrent_change</c> the caller may retry.
    /// </summary>
    private async Task<BankAccountCommandResult> SaveAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, BankAccountCommandResult result,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            return await PriorAsync(context, metadata, action, command, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }

        return result;
    }

    /// <summary>
    /// ADR-011 §7: an authorized terminal refusal is the key's result for good. It is recorded alone
    /// (no business change, no audit event) and answered; a retry of the key replays it.
    /// </summary>
    private Task<BankAccountCommandResult> TerminalAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, Guid? accountId, BankAccountCommandResult refusal,
        CancellationToken cancellationToken)
    {
        AddCommand(context, metadata, command, payloadHash, accountId, refusal, timeProvider.GetUtcNow());
        return SaveAsync(context, metadata, action, command, payloadHash, refusal, cancellationToken);
    }

    private static ReadOutcome ReadOutcomeOf(CommandOutcome outcome) =>
        outcome == CommandOutcome.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable;

    private void Audit(FinanceContext context, CommandMetadata metadata, FinanceAuditEnvelope envelope, Guid accountId,
        string eventType, string operation, long? versionBefore, long? versionAfter, string before, string after,
        string? approvalContextJson, string reason)
    {
        auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId,
            eventType,
            EntityType,
            accountId.ToString("D"),
            operation,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            context.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            before,
            after,
            reason,
            approvalContextJson,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            context.CorrelationId,
            context.CausationId,
            metadata.IdempotencyKey));
    }

    private static string AccountAudit(BankAccountRow row) => JsonSerializer.Serialize(new
    {
        row.Id,
        version = VersionText(row.Version),
        status = row.Status == Approved ? "Approved" : "Proposed",
        row.CurrencyId,
        row.DisplayName,
        row.ProposedBy,
        row.ApprovedBy,
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static BankAccountCommandResult Succeeded(FinanceContext context, BankAccountRow row) =>
        new BankAccountCommandResult(CommandOutcome.Succeeded, View(row), IsReplay: false, context.CorrelationId, []);

    private static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static BankAccountCommandResult Refused(FinanceContext context, CommandOutcome outcome, params ValidationIssue[] issues) =>
        new BankAccountCommandResult(outcome, null, IsReplay: false, context.CorrelationId, issues);
}

/// <summary>
/// The ADR-009 envelopes of the bank-account master's writes, one per command: a proposal is audited
/// with the roles that granted <c>finance.bank-account.propose</c>, an approval with those of
/// <c>finance.bank-account.approve</c>.
/// </summary>
public sealed record FinanceBankAccountEnvelopes(FinanceAuditEnvelope Propose, FinanceAuditEnvelope Approve);
