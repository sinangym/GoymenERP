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
/// API-30: the Finance cash account (kasa) master. A definition only — no balance, no opening balance,
/// no money movement. It follows the bank-account master (<see cref="FinanceBankAccountService"/>):
/// <list type="bullet">
/// <item><b>Lifecycle:</b> a proposal (version 1) is neither active nor selectable; one decision by an
/// authorized actor OTHER than the proposer approves (active) or rejects it (with a reason). The
/// database refuses a decision by the proposer too (<c>CK_CashAccounts_Decision</c>, binary collation).</item>
/// <item><b>Currency (FNS-D24):</b> one of Settings' money currency candidates of the server-resolved
/// company, checked on propose and again on approve, exactly as the bank master does. Nothing is
/// hard-coded; an unavailable Settings source refuses, fail-closed.</item>
/// <item><b>Name:</b> a proposed or approved name is unique per company (<c>UX_CashAccounts_LiveName</c>);
/// a rejected proposal frees it.</item>
/// <item><b>Authority:</b> server company/actor must be the request's; the action is checked before
/// anything is read, after every owner await, on replay and immediately before the save.</item>
/// <item><b>Visibility:</b> proposals and rejections are shown only to a caller who may propose or
/// approve; a read-only caller sees approved accounts, in the list and by id alike.</item>
/// <item><b>Idempotency (ADR-011):</b> the cash account's own command record keyed by (company, command,
/// key hash) with the actor and the canonical payload hash; terminal refusals (not found, stale version)
/// are recorded and replayed; the loser of a same-key race answers the winner's record.</item>
/// <item><b>Audit (ADR-009):</b> each write is audited with the roles that granted its own permission.</item>
/// </list>
/// </summary>
public sealed class FinanceCashAccountService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    ISettingsMoneyCandidateReader currencies,
    IAuditEventCollector auditEvents,
    FinanceCashAccountEnvelopes envelopes,
    TimeProvider timeProvider) : IFinanceCashAccountMaster
{
    public const string ProposeCommand = "finance.cash-account.propose";
    public const string ApproveCommand = "finance.cash-account.approve";
    public const string RejectCommand = "finance.cash-account.reject";
    internal const string LiveNameIndex = "UX_CashAccounts_LiveName";
    private const string EntityType = "Finance.CashAccount";
    private const int Proposed = 1;
    private const int Approved = 2;
    private const int Rejected = 3;
    private const int IdentityMaximumLength = 128;
    private const int DisplayNameMaximumLength = 200;
    private const int ReasonMaximumLength = 500;

    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<CashAccountCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        CashAccountProposeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (await StartAsync(context, metadata, FinanceAction.ProposeCashAccount, null, cancellationToken) is { } refused)
        {
            return refused;
        }

        // A null text cannot be the contract's JSON; refused before it is hashed.
        if (request.DisplayName is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("displayName", "display_name_invalid"));
        }

        var payloadHash = PayloadHash(ProposeCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ProposeCashAccount, ProposeCommand, payloadHash, cancellationToken)
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

        if (await CurrencyRefusalAsync(context, FinanceAction.ProposeCashAccount, null, request.CurrencyId, cancellationToken)
            is { } currencyRefused)
        {
            return currencyRefused;
        }

        // The database's unique index is the authority; this answers the common case before a write.
        if (await dbContext.CashAccounts.AsNoTracking().AnyAsync(row => row.DisplayName == request.DisplayName
            && (row.Status == Proposed || row.Status == Approved), cancellationToken))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("displayName", "cash_account_name_taken"));
        }

        var now = timeProvider.GetUtcNow();
        var row = new CashAccountRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            CurrencyId = request.CurrencyId,
            DisplayName = request.DisplayName,
            Status = Proposed,
            Version = 1,
            ProposedBy = context.ActorId,
            ProposedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.CashAccounts.Add(row);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, ProposeCommand, payloadHash, row.Id, result, now);
        Audit(context, metadata, envelopes.Propose, row.Id, "finance.cash-account.proposed.v1", "Propose", versionBefore: null,
            row.Version, "{}", AccountAudit(row), approvalContextJson: null, "Finans kasa hesabı önerisi (para etkisi yok)");
        return await SaveAsync(context, metadata, FinanceAction.ProposeCashAccount, ProposeCommand, payloadHash, result,
            cancellationToken);
    }

    public async Task<CashAccountCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        CashAccountApproveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (await StartAsync(context, metadata, FinanceAction.ApproveCashAccount, request.AccountId, cancellationToken)
            is { } refused)
        {
            return refused;
        }

        if (IdentityRefusal(context, request.AccountId, request.ExpectedVersion) is { } malformed)
        {
            return malformed;
        }

        var payloadHash = PayloadHash(ApproveCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ApproveCashAccount, ApproveCommand, payloadHash, cancellationToken)
            is { } prior)
        {
            return prior;
        }

        var (row, terminal) = await DecisionTargetAsync(context, metadata, FinanceAction.ApproveCashAccount, ApproveCommand,
            payloadHash, request.AccountId, request.ExpectedVersion, cancellationToken);
        if (terminal is not null)
        {
            return terminal;
        }

        if (await CurrencyRefusalAsync(context, FinanceAction.ApproveCashAccount, row!.Id, row.CurrencyId, cancellationToken)
            is { } currencyRefused)
        {
            return currencyRefused;
        }

        return await DecideAsync(context, metadata, FinanceAction.ApproveCashAccount, ApproveCommand, payloadHash, row,
            Approved, reason: null, envelopes.Approve, "finance.cash-account.approved.v1", "Approve",
            "Finans kasa hesabı onayı (hazırlayandan farklı tek onay; para etkisi yok)", cancellationToken);
    }

    /// <summary>
    /// Rejects a proposal: the same decider rule as approval (not the proposer), a reason, and the
    /// approve authority — a rejection is the other answer of the same decision.
    /// </summary>
    public async Task<CashAccountCommandResult> RejectAsync(FinanceContext context, CommandMetadata metadata,
        CashAccountRejectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (await StartAsync(context, metadata, FinanceAction.ApproveCashAccount, request.AccountId, cancellationToken)
            is { } refused)
        {
            return refused;
        }

        if (IdentityRefusal(context, request.AccountId, request.ExpectedVersion) is { } malformed)
        {
            return malformed;
        }

        if (!IsCanonical(request.Reason))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("reason", "reason_invalid"));
        }

        if (request.Reason.Length > ReasonMaximumLength)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("reason", "finance_value_does_not_fit"));
        }

        var payloadHash = PayloadHash(RejectCommand, request);
        if (await PriorAsync(context, metadata, FinanceAction.ApproveCashAccount, RejectCommand, payloadHash, cancellationToken)
            is { } prior)
        {
            return prior;
        }

        var (row, terminal) = await DecisionTargetAsync(context, metadata, FinanceAction.ApproveCashAccount, RejectCommand,
            payloadHash, request.AccountId, request.ExpectedVersion, cancellationToken);
        if (terminal is not null)
        {
            return terminal;
        }

        return await DecideAsync(context, metadata, FinanceAction.ApproveCashAccount, RejectCommand, payloadHash, row!,
            Rejected, request.Reason, envelopes.Approve, "finance.cash-account.rejected.v1", "Reject",
            "Finans kasa hesabı reddi (hazırlayandan farklı tek karar; para etkisi yok)", cancellationToken);
    }

    public async Task<ReadResult<CashAccountMasterPage>> ListAsync(FinanceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadFenceAsync(context, null, cancellationToken) is { } outcome)
        {
            return new ReadResult<CashAccountMasterPage>(outcome, null);
        }

        var query = dbContext.CashAccounts.AsNoTracking();
        if (!await SeesProposalsAsync(context, cancellationToken))
        {
            query = query.Where(row => row.Status == Approved);
        }

        var rows = await query.OrderBy(row => row.DisplayName).ThenBy(row => row.Id).ToListAsync(cancellationToken);
        if (await ReadFenceAsync(context, null, cancellationToken) is { } again)
        {
            return new ReadResult<CashAccountMasterPage>(again, null);
        }

        return new ReadResult<CashAccountMasterPage>(ReadOutcome.Found, new CashAccountMasterPage(context.CompanyId,
            [.. rows.Select(row => new CashAccountMasterItem(View(row),
                string.Equals(row.ProposedBy, context.ActorId, StringComparison.Ordinal)))]));
    }

    public async Task<ReadResult<CashAccountView>> GetAsync(FinanceContext context, Guid accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadFenceAsync(context, accountId, cancellationToken) is { } outcome)
        {
            return new ReadResult<CashAccountView>(outcome, null);
        }

        var row = await dbContext.CashAccounts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == accountId, cancellationToken);
        if (row is not null && row.Status != Approved && !await SeesProposalsAsync(context, cancellationToken))
        {
            // Not disclosed to a read-only caller: the same answer as no account.
            row = null;
        }

        if (await ReadFenceAsync(context, accountId, cancellationToken) is { } again)
        {
            return new ReadResult<CashAccountView>(again, null);
        }

        return row is null
            ? new ReadResult<CashAccountView>(ReadOutcome.NotFound, null)
            : new ReadResult<CashAccountView>(ReadOutcome.Found, View(row));
    }

    /// <summary>
    /// Settings' candidates under the PROPOSE authority, passed through as given (shape-checked, never
    /// filtered, reordered or defaulted).
    /// </summary>
    public async Task<ReadResult<CashAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await FenceAsync(context, FinanceAction.ProposeCashAccount, null, cancellationToken) is { } refused)
        {
            return new ReadResult<CashAccountCurrencyCandidates>(ReadOutcomeOf(refused.Outcome), null);
        }

        IReadOnlyList<MoneyCurrencyCandidateV1> candidates;
        try
        {
            candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(
                await currencies.GetMoneyCurrencyCandidatesAsync(cancellationToken));
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceAsync(context, FinanceAction.ProposeCashAccount, null, cancellationToken) is { } fenced)
            {
                return new ReadResult<CashAccountCurrencyCandidates>(ReadOutcomeOf(fenced.Outcome), null);
            }

            return new ReadResult<CashAccountCurrencyCandidates>(
                exception.Code == "settings_access_denied" ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }

        if (await FenceAsync(context, FinanceAction.ProposeCashAccount, null, cancellationToken) is { } afterSettings)
        {
            return new ReadResult<CashAccountCurrencyCandidates>(ReadOutcomeOf(afterSettings.Outcome), null);
        }

        return new ReadResult<CashAccountCurrencyCandidates>(ReadOutcome.Found, new CashAccountCurrencyCandidates(
            context.CompanyId,
            [.. candidates.Select(candidate => new CashAccountCurrencyCandidate(candidate.CurrencyId, candidate.Code,
                candidate.DisplayLabel))]));
    }

    /// <summary>The canonical content of a command: its name and the exact contract JSON of the request.</summary>
    internal static string PayloadHash<T>(string command, T request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command + "|" + JsonSerializer.Serialize(request, Json))))
            .ToLowerInvariant();

    internal static string VersionText(long version) => version.ToString(CultureInfo.InvariantCulture);

    private static CashAccountView View(CashAccountRow row) =>
        new CashAccountView(row.Id, row.CompanyId, row.CurrencyId, row.DisplayName, StatusOf(row.Status),
            IsActive: row.Status == Approved, row.RejectionReason, VersionText(row.Version));

    private static CashAccountStatus StatusOf(int status) => status switch
    {
        Proposed => CashAccountStatus.Proposed,
        Approved => CashAccountStatus.Approved,
        Rejected => CashAccountStatus.Rejected,
        _ => throw new InvalidOperationException("Kasa hesabı durumu tanınmıyor."),
    };

    private static CashAccountCommandResult? IdentityRefusal(FinanceContext context, Guid accountId, string? expectedVersion)
    {
        // Malformed identities are refused before anything is read (never stored, so never a replay).
        if (accountId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("accountId", "identity_required"));
        }

        return IsCanonical(expectedVersion)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
    }

    /// <summary>
    /// The proposal a decision names: not the company's (terminal), a stale version (terminal), not a
    /// proposal any more, or the proposer's own (maker≠checker).
    /// </summary>
    private async Task<(CashAccountRow? Row, CashAccountCommandResult? Refusal)> DecisionTargetAsync(FinanceContext context,
        CommandMetadata metadata, FinanceAction action, string command, string payloadHash, Guid accountId,
        string expectedVersion, CancellationToken cancellationToken)
    {
        var row = await dbContext.CashAccounts.SingleOrDefaultAsync(candidate => candidate.Id == accountId, cancellationToken);
        if (row is null)
        {
            // Another company's account is not visible either: the same answer, nothing leaks (§7).
            return (null, await TerminalAsync(context, metadata, action, command, payloadHash, accountId: null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("accountId", "cash_account_not_found")),
                cancellationToken));
        }

        if (!string.Equals(VersionText(row.Version), expectedVersion, StringComparison.Ordinal))
        {
            return (null, await TerminalAsync(context, metadata, action, command, payloadHash, row.Id,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                cancellationToken));
        }

        if (row.Status != Proposed)
        {
            return (null, Refused(context, CommandOutcome.ValidationFailed, Issue("accountId", "cash_account_not_proposed")));
        }

        // Maker≠checker: a single authorized person other than the one who proposed it.
        return string.Equals(row.ProposedBy, context.ActorId, StringComparison.Ordinal)
            ? (null, Refused(context, CommandOutcome.Denied, Issue("approval", "finance_approver_is_proposer")))
            : (row, null);
    }

    private async Task<CashAccountCommandResult> DecideAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, CashAccountRow row, int status, string? reason,
        FinanceAuditEnvelope envelope, string eventType, string operation, string auditReason,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var versionBefore = row.Version;
        var before = AccountAudit(row);
        row.Status = status;
        row.DecidedBy = context.ActorId;
        row.DecidedAtUtc = now;
        row.RejectionReason = reason;
        row.UpdatedAtUtc = now;
        row.Version = checked(row.Version + 1);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, command, payloadHash, row.Id, result, now);
        // The decider is named for what they did: a rejection is not recorded as an approval.
        object decision = status == Rejected
            ? new { rule = "O4", proposedBy = row.ProposedBy, rejectedBy = context.ActorId }
            : new { rule = "O4", proposedBy = row.ProposedBy, approvedBy = context.ActorId };
        var approval = JsonSerializer.Serialize(decision, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Audit(context, metadata, envelope, row.Id, eventType, operation, versionBefore, row.Version, before,
            AccountAudit(row), approval, reason is null ? auditReason : auditReason + ": " + reason);
        return await SaveAsync(context, metadata, action, command, payloadHash, result, cancellationToken);
    }

    /// <summary>Context, the action's fresh authority, and the key and context storage limits.</summary>
    private async Task<CashAccountCommandResult?> StartAsync(FinanceContext context, CommandMetadata metadata,
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
    /// ADR-011: the same (command, key hash) again answers its stored result as a replay with its first
    /// correlation; another payload or actor is a conflict that discloses nothing. The action is measured
    /// afresh before the replay.
    /// </summary>
    private async Task<CashAccountCommandResult?> PriorAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, CancellationToken cancellationToken)
    {
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var prior = await dbContext.CashAccountCommands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        if (prior.PayloadHash != payloadHash || !string.Equals(prior.ActorId, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.IdempotencyConflict);
        }

        if (await FenceAsync(context, action, prior.CashAccountId, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        var stored = JsonSerializer.Deserialize<CashAccountCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı komut sonucu okunamadı.");
        return stored with { IsReplay = true };
    }

    /// <summary>The currency must be one of Settings' candidates now; an unavailable source refuses.</summary>
    private async Task<CashAccountCommandResult?> CurrencyRefusalAsync(FinanceContext context, FinanceAction action,
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
    private async Task<CashAccountCommandResult?> FenceAsync(FinanceContext context, FinanceAction action, Guid? accountId,
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

    /// <summary>Whether proposals and rejections are the caller's business: a fresh grant to propose or approve.</summary>
    private async Task<bool> SeesProposalsAsync(FinanceContext context, CancellationToken cancellationToken) =>
        await permissions.CheckAsync(context, FinanceAction.ProposeCashAccount, null, cancellationToken) == PermissionDecision.Allowed
        || await permissions.CheckAsync(context, FinanceAction.ApproveCashAccount, null, cancellationToken) == PermissionDecision.Allowed;

    private async Task<ReadOutcome?> ReadFenceAsync(FinanceContext context, Guid? accountId, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return ReadOutcome.Denied;
        }

        var decision = await permissions.CheckAsync(context, FinanceAction.ReadCashAccounts, accountId, cancellationToken);
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
        Guid? accountId, CashAccountCommandResult result, DateTimeOffset now) =>
        dbContext.CashAccountCommands.Add(new CashAccountCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            CashAccountId = accountId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    /// <summary>
    /// One save, fenced again immediately before it. A lost race (a unique key or the row version) clears
    /// the tracker and reads the key afresh: a same-key winner is this answer too (ADR-011 §8). A propose
    /// that lost only the live-name index answers <c>cash_account_name_taken</c>; anything else is a
    /// <c>concurrent_change</c> the caller may retry.
    /// </summary>
    private async Task<CashAccountCommandResult> SaveAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, CashAccountCommandResult result,
        CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, action, result.Resource?.Id, cancellationToken) is { } fenced)
        {
            dbContext.ChangeTracker.Clear();
            return fenced;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            dbContext.ChangeTracker.Clear();
            if (await PriorAsync(context, metadata, action, command, payloadHash, cancellationToken) is { } prior)
            {
                return prior;
            }

            return exception.InnerException is SqlException { Number: 2601 } duplicate
                && duplicate.Message.Contains(LiveNameIndex, StringComparison.Ordinal)
                ? Refused(context, CommandOutcome.ValidationFailed, Issue("displayName", "cash_account_name_taken"))
                : Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }

        return result;
    }

    /// <summary>ADR-011 §7: an authorized terminal refusal is recorded alone (no change, no audit) and answered.</summary>
    private Task<CashAccountCommandResult> TerminalAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, Guid? accountId, CashAccountCommandResult refusal,
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

    private static string AccountAudit(CashAccountRow row) => JsonSerializer.Serialize(new
    {
        row.Id,
        version = VersionText(row.Version),
        status = StatusOf(row.Status).ToString(),
        row.CurrencyId,
        row.DisplayName,
        row.ProposedBy,
        row.DecidedBy,
        row.RejectionReason,
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static CashAccountCommandResult Succeeded(FinanceContext context, CashAccountRow row) =>
        new CashAccountCommandResult(CommandOutcome.Succeeded, View(row), IsReplay: false, context.CorrelationId, []);

    private static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static CashAccountCommandResult Refused(FinanceContext context, CommandOutcome outcome, params ValidationIssue[] issues) =>
        new CashAccountCommandResult(outcome, null, IsReplay: false, context.CorrelationId, issues);
}

/// <summary>
/// The ADR-009 envelopes of the cash-account master's writes: a proposal with the roles that granted
/// <c>finance.cash-account.propose</c>; an approval or a rejection with those of
/// <c>finance.cash-account.approve</c>.
/// </summary>
public sealed record FinanceCashAccountEnvelopes(FinanceAuditEnvelope Propose, FinanceAuditEnvelope Approve);
