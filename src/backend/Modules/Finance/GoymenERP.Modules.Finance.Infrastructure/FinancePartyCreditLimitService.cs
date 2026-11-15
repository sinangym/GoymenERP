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
/// API-85a (F018 plan §L1; FNS-D27/D27a): the Finance party credit limit master. It moves no money.
/// <list type="bullet">
/// <item><b>One step (FNS-D27):</b> a holder of <c>finance.credit-limits.approve</c> sets, changes or removes an
/// account's limit directly: the record is inserted approved, supersedes the account's previous effective
/// record (and any pending proposal) and is audited with the old and the new value.</item>
/// <item><b>Proposal (FNS-D27a):</b> a holder of only <c>finance.credit-limits.propose</c> proposes; a holder of
/// the approve authority OTHER than the proposer approves it or rejects it with a reason. The database refuses a
/// self-decision too (<c>CK_PartyCreditLimits_Decision</c>, binary collation).</item>
/// <item><b>Revision:</b> every direct change and proposal names the account's limit revision ("0" before the
/// first approved record). An approved record takes the next revision, unique per account
/// (<c>UX_PartyCreditLimits_Revision</c>): two changes from the same revision cannot both win. A proposal made
/// on a revision that has moved since cannot be approved (<c>credit_limit_revision_stale</c>); it can be
/// rejected.</item>
/// <item><b>Amount:</b> exact decimal text &gt; 0 in one Settings money currency candidate (FNS-D24), stored as
/// given, never rounded or converted. A Remove means "no limit" (the Sales risk port answers NoSource).</item>
/// <item><b>Party:</b> the (party, company account) pair is read through <see cref="IFinancePartyAccountPort"/>
/// (Counterparty's); a Set needs an active account and party, a Remove does not.</item>
/// <item><b>Authority:</b> server company/actor must be the request's; the action is checked before anything is
/// read, after every owner await, on replay and immediately before the save.</item>
/// <item><b>Visibility:</b> proposals, rejections and superseded proposals are shown only to a caller who may
/// propose or approve; a read-only caller sees approved records (and approved records since superseded).</item>
/// <item><b>Idempotency (ADR-011) and audit (ADR-009):</b> as the cash account master.</item>
/// </list>
/// Not built (open): a second approval above an amount threshold (F018 N-3, OPEN-009).
/// </summary>
public sealed class FinancePartyCreditLimitService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    ISettingsMoneyCandidateReader currencies,
    IFinancePartyAccountPort parties,
    IAuditEventCollector auditEvents,
    FinancePartyCreditLimitEnvelopes envelopes,
    TimeProvider timeProvider) : IFinancePartyCreditLimitMaster
{
    public const string SetCommand = "finance.credit-limit.set";
    public const string RemoveCommand = "finance.credit-limit.remove";
    public const string ProposeCommand = "finance.credit-limit.propose";
    public const string ApproveCommand = "finance.credit-limit.approve";
    public const string RejectCommand = "finance.credit-limit.reject";
    private const string EntityType = "Finance.PartyCreditLimit";
    internal const int SetKind = 1;
    internal const int RemoveKind = 2;
    internal const int Proposed = 1;
    internal const int Approved = 2;
    internal const int Rejected = 3;
    internal const int Superseded = 4;
    private const int IdentityMaximumLength = 128;
    private const int AmountMaximumLength = 64;
    private const int ReasonMaximumLength = 500;

    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<PartyCreditLimitCommandResult> SetAsync(FinanceContext context, CommandMetadata metadata,
        PartyCreditLimitSetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ApproveCreditLimit;
        if (await StartAsync(context, metadata, action, null, cancellationToken) is { } refused)
        {
            return refused;
        }

        // A null text cannot be the contract's JSON; refused before it is hashed.
        if (request.Amount is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("amount", "credit_limit_amount_invalid"));
        }

        if (request.ExpectedRevision is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedRevision", "identity_required"));
        }

        var payloadHash = PayloadHash(SetCommand, request);
        if (await PriorAsync(context, metadata, action, SetCommand, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (AccountRefusal(context, request.PartyId, request.CompanyAccountId, request.ExpectedRevision) is { } malformed)
        {
            return malformed;
        }

        if (SetPayloadRefusal(context, request.CurrencyId, request.Amount) is { } invalid)
        {
            return invalid;
        }

        if (await CurrencyRefusalAsync(context, action, request.CurrencyId, cancellationToken) is { } currencyRefused)
        {
            return currencyRefused;
        }

        if (await PartyRefusalAsync(context, action, request.PartyId, request.CompanyAccountId, requireActive: true,
                cancellationToken) is { } partyRefused)
        {
            return partyRefused;
        }

        return await DirectAsync(context, metadata, SetCommand, payloadHash, request.PartyId, request.CompanyAccountId,
            request.ExpectedRevision, SetKind, request.CurrencyId, request.Amount, "finance.credit-limit.set.v1", "Set",
            "Finans kredi limiti tanımı (onay yetkilisi tek adımda; para etkisi yok)", cancellationToken);
    }

    public async Task<PartyCreditLimitCommandResult> RemoveAsync(FinanceContext context, CommandMetadata metadata,
        PartyCreditLimitRemoveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ApproveCreditLimit;
        if (await StartAsync(context, metadata, action, null, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (request.ExpectedRevision is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedRevision", "identity_required"));
        }

        var payloadHash = PayloadHash(RemoveCommand, request);
        if (await PriorAsync(context, metadata, action, RemoveCommand, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (AccountRefusal(context, request.PartyId, request.CompanyAccountId, request.ExpectedRevision) is { } malformed)
        {
            return malformed;
        }

        // A limit is removed from a passive account too: "no limit" is never more permissive than a stale one.
        if (await PartyRefusalAsync(context, action, request.PartyId, request.CompanyAccountId, requireActive: false,
                cancellationToken) is { } partyRefused)
        {
            return partyRefused;
        }

        return await DirectAsync(context, metadata, RemoveCommand, payloadHash, request.PartyId, request.CompanyAccountId,
            request.ExpectedRevision, RemoveKind, currencyId: null, amount: null, "finance.credit-limit.removed.v1",
            "Remove", "Finans kredi limiti kaldırma (onay yetkilisi tek adımda; limit yok; para etkisi yok)",
            cancellationToken);
    }

    public async Task<PartyCreditLimitCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        PartyCreditLimitProposeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ProposeCreditLimit;
        if (await StartAsync(context, metadata, action, null, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (request.ExpectedRevision is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedRevision", "identity_required"));
        }

        // An undefined kind has no canonical payload text (the enum serializer refuses it), so it is refused before the
        // payload hash is taken; the switch below keeps the same refusal for completeness.
        if (!Enum.IsDefined(request.Kind))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("kind", "credit_limit_kind_invalid"));
        }

        var payloadHash = PayloadHash(ProposeCommand, request);
        if (await PriorAsync(context, metadata, action, ProposeCommand, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (AccountRefusal(context, request.PartyId, request.CompanyAccountId, request.ExpectedRevision) is { } malformed)
        {
            return malformed;
        }

        int kind;
        switch (request.Kind)
        {
            case PartyCreditLimitKind.Set:
                if (request.CurrencyId is not { } currencyId || request.Amount is null)
                {
                    return Refused(context, CommandOutcome.ValidationFailed,
                        Issue(request.CurrencyId is null ? "currencyId" : "amount",
                            request.CurrencyId is null ? "identity_required" : "credit_limit_amount_invalid"));
                }

                if (SetPayloadRefusal(context, currencyId, request.Amount) is { } invalid)
                {
                    return invalid;
                }

                if (await CurrencyRefusalAsync(context, action, currencyId, cancellationToken) is { } currencyRefused)
                {
                    return currencyRefused;
                }

                kind = SetKind;
                break;
            case PartyCreditLimitKind.Remove:
                if (request.CurrencyId is not null || request.Amount is not null)
                {
                    return Refused(context, CommandOutcome.ValidationFailed,
                        Issue(request.CurrencyId is not null ? "currencyId" : "amount", "credit_limit_remove_carries_no_amount"));
                }

                kind = RemoveKind;
                break;
            default:
                return Refused(context, CommandOutcome.ValidationFailed, Issue("kind", "credit_limit_kind_invalid"));
        }

        if (await PartyRefusalAsync(context, action, request.PartyId, request.CompanyAccountId,
                requireActive: kind == SetKind, cancellationToken) is { } partyRefused)
        {
            return partyRefused;
        }

        var account = await AccountAsync(request.CompanyAccountId, cancellationToken);
        if (await FenceAsync(context, action, null, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        if (!string.Equals(VersionText(account.Revision), request.ExpectedRevision, StringComparison.Ordinal))
        {
            return await TerminalAsync(context, metadata, action, ProposeCommand, payloadHash, targetId: null,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedRevision", "version_conflict")),
                cancellationToken);
        }

        if (account.OtherParty(request.PartyId))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("companyAccountId", "party_account_ineligible"));
        }

        if (account.Pending is not null)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("companyAccountId", "credit_limit_proposal_pending"));
        }

        if (kind == RemoveKind && account.Effective is not { Kind: SetKind })
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("kind", "credit_limit_not_set"));
        }

        var now = timeProvider.GetUtcNow();
        var row = new PartyCreditLimitRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            PartyId = request.PartyId,
            CompanyAccountId = request.CompanyAccountId,
            Kind = kind,
            CurrencyId = kind == SetKind ? request.CurrencyId : null,
            AmountText = kind == SetKind ? request.Amount : null,
            Status = Proposed,
            IsDirect = false,
            BaseRevision = account.Revision,
            Version = 1,
            ProposedBy = context.ActorId,
            ProposedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.PartyCreditLimits.Add(row);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, ProposeCommand, payloadHash, row.Id, result, now);
        Audit(context, metadata, envelopes.Propose, row.Id, "finance.credit-limit.proposed.v1", "Propose",
            versionBefore: null, row.Version, AccountAudit(account.Effective, null), RecordAudit(row),
            approvalContextJson: null, "Finans kredi limiti önerisi (onaylanana kadar geçerli limit değişmez; para etkisi yok)");
        return await SaveAsync(context, metadata, action, ProposeCommand, payloadHash, result, cancellationToken);
    }

    public async Task<PartyCreditLimitCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        PartyCreditLimitApproveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ApproveCreditLimit;
        if (await StartAsync(context, metadata, action, request.ProposalId, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (ProposalIdentityRefusal(context, request.ProposalId, request.ExpectedVersion) is { } malformed)
        {
            return malformed;
        }

        var payloadHash = PayloadHash(ApproveCommand, request);
        if (await PriorAsync(context, metadata, action, ApproveCommand, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        var (row, terminal) = await DecisionTargetAsync(context, metadata, ApproveCommand, payloadHash,
            request.ProposalId, request.ExpectedVersion, cancellationToken);
        if (terminal is not null)
        {
            return terminal;
        }

        if (row!.Kind == SetKind
            && await CurrencyRefusalAsync(context, action, row.CurrencyId!.Value, cancellationToken) is { } currencyRefused)
        {
            return currencyRefused;
        }

        if (await PartyRefusalAsync(context, action, row.PartyId, row.CompanyAccountId, requireActive: row.Kind == SetKind,
                cancellationToken) is { } partyRefused)
        {
            return partyRefused;
        }

        var account = await AccountAsync(row.CompanyAccountId, cancellationToken);
        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        // The account moved since the proposal was made: approving it would overwrite a change its approver
        // never saw. It stays pending until it is rejected (or superseded by a direct change).
        if (row.BaseRevision != account.Revision)
        {
            return await TerminalAsync(context, metadata, action, ApproveCommand, payloadHash, row.Id,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "credit_limit_revision_stale")),
                cancellationToken);
        }

        if (row.Kind == RemoveKind && account.Effective is not { Kind: SetKind })
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("proposalId", "credit_limit_not_set"));
        }

        var now = timeProvider.GetUtcNow();
        var before = AccountAudit(account.Effective, row);
        if (account.Effective is { } effective)
        {
            Supersede(effective, now);
        }

        var versionBefore = row.Version;
        row.Status = Approved;
        row.DecidedBy = context.ActorId;
        row.DecidedAtUtc = now;
        row.AccountRevision = checked(account.Revision + 1);
        row.UpdatedAtUtc = now;
        row.Version = checked(row.Version + 1);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, ApproveCommand, payloadHash, row.Id, result, now);
        var approval = JsonSerializer.Serialize(new { rule = "FNS-D27a", proposedBy = row.ProposedBy, approvedBy = context.ActorId },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Audit(context, metadata, envelopes.Approve, row.Id, "finance.credit-limit.approved.v1", "Approve", versionBefore,
            row.Version, before, RecordAudit(row), approval,
            "Finans kredi limiti öneri onayı (önerenden farklı tek onay; önceki geçerli limitin yerine geçer; para etkisi yok)");
        return await SaveAsync(context, metadata, action, ApproveCommand, payloadHash, result, cancellationToken);
    }

    /// <summary>
    /// Rejects a proposal: the same decider rule as approval (not the proposer), a reason, and the approve
    /// authority. The effective limit does not change.
    /// </summary>
    public async Task<PartyCreditLimitCommandResult> RejectAsync(FinanceContext context, CommandMetadata metadata,
        PartyCreditLimitRejectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ApproveCreditLimit;
        if (await StartAsync(context, metadata, action, request.ProposalId, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (ProposalIdentityRefusal(context, request.ProposalId, request.ExpectedVersion) is { } malformed)
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
        if (await PriorAsync(context, metadata, action, RejectCommand, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        var (row, terminal) = await DecisionTargetAsync(context, metadata, RejectCommand, payloadHash,
            request.ProposalId, request.ExpectedVersion, cancellationToken);
        if (terminal is not null)
        {
            return terminal;
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = row!.Version;
        var before = RecordAudit(row);
        row.Status = Rejected;
        row.DecidedBy = context.ActorId;
        row.DecidedAtUtc = now;
        row.RejectionReason = request.Reason;
        row.UpdatedAtUtc = now;
        row.Version = checked(row.Version + 1);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, RejectCommand, payloadHash, row.Id, result, now);
        var decision = JsonSerializer.Serialize(new { rule = "FNS-D27a", proposedBy = row.ProposedBy, rejectedBy = context.ActorId },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Audit(context, metadata, envelopes.Approve, row.Id, "finance.credit-limit.rejected.v1", "Reject", versionBefore,
            row.Version, before, RecordAudit(row), decision,
            "Finans kredi limiti öneri reddi (önerenden farklı tek karar; geçerli limit değişmez): " + request.Reason);
        return await SaveAsync(context, metadata, action, RejectCommand, payloadHash, result, cancellationToken);
    }

    public async Task<ReadResult<PartyCreditLimitListPage>> ListAsync(FinanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadFenceAsync(context, null, cancellationToken) is { } outcome)
        {
            return new ReadResult<PartyCreditLimitListPage>(outcome, null);
        }

        var seesProposals = await SeesProposalsAsync(context, cancellationToken);
        var live = await dbContext.PartyCreditLimits.AsNoTracking()
            .Where(row => row.Status == Approved || (seesProposals && row.Status == Proposed))
            .ToListAsync(cancellationToken);
        var accountIds = live.Select(row => row.CompanyAccountId).Distinct().ToArray();
        var revisions = await dbContext.PartyCreditLimits.AsNoTracking()
            .Where(row => accountIds.Contains(row.CompanyAccountId) && row.AccountRevision != null)
            .GroupBy(row => row.CompanyAccountId)
            .Select(group => new { CompanyAccountId = group.Key, Revision = group.Max(row => row.AccountRevision) })
            .ToDictionaryAsync(item => item.CompanyAccountId, item => item.Revision ?? 0, cancellationToken);
        if (await ReadFenceAsync(context, null, cancellationToken) is { } again)
        {
            return new ReadResult<PartyCreditLimitListPage>(again, null);
        }

        var items = live.GroupBy(row => row.CompanyAccountId)
            .OrderBy(group => group.Key)
            .Select(group => new PartyCreditLimitListItem(AccountView(context, group.Key,
                group.SingleOrDefault(row => row.Status == Approved), group.SingleOrDefault(row => row.Status == Proposed),
                revisions.GetValueOrDefault(group.Key))))
            .ToList();
        return new ReadResult<PartyCreditLimitListPage>(ReadOutcome.Found,
            new PartyCreditLimitListPage(context.CompanyId, items));
    }

    public async Task<ReadResult<PartyCreditLimitAccountView>> GetAccountAsync(FinanceContext context,
        Guid companyAccountId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadFenceAsync(context, null, cancellationToken) is { } outcome)
        {
            return new ReadResult<PartyCreditLimitAccountView>(outcome, null);
        }

        var seesProposals = await SeesProposalsAsync(context, cancellationToken);
        var account = companyAccountId == Guid.Empty ? null : await ReadAccountAsync(companyAccountId, cancellationToken);
        if (await ReadFenceAsync(context, null, cancellationToken) is { } again)
        {
            return new ReadResult<PartyCreditLimitAccountView>(again, null);
        }

        var pending = seesProposals ? account?.Pending : null;
        // Nothing the caller may see: the same answer as an account that never had a limit (nothing leaks).
        return account is null || (account.Effective is null && pending is null)
            ? new ReadResult<PartyCreditLimitAccountView>(ReadOutcome.NotFound, null)
            : new ReadResult<PartyCreditLimitAccountView>(ReadOutcome.Found,
                AccountView(context, companyAccountId, account.Effective, pending, account.Revision));
    }

    public async Task<ReadResult<PartyCreditLimitHistoryPage>> HistoryAsync(FinanceContext context, Guid companyAccountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadFenceAsync(context, null, cancellationToken) is { } outcome)
        {
            return new ReadResult<PartyCreditLimitHistoryPage>(outcome, null);
        }

        var seesProposals = await SeesProposalsAsync(context, cancellationToken);
        var rows = companyAccountId == Guid.Empty
            ? []
            : await dbContext.PartyCreditLimits.AsNoTracking()
                .Where(row => row.CompanyAccountId == companyAccountId && (seesProposals || row.AccountRevision != null))
                .OrderBy(row => row.ProposedAtUtc).ThenBy(row => row.AccountRevision).ThenBy(row => row.Id)
                .ToListAsync(cancellationToken);
        if (await ReadFenceAsync(context, null, cancellationToken) is { } again)
        {
            return new ReadResult<PartyCreditLimitHistoryPage>(again, null);
        }

        return rows.Count == 0
            ? new ReadResult<PartyCreditLimitHistoryPage>(ReadOutcome.NotFound, null)
            : new ReadResult<PartyCreditLimitHistoryPage>(ReadOutcome.Found,
                new PartyCreditLimitHistoryPage(context.CompanyId, companyAccountId, [.. rows.Select(View)]));
    }

    /// <summary>
    /// Settings' candidates under the PROPOSE or the APPROVE authority, passed through as given (shape-checked,
    /// never filtered, reordered or defaulted).
    /// </summary>
    public async Task<ReadResult<PartyCreditLimitCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await WriterFenceAsync(context, cancellationToken) is { } refused)
        {
            return new ReadResult<PartyCreditLimitCurrencyCandidates>(refused, null);
        }

        IReadOnlyList<MoneyCurrencyCandidateV1> candidates;
        try
        {
            candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(
                await currencies.GetMoneyCurrencyCandidatesAsync(cancellationToken));
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await WriterFenceAsync(context, cancellationToken) is { } fenced)
            {
                return new ReadResult<PartyCreditLimitCurrencyCandidates>(fenced, null);
            }

            return new ReadResult<PartyCreditLimitCurrencyCandidates>(
                exception.Code == "settings_access_denied" ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }

        if (await WriterFenceAsync(context, cancellationToken) is { } afterSettings)
        {
            return new ReadResult<PartyCreditLimitCurrencyCandidates>(afterSettings, null);
        }

        return new ReadResult<PartyCreditLimitCurrencyCandidates>(ReadOutcome.Found, new PartyCreditLimitCurrencyCandidates(
            context.CompanyId,
            [.. candidates.Select(candidate => new PartyCreditLimitCurrencyCandidate(candidate.CurrencyId, candidate.Code,
                candidate.DisplayLabel))]));
    }

    /// <summary>The canonical content of a command: its name and the exact contract JSON of the request.</summary>
    internal static string PayloadHash<T>(string command, T request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command + "|" + JsonSerializer.Serialize(request, Json))))
            .ToLowerInvariant();

    internal static string VersionText(long version) => version.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The exact amount rule: a positive decimal text (digits and at most one inner dot, no sign, no exponent, no
    /// surrounding space) that fits the column and is exactly a CLR decimal, so the Sales risk port can answer it
    /// without rounding. Leading zeros and trailing fractional zeros are kept as given (the value is exact).
    /// </summary>
    internal static bool IsExactAmount(string? amount) =>
        amount is not null
        && amount.Length <= AmountMaximumLength
        && FinanceExactDecimalText.TryParsePositive(amount, out _)
        && decimal.TryParse(amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
        && FinanceExactDecimalText.Equal(amount, value.ToString(CultureInfo.InvariantCulture));

    private async Task<PartyCreditLimitCommandResult> DirectAsync(FinanceContext context, CommandMetadata metadata,
        string command, string payloadHash, Guid partyId, Guid companyAccountId, string expectedRevision, int kind,
        Guid? currencyId, string? amount, string eventType, string operation, string auditReason,
        CancellationToken cancellationToken)
    {
        const FinanceAction action = FinanceAction.ApproveCreditLimit;
        var account = await AccountAsync(companyAccountId, cancellationToken);
        if (await FenceAsync(context, action, null, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        if (!string.Equals(VersionText(account.Revision), expectedRevision, StringComparison.Ordinal))
        {
            return await TerminalAsync(context, metadata, action, command, payloadHash, targetId: null,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedRevision", "version_conflict")),
                cancellationToken);
        }

        if (account.OtherParty(partyId))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("companyAccountId", "party_account_ineligible"));
        }

        if (kind == RemoveKind && account.Effective is not { Kind: SetKind })
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("companyAccountId", "credit_limit_not_set"));
        }

        var now = timeProvider.GetUtcNow();
        var before = AccountAudit(account.Effective, account.Pending);
        // A direct change by an approver supersedes the pending proposal too: it was made on this revision and
        // could never be approved after it.
        if (account.Pending is { } pending)
        {
            Supersede(pending, now);
        }

        if (account.Effective is { } effective)
        {
            Supersede(effective, now);
        }

        var row = new PartyCreditLimitRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            PartyId = partyId,
            CompanyAccountId = companyAccountId,
            Kind = kind,
            CurrencyId = currencyId,
            AmountText = amount,
            Status = Approved,
            IsDirect = true,
            BaseRevision = account.Revision,
            AccountRevision = checked(account.Revision + 1),
            Version = 1,
            ProposedBy = context.ActorId,
            ProposedAtUtc = now,
            DecidedBy = context.ActorId,
            DecidedAtUtc = now,
            UpdatedAtUtc = now,
        };
        dbContext.PartyCreditLimits.Add(row);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, command, payloadHash, row.Id, result, now);
        var approval = JsonSerializer.Serialize(new { rule = "FNS-D27", approvedBy = context.ActorId, direct = true },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Audit(context, metadata, envelopes.Approve, row.Id, eventType, operation, versionBefore: null, row.Version, before,
            RecordAudit(row), approval, auditReason);
        return await SaveAsync(context, metadata, action, command, payloadHash, result, cancellationToken);
    }

    private static void Supersede(PartyCreditLimitRow row, DateTimeOffset now)
    {
        row.Status = Superseded;
        row.SupersededAtUtc = now;
        row.UpdatedAtUtc = now;
        row.Version = checked(row.Version + 1);
    }

    /// <summary>The account's live rows (tracked, for a write) and its revision.</summary>
    private async Task<AccountState> AccountAsync(Guid companyAccountId, CancellationToken cancellationToken)
    {
        var live = await dbContext.PartyCreditLimits
            .Where(row => row.CompanyAccountId == companyAccountId && (row.Status == Proposed || row.Status == Approved))
            .ToListAsync(cancellationToken);
        var revision = await dbContext.PartyCreditLimits.AsNoTracking()
            .Where(row => row.CompanyAccountId == companyAccountId && row.AccountRevision != null)
            .MaxAsync(row => row.AccountRevision, cancellationToken) ?? 0;
        var other = await dbContext.PartyCreditLimits.AsNoTracking()
            .Where(row => row.CompanyAccountId == companyAccountId)
            .Select(row => row.PartyId).Distinct().ToListAsync(cancellationToken);
        return new AccountState(live.SingleOrDefault(row => row.Status == Approved),
            live.SingleOrDefault(row => row.Status == Proposed), revision, other);
    }

    private async Task<AccountState?> ReadAccountAsync(Guid companyAccountId, CancellationToken cancellationToken)
    {
        var live = await dbContext.PartyCreditLimits.AsNoTracking()
            .Where(row => row.CompanyAccountId == companyAccountId && (row.Status == Proposed || row.Status == Approved))
            .ToListAsync(cancellationToken);
        if (live.Count == 0)
        {
            return null;
        }

        var revision = await dbContext.PartyCreditLimits.AsNoTracking()
            .Where(row => row.CompanyAccountId == companyAccountId && row.AccountRevision != null)
            .MaxAsync(row => row.AccountRevision, cancellationToken) ?? 0;
        return new AccountState(live.SingleOrDefault(row => row.Status == Approved),
            live.SingleOrDefault(row => row.Status == Proposed), revision, []);
    }

    /// <summary>
    /// An account's limit state: its effective record, its pending proposal, its revision and every party its
    /// records named (one, unless Counterparty moved the account, which is refused rather than guessed).
    /// </summary>
    private sealed record AccountState(PartyCreditLimitRow? Effective, PartyCreditLimitRow? Pending, long Revision,
        IReadOnlyList<Guid> Parties)
    {
        public bool OtherParty(Guid partyId) => Parties.Any(party => party != partyId);
    }

    private PartyCreditLimitAccountView AccountView(FinanceContext context, Guid companyAccountId,
        PartyCreditLimitRow? effective, PartyCreditLimitRow? pending, long revision) =>
        new PartyCreditLimitAccountView(context.CompanyId, (effective ?? pending)!.PartyId, companyAccountId,
            effective is null ? null : View(effective), pending is null ? null : View(pending), VersionText(revision),
            pending is not null && string.Equals(pending.ProposedBy, context.ActorId, StringComparison.Ordinal));

    private static PartyCreditLimitRecordView View(PartyCreditLimitRow row) =>
        new PartyCreditLimitRecordView(row.Id, row.CompanyId, row.PartyId, row.CompanyAccountId, KindOf(row.Kind),
            row.CurrencyId, row.AmountText, StatusOf(row.Status), row.IsDirect,
            row.AccountRevision is { } revision ? VersionText(revision) : null, row.RejectionReason, row.ProposedAtUtc,
            row.DecidedAtUtc, row.SupersededAtUtc, VersionText(row.Version));

    private static PartyCreditLimitKind KindOf(int kind) => kind switch
    {
        SetKind => PartyCreditLimitKind.Set,
        RemoveKind => PartyCreditLimitKind.Remove,
        _ => throw new InvalidOperationException("Kredi limiti kayıt türü tanınmıyor."),
    };

    private static PartyCreditLimitStatus StatusOf(int status) => status switch
    {
        Proposed => PartyCreditLimitStatus.Proposed,
        Approved => PartyCreditLimitStatus.Approved,
        Rejected => PartyCreditLimitStatus.Rejected,
        Superseded => PartyCreditLimitStatus.Superseded,
        _ => throw new InvalidOperationException("Kredi limiti kayıt durumu tanınmıyor."),
    };

    private static PartyCreditLimitCommandResult? AccountRefusal(FinanceContext context, Guid partyId,
        Guid companyAccountId, string expectedRevision)
    {
        // Malformed identities are refused before anything is read (never stored, so never a replay).
        if (partyId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("partyId", "identity_required"));
        }

        if (companyAccountId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("companyAccountId", "identity_required"));
        }

        return IsCanonical(expectedRevision)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("expectedRevision", "identity_required"));
    }

    private static PartyCreditLimitCommandResult? SetPayloadRefusal(FinanceContext context, Guid currencyId, string amount)
    {
        if (currencyId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("currencyId", "identity_required"));
        }

        return IsExactAmount(amount)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("amount", "credit_limit_amount_invalid"));
    }

    private static PartyCreditLimitCommandResult? ProposalIdentityRefusal(FinanceContext context, Guid proposalId,
        string? expectedVersion)
    {
        if (proposalId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("proposalId", "identity_required"));
        }

        return IsCanonical(expectedVersion)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
    }

    /// <summary>
    /// The proposal a decision names: not the company's (terminal), a stale version (terminal), not a proposal
    /// any more, or the proposer's own (maker≠checker).
    /// </summary>
    private async Task<(PartyCreditLimitRow? Row, PartyCreditLimitCommandResult? Refusal)> DecisionTargetAsync(
        FinanceContext context, CommandMetadata metadata, string command, string payloadHash, Guid proposalId,
        string expectedVersion, CancellationToken cancellationToken)
    {
        const FinanceAction action = FinanceAction.ApproveCreditLimit;
        var row = await dbContext.PartyCreditLimits.SingleOrDefaultAsync(candidate => candidate.Id == proposalId,
            cancellationToken);
        if (row is null)
        {
            // Another company's record is not visible either: the same answer, nothing leaks (§7).
            return (null, await TerminalAsync(context, metadata, action, command, payloadHash, targetId: null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("proposalId", "credit_limit_proposal_not_found")),
                cancellationToken));
        }

        if (!string.Equals(VersionText(row.Version), expectedVersion, StringComparison.Ordinal))
        {
            return (null, await TerminalAsync(context, metadata, action, command, payloadHash, row.Id,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                cancellationToken));
        }

        if (row.Status != Proposed || row.IsDirect)
        {
            return (null, Refused(context, CommandOutcome.ValidationFailed, Issue("proposalId", "credit_limit_not_proposed")));
        }

        // Maker≠checker: an authorized person other than the one who proposed it.
        return string.Equals(row.ProposedBy, context.ActorId, StringComparison.Ordinal)
            ? (null, Refused(context, CommandOutcome.Denied, Issue("approval", "finance_approver_is_proposer")))
            : (row, null);
    }

    /// <summary>The (party, company account) pair through Counterparty's port, fenced again after it.</summary>
    private async Task<PartyCreditLimitCommandResult?> PartyRefusalAsync(FinanceContext context, FinanceAction action,
        Guid partyId, Guid companyAccountId, bool requireActive, CancellationToken cancellationToken)
    {
        var party = await parties.GetAuthorizedAsync(context, partyId, companyAccountId, cancellationToken);
        if (await FenceAsync(context, action, null, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        if (party.Outcome != ReadOutcome.Found || party.Resource is not { } resource)
        {
            return party.Outcome switch
            {
                ReadOutcome.Denied => Refused(context, CommandOutcome.Denied),
                ReadOutcome.NotFound => Refused(context, CommandOutcome.ValidationFailed,
                    Issue("companyAccountId", "party_account_ineligible")),
                _ => Refused(context, CommandOutcome.Unavailable, Issue("companyAccountId", "party_account_unavailable")),
            };
        }

        return resource.CompanyId != context.CompanyId || resource.PartyId != partyId
            || resource.CompanyAccountId != companyAccountId
            || (requireActive && (!resource.IsActive || !resource.IsPartyActive))
            ? Refused(context, CommandOutcome.ValidationFailed, Issue("companyAccountId", "party_account_ineligible"))
            : null;
    }

    /// <summary>Context, the action's fresh authority, and the key and context storage limits.</summary>
    private async Task<PartyCreditLimitCommandResult?> StartAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, Guid? targetId, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, action, targetId, cancellationToken) is { } denied)
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
    /// correlation; another payload or actor is a conflict that discloses nothing. The action is measured afresh
    /// before the replay.
    /// </summary>
    private async Task<PartyCreditLimitCommandResult?> PriorAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, CancellationToken cancellationToken)
    {
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var prior = await dbContext.PartyCreditLimitCommands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        if (prior.PayloadHash != payloadHash || !string.Equals(prior.ActorId, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.IdempotencyConflict);
        }

        if (await FenceAsync(context, action, prior.PartyCreditLimitId, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        var stored = JsonSerializer.Deserialize<PartyCreditLimitCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı komut sonucu okunamadı.");
        return stored with { IsReplay = true };
    }

    /// <summary>The currency must be one of Settings' candidates now; an unavailable source refuses.</summary>
    private async Task<PartyCreditLimitCommandResult?> CurrencyRefusalAsync(FinanceContext context, FinanceAction action,
        Guid currencyId, CancellationToken cancellationToken)
    {
        IReadOnlyList<MoneyCurrencyCandidateV1> candidates;
        try
        {
            candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(
                await currencies.GetMoneyCurrencyCandidatesAsync(cancellationToken));
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceAsync(context, action, null, cancellationToken) is { } fenced)
            {
                return fenced;
            }

            return exception.Code == "settings_access_denied"
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable, Issue("currencyId", exception.Code));
        }

        if (await FenceAsync(context, action, null, cancellationToken) is { } afterSettings)
        {
            return afterSettings;
        }

        return candidates.Any(candidate => candidate.CurrencyId == currencyId)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("currencyId", "currency_not_a_candidate"));
    }

    /// <summary>Company/actor still the request's, then a fresh decision on the action.</summary>
    private async Task<PartyCreditLimitCommandResult?> FenceAsync(FinanceContext context, FinanceAction action,
        Guid? targetId, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }

        var decision = await permissions.CheckAsync(context, action, targetId, cancellationToken);
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
        await permissions.CheckAsync(context, FinanceAction.ProposeCreditLimit, null, cancellationToken) == PermissionDecision.Allowed
        || await permissions.CheckAsync(context, FinanceAction.ApproveCreditLimit, null, cancellationToken) == PermissionDecision.Allowed;

    /// <summary>The propose OR the approve authority: denied only when neither is granted.</summary>
    private async Task<ReadOutcome?> WriterFenceAsync(FinanceContext context, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, FinanceAction.ProposeCreditLimit, null, cancellationToken) is not { } propose)
        {
            return null;
        }

        if (await FenceAsync(context, FinanceAction.ApproveCreditLimit, null, cancellationToken) is not { } approve)
        {
            return null;
        }

        return propose.Outcome == CommandOutcome.Unavailable || approve.Outcome == CommandOutcome.Unavailable
            ? ReadOutcome.Unavailable
            : ReadOutcome.Denied;
    }

    private async Task<ReadOutcome?> ReadFenceAsync(FinanceContext context, Guid? targetId, CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return ReadOutcome.Denied;
        }

        var decision = await permissions.CheckAsync(context, FinanceAction.ReadCreditLimits, targetId, cancellationToken);
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
        Guid? targetId, PartyCreditLimitCommandResult result, DateTimeOffset now) =>
        dbContext.PartyCreditLimitCommands.Add(new PartyCreditLimitCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            PartyCreditLimitId = targetId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    /// <summary>
    /// One save, fenced again immediately before it. A lost race (a unique key — the account's live state or its
    /// next revision — or a row version) clears the tracker and reads the key afresh: a same-key winner is this
    /// answer too (ADR-011 §8); anything else is a <c>concurrent_change</c> the caller may retry after reading the
    /// account again.
    /// </summary>
    private async Task<PartyCreditLimitCommandResult> SaveAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, PartyCreditLimitCommandResult result,
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

            return Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }

        return result;
    }

    /// <summary>ADR-011 §7: an authorized terminal refusal is recorded alone (no change, no audit) and answered.</summary>
    private Task<PartyCreditLimitCommandResult> TerminalAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, Guid? targetId, PartyCreditLimitCommandResult refusal,
        CancellationToken cancellationToken)
    {
        // Nothing read for the decision may be saved with the refusal.
        dbContext.ChangeTracker.Clear();
        AddCommand(context, metadata, command, payloadHash, targetId, refusal, timeProvider.GetUtcNow());
        return SaveAsync(context, metadata, action, command, payloadHash, refusal, cancellationToken);
    }

    private void Audit(FinanceContext context, CommandMetadata metadata, FinanceAuditEnvelope envelope, Guid recordId,
        string eventType, string operation, long? versionBefore, long? versionAfter, string before, string after,
        string? approvalContextJson, string reason)
    {
        auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId,
            eventType,
            EntityType,
            recordId.ToString("D"),
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

    /// <summary>FNS-D27: the old value beside the new one — the account's effective record and, if any, the other
    /// record the command decides or supersedes.</summary>
    private static string AccountAudit(PartyCreditLimitRow? effective, PartyCreditLimitRow? other) =>
        effective is null && other is null
            ? "{}"
            : JsonSerializer.Serialize(new
            {
                effective = effective is null ? null : RecordAuditObject(effective),
                other = other is null ? null : RecordAuditObject(other),
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string RecordAudit(PartyCreditLimitRow row) =>
        JsonSerializer.Serialize(RecordAuditObject(row), new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static object RecordAuditObject(PartyCreditLimitRow row) => new
    {
        row.Id,
        version = VersionText(row.Version),
        kind = KindOf(row.Kind).ToString(),
        status = StatusOf(row.Status).ToString(),
        row.PartyId,
        row.CompanyAccountId,
        row.CurrencyId,
        amount = row.AmountText,
        row.IsDirect,
        baseRevision = VersionText(row.BaseRevision),
        accountRevision = row.AccountRevision is { } revision ? VersionText(revision) : null,
        row.ProposedBy,
        row.DecidedBy,
        row.RejectionReason,
    };

    private static PartyCreditLimitCommandResult Succeeded(FinanceContext context, PartyCreditLimitRow row) =>
        new PartyCreditLimitCommandResult(CommandOutcome.Succeeded, View(row), IsReplay: false, context.CorrelationId, []);

    private static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static PartyCreditLimitCommandResult Refused(FinanceContext context, CommandOutcome outcome,
        params ValidationIssue[] issues) =>
        new PartyCreditLimitCommandResult(outcome, null, IsReplay: false, context.CorrelationId, issues);
}

/// <summary>
/// The ADR-009 envelopes of the credit limit master's writes: a proposal with the roles that granted
/// <c>finance.credit-limits.propose</c>; a direct change, an approval or a rejection with those of
/// <c>finance.credit-limits.approve</c>.
/// </summary>
public sealed record FinancePartyCreditLimitEnvelopes(FinanceAuditEnvelope Propose, FinanceAuditEnvelope Approve);
