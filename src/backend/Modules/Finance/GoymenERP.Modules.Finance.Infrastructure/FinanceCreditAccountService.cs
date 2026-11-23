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
/// FIN-SC47 (FNS-D87, FNS-D88, FNS-D93, FNS-D94): the credit account skeleton with its imported bank repayment plan.
/// <list type="bullet">
/// <item><b>Group (FNS-D87):</b> cash, non-cash, instrument/factoring or leasing with the source-backed profile and the
/// lender's actual Counterparty identities. No lender master, role, or zero-filled source amount is invented.</item>
/// <item><b>Plan (FNS-D93/FNS-D94):</b> the bank's lines are stored as given; <see cref="FinanceCreditPlanCalculator"/>
/// checks source-included charges and installment sums with the currency profile's smallest-unit tolerance; the principal total
/// equals the credit amount exactly. A failing plan
/// is refused (not stored, so it never reaches approval); the approval checks the stored plan again. Interest is never
/// calculated.</item>
/// <item><b>Two people (FNS-D88):</b> a holder of <c>finance.credits.propose</c> proposes; a holder of
/// <c>finance.credits.approve</c> other than the proposer approves or rejects (here, in the write guard and in
/// <c>CK_CreditAccounts_Decision</c>). The approval writes no movement and no voucher.</item>
/// <item><b>Currency:</b> one of Settings' money currency candidates now; an unavailable source refuses.</item>
/// <item><b>Authority, idempotency (ADR-011) and audit (ADR-009):</b> as the opening balance.</item>
/// </list>
/// Contract approval is distinct from actual use, repayment, accrual and the resulting accounting proposal.
/// </summary>
public sealed partial class FinanceCreditAccountService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    ISettingsMoneyCandidateReaderV2 currencies,
    IAuditEventCollector auditEvents,
    FinanceCreditAccountEnvelopes envelopes,
    TimeProvider timeProvider,
    ISettingsPrerequisiteProvider? settings = null,
    IFinanceCreditLenderPortV1? lenders = null) : IFinanceCreditAccountService
{
    public const string ProposeCommand = "finance.credit-account.propose";
    public const string ApproveCommand = "finance.credit-account.approve";
    public const string RejectCommand = "finance.credit-account.reject";
    private const string EntityType = "Finance.CreditAccount";
    internal const int CashGroup = 1;
    internal const int NonCashGroup = 2;
    internal const int InstrumentAndFactoringGroup = 3;
    internal const int LeasingGroup = 4;
    internal const int Proposed = 1;
    internal const int Approved = 2;
    internal const int Rejected = 3;
    internal const int LenderNameMaximumLength = 200;
    internal const int ContractReferenceMaximumLength = 100;
    private const int IdentityMaximumLength = 128;
    private const int ReasonMaximumLength = 500;

    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public async Task<CreditAccountCommandResult> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        CreditAccountProposeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ProposeCredit;
        if (await StartAsync(context, metadata, action, cancellationToken) is { } refused)
        {
            return refused;
        }

        // The strict string-enum contract cannot serialize an undefined group. Refuse it after
        // authority validation and before hashing; valid requests keep their existing hash.
        if (!Enum.IsDefined(request.Group))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("group", "credit_group_invalid"));
        }

        // A null text or a null plan cannot be the contract's JSON; refused before it is hashed.
        if (request.LenderName is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("lenderName", "credit_lender_invalid"));
        }

        if (request.ContractReference is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("contractReference", "credit_contract_reference_invalid"));
        }

        if (request.PrincipalAmount is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("principalAmount", FinanceCreditPlanCalculator.PrincipalAmountInvalid));
        }

        if (request.Installments is null)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("installments", FinanceCreditPlanCalculator.PlanEmpty));
        }

        if (!FinanceCreditAccountProfilePolicy.DefinedEnums(request))
            return Refused(context, CommandOutcome.ValidationFailed, Issue("profile", "credit_source_profile_invalid"));

        var payloadHash = PayloadHash(ProposeCommand, request);
        if (await PriorAsync(context, metadata, action, ProposeCommand, payloadHash, cancellationToken) is { } prior)
        {
            return prior;
        }

        if (PayloadRefusal(context, request) is { } invalid)
        {
            return invalid;
        }

        if (await CurrencyRefusalAsync(context, action, request.CurrencyId, cancellationToken) is { } currencyRefused)
        {
            return currencyRefused;
        }

        var source = await SourcesAsync(context, action, request, cancellationToken);
        if (source.Refusal is not null) return source.Refusal;
        var moneyProfile = source.MoneyProfile!;
        if (request.Profile!.PlanNature == CreditPlanNatureV1.Installments)
        {
            var plan = FinanceCreditPlanCalculator.Validate(request.PrincipalAmount, request.Installments, request.CurrencyId);
            if (!plan.IsValid) return Refused(context, CommandOutcome.ValidationFailed, [.. plan.Issues]);
        }

        var live = await dbContext.CreditAccounts.AsNoTracking()
            .AnyAsync(row => row.LenderPartyId == request.Profile.LenderPartyId
                && row.LenderCompanyPartyAccountId == request.Profile.LenderCompanyPartyAccountId
                && row.ContractReference == request.ContractReference
                && (row.Status == Proposed || row.Status == Approved), cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } afterRead)
        {
            return afterRead;
        }

        if (live)
        {
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("contractReference", "credit_account_exists"));
        }

        var now = timeProvider.GetUtcNow();
        var row = new CreditAccountRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            CreditGroup = GroupCode(request.Group),
            LenderName = request.LenderName,
            ContractReference = request.ContractReference,
            CurrencyId = request.CurrencyId,
            PrincipalAmountText = request.PrincipalAmount,
            StartDate = request.StartDate,
            LenderPartyId = request.Profile.LenderPartyId,
            LenderCompanyPartyAccountId = request.Profile.LenderCompanyPartyAccountId,
            LenderPartyVersion = source.Lender!.PartyVersion,
            LenderCompanyAccountVersion = source.Lender!.CompanyAccountVersion,
            SourceProfileJson = JsonSerializer.Serialize(request.Profile, Json),
            MoneyProfileId = moneyProfile.Profile.ProfileId,
            MoneyProfileVersion = moneyProfile.Profile.Version,
            CurrencyScale = moneyProfile.Scale,
            Status = Proposed,
            ProposedBy = context.ActorId,
            ProposedAtUtc = now,
            Version = 1,
            UpdatedAtUtc = now,
        };
        var lines = request.Installments.Select(line => new CreditAccountInstallmentRow
        {
            CompanyId = context.CompanyId,
            CreditAccountId = row.Id,
            Sequence = line.Sequence,
            DueDate = line.DueDate,
            InstallmentText = line.Installment,
            PrincipalText = line.Principal,
            InterestText = line.Interest,
            BsmvText = line.Bsmv,
            KkdfText = line.Kkdf,
            ChargesJson = line.Charges is null ? null : JsonSerializer.Serialize(line.Charges, Json),
        }).ToList();
        dbContext.CreditAccounts.Add(row);
        dbContext.CreditAccountInstallments.AddRange(lines);
        var result = Succeeded(context, row, lines);
        AddCommand(context, metadata, ProposeCommand, payloadHash, row.Id, result, now);
        var approval = JsonSerializer.Serialize(new { rule = "FNS-D88", proposedBy = context.ActorId }, AuditJson);
        Audit(context, metadata, envelopes.Propose, row.Id, "finance.credit-account.proposed.v1", "Propose",
            versionBefore: null, row.Version, "{}", AccountAudit(row, lines), approval,
            "Finans kredi hesabı önerisi (kaynak belgesi ve plan türü kaydedildi; onay bekler)");
        return await SaveAsync(context, metadata, action, ProposeCommand, payloadHash, result, cancellationToken);
    }

    public async Task<CreditAccountCommandResult> ApproveAsync(FinanceContext context, CommandMetadata metadata,
        CreditAccountApproveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ApproveCredit;
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

        if (IdentityRefusal(context, request.CreditAccountId, request.ExpectedVersion) is { } malformed)
        {
            return malformed;
        }

        return await DecideAsync(context, metadata, action, ApproveCommand, payloadHash, request.CreditAccountId,
            request.ExpectedVersion, Approved, reason: null, "finance.credit-account.approved.v1", "Approve",
            "Finans kredi hesabı onayı (önerenden farklı kişi; ödeme planı yeniden doğrulandı; hareket ve fiş yok)",
            cancellationToken);
    }

    public async Task<CreditAccountCommandResult> RejectAsync(FinanceContext context, CommandMetadata metadata,
        CreditAccountRejectRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ApproveCredit;
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

        if (IdentityRefusal(context, request.CreditAccountId, request.ExpectedVersion) is { } malformed)
        {
            return malformed;
        }

        return await DecideAsync(context, metadata, action, RejectCommand, payloadHash, request.CreditAccountId,
            request.ExpectedVersion, Rejected, request.Reason, "finance.credit-account.rejected.v1", "Reject",
            "Finans kredi hesabı reddi (önerenden farklı kişi; hareket ve fiş yok)", cancellationToken);
    }

    /// <summary>The company's credit accounts with their plans, newest proposal first.</summary>
    public async Task<ReadResult<CreditAccountListPage>> ListAsync(FinanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ReadCredits;
        if (await ReadOutcomeFenceAsync(context, action, cancellationToken) is { } refused)
        {
            return new ReadResult<CreditAccountListPage>(refused, null);
        }

        var rows = await dbContext.CreditAccounts.AsNoTracking()
            .OrderByDescending(row => row.ProposedAtUtc).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var lines = await dbContext.CreditAccountInstallments.AsNoTracking()
            .ToListAsync(cancellationToken);
        if (await ReadOutcomeFenceAsync(context, action, cancellationToken) is { } fenced)
        {
            return new ReadResult<CreditAccountListPage>(fenced, null);
        }

        var plans = lines.GroupBy(line => line.CreditAccountId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<CreditAccountInstallmentRow>)group.ToList());
        return new ReadResult<CreditAccountListPage>(ReadOutcome.Found, new CreditAccountListPage(context.CompanyId,
            [.. rows.Select(row => View(row, plans.TryGetValue(row.Id, out var plan) ? plan : [], context.ActorId))]));
    }

    /// <summary>One credit account with its plan; another company's or an unknown one answers NotFound.</summary>
    public async Task<ReadResult<CreditAccountView>> GetAsync(FinanceContext context, Guid creditAccountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ReadCredits;
        if (await ReadOutcomeFenceAsync(context, action, cancellationToken) is { } refused)
        {
            return new ReadResult<CreditAccountView>(refused, null);
        }

        var row = creditAccountId == Guid.Empty
            ? null
            : await dbContext.CreditAccounts.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == creditAccountId, cancellationToken);
        var lines = row is null
            ? []
            : await dbContext.CreditAccountInstallments.AsNoTracking()
                .Where(line => line.CreditAccountId == row.Id).ToListAsync(cancellationToken);
        if (await ReadOutcomeFenceAsync(context, action, cancellationToken) is { } fenced)
        {
            return new ReadResult<CreditAccountView>(fenced, null);
        }

        return row is null
            ? new ReadResult<CreditAccountView>(ReadOutcome.NotFound, null)
            : new ReadResult<CreditAccountView>(ReadOutcome.Found, View(row, lines, context.ActorId));
    }

    /// <summary>FNS-D94: the plan check alone, under the propose authority; stores and audits nothing.</summary>
    public async Task<ReadResult<CreditPlanValidationView>> ValidatePlanAsync(FinanceContext context,
        CreditPlanValidationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (await ReadOutcomeFenceAsync(context, FinanceAction.ProposeCredit, cancellationToken) is { } refused)
        {
            return new ReadResult<CreditPlanValidationView>(refused, null);
        }

        if (request.CurrencyId is not { } currencyId || currencyId == Guid.Empty || request.EffectiveOn is not { } effectiveOn
            || effectiveOn == default)
            return new ReadResult<CreditPlanValidationView>(ReadOutcome.Found, new CreditPlanValidationView(context.CompanyId, false, null, [Issue("currencyId", "credit_plan_profile_required")]));
        var profileRead = await MoneyProfileAsync(context, FinanceAction.ProposeCredit, currencyId, effectiveOn, cancellationToken);
        if (profileRead.Refusal is { } failure)
            return new ReadResult<CreditPlanValidationView>(failure.Outcome == CommandOutcome.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        var verdict = FinanceCreditPlanCalculator.Validate(request.PrincipalAmount, request.Installments, currencyId);
        return new ReadResult<CreditPlanValidationView>(ReadOutcome.Found,
            new CreditPlanValidationView(context.CompanyId, verdict.IsValid, verdict.Totals, verdict.Issues,
                Snapshot(profileRead.Profile!)));
    }

    /// <summary>Settings' money currency candidates under the propose authority, passed through as given.</summary>
    public async Task<ReadResult<CreditAccountCurrencyCandidates>> CurrencyCandidatesAsync(FinanceContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ProposeCredit;
        if (await ReadOutcomeFenceAsync(context, action, cancellationToken) is { } refused)
        {
            return new ReadResult<CreditAccountCurrencyCandidates>(refused, null);
        }

        IReadOnlyList<MoneyCurrencyCandidateV1> candidates;
        try
        {
            candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(
                await currencies.GetMoneyCurrencyCandidatesAsync(PlanMoneyOperation, cancellationToken));
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await ReadOutcomeFenceAsync(context, action, cancellationToken) is { } fenced)
            {
                return new ReadResult<CreditAccountCurrencyCandidates>(fenced, null);
            }

            return new ReadResult<CreditAccountCurrencyCandidates>(
                exception.Code == "settings_access_denied" ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        }

        if (await ReadOutcomeFenceAsync(context, action, cancellationToken) is { } afterSettings)
        {
            return new ReadResult<CreditAccountCurrencyCandidates>(afterSettings, null);
        }

        return new ReadResult<CreditAccountCurrencyCandidates>(ReadOutcome.Found, new CreditAccountCurrencyCandidates(
            context.CompanyId,
            [.. candidates.Select(candidate => new CreditAccountCurrencyCandidate(candidate.CurrencyId, candidate.Code,
                candidate.DisplayLabel))]));
    }

    /// <summary>The canonical content of a command: its name and the exact contract JSON of the request.</summary>
    internal static string PayloadHash<T>(string command, T request) =>
        FinancePartyCreditLimitService.PayloadHash(command, request);

    /// <summary>
    /// The decision: the account at its version, still proposed, not the caller's own, an approval only over a stored plan
    /// that still holds; then one save.
    /// </summary>
    private async Task<CreditAccountCommandResult> DecideAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, Guid creditAccountId, string expectedVersion,
        int status, string? reason, string eventType, string operation, string auditReason,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.CreditAccounts
            .SingleOrDefaultAsync(candidate => candidate.Id == creditAccountId, cancellationToken);
        var lines = row is null
            ? []
            : await dbContext.CreditAccountInstallments.AsNoTracking()
                .Where(line => line.CreditAccountId == row.Id).ToListAsync(cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } fenced)
        {
            dbContext.ChangeTracker.Clear();
            return fenced;
        }

        if (row is null)
        {
            return await TerminalAsync(context, metadata, action, command, payloadHash, targetId: null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("creditAccountId", "credit_account_not_found")),
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
                Issue("creditAccountId", "credit_account_not_proposed"));
        }

        // FNS-D88 maker≠checker: a person other than the one who proposed it.
        if (string.Equals(row.ProposedBy, context.ActorId, StringComparison.Ordinal))
        {
            dbContext.ChangeTracker.Clear();
            return Refused(context, CommandOutcome.Denied, Issue("approval", "finance_approver_is_proposer"));
        }

        // FNS-D94: a plan that does not hold never reaches an approval (the stored lines are checked again).
        var storedProfile = Profile(row);
        if (status == Approved && storedProfile is not null)
        {
            var source = await SourcesAsync(context, action, new CreditAccountProposeRequest(GroupOf(row.CreditGroup), row.LenderName,
                row.ContractReference, row.CurrencyId, row.PrincipalAmountText, row.StartDate, Inputs(lines), storedProfile), cancellationToken);
            if (source.Refusal is not null) { dbContext.ChangeTracker.Clear(); return source.Refusal; }
            if (source.MoneyProfile!.Profile.ProfileId != row.MoneyProfileId
                || source.MoneyProfile.Profile.Version != row.MoneyProfileVersion || source.MoneyProfile.Scale != row.CurrencyScale)
            {
                dbContext.ChangeTracker.Clear();
                return Refused(context, CommandOutcome.VersionConflict, Issue("profile", "credit_plan_profile_changed"));
            }
        }
        var verdict = storedProfile?.PlanNature == CreditPlanNatureV1.SourceMovements ? null
            : FinanceCreditPlanCalculator.Validate(row.PrincipalAmountText, Inputs(lines), row.CurrencyId);
        if (status == Approved && verdict is { IsValid: false })
        {
            dbContext.ChangeTracker.Clear();
            return Refused(context, CommandOutcome.ValidationFailed, [.. verdict.Issues]);
        }

        var now = timeProvider.GetUtcNow();
        var versionBefore = row.Version;
        var before = AccountAudit(row, lines);
        row.Status = status;
        row.DecidedBy = context.ActorId;
        row.DecidedAtUtc = now;
        row.RejectionReason = reason;
        row.UpdatedAtUtc = now;
        row.Version = checked(row.Version + 1);
        var result = Succeeded(context, row, lines);
        AddCommand(context, metadata, command, payloadHash, row.Id, result, now);
        // The decider is named for what they did: a rejection is not recorded as an approval.
        object decision = status == Rejected
            ? new { rule = "FNS-D88", proposedBy = row.ProposedBy, rejectedBy = context.ActorId }
            : new { rule = "FNS-D88", proposedBy = row.ProposedBy, approvedBy = context.ActorId };
        Audit(context, metadata, envelopes.Approve, row.Id, eventType, operation, versionBefore, row.Version, before,
            AccountAudit(row, lines), JsonSerializer.Serialize(decision, AuditJson),
            reason is null ? auditReason : auditReason + ": " + reason);
        return await SaveAsync(context, metadata, action, command, payloadHash, result, cancellationToken);
    }

    /// <summary>The request's shape: group, lender, contract reference, currency identity and the FNS-D94 plan check.</summary>
    private static CreditAccountCommandResult? PayloadRefusal(FinanceContext context,
        CreditAccountProposeRequest request)
    {
        var issues = new List<ValidationIssue>();
        if (!Enum.IsDefined(request.Group))
        {
            issues.Add(Issue("group", "credit_group_invalid"));
        }

        if (!IsCanonical(request.LenderName) || request.LenderName.Length > LenderNameMaximumLength)
        {
            issues.Add(Issue("lenderName", "credit_lender_invalid"));
        }

        if (!IsCanonical(request.ContractReference) || request.ContractReference.Length > ContractReferenceMaximumLength)
        {
            issues.Add(Issue("contractReference", "credit_contract_reference_invalid"));
        }

        if (request.CurrencyId == Guid.Empty)
        {
            issues.Add(Issue("currencyId", "identity_required"));
        }

        if (!FinanceExactDecimalText.TryParsePositive(request.PrincipalAmount, out _)
            || request.PrincipalAmount.Length > FinanceCreditPlanCalculator.AmountMaximumLength)
            issues.Add(Issue("principalAmount", FinanceCreditPlanCalculator.PrincipalAmountInvalid));
        issues.AddRange(FinanceCreditAccountProfilePolicy.Validate(request));
        return issues.Count == 0 ? null : Refused(context, CommandOutcome.ValidationFailed, [.. issues]);
    }

    private static CreditAccountCommandResult? ReasonRefusal(FinanceContext context, string reason)
    {
        if (!IsCanonical(reason))
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("reason", "reason_invalid"));
        }

        return reason.Length > ReasonMaximumLength
            ? Refused(context, CommandOutcome.ValidationFailed, Issue("reason", "finance_value_does_not_fit"))
            : null;
    }

    /// <summary>The currency must be one of Settings' candidates now; an unavailable source refuses.</summary>
    private async Task<CreditAccountCommandResult?> CurrencyRefusalAsync(FinanceContext context, FinanceAction action,
        Guid currencyId, CancellationToken cancellationToken)
    {
        IReadOnlyList<MoneyCurrencyCandidateV1> candidates;
        try
        {
            candidates = MoneyCurrencyCandidatesV1.EnsureWellFormed(
                await currencies.GetMoneyCurrencyCandidatesAsync(PlanMoneyOperation, cancellationToken));
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

    private static CreditAccountCommandResult? IdentityRefusal(FinanceContext context, Guid creditAccountId,
        string expectedVersion)
    {
        // Malformed identities are refused after the replay lookup (never stored, so never a replay).
        if (creditAccountId == Guid.Empty)
        {
            return Refused(context, CommandOutcome.ValidationFailed, Issue("creditAccountId", "identity_required"));
        }

        return IsCanonical(expectedVersion)
            ? null
            : Refused(context, CommandOutcome.ValidationFailed, Issue("expectedVersion", "identity_required"));
    }

    /// <summary>Context, the action's fresh authority, and the key and context storage limits.</summary>
    private async Task<CreditAccountCommandResult?> StartAsync(FinanceContext context, CommandMetadata metadata,
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
    private async Task<CreditAccountCommandResult?> PriorAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, CancellationToken cancellationToken)
    {
        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var prior = await dbContext.CreditAccountCommands.AsNoTracking()
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

        var stored = JsonSerializer.Deserialize<CreditAccountCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayıtlı komut sonucu okunamadı.");
        return stored with { IsReplay = true };
    }

    /// <summary>Company/actor still the request's, then a fresh decision on the action.</summary>
    private async Task<CreditAccountCommandResult?> FenceAsync(FinanceContext context, FinanceAction action,
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
        Guid? targetId, CreditAccountCommandResult result, DateTimeOffset now) =>
        dbContext.CreditAccountCommands.Add(new CreditAccountCommandRow
        {
            CompanyId = context.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            CreditAccountId = targetId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    /// <summary>
    /// One save, fenced again immediately before it. A lost race (the live slot or the row version) clears the tracker and
    /// reads the key afresh: a same-key winner is this answer too (ADR-011 §8); anything else is a
    /// <c>concurrent_change</c>.
    /// </summary>
    private async Task<CreditAccountCommandResult> SaveAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, CreditAccountCommandResult result,
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
    private Task<CreditAccountCommandResult> TerminalAsync(FinanceContext context, CommandMetadata metadata,
        FinanceAction action, string command, string payloadHash, Guid? targetId, CreditAccountCommandResult refusal,
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

    private static string AccountAudit(CreditAccountRow row, IReadOnlyList<CreditAccountInstallmentRow> lines) =>
        JsonSerializer.Serialize(new
        {
            row.Id,
            version = VersionText(row.Version),
            group = GroupOf(row.CreditGroup).ToString(),
            row.LenderName,
            row.ContractReference,
            row.CurrencyId,
            principalAmount = row.PrincipalAmountText,
            row.StartDate,
            profile = Profile(row),
            tolerance = Snapshot(row),
            row.LenderPartyVersion,
            row.LenderCompanyAccountVersion,
            status = StatusOf(row.Status).ToString(),
            row.ProposedBy,
            row.ProposedAtUtc,
            row.DecidedBy,
            row.DecidedAtUtc,
            row.RejectionReason,
            installments = Ordered(lines).Select(line => new
            {
                line.Sequence,
                line.DueDate,
                installment = line.InstallmentText,
                principal = line.PrincipalText,
                interest = line.InterestText,
                bsmv = line.BsmvText,
                kkdf = line.KkdfText,
                charges = Charges(line),
            }),
        }, AuditJson);

    private static CreditAccountView View(CreditAccountRow row, IReadOnlyList<CreditAccountInstallmentRow> lines,
        string actorId)
    {
        var ordered = Ordered(lines);
        // The stored plan always reads (the database checks and the proposal's calculator); an unreadable one fails closed.
        var profile = Profile(row);
        var totals = profile?.PlanNature == CreditPlanNatureV1.SourceMovements ? null
            : FinanceCreditPlanCalculator.Validate(row.PrincipalAmountText, Inputs(ordered), row.CurrencyId).Totals
                ?? throw new InvalidOperationException("Kredi hesabının kayıtlı ödeme planı okunamadı.");
        return new CreditAccountView(row.Id, row.CompanyId, GroupOf(row.CreditGroup), row.LenderName,
            row.ContractReference, row.CurrencyId, row.PrincipalAmountText, row.StartDate, StatusOf(row.Status),
            string.Equals(row.ProposedBy, actorId, StringComparison.Ordinal), row.ProposedAtUtc, row.DecidedAtUtc,
            row.RejectionReason, VersionText(row.Version), totals,
            [.. ordered.Select(line => new CreditPlanInstallmentView(line.Sequence, line.DueDate, line.InstallmentText,
                line.PrincipalText, line.InterestText, line.BsmvText, line.KkdfText, Charges(line)))], profile, Snapshot(row));
    }

    private static List<CreditAccountInstallmentRow> Ordered(IEnumerable<CreditAccountInstallmentRow> lines) =>
        [.. lines.OrderBy(line => line.Sequence)];

    private static List<CreditPlanInstallmentInput> Inputs(IEnumerable<CreditAccountInstallmentRow> lines) =>
        [.. Ordered(lines).Select(line => new CreditPlanInstallmentInput(line.Sequence, line.DueDate, line.InstallmentText,
            line.PrincipalText, line.InterestText, line.BsmvText, line.KkdfText, Charges(line)))];

    internal static int GroupCode(CreditAccountGroup group) => group switch
    {
        CreditAccountGroup.Cash => CashGroup,
        CreditAccountGroup.NonCash => NonCashGroup,
        CreditAccountGroup.InstrumentAndFactoring => InstrumentAndFactoringGroup,
        CreditAccountGroup.Leasing => LeasingGroup,
        _ => throw new InvalidOperationException("Kredi grubu tanınmıyor."),
    };

    private static CreditAccountGroup GroupOf(int group) => group switch
    {
        CashGroup => CreditAccountGroup.Cash,
        NonCashGroup => CreditAccountGroup.NonCash,
        InstrumentAndFactoringGroup => CreditAccountGroup.InstrumentAndFactoring,
        LeasingGroup => CreditAccountGroup.Leasing,
        _ => throw new InvalidOperationException("Kredi grubu tanınmıyor."),
    };

    private static CreditAccountStatus StatusOf(int status) => status switch
    {
        Proposed => CreditAccountStatus.Proposed,
        Approved => CreditAccountStatus.Approved,
        Rejected => CreditAccountStatus.Rejected,
        _ => throw new InvalidOperationException("Kredi hesabı durumu tanınmıyor."),
    };

    private static string VersionText(long version) => FinancePartyCreditLimitService.VersionText(version);

    private static CreditAccountCommandResult Succeeded(FinanceContext context, CreditAccountRow row,
        IReadOnlyList<CreditAccountInstallmentRow> lines) =>
        new(CommandOutcome.Succeeded, View(row, lines, context.ActorId), IsReplay: false, context.CorrelationId, []);

    private static bool IsCanonical(string? value) =>
        !string.IsNullOrWhiteSpace(value) && string.Equals(value, value.Trim(), StringComparison.Ordinal);

    private static ValidationIssue Issue(string field, string code) => new(field, code);

    private static CreditAccountCommandResult Refused(FinanceContext context, CommandOutcome outcome,
        params ValidationIssue[] issues) =>
        new(outcome, null, IsReplay: false, context.CorrelationId, issues);
}

/// <summary>
/// The ADR-009 envelopes of the credit account writes: the roles that granted <c>finance.credits.propose</c> and
/// <c>finance.credits.approve</c>.
/// </summary>
public sealed record FinanceCreditAccountEnvelopes(FinanceAuditEnvelope Propose, FinanceAuditEnvelope Approve);
