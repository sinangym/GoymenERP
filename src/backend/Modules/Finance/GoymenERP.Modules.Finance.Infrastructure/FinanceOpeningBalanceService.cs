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
/// FIN-S9 (FNS-D23): the cash, bank and party opening balances, entered only while the Settings opening period is open.
/// <list type="bullet">
/// <item><b>Opening period:</b> read through <see cref="IFinanceOpeningPeriodReader"/> on every proposal and every
/// decision, fenced again after it. Unavailable refuses as Unavailable; Closed refuses as a validation failure. The
/// default reader answers Unavailable until the Settings adapter is registered: the period is never assumed open.</item>
/// <item><b>Two people (FNS-D23):</b> a holder of <c>finance.opening-balances.propose</c> proposes; a holder of
/// <c>finance.opening-balances.approve</c> other than the proposer approves or rejects (here and in
/// <c>CK_OpeningBalances_Decision</c>).</item>
/// <item><b>Target:</b> an approved cash or bank account in the opening's currency; or an active (party, company account)
/// pair read through <see cref="IFinancePartyAccountPort"/> with one Settings money currency candidate and a direction.
/// One live (proposed or approved) opening per (target kind, target, currency).</item>
/// <item><b>Amount:</b> exact decimal text greater than zero, stored as given, never rounded or converted. A negative bank
/// opening (overdraft) is refused: not decided.</item>
/// <item><b>AsOfDate (FNS-D86):</b> a Europe/Istanbul business date, not after today's (ASSUMPTION-FIN-S9-ASOF); an
/// unresolvable business day fails closed. The period version is written on the proposal (and on a reversal proposal);
/// a decision under a changed period version is refused.</item>
/// <item><b>Movement (FNS-D84):</b> the approval writes exactly one opening movement in the same save; the unique
/// (company, opening, kind) key and the idempotency record keep it single.</item>
/// <item><b>Correction (FNS-D85):</b> a reversal of an approved opening, proposed and decided by two people
/// (ASSUMPTION-FIN-S9-REVERSAL); its approval writes the reversal movement and marks the opening reversed, freeing the slot
/// for a new proposal.</item>
/// <item><b>Authority, idempotency (ADR-011) and audit (ADR-009):</b> as the collateral master.</item>
/// </list>
/// Treasury movements participate in the FNS-D102 internal ledger. Not built here:
/// an open item for a party opening, cheque/note, loan and DBS openings.
/// </summary>
public sealed class FinanceOpeningBalanceService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    ISettingsMoneyCandidateReader currencies,
    IFinancePartyAccountPort parties,
    IFinanceOpeningPeriodReader openingPeriod,
    IAuditEventCollector auditEvents,
    FinanceOpeningBalanceEnvelopes envelopes,
    TimeProvider timeProvider,
    IFinanceTreasuryBalanceGuard? balanceGuard = null,
    IAuditTransactionWriterV1? auditOwner = null) : IFinanceOpeningBalanceService
{
    public const string ProposeCommand = "finance.opening-balance.propose";
    public const string ApproveCommand = "finance.opening-balance.approve";
    public const string RejectCommand = "finance.opening-balance.reject";
    public const string ProposeReversalCommand = "finance.opening-balance.reversal.propose";
    public const string ApproveReversalCommand = "finance.opening-balance.reversal.approve";
    public const string RejectReversalCommand = "finance.opening-balance.reversal.reject";
    private const string EntityType = "Finance.OpeningBalance";
    internal const int CashAccountKind = 1;
    internal const int BankAccountKind = 2;
    internal const int PartyAccountKind = 3;
    internal const int DebitDirection = 1;
    internal const int CreditDirection = 2;
    internal const int Proposed = 1;
    internal const int Approved = 2;
    internal const int Rejected = 3;
    internal const int Reversed = 4;
    internal const int OpeningMovement = 1;
    internal const int ReversalMovement = 2;
    private const int AccountApproved = 2;
    private const int IdentityMaximumLength = 128;
    private const int PeriodVersionMaximumLength = 64;
    private const int ReasonMaximumLength = 500;

    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public async Task<OpeningBalanceCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        OpeningBalanceProposeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ProposeOpeningBalance;
        if (await StartAsync(context, metadata, action, cancellationToken) is { } refused)
        {
            return refused;
        }

        // A null text cannot be the contract's JSON; refused before it is hashed.
        if (request.Amount is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("amount", "opening_amount_invalid"));
        }

        var payloadHash = PayloadHash(ProposeCommand, request);
        if (await PriorAsync(context, metadata, action, ProposeCommand, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (PayloadRefusal(context, request) is { } invalid)
        {
            return invalid;
        }

        // FNS-D86: AsOfDate is a Europe/Istanbul business date; never after today's (ASSUMPTION-FIN-S9-ASOF).
        if (!FinanceBusinessDay.TryToday(timeProvider, out var today))
        {
            return Refused(context, CommandOutcome.Unavailable,
                Issue("asOfDate", FinanceBusinessDay.BusinessDayUnavailable));
        }

        if (request.AsOfDate > today)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("asOfDate", "opening_as_of_date_in_future"));
        }

        var (period, periodRefused) = await PeriodAsync(context, action, cancellationToken);
        if (periodRefused is not null)
        {
            return periodRefused;
        }

        if (await TargetRefusalAsync(context, action, request, cancellationToken) is { } targetRefused)
        {
            return targetRefused;
        }

        var kind = KindCode(request.TargetKind);
        var live = await dbContext.OpeningBalances.AsNoTracking()
            .AnyAsync(row => row.TargetKind == kind && row.TargetId == request.TargetId
                && row.CurrencyId == request.CurrencyId && (row.Status == Proposed || row.Status == Approved),
                cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        if (live)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("targetId", "opening_balance_exists"));
        }

        var now = timeProvider.GetUtcNow();
        var row = new OpeningBalanceRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            TargetKind = kind,
            TargetId = request.TargetId,
            PartyId = request.TargetKind == OpeningBalanceTargetKind.PartyAccount ? request.PartyId : null,
            CurrencyId = request.CurrencyId,
            Direction = request.Direction is { } direction ? DirectionCode(direction) : null,
            AmountText = request.Amount,
            AsOfDate = request.AsOfDate,
            Status = Proposed,
            OpeningPeriodVersion = period!.Version!,
            ProposedBy = context.ActorId,
            ProposedAtUtc = now,
            Version = 1,
            UpdatedAtUtc = now,
        };
        dbContext.OpeningBalances.Add(row);
        var result = Succeeded(context, row);
        AddCommand(context, metadata, ProposeCommand, payloadHash, row.Id, result, now);
        var approval = JsonSerializer.Serialize(new
        {
            rule = "FNS-D23",
            proposedBy = context.ActorId,
            openingPeriodVersion = period.Version,
        }, AuditJson);
        Audit(context, metadata, envelopes.Propose, row.Id, "finance.opening-balance.proposed.v1", "Propose",
            versionBefore: null, row.Version, "{}", BalanceAudit(row), approval,
            "Finans açılış bakiyesi önerisi (açılış dönemi açık; onay bekler; onaya kadar hareket yok)");
        return await SaveAsync(context, metadata, action, ProposeCommand, payloadHash, result, cancellationToken);
    }

    public async Task<OpeningBalanceCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        OpeningBalanceApproveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        if (await StartAsync(context, metadata, FinanceAction.ApproveOpeningBalance, cancellationToken)
            is { } denied) return denied;
        return await FinanceTreasuryTransaction.ExecuteAsync(dbContext,
            () => ApproveTreasuryCoreAsync(context, metadata, request, cancellationToken),
            async () => await FenceAsync(context, FinanceAction.ApproveOpeningBalance, cancellationToken)
                ?? Refused(context, CommandOutcome.Unavailable, Issue("treasuryAccountId", TreasuryBalanceAnswer.UnavailableCode)), auditOwner, cancellationToken);
    }

    private async Task<OpeningBalanceCommandResult> ApproveTreasuryCoreAsync(FinanceContext context, CommandMetadata metadata,
        OpeningBalanceApproveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ApproveOpeningBalance;
        if (await StartAsync(context, metadata, action, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (request.ExpectedVersion is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
        }

        var payloadHash = PayloadHash(ApproveCommand, request);
        if (await PriorAsync(context, metadata, action, ApproveCommand, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (IdentityRefusal(context, request.OpeningBalanceId, request.ExpectedVersion) is { } malformed)
        {
            return malformed;
        }

        return await DecideAsync(context, metadata, action, ApproveCommand, payloadHash, request.OpeningBalanceId,
            request.ExpectedVersion, Approved, reason: null, "finance.opening-balance.approved.v1", "Approve",
            "Finans açılış bakiyesi onayı (önerenden farklı müdür; tek açılış hareketi yazıldı)", cancellationToken);
    }

    public async Task<OpeningBalanceCommandResult> RejectAsync(FinanceContext context, CommandMetadata metadata,
        OpeningBalanceRejectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ApproveOpeningBalance;
        if (await StartAsync(context, metadata, action, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (request.ExpectedVersion is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
        }

        if (ReasonRefusal(context, request.Reason) is { } badReason)
        {
            return badReason;
        }

        var payloadHash = PayloadHash(RejectCommand, request);
        if (await PriorAsync(context, metadata, action, RejectCommand, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (IdentityRefusal(context, request.OpeningBalanceId, request.ExpectedVersion) is { } malformed)
        {
            return malformed;
        }

        return await DecideAsync(context, metadata, action, RejectCommand, payloadHash, request.OpeningBalanceId,
            request.ExpectedVersion, Rejected, request.Reason, "finance.opening-balance.rejected.v1", "Reject",
            "Finans açılış bakiyesi reddi (önerenden farklı müdür; hareket yok)", cancellationToken);
    }

    /// <summary>FNS-D85: the reversal proposal of an approved opening, under the propose authority.</summary>
    public async Task<OpeningBalanceCommandResult> ProposeReversalAsync(FinanceContext context, CommandMetadata metadata,
        OpeningBalanceReversalProposeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ProposeOpeningBalance;
        if (await StartAsync(context, metadata, action, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (request.ExpectedVersion is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
        }

        if (ReasonRefusal(context, request.Reason) is { } badReason)
        {
            return badReason;
        }

        var payloadHash = PayloadHash(ProposeReversalCommand, request);
        if (await PriorAsync(context, metadata, action, ProposeReversalCommand, payloadHash, cancellationToken)
            is { } prior)
        {
            return prior;
        }

        if (IdentityRefusal(context, request.OpeningBalanceId, request.ExpectedVersion) is { } malformed)
        {
            return malformed;
        }

        var (period, periodRefused) = await PeriodAsync(context, action, cancellationToken);
        if (periodRefused is not null)
        {
            return periodRefused;
        }

        var row = await dbContext.OpeningBalances.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.OpeningBalanceId, cancellationToken);
        var live = row is not null && await dbContext.OpeningBalanceReversals.AsNoTracking()
            .AnyAsync(reversal => reversal.OpeningBalanceId == row.Id
                && (reversal.Status == Proposed || reversal.Status == Approved), cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        if (row is null)
        {
            return await TerminalAsync(context, metadata, action, ProposeReversalCommand, payloadHash, targetId: null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("openingBalanceId", "opening_balance_not_found")),
                cancellationToken);
        }

        if (!string.Equals(VersionText(row.Version), request.ExpectedVersion, StringComparison.Ordinal))
        {
            return await TerminalAsync(context, metadata, action, ProposeReversalCommand, payloadHash, row.Id,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                cancellationToken);
        }

        if (row.Status != Approved)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("openingBalanceId", "opening_balance_not_approved"));
        }

        if (live)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("openingBalanceId", "opening_balance_reversal_exists"));
        }

        var now = timeProvider.GetUtcNow();
        var reversal = new OpeningBalanceReversalRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            OpeningBalanceId = row.Id,
            Status = Proposed,
            Reason = request.Reason,
            OpeningPeriodVersion = period!.Version!,
            ProposedBy = context.ActorId,
            ProposedAtUtc = now,
            Version = 1,
            UpdatedAtUtc = now,
        };
        dbContext.OpeningBalanceReversals.Add(reversal);
        var result = Succeeded(context, row, reversal);
        AddCommand(context, metadata, ProposeReversalCommand, payloadHash, row.Id, result, now);
        var approval = JsonSerializer.Serialize(new
        {
            rule = "FNS-D85",
            proposedBy = context.ActorId,
            openingPeriodVersion = period.Version,
        }, AuditJson);
        Audit(context, metadata, envelopes.Propose, row.Id, "finance.opening-balance.reversal-proposed.v1",
            "ProposeReversal", row.Version, row.Version, BalanceAudit(row), ReversalAudit(reversal), approval,
            "Finans açılış bakiyesi ters kayıt önerisi: " + request.Reason);
        return await SaveAsync(context, metadata, action, ProposeReversalCommand, payloadHash, result,
            cancellationToken);
    }

    /// <summary>FNS-D85: the approval of a pending reversal; writes the reversal movement and marks the opening reversed.</summary>
    public async Task<OpeningBalanceCommandResult> ApproveReversalAsync(FinanceContext context,
        CommandMetadata metadata, OpeningBalanceReversalApproveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        if (await StartAsync(context, metadata, FinanceAction.ApproveOpeningBalance, cancellationToken)
            is { } denied) return denied;
        return await FinanceTreasuryTransaction.ExecuteAsync(dbContext,
            () => ApproveReversalTreasuryCoreAsync(context, metadata, request, cancellationToken),
            async () => await FenceAsync(context, FinanceAction.ApproveOpeningBalance, cancellationToken)
                ?? Refused(context, CommandOutcome.Unavailable, Issue("treasuryAccountId", TreasuryBalanceAnswer.UnavailableCode)), auditOwner, cancellationToken);
    }

    private async Task<OpeningBalanceCommandResult> ApproveReversalTreasuryCoreAsync(FinanceContext context,
        CommandMetadata metadata, OpeningBalanceReversalApproveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ApproveOpeningBalance;
        if (await StartAsync(context, metadata, action, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (request.ExpectedVersion is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
        }

        var payloadHash = PayloadHash(ApproveReversalCommand, request);
        if (await PriorAsync(context, metadata, action, ApproveReversalCommand, payloadHash, cancellationToken)
            is { } prior)
        {
            return prior;
        }

        if (IdentityRefusal(context, request.OpeningBalanceId, request.ExpectedVersion) is { } malformed)
        {
            return malformed;
        }

        return await DecideReversalAsync(context, metadata, action, ApproveReversalCommand, payloadHash,
            request.OpeningBalanceId, request.ExpectedVersion, Approved, reason: null, cancellationToken);
    }

    public async Task<OpeningBalanceCommandResult> RejectReversalAsync(FinanceContext context,
        CommandMetadata metadata, OpeningBalanceReversalRejectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ApproveOpeningBalance;
        if (await StartAsync(context, metadata, action, cancellationToken) is { } refused)
        {
            return refused;
        }

        if (request.ExpectedVersion is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
        }

        if (ReasonRefusal(context, request.Reason) is { } badReason)
        {
            return badReason;
        }

        var payloadHash = PayloadHash(RejectReversalCommand, request);
        if (await PriorAsync(context, metadata, action, RejectReversalCommand, payloadHash, cancellationToken)
            is { } prior)
        {
            return prior;
        }

        if (IdentityRefusal(context, request.OpeningBalanceId, request.ExpectedVersion) is { } malformed)
        {
            return malformed;
        }

        return await DecideReversalAsync(context, metadata, action, RejectReversalCommand, payloadHash,
            request.OpeningBalanceId, request.ExpectedVersion, Rejected, request.Reason, cancellationToken);
    }

    /// <summary>The Settings opening period under the read authority; answered whatever its state.</summary>
    public async Task<ReadResult<OpeningPeriodView>> GetPeriodAsync(FinanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadOutcomeFenceAsync(context, FinanceAction.ReadOpeningBalances, cancellationToken) is { } refused)
        {
            return new ReadResult<OpeningPeriodView>(refused, null);
        }

        var answer = Normalize(await openingPeriod.ReadAsync(context.CompanyId, cancellationToken));
        if (await ReadOutcomeFenceAsync(context, FinanceAction.ReadOpeningBalances, cancellationToken) is { } fenced)
        {
            return new ReadResult<OpeningPeriodView>(fenced, null);
        }

        return new ReadResult<OpeningPeriodView>(ReadOutcome.Found,
            new OpeningPeriodView(context.CompanyId, answer.State, answer.Version));
    }

    /// <summary>
    /// FNS-D23: the company's opening balances, only while the period is open (otherwise NotFound: the screen is not
    /// shown). Newest proposal first.
    /// </summary>
    public async Task<ReadResult<OpeningBalanceListPage>> ListAsync(FinanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadOutcomeFenceAsync(context, FinanceAction.ReadOpeningBalances, cancellationToken) is { } refused)
        {
            return new ReadResult<OpeningBalanceListPage>(refused, null);
        }

        var answer = Normalize(await openingPeriod.ReadAsync(context.CompanyId, cancellationToken));
        if (await ReadOutcomeFenceAsync(context, FinanceAction.ReadOpeningBalances, cancellationToken) is { } fenced)
        {
            return new ReadResult<OpeningBalanceListPage>(fenced, null);
        }

        if (answer.State != OpeningPeriodState.Open)
        {
            return new ReadResult<OpeningBalanceListPage>(
                answer.State == OpeningPeriodState.Closed ? ReadOutcome.NotFound : ReadOutcome.Unavailable, null);
        }

        var rows = await dbContext.OpeningBalances.AsNoTracking()
            .OrderByDescending(row => row.ProposedAtUtc).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var reversals = await dbContext.OpeningBalanceReversals.AsNoTracking()
            .ToListAsync(cancellationToken);
        if (await ReadOutcomeFenceAsync(context, FinanceAction.ReadOpeningBalances, cancellationToken) is { } again)
        {
            return new ReadResult<OpeningBalanceListPage>(again, null);
        }

        // Each opening shows its latest reversal (a pending or approved one, else the newest rejected one).
        var latest = reversals
            .GroupBy(reversal => reversal.OpeningBalanceId)
            .ToDictionary(group => group.Key, group => group
                .OrderByDescending(reversal => reversal.Status is Proposed or Approved)
                .ThenByDescending(reversal => reversal.ProposedAtUtc).ThenBy(reversal => reversal.Id)
                .First());
        return new ReadResult<OpeningBalanceListPage>(ReadOutcome.Found,
            new OpeningBalanceListPage(context.CompanyId, [.. rows.Select(row => View(row, context.ActorId,
                latest.TryGetValue(row.Id, out var reversal) ? reversal : null))]));
    }

    /// <summary>Settings' money currency candidates under the propose authority, passed through as given.</summary>
    public async Task<ReadResult<OpeningBalanceCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ProposeOpeningBalance;
        if (await ReadOutcomeFenceAsync(context, action, cancellationToken) is { } refused)
        {
            return new ReadResult<OpeningBalanceCurrencyCandidates>(refused, null);
        }

        IReadOnlyList<MoneyCurrencyCandidateV1> candidates;
        try
        {
            candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(
                await currencies.GetMoneyCurrencyCandidatesAsync(cancellationToken));
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await ReadOutcomeFenceAsync(context, action, cancellationToken) is { } fenced)
            {
                return new ReadResult<OpeningBalanceCurrencyCandidates>(fenced, null);
            }

            return new ReadResult<OpeningBalanceCurrencyCandidates>(
                exception.Code == "settings_access_denied" ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }

        if (await ReadOutcomeFenceAsync(context, action, cancellationToken) is { } afterSettings)
        {
            return new ReadResult<OpeningBalanceCurrencyCandidates>(afterSettings, null);
        }

        return new ReadResult<OpeningBalanceCurrencyCandidates>(ReadOutcome.Found, new OpeningBalanceCurrencyCandidates(
            context.CompanyId,
            [.. candidates.Select(candidate => new OpeningBalanceCurrencyCandidate(candidate.CurrencyId, candidate.Code,
                candidate.DisplayLabel))]));
    }

    /// <summary>The canonical content of a command: its name and the exact contract JSON of the request.</summary>
    internal static string PayloadHash<T>(string command, T request) =>
        FinancePartyCreditLimitService.PayloadHash(command, request);

    /// <summary>
    /// A Settings answer Finance can act on: Open or Closed with a canonical version that fits; anything else is
    /// Unavailable (fail closed).
    /// </summary>
    internal static FinanceOpeningPeriodAnswer Normalize(FinanceOpeningPeriodAnswer? answer) =>
        answer is { State: OpeningPeriodState.Open or OpeningPeriodState.Closed, Version: { } version }
        && IsCanonical(version) && version.Length <= PeriodVersionMaximumLength
            ? answer
            : FinanceOpeningPeriodAnswer.NotAvailable;

    /// <summary>The decision: the period still open, the proposal at its version, not the caller's own, then one save.</summary>
    private async Task<OpeningBalanceCommandResult> DecideAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, Guid openingBalanceId, string expectedVersion,
        int status, string? reason, string eventType, string operation, string auditReason,
        CancellationToken cancellationToken)
    {
        var (period, periodRefused) = await PeriodAsync(context, action, cancellationToken);
        if (periodRefused is not null)
        {
            return periodRefused;
        }

        var row = await dbContext.OpeningBalances
            .SingleOrDefaultAsync(row => row.Id == openingBalanceId, cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } fenced)
        {
            dbContext.ChangeTracker.Clear();
            return fenced;
        }

        if (row is null)
        {
            return await TerminalAsync(context, metadata, action, command, payloadHash, targetId: null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("openingBalanceId", "opening_balance_not_found")),
                cancellationToken);
        }

        if (!string.Equals(VersionText(row.Version), expectedVersion, StringComparison.Ordinal))
        {
            return await TerminalAsync(context, metadata, action, command, payloadHash, row.Id,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                cancellationToken);
        }

        if (row.Status != Proposed)
        {
            dbContext.ChangeTracker.Clear();
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("openingBalanceId", "opening_balance_not_proposed"));
        }

        // FNS-D23 maker≠approver: a person other than the one who proposed it.
        if (string.Equals(row.ProposedBy, context.ActorId, StringComparison.Ordinal))
        {
            dbContext.ChangeTracker.Clear();
            return Refused(context, CommandOutcome.Denied, Issue("approval", "finance_approver_is_proposer"));
        }

        // FNS-D86: the period setting changed since the proposal (closed and opened again): decided by neither side.
        if (!string.Equals(row.OpeningPeriodVersion, period!.Version, StringComparison.Ordinal))
        {
            dbContext.ChangeTracker.Clear();
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("openingPeriod", "opening_period_version_changed"));
        }

        if (status == Approved && await CarriedOpeningRefusalAsync(context, action, row.AsOfDate, cancellationToken) is { } carried)
            return carried;

        var now = timeProvider.GetUtcNow();
        var versionBefore = row.Version;
        var before = BalanceAudit(row);
        row.Status = status;
        row.DecidedBy = context.ActorId;
        row.DecidedAtUtc = now;
        row.RejectionReason = reason;
        row.UpdatedAtUtc = now;
        row.Version = checked(row.Version + 1);
        if (status == Approved)
        {
            // FNS-D84: the one opening movement, in the same save as the approval.
            dbContext.OpeningBalanceMovements.Add(Movement(row, OpeningMovement, context.ActorId, now));
        }

        var result = Succeeded(context, row);
        AddCommand(context, metadata, command, payloadHash, row.Id, result, now);
        // The decider is named for what they did: a rejection is not recorded as an approval.
        object decision = status == Rejected
            ? new { rule = "FNS-D23", proposedBy = row.ProposedBy, rejectedBy = context.ActorId,
                openingPeriodVersion = period.Version }
            : new { rule = "FNS-D23", proposedBy = row.ProposedBy, approvedBy = context.ActorId,
                openingPeriodVersion = period.Version, movement = "opening" };
        Audit(context, metadata, envelopes.Approve, row.Id, eventType, operation, versionBefore, row.Version, before,
            BalanceAudit(row), JsonSerializer.Serialize(decision, AuditJson),
            reason is null ? auditReason : auditReason + ": " + reason);
        return await SaveAsync(context, metadata, action, command, payloadHash, result, cancellationToken);
    }

    /// <summary>
    /// FNS-D42: an opening movement cannot change a balance already captured by an active period carry.
    /// Both approval paths run under FinanceTreasuryTransaction's SERIALIZABLE transaction, so this
    /// company-filtered range read and the movement write serialize with the actual closure snapshot.
    /// Initial setup remains possible without inventing a requirement to have an Accounting period first.
    /// </summary>
    private async Task<OpeningBalanceCommandResult?> CarriedOpeningRefusalAsync(FinanceContext context,
        FinanceAction action, DateOnly asOfDate, CancellationToken cancellationToken)
    {
        var captured = await dbContext.PeriodCarryVouchers.AsNoTracking()
            .AnyAsync(voucher => voucher.SourceEndsOn >= asOfDate
                && !dbContext.PeriodCarryReversals.Any(reversal => reversal.VoucherId == voucher.Id), cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } fenced)
        {
            dbContext.ChangeTracker.Clear(); return fenced;
        }
        if (!captured) return null;
        dbContext.ChangeTracker.Clear();
        return Refused(context, CommandOutcome.ValidationFailed, Issue("asOfDate", "opening_balance_period_carried"));
    }

    /// <summary>
    /// FNS-D85: the decision on the opening's pending reversal — the period still open at the reversal's version, the
    /// reversal at its expected version, not the caller's own; an approval marks the opening reversed and writes the one
    /// reversal movement. One save.
    /// </summary>
    private async Task<OpeningBalanceCommandResult> DecideReversalAsync(FinanceContext context,
        CommandMetadata metadata, FinanceAction action, string command, string payloadHash, Guid openingBalanceId,
        string expectedVersion, int status, string? reason, CancellationToken cancellationToken)
    {
        var (period, periodRefused) = await PeriodAsync(context, action, cancellationToken);
        if (periodRefused is not null)
        {
            return periodRefused;
        }

        var row = await dbContext.OpeningBalances
            .SingleOrDefaultAsync(row => row.Id == openingBalanceId, cancellationToken);
        var reversal = row is null
            ? null
            : await dbContext.OpeningBalanceReversals
                .SingleOrDefaultAsync(reversal => reversal.OpeningBalanceId == row.Id && reversal.Status == Proposed,
                    cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } fenced)
        {
            dbContext.ChangeTracker.Clear();
            return fenced;
        }

        if (row is null)
        {
            return await TerminalAsync(context, metadata, action, command, payloadHash, targetId: null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("openingBalanceId", "opening_balance_not_found")),
                cancellationToken);
        }

        if (reversal is null || row.Status != Approved)
        {
            dbContext.ChangeTracker.Clear();
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("openingBalanceId", "opening_balance_reversal_not_proposed"));
        }

        if (!string.Equals(VersionText(reversal.Version), expectedVersion, StringComparison.Ordinal))
        {
            return await TerminalAsync(context, metadata, action, command, payloadHash, row.Id,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                cancellationToken);
        }

        if (string.Equals(reversal.ProposedBy, context.ActorId, StringComparison.Ordinal))
        {
            dbContext.ChangeTracker.Clear();
            return Refused(context, CommandOutcome.Denied, Issue("approval", "finance_approver_is_proposer"));
        }

        if (!string.Equals(reversal.OpeningPeriodVersion, period!.Version, StringComparison.Ordinal))
        {
            dbContext.ChangeTracker.Clear();
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("openingPeriod", "opening_period_version_changed"));
        }

        if (status == Approved && await CarriedOpeningRefusalAsync(context, action, row.AsOfDate, cancellationToken) is { } carried)
            return carried;

        if (status == Approved && row.TargetKind is CashAccountKind or BankAccountKind)
        {
            TreasuryBalanceAnswer? balance;
            try
            {
                balance = await (balanceGuard ?? new UnavailableTreasuryBalanceGuard()).CheckOutgoingAsync(context,
                    row.TargetKind == BankAccountKind ? ExpensePaymentSourceKind.Bank : ExpensePaymentSourceKind.Cash,
                    row.TargetId, new MoneyText(row.AmountText, row.CurrencyId), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException) { balance = null; }
            if (await FenceAsync(context, action, cancellationToken) is { } afterBalance) return afterBalance;
            if (balance is null || balance.Outcome == TreasuryBalanceOutcome.Unavailable)
                return Refused(context, CommandOutcome.Unavailable,
                    Issue("treasuryAccountId", TreasuryBalanceAnswer.UnavailableCode));
            if (balance.Outcome != TreasuryBalanceOutcome.Sufficient)
                return Refused(context, CommandOutcome.ValidationFailed,
                    Issue("amount", TreasuryBalanceAnswer.InsufficientCode));
        }

        var now = timeProvider.GetUtcNow();
        var openingVersionBefore = row.Version;
        var before = ReversalAudit(reversal);
        reversal.Status = status;
        reversal.DecidedBy = context.ActorId;
        reversal.DecidedAtUtc = now;
        reversal.RejectionReason = reason;
        reversal.UpdatedAtUtc = now;
        reversal.Version = checked(reversal.Version + 1);
        if (status == Approved)
        {
            row.Status = Reversed;
            row.UpdatedAtUtc = now;
            row.Version = checked(row.Version + 1);
            dbContext.OpeningBalanceMovements.Add(Movement(row, ReversalMovement, context.ActorId, now));
        }

        var result = Succeeded(context, row, reversal);
        AddCommand(context, metadata, command, payloadHash, row.Id, result, now);
        object decision = status == Rejected
            ? new { rule = "FNS-D85", proposedBy = reversal.ProposedBy, rejectedBy = context.ActorId,
                openingPeriodVersion = period.Version }
            : new { rule = "FNS-D85", proposedBy = reversal.ProposedBy, approvedBy = context.ActorId,
                openingPeriodVersion = period.Version, movement = "reversal" };
        Audit(context, metadata, envelopes.Approve, row.Id,
            status == Rejected
                ? "finance.opening-balance.reversal-rejected.v1"
                : "finance.opening-balance.reversal-approved.v1",
            status == Rejected ? "RejectReversal" : "ApproveReversal", openingVersionBefore, row.Version, before,
            ReversalAudit(reversal), JsonSerializer.Serialize(decision, AuditJson),
            status == Rejected
                ? "Finans açılış bakiyesi ters kaydı reddi: " + reason
                : "Finans açılış bakiyesi ters kaydı onayı (ters hareket yazıldı; yeni öneri girilebilir)");
        return await SaveAsync(context, metadata, action, command, payloadHash, result, cancellationToken);
    }

    /// <summary>
    /// The one movement of an opening (FNS-D84) or of its reversal (FNS-D85): the opening's target, currency, direction
    /// and exact amount on its AsOfDate; the kind carries the side.
    /// </summary>
    private static OpeningBalanceMovementRow Movement(OpeningBalanceRow row, int kind, string actorId,
        DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        CompanyId = row.CompanyId,
        OpeningBalanceId = row.Id,
        Kind = kind,
        TargetKind = row.TargetKind,
        TargetId = row.TargetId,
        PartyId = row.PartyId,
        CurrencyId = row.CurrencyId,
        Direction = row.Direction,
        AmountText = row.AmountText,
        OccurredOn = row.AsOfDate,
        RecordedBy = actorId,
        RecordedAtUtc = now,
    };

    private static OpeningBalanceCommandResult? ReasonRefusal(FinanceContext context, string reason)
    {
        if (!IsCanonical(reason))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("reason", "reason_invalid"));
        }

        return reason.Length > ReasonMaximumLength
            ? Refused(context, CommandOutcome.ValidationFailed, Issue("reason", "finance_value_does_not_fit"))
            : null;
    }

    /// <summary>FNS-D23: the Settings opening period, fenced again after it; only Open lets a command through.</summary>
    private async Task<(FinanceOpeningPeriodAnswer? Answer, OpeningBalanceCommandResult? Refusal)> PeriodAsync(
        FinanceContext context, FinanceAction action, CancellationToken cancellationToken)
    {
        var answer = Normalize(await openingPeriod.ReadAsync(context.CompanyId, cancellationToken));
        if (await FenceAsync(context, action, cancellationToken) is { } fenced)
        {
            return (null, fenced);
        }

        return answer.State switch
        {
            OpeningPeriodState.Open => (answer, null),
            OpeningPeriodState.Closed => (null, Refused(context, CommandOutcome.ValidationFailed,
                Issue("openingPeriod", "opening_period_closed"))),
            _ => (null, Refused(context, CommandOutcome.Unavailable,
                Issue("openingPeriod", "opening_period_unavailable"))),
        };
    }

    /// <summary>The request's shape: identities, the kind's party/direction pairing and the exact positive amount.</summary>
    private static OpeningBalanceCommandResult? PayloadRefusal(FinanceContext context,
        OpeningBalanceProposeRequest request)
    {
        if (!Enum.IsDefined(request.TargetKind))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("targetKind", "opening_target_kind_invalid"));
        }

        if (request.TargetId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("targetId", "identity_required"));
        }

        if (request.CurrencyId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("currencyId", "identity_required"));
        }

        if (request.TargetKind == OpeningBalanceTargetKind.PartyAccount)
        {
            if (request.PartyId is not { } partyId || partyId == Guid.Empty)
            {
                return Refused(context, CommandOutcome.ValidationFailed, Issue("partyId", "identity_required"));
            }

            if (request.Direction is not { } direction || !Enum.IsDefined(direction))
            {
                return Refused(context, CommandOutcome.ValidationFailed, Issue("direction", "opening_direction_required"));
            }
        }
        else if (request.PartyId is not null || request.Direction is not null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("targetKind", "opening_target_kind_invalid"));
        }

        return FinancePartyCreditLimitService.IsExactAmount(request.Amount)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("amount", "opening_amount_invalid"));
    }

    /// <summary>The target: an approved cash/bank account in the currency, or an active party account and a candidate currency.</summary>
    private async Task<OpeningBalanceCommandResult?> TargetRefusalAsync(FinanceContext context, FinanceAction action,
        OpeningBalanceProposeRequest request, CancellationToken cancellationToken)
    {
        switch (request.TargetKind)
        {
            case OpeningBalanceTargetKind.CashAccount:
            {
                var account = await dbContext.CashAccounts.AsNoTracking()
                    .Where(row => row.Id == request.TargetId)
                    .Select(row => new { row.Status, row.CurrencyId })
                    .SingleOrDefaultAsync(cancellationToken);
                if (await FenceAsync(context, action, cancellationToken) is { } fenced)
                {
                    return fenced;
                }

                return AccountRefusal(context, account?.Status, account?.CurrencyId, request.CurrencyId);
            }

            case OpeningBalanceTargetKind.BankAccount:
            {
                var account = await dbContext.BankAccounts.AsNoTracking()
                    .Where(row => row.Id == request.TargetId)
                    .Select(row => new { row.Status, row.CurrencyId })
                    .SingleOrDefaultAsync(cancellationToken);
                if (await FenceAsync(context, action, cancellationToken) is { } fenced)
                {
                    return fenced;
                }

                return AccountRefusal(context, account?.Status, account?.CurrencyId, request.CurrencyId);
            }

            default:
                if (await CurrencyRefusalAsync(context, action, request.CurrencyId, cancellationToken) is { } currency)
                {
                    return currency;
                }

                return await PartyRefusalAsync(context, action, request.PartyId!.Value, request.TargetId,
                    cancellationToken);
        }
    }

    private static OpeningBalanceCommandResult? AccountRefusal(FinanceContext context, int? status, Guid? currencyId,
        Guid requestedCurrencyId)
    {
        if (status != AccountApproved)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("targetId", "opening_target_not_eligible"));
        }

        return currencyId == requestedCurrencyId
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("currencyId", "opening_currency_mismatch"));
    }

    /// <summary>The (party, company account) pair through Counterparty's port, fenced again after it.</summary>
    private async Task<OpeningBalanceCommandResult?> PartyRefusalAsync(FinanceContext context, FinanceAction action,
        Guid partyId, Guid companyAccountId, CancellationToken cancellationToken)
    {
        var party = await parties.GetAuthorizedAsync(context, partyId, companyAccountId, cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        if (party.Outcome != ReadOutcome.Found || party.Resource is not { } resource)
        {
            return party.Outcome switch
            {
                ReadOutcome.Denied => Refused(context, CommandOutcome.Denied),
                ReadOutcome.NotFound => Refused(context, CommandOutcome.ValidationFailed,
                    Issue("targetId", "party_account_ineligible")),
                _ => Refused(context, CommandOutcome.Unavailable, Issue("targetId", "party_account_unavailable")),
            };
        }

        return resource.CompanyId != context.CompanyId || resource.PartyId != partyId
            || resource.CompanyAccountId != companyAccountId || !resource.IsActive || !resource.IsPartyActive
            ? Refused(context, CommandOutcome.ValidationFailed, Issue("targetId", "party_account_ineligible"))
            : null;
    }

    /// <summary>The currency must be one of Settings' candidates now; an unavailable source refuses.</summary>
    private async Task<OpeningBalanceCommandResult?> CurrencyRefusalAsync(FinanceContext context, FinanceAction action,
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
            if (await FenceAsync(context, action, cancellationToken) is { } fenced)
            {
                return fenced;
            }

            return exception.Code == "settings_access_denied"
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable, Issue("currencyId", exception.Code));
        }

        if (await FenceAsync(context, action, cancellationToken) is { } afterSettings)
        {
            return afterSettings;
        }

        return candidates.Any(candidate => candidate.CurrencyId == currencyId)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("currencyId", "currency_not_a_candidate"));
    }

    private static OpeningBalanceCommandResult? IdentityRefusal(FinanceContext context, Guid openingBalanceId,
        string expectedVersion)
    {
        // Malformed identities are refused after the replay lookup (never stored, so never a replay).
        if (openingBalanceId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("openingBalanceId", "identity_required"));
        }

        return IsCanonical(expectedVersion)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
    }

    /// <summary>Context, the action's fresh authority, and the key and context storage limits.</summary>
    private async Task<OpeningBalanceCommandResult?> StartAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, action, cancellationToken) is { } denied)
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
    /// ADR-011: the same (command, key hash) again answers its stored result as a replay with its first correlation;
    /// another payload or actor is a conflict that discloses nothing. The action is measured afresh before the replay.
    /// </summary>
    private async Task<OpeningBalanceCommandResult?> PriorAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, CancellationToken cancellationToken)
    {
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var prior = await dbContext.OpeningBalanceCommands.AsNoTracking()
            .SingleOrDefaultAsync(row => row.CommandName == command && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (prior is null)
        {
            return null;
        }

        if (prior.PayloadHash != payloadHash || !string.Equals(prior.ActorId, context.ActorId, StringComparison.Ordinal))
        {
            return Refused(context, CommandOutcome.IdempotencyConflict);
        }

        if (await FenceAsync(context, action, cancellationToken) is { } fenced)
        {
            return fenced;
        }

        var stored = JsonSerializer.Deserialize<OpeningBalanceCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı komut sonucu okunamadı.");
        return stored with { IsReplay = true };
    }

    /// <summary>Company/actor still the request's, then a fresh decision on the action.</summary>
    private async Task<OpeningBalanceCommandResult?> FenceAsync(FinanceContext context, FinanceAction action,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return Refused(context, CommandOutcome.Denied);
        }

        var decision = await permissions.CheckAsync(context, action, null, cancellationToken);
        if (decision != PermissionDecision.Allowed)
        {
            return decision == PermissionDecision.Denied
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable, Issue("permission", "finance_permission_mapping_unavailable"));
        }

        return SameContext(context) ? null : Refused(context, CommandOutcome.Denied);
    }

    private async Task<ReadOutcome?> ReadOutcomeFenceAsync(FinanceContext context, FinanceAction action,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context))
        {
            return ReadOutcome.Denied;
        }

        var decision = await permissions.CheckAsync(context, action, null, cancellationToken);
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
        Guid? targetId, OpeningBalanceCommandResult result, DateTimeOffset now) =>
        dbContext.OpeningBalanceCommands.Add(new OpeningBalanceCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            OpeningBalanceId = targetId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    /// <summary>
    /// One save, fenced again immediately before it. A lost race (the live slot or the row version) clears the tracker and
    /// reads the key afresh: a same-key winner is this answer too (ADR-011 §8); anything else is a
    /// <c>concurrent_change</c>.
    /// </summary>
    private async Task<OpeningBalanceCommandResult> SaveAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, OpeningBalanceCommandResult result,
        CancellationToken cancellationToken)
    {
        if (await FenceAsync(context, action, cancellationToken) is { } fenced)
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
            if (FinanceTreasuryTransaction.RequiresFullRollback(dbContext)) throw;
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
    private Task<OpeningBalanceCommandResult> TerminalAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, Guid? targetId, OpeningBalanceCommandResult refusal,
        CancellationToken cancellationToken)
    {
        // Nothing read for the decision may be saved with the refusal.
        dbContext.ChangeTracker.Clear();
        AddCommand(context, metadata, command, payloadHash, targetId, refusal, timeProvider.GetUtcNow());
        return SaveAsync(context, metadata, action, command, payloadHash, refusal, cancellationToken);
    }

    private void Audit(FinanceContext context, CommandMetadata metadata, FinanceAuditEnvelope envelope, Guid recordId,
        string eventType, string operation, long? versionBefore, long? versionAfter, string before, string after,
        string approval, string reason) =>
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
            approval,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
            context.CorrelationId,
            context.CausationId,
            metadata.IdempotencyKey));

    private static string BalanceAudit(OpeningBalanceRow row) => JsonSerializer.Serialize(new
    {
        row.Id,
        version = VersionText(row.Version),
        targetKind = KindOf(row.TargetKind).ToString(),
        row.TargetId,
        row.PartyId,
        row.CurrencyId,
        direction = row.Direction is { } direction ? DirectionOf(direction).ToString() : null,
        amount = row.AmountText,
        row.AsOfDate,
        status = StatusOf(row.Status).ToString(),
        row.OpeningPeriodVersion,
        row.ProposedBy,
        row.ProposedAtUtc,
        row.DecidedBy,
        row.DecidedAtUtc,
        row.RejectionReason,
    }, AuditJson);

    private static string ReversalAudit(OpeningBalanceReversalRow row) => JsonSerializer.Serialize(new
    {
        row.Id,
        row.OpeningBalanceId,
        version = VersionText(row.Version),
        status = ReversalStatusOf(row.Status).ToString(),
        row.Reason,
        row.OpeningPeriodVersion,
        row.ProposedBy,
        row.ProposedAtUtc,
        row.DecidedBy,
        row.DecidedAtUtc,
        row.RejectionReason,
    }, AuditJson);

    private static OpeningBalanceView View(OpeningBalanceRow row, string actorId,
        OpeningBalanceReversalRow? reversal = null) =>
        new(row.Id, row.CompanyId, KindOf(row.TargetKind), row.TargetId, row.PartyId, row.CurrencyId,
            row.Direction is { } direction ? DirectionOf(direction) : null, row.AmountText, row.AsOfDate,
            StatusOf(row.Status), row.OpeningPeriodVersion,
            string.Equals(row.ProposedBy, actorId, StringComparison.Ordinal), row.ProposedAtUtc, row.DecidedAtUtc,
            row.RejectionReason, VersionText(row.Version),
            reversal is null ? null : new OpeningBalanceReversalView(reversal.Id, ReversalStatusOf(reversal.Status),
                reversal.Reason, reversal.OpeningPeriodVersion,
                string.Equals(reversal.ProposedBy, actorId, StringComparison.Ordinal), reversal.ProposedAtUtc,
                reversal.DecidedAtUtc, reversal.RejectionReason, VersionText(reversal.Version)));

    internal static int KindCode(OpeningBalanceTargetKind kind) => kind switch
    {
        OpeningBalanceTargetKind.CashAccount => CashAccountKind,
        OpeningBalanceTargetKind.BankAccount => BankAccountKind,
        OpeningBalanceTargetKind.PartyAccount => PartyAccountKind,
        _ => throw new InvalidOperationException("Açılış bakiyesi hedef türü tanınmıyor."),
    };

    private static OpeningBalanceTargetKind KindOf(int kind) => kind switch
    {
        CashAccountKind => OpeningBalanceTargetKind.CashAccount,
        BankAccountKind => OpeningBalanceTargetKind.BankAccount,
        PartyAccountKind => OpeningBalanceTargetKind.PartyAccount,
        _ => throw new InvalidOperationException("Açılış bakiyesi hedef türü tanınmıyor."),
    };

    internal static int DirectionCode(OpeningBalanceDirection direction) => direction switch
    {
        OpeningBalanceDirection.Debit => DebitDirection,
        OpeningBalanceDirection.Credit => CreditDirection,
        _ => throw new InvalidOperationException("Açılış bakiyesi yönü tanınmıyor."),
    };

    private static OpeningBalanceDirection DirectionOf(int direction) => direction switch
    {
        DebitDirection => OpeningBalanceDirection.Debit,
        CreditDirection => OpeningBalanceDirection.Credit,
        _ => throw new InvalidOperationException("Açılış bakiyesi yönü tanınmıyor."),
    };

    private static OpeningBalanceStatus StatusOf(int status) => status switch
    {
        Proposed => OpeningBalanceStatus.Proposed,
        Approved => OpeningBalanceStatus.Approved,
        Rejected => OpeningBalanceStatus.Rejected,
        Reversed => OpeningBalanceStatus.Reversed,
        _ => throw new InvalidOperationException("Açılış bakiyesi durumu tanınmıyor."),
    };

    private static OpeningBalanceReversalStatus ReversalStatusOf(int status) => status switch
    {
        Proposed => OpeningBalanceReversalStatus.Proposed,
        Approved => OpeningBalanceReversalStatus.Approved,
        Rejected => OpeningBalanceReversalStatus.Rejected,
        _ => throw new InvalidOperationException("Açılış bakiyesi ters kayıt durumu tanınmıyor."),
    };

    private static string VersionText(long version) => FinancePartyCreditLimitService.VersionText(version);

    private static OpeningBalanceCommandResult Succeeded(FinanceContext context, OpeningBalanceRow row,
        OpeningBalanceReversalRow? reversal = null) =>
        new(CommandOutcome.Succeeded, View(row, context.ActorId, reversal), IsReplay: false, context.CorrelationId, []);

    private static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static ValidationIssue Issue(string field, string code) => new(field, code);

    private static OpeningBalanceCommandResult Refused(FinanceContext context, CommandOutcome outcome,
        params ValidationIssue[] issues) =>
        new(outcome, null, IsReplay: false, context.CorrelationId, issues);
}

/// <summary>
/// The ADR-009 envelopes of the opening balance writes: the roles that granted <c>finance.opening-balances.propose</c>
/// and <c>finance.opening-balances.approve</c>.
/// </summary>
public sealed record FinanceOpeningBalanceEnvelopes(FinanceAuditEnvelope Propose, FinanceAuditEnvelope Approve);
