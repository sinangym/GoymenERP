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
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// The centrally approved Settings money operation whose profile governs each leg amount of a transfer. Null or blank
/// refuses every transfer as unavailable (no default scale).
/// </summary>
public sealed record CashTransferBinding(string? MoneyOperation);

public sealed record CashTransferAuditEnvelopes(FinanceAuditEnvelope Commit);

/// <summary>
/// FNS-D24 (API-32/34): money moved between two treasury accounts of the same company. A same-currency transfer and a
/// currency exchange are each ONE committed record carrying both legs (and, for an exchange, the transaction rate);
/// no separate outgoing and incoming movement is written. Preview runs the commit's checks without writing. The
/// commit is refused unless the actor confirms it, both accounts are eligible, both leg profiles match the approved
/// Settings profiles, an exchange's rate rule answers Consistent, the record period is open and the treasury balance
/// guard answers Sufficient for the source leg (FNS-D11: refused, never clipped). Every unavailable answer refuses.
/// </summary>
public sealed class CashTransferService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    IFinanceTreasuryBalanceGuard balanceGuard,
    IFinanceCurrencyExchangeRateRule exchangeRateRule,
    ISettingsPrerequisiteProvider settings,
    IFinanceRecordPeriodRule periodRule,
    IAuditEventCollector auditEvents,
    CashTransferBinding binding,
    CashTransferAuditEnvelopes envelopes,
    TimeProvider timeProvider,
    IAuditTransactionWriterV1? auditOwner = null) : ICashTransferService
{
    public const string CommitCommand = "finance.cash-transfer.commit";

    private const string EntityType = "Finance.CashTransfer";
    private const string ListOrdering = "recordedAtUtc desc, id desc";
    private const int ApprovedTreasuryAccountStatus = 2;
    private const int IdentityMaximumLength = 128;
    private const int PageSize = 50;
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<CashTransferPreviewResult> PreviewAsync(
        FinanceContext context,
        CashTransferPreparation preparation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!SameContext(context)) return Preview(new Refusal(CommandOutcome.Denied, []));
        if (await FenceAsync(context, FinanceAction.CommitCashTransfer, cancellationToken) is { } denied)
            return Preview(denied);
        return await FinanceTreasuryTransaction.ExecuteAsync(dbContext,
            () => PreviewTreasuryCoreAsync(context, preparation, cancellationToken),
            async () => Preview(await FenceAsync(context, FinanceAction.CommitCashTransfer, cancellationToken)
                ?? new Refusal(CommandOutcome.Unavailable, [Issue("treasuryAccountId", TreasuryBalanceAnswer.UnavailableCode)])), auditOwner, cancellationToken);
    }

    private async Task<CashTransferPreviewResult> PreviewTreasuryCoreAsync(
        FinanceContext context,
        CashTransferPreparation preparation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.CommitCashTransfer;
        if (!SameContext(context)) return Preview(new Refusal(CommandOutcome.Denied, []));
        if (await FenceAsync(context, action, cancellationToken) is { } denied) return Preview(denied);
        if (preparation is null)
            return Preview(new Refusal(CommandOutcome.ValidationFailed, [Issue("preparation", "identity_required")]));
        var shape = CashTransferValidator.ValidateShape(preparation);
        if (shape.Count > 0) return Preview(new Refusal(CommandOutcome.ValidationFailed, [.. shape]));
        if (await CheckAsync(context, preparation, action, cancellationToken) is { } refusal) return Preview(refusal);
        if (await FenceAsync(context, action, cancellationToken) is { } afterChecks) return Preview(afterChecks);
        return new CashTransferPreviewResult(CommandOutcome.Succeeded, preparation, []);
    }

    public async Task<CashTransferCommandResult> CommitAsync(
        FinanceContext context,
        CommandMetadata metadata,
        CommitCashTransferRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        if (await FenceAsync(context, FinanceAction.CommitCashTransfer, cancellationToken) is { } denied)
            return Refused(context, denied);
        return await FinanceTreasuryTransaction.ExecuteAsync(dbContext,
            () => CommitTreasuryCoreAsync(context, metadata, request, cancellationToken),
            async () => Refused(context, await FenceAsync(context, FinanceAction.CommitCashTransfer, cancellationToken)
                ?? new Refusal(CommandOutcome.Unavailable, [Issue("treasuryAccountId", TreasuryBalanceAnswer.UnavailableCode)])), auditOwner, cancellationToken);
    }

    private async Task<CashTransferCommandResult> CommitTreasuryCoreAsync(
        FinanceContext context,
        CommandMetadata metadata,
        CommitCashTransferRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.CommitCashTransfer;
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        if (await FenceAsync(context, action, cancellationToken) is { } denied) return Refused(context, denied);
        if (!ValidMetadata(context, metadata))
            return Refused(context, CommandOutcome.ValidationFailed, Issue("idempotencyKey", "idempotency_key_invalid"));

        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payloadHash = Hash(request);
        if (await PriorAsync(context, action, keyHash, payloadHash, cancellationToken) is { } prior) return prior;
        if (request.Preparation is null)
            return Refused(context, CommandOutcome.ValidationFailed, Issue("preparation", "identity_required"));
        var shape = CashTransferValidator.ValidateShape(request.Preparation);
        if (shape.Count > 0) return Refused(context, CommandOutcome.ValidationFailed, [.. shape]);
        var commitIssues = CashTransferValidator.ValidateCommit(request);
        if (commitIssues.Count > 0) return Refused(context, CommandOutcome.ValidationFailed, [.. commitIssues]);
        if (await CheckAsync(context, request.Preparation, action, cancellationToken) is { } refusal)
            return Refused(context, refusal);

        var now = timeProvider.GetUtcNow();
        var row = NewRow(context, request.Preparation, now);
        dbContext.CashTransfers.Add(row);
        var result = new CashTransferCommandResult(CommandOutcome.Succeeded, View(row), IsReplay: false,
            context.CorrelationId, []);
        AddCommand(context, keyHash, payloadHash, row.Id, result, now);
        Audit(context, metadata, row);
        // FNS-D63 does not define the exchange posting mapping; this event publishes only the
        // decided same-currency source, without synthesising an exchange difference or TL value.
        if (request.Preparation.Kind == CashTransferKind.SameCurrency)
            FinanceFinancialMovementOutboxV1.Transfer(dbContext, context, View(row));
        if (await FenceAsync(context, action, cancellationToken) is { } beforeSave)
            return Refused(context, beforeSave);
        return await SaveAsync(context, action, keyHash, payloadHash, cancellationToken) ?? result;
    }

    public async Task<ReadResult<CashTransferPage>> ListAuthorizedAsync(
        FinanceContext context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return new ReadResult<CashTransferPage>(ReadOutcome.Denied, null);
        var decision = await permissions.CheckAsync(context, FinanceAction.ReadCashTransfers, null, cancellationToken);
        if (decision != PermissionDecision.Allowed || !SameContext(context))
            return ReadFailure<CashTransferPage>(decision);
        var query = dbContext.CashTransfers.AsNoTracking();
        if (cursor is not null)
        {
            if (DraftListCursor.Decode(cursor, context.CompanyId, ListOrdering) is not { } after)
                return new ReadResult<CashTransferPage>(ReadOutcome.NotFound, null);
            query = query.Where(row => row.RecordedAtUtc < after.CreatedAtUtc
                || (row.RecordedAtUtc == after.CreatedAtUtc && row.Id.CompareTo(after.Id) < 0));
        }

        var rows = await query.OrderByDescending(row => row.RecordedAtUtc).ThenByDescending(row => row.Id)
            .Take(PageSize + 1).ToListAsync(cancellationToken);
        var again = await permissions.CheckAsync(context, FinanceAction.ReadCashTransfers, null, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context)) return ReadFailure<CashTransferPage>(again);
        var page = rows.Take(PageSize).ToArray();
        var next = rows.Count > PageSize
            ? DraftListCursor.Encode(context.CompanyId, page[^1].RecordedAtUtc, page[^1].Id, ListOrdering)
            : null;
        return new ReadResult<CashTransferPage>(
            ReadOutcome.Found,
            new CashTransferPage(context.CompanyId, page.Select(View).ToArray(), next));
    }

    public async Task<ReadResult<CashTransferView>> DetailAuthorizedAsync(
        FinanceContext context,
        Guid transferId,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return new ReadResult<CashTransferView>(ReadOutcome.Denied, null);
        var decision = await permissions.CheckAsync(context, FinanceAction.ReadCashTransfers, transferId,
            cancellationToken);
        if (decision != PermissionDecision.Allowed || !SameContext(context))
            return ReadFailure<CashTransferView>(decision);
        var row = transferId == Guid.Empty
            ? null
            : await dbContext.CashTransfers.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == transferId, cancellationToken);
        var again = await permissions.CheckAsync(context, FinanceAction.ReadCashTransfers, transferId,
            cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context)) return ReadFailure<CashTransferView>(again);
        return row is null
            ? new ReadResult<CashTransferView>(ReadOutcome.NotFound, null)
            : new ReadResult<CashTransferView>(ReadOutcome.Found, View(row));
    }

    public async Task<ReadResult<CashTransferAccountContextView>> AccountContextAuthorizedAsync(
        FinanceContext context,
        CashTransferAccountKind accountKind,
        Guid accountId,
        DateOnly occurredOn,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return new ReadResult<CashTransferAccountContextView>(ReadOutcome.Denied, null);
        const FinanceAction action = FinanceAction.CommitCashTransfer;
        var decision = await permissions.CheckAsync(context, action, null, cancellationToken);
        if (decision != PermissionDecision.Allowed || !SameContext(context))
            return ReadFailure<CashTransferAccountContextView>(decision);
        if (!Enum.IsDefined(accountKind) || accountId == Guid.Empty)
            return new ReadResult<CashTransferAccountContextView>(ReadOutcome.NotFound, null);

        var currencyId = await EligibleCurrencyAsync(accountKind, accountId, cancellationToken);
        MoneyProfileV1? profile = null;
        var profileUnavailable = string.IsNullOrWhiteSpace(binding.MoneyOperation);
        if (currencyId is { } currency && !profileUnavailable)
        {
            try
            {
                profile = await settings.GetMoneyProfileAsync(currency, binding.MoneyOperation!, occurredOn,
                    cancellationToken);
            }
            catch (SettingsPrerequisiteException exception)
            {
                var afterRefusal = await permissions.CheckAsync(context, action, null, cancellationToken);
                if (afterRefusal != PermissionDecision.Allowed || !SameContext(context))
                    return ReadFailure<CashTransferAccountContextView>(afterRefusal);
                return new ReadResult<CashTransferAccountContextView>(
                    exception.Code == "settings_access_denied" ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
            }
        }

        var again = await permissions.CheckAsync(context, action, null, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context))
            return ReadFailure<CashTransferAccountContextView>(again);
        if (currencyId is not { } eligibleCurrency)
            return new ReadResult<CashTransferAccountContextView>(ReadOutcome.NotFound, null);
        if (profileUnavailable || profile?.Profile is null || profile.Profile.ProfileId == Guid.Empty
            || profile.Profile.Version <= 0 || profile.CompanyId != context.CompanyId
            || profile.CurrencyId != eligibleCurrency
            || !string.Equals(profile.Operation, binding.MoneyOperation, StringComparison.Ordinal) || !profile.IsActive
            || occurredOn < profile.EffectiveFrom
            || (profile.EffectiveThrough is { } through && occurredOn > through))
            return new ReadResult<CashTransferAccountContextView>(ReadOutcome.Unavailable, null);
        return new ReadResult<CashTransferAccountContextView>(ReadOutcome.Found, new CashTransferAccountContextView(
            context.CompanyId, accountKind, accountId, eligibleCurrency,
            new ProfileReference(profile.Profile.ProfileId.ToString("D"),
                profile.Profile.Version.ToString(CultureInfo.InvariantCulture)),
            occurredOn));
    }

    internal static string Hash<T>(T request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json))))
            .ToLowerInvariant();

    /// <summary>
    /// The commit's owner checks in a fixed order, each fenced after its await: both accounts eligible, both leg
    /// profiles, the exchange rate rule (exchange only), the record period and the source balance.
    /// </summary>
    private async Task<Refusal?> CheckAsync(
        FinanceContext context,
        CashTransferPreparation preparation,
        FinanceAction action,
        CancellationToken cancellationToken)
    {
        var sourceCurrency = await EligibleCurrencyAsync(preparation.SourceKind, preparation.SourceAccountId,
            cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } afterSource) return afterSource;
        var targetCurrency = await EligibleCurrencyAsync(preparation.TargetKind, preparation.TargetAccountId,
            cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } afterTarget) return afterTarget;
        var accountIssues = new List<ValidationIssue>();
        if (sourceCurrency != preparation.SourceMoney.CurrencyId)
            accountIssues.Add(Issue("sourceAccountId", "cash_transfer_source_ineligible"));
        if (targetCurrency != preparation.TargetMoney.CurrencyId)
            accountIssues.Add(Issue("targetAccountId", "cash_transfer_target_ineligible"));
        if (accountIssues.Count > 0) return new Refusal(CommandOutcome.ValidationFailed, [.. accountIssues]);

        if (await LegProfileAsync(context, action, "sourceProfile", "sourceMoney.amount", preparation.SourceMoney,
                preparation.SourceProfile, preparation.OccurredOn, cancellationToken) is { } sourceProfile)
            return sourceProfile;
        if (await LegProfileAsync(context, action, "targetProfile", "targetMoney.amount", preparation.TargetMoney,
                preparation.TargetProfile, preparation.OccurredOn, cancellationToken) is { } targetProfile)
            return targetProfile;

        if (preparation.Kind == CashTransferKind.CurrencyExchange)
        {
            // FNS-D24: the rate source, the rate/amount consistency and the exchange difference are not decided; the
            // registered rule answers Unavailable until they are (NEEDS-SINAN), so the exchange stays fail-closed.
            CurrencyExchangeRateAnswer? rate;
            try
            {
                rate = await exchangeRateRule.CheckAsync(context, preparation.SourceMoney, preparation.TargetMoney,
                    preparation.Rate!, preparation.OccurredOn, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                rate = null;
            }

            if (await FenceAsync(context, action, cancellationToken) is { } afterRate) return afterRate;
            if (rate is null || rate.Outcome == CurrencyExchangeRateOutcome.Unavailable)
                return new Refusal(CommandOutcome.Unavailable,
                    [Issue("rate", rate?.Code ?? CurrencyExchangeRateAnswer.UnavailableCode)]);
            if (rate.Outcome != CurrencyExchangeRateOutcome.Consistent)
                return new Refusal(CommandOutcome.ValidationFailed,
                    [Issue("rate", rate.Code ?? CurrencyExchangeRateAnswer.InconsistentCode)]);
        }

        var period = await periodRule.CheckAsync(context, preparation.OccurredOn, preparation.ValueOn,
            timeProvider.GetUtcNow(), cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } afterPeriod) return afterPeriod;
        if (period.Outcome != RecordPeriodOutcome.Open)
            return new Refusal(period.Outcome == RecordPeriodOutcome.Unavailable
                    ? CommandOutcome.Unavailable : CommandOutcome.ValidationFailed,
                [Issue("occurredOn", period.Code ?? (period.Outcome == RecordPeriodOutcome.Unavailable
                    ? RecordPeriodAnswer.RuleUnavailableCode : RecordPeriodAnswer.PeriodNotOpenCode))]);

        // FNS-D11: the CURRENT balance of the source account must cover the outgoing leg; never clipped.
        TreasuryBalanceAnswer? balance;
        try
        {
            balance = await balanceGuard.CheckOutgoingAsync(context, ToSourceKind(preparation.SourceKind),
                preparation.SourceAccountId, preparation.SourceMoney, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            balance = null;
        }

        if (await FenceAsync(context, action, cancellationToken) is { } afterBalance) return afterBalance;
        if (balance is null || balance.Outcome == TreasuryBalanceOutcome.Unavailable)
            return new Refusal(CommandOutcome.Unavailable,
                [Issue("sourceAccountId", TreasuryBalanceAnswer.UnavailableCode)]);
        if (balance.Outcome != TreasuryBalanceOutcome.Sufficient)
            return new Refusal(CommandOutcome.ValidationFailed,
                [Issue("sourceMoney.amount", TreasuryBalanceAnswer.InsufficientCode)]);
        return null;
    }

    private async Task<Refusal?> LegProfileAsync(
        FinanceContext context,
        FinanceAction action,
        string field,
        string amountField,
        MoneyText money,
        ProfileReference reference,
        DateOnly occurredOn,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(binding.MoneyOperation))
            return new Refusal(CommandOutcome.Unavailable, [Issue(field, "finance_operation_mapping_unavailable")]);
        try
        {
            var profile = await settings.GetMoneyProfileAsync(money.CurrencyId, binding.MoneyOperation, occurredOn,
                cancellationToken);
            if (await FenceAsync(context, action, cancellationToken) is { } fenced) return fenced;
            if (profile is null)
                return new Refusal(CommandOutcome.Unavailable, [Issue(field, "settings_provider_unavailable")]);
            var issues = CashTransferValidator.ValidateLegProfile(field, amountField, money, reference, occurredOn,
                context.CompanyId, profile, binding.MoneyOperation);
            return issues.Count == 0 ? null : new Refusal(CommandOutcome.ValidationFailed, [.. issues]);
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceAsync(context, action, cancellationToken) is { } fenced) return fenced;
            return exception.Code == "settings_access_denied"
                ? new Refusal(CommandOutcome.Denied, [])
                : new Refusal(CommandOutcome.Unavailable, [Issue(field, exception.Code)]);
        }
    }

    /// <summary>
    /// The account's currency when it may take part in a transfer, else null: a bank account must be approved,
    /// money-movement-eligible and hold an IBAN; a cash account approved. Each read is Finance's own table.
    /// </summary>
    private async Task<Guid?> EligibleCurrencyAsync(
        CashTransferAccountKind accountKind,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        if (accountKind == CashTransferAccountKind.Bank)
        {
            var bank = await dbContext.BankAccounts.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == accountId, cancellationToken);
            return bank is not null && bank.Status == ApprovedTreasuryAccountStatus
                && bank.MoneyMovementEligible == true && !string.IsNullOrWhiteSpace(bank.Iban)
                && bank.CurrencyId != Guid.Empty
                    ? bank.CurrencyId
                    : null;
        }

        var cash = await dbContext.CashAccounts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == accountId, cancellationToken);
        return cash is not null && cash.Status == ApprovedTreasuryAccountStatus && cash.CurrencyId != Guid.Empty
            ? cash.CurrencyId
            : null;
    }

    private async Task<CashTransferCommandResult?> PriorAsync(
        FinanceContext context,
        FinanceAction action,
        string keyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.CashTransferCommands.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CommandName == CommitCommand && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (await FenceAsync(context, action, cancellationToken) is { } fenced) return Refused(context, fenced);
        if (prior is null) return null;
        if (prior.PayloadHash != payloadHash || prior.ActorId != context.ActorId)
            return Refused(context, CommandOutcome.IdempotencyConflict);
        var stored = JsonSerializer.Deserialize<CashTransferCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayitli virman komut sonucu okunamadi.");
        return stored with { IsReplay = true };
    }

    private async Task<CashTransferCommandResult?> SaveAsync(
        FinanceContext context,
        FinanceAction action,
        string keyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException
            || exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            if (FinanceTreasuryTransaction.RequiresFullRollback(dbContext)) throw;
            dbContext.ChangeTracker.Clear();
            return await PriorAsync(context, action, keyHash, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }
    }

    private async Task<Refusal?> FenceAsync(
        FinanceContext context,
        FinanceAction action,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return new Refusal(CommandOutcome.Denied, []);
        var decision = await permissions.CheckAsync(context, action, null, cancellationToken);
        if (decision != PermissionDecision.Allowed)
            return decision == PermissionDecision.Denied
                ? new Refusal(CommandOutcome.Denied, [])
                : new Refusal(CommandOutcome.Unavailable,
                    [Issue("permission", "finance_permission_mapping_unavailable")]);
        return SameContext(context) ? null : new Refusal(CommandOutcome.Denied, []);
    }

    private void AddCommand(
        FinanceContext context,
        string keyHash,
        string payloadHash,
        Guid transferId,
        CashTransferCommandResult result,
        DateTimeOffset now) => dbContext.CashTransferCommands.Add(new CashTransferCommandRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            CommandName = CommitCommand,
            IdempotencyKeyHash = keyHash,
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            TransferId = transferId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    private void Audit(FinanceContext context, CommandMetadata metadata, CashTransferRow row) =>
        auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId,
            "finance.cash-transfer.recorded.v1",
            EntityType,
            row.Id.ToString("D"),
            "Record",
            EntityVersionBefore: null,
            row.Version,
            AuditActorTypes.User,
            context.ActorId,
            envelopes.Commit.PermissionCode,
            envelopes.Commit.RoleCodes,
            "{}",
            AuditJson(row),
            "Hesaplar arasi virman / doviz bozdurma",
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelopes.Commit.SourceChannel,
            envelopes.Commit.ClientIpAddress,
            envelopes.Commit.UserAgent,
            context.CorrelationId,
            context.CausationId,
            metadata.IdempotencyKey));

    private bool SameContext(FinanceContext context) =>
        currentUser.IsAuthenticated && !string.IsNullOrWhiteSpace(currentUser.UserId)
        && currentUser.UserId == context.ActorId && companyContext.IsResolved
        && companyContext.CompanyId != Guid.Empty && companyContext.CompanyId == context.CompanyId
        && currentUser.CanAccessCompany(context.CompanyId);

    private static bool ValidMetadata(FinanceContext context, CommandMetadata metadata) =>
        !string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
        && metadata.IdempotencyKey == metadata.IdempotencyKey.Trim()
        && metadata.IdempotencyKey.Length <= IdentityMaximumLength
        && context.ActorId.Length <= IdentityMaximumLength
        && context.CorrelationId.Length <= IdentityMaximumLength;

    private static CashTransferRow NewRow(
        FinanceContext context,
        CashTransferPreparation preparation,
        DateTimeOffset now) => new CashTransferRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            Version = 1,
            Kind = FromKind(preparation.Kind),
            SourceKind = FromAccountKind(preparation.SourceKind),
            SourceAccountId = preparation.SourceAccountId,
            SourceCurrencyId = preparation.SourceMoney.CurrencyId,
            SourceAmountText = preparation.SourceMoney.Amount,
            SourceProfileId = preparation.SourceProfile.ProfileId,
            SourceProfileVersion = preparation.SourceProfile.Version,
            TargetKind = FromAccountKind(preparation.TargetKind),
            TargetAccountId = preparation.TargetAccountId,
            TargetCurrencyId = preparation.TargetMoney.CurrencyId,
            TargetAmountText = preparation.TargetMoney.Amount,
            TargetProfileId = preparation.TargetProfile.ProfileId,
            TargetProfileVersion = preparation.TargetProfile.Version,
            RateText = preparation.Rate,
            OccurredOn = preparation.OccurredOn,
            ValueOn = preparation.ValueOn,
            Reference = preparation.Reference,
            Description = preparation.Description,
            TransferConfirmed = true,
            RecordedBy = context.ActorId,
            RecordedAtUtc = now,
        };

    private static ExpensePaymentSourceKind ToSourceKind(CashTransferAccountKind kind) => kind switch
    {
        CashTransferAccountKind.Bank => ExpensePaymentSourceKind.Bank,
        CashTransferAccountKind.Cash => ExpensePaymentSourceKind.Cash,
        _ => throw new InvalidOperationException("Bilinmeyen virman hesap turu."),
    };

    private static int FromAccountKind(CashTransferAccountKind kind) => kind switch
    {
        CashTransferAccountKind.Bank => CashTransferAccountKinds.Bank,
        CashTransferAccountKind.Cash => CashTransferAccountKinds.Cash,
        _ => throw new InvalidOperationException("Bilinmeyen virman hesap turu."),
    };

    private static CashTransferAccountKind ToAccountKind(int kind) => kind switch
    {
        CashTransferAccountKinds.Bank => CashTransferAccountKind.Bank,
        CashTransferAccountKinds.Cash => CashTransferAccountKind.Cash,
        _ => throw new InvalidOperationException("Bilinmeyen virman hesap turu."),
    };

    private static int FromKind(CashTransferKind kind) => kind switch
    {
        CashTransferKind.SameCurrency => CashTransferKinds.SameCurrency,
        CashTransferKind.CurrencyExchange => CashTransferKinds.CurrencyExchange,
        _ => throw new InvalidOperationException("Bilinmeyen virman turu."),
    };

    private static CashTransferKind ToKind(int kind) => kind switch
    {
        CashTransferKinds.SameCurrency => CashTransferKind.SameCurrency,
        CashTransferKinds.CurrencyExchange => CashTransferKind.CurrencyExchange,
        _ => throw new InvalidOperationException("Bilinmeyen virman turu."),
    };

    private static CashTransferPreparation Preparation(CashTransferRow row) => new CashTransferPreparation(
        ToKind(row.Kind),
        ToAccountKind(row.SourceKind), row.SourceAccountId,
        new MoneyText(row.SourceAmountText, row.SourceCurrencyId),
        new ProfileReference(row.SourceProfileId, row.SourceProfileVersion),
        ToAccountKind(row.TargetKind), row.TargetAccountId,
        new MoneyText(row.TargetAmountText, row.TargetCurrencyId),
        new ProfileReference(row.TargetProfileId, row.TargetProfileVersion),
        row.RateText, row.OccurredOn, row.ValueOn, row.Reference, row.Description);

    private static CashTransferView View(CashTransferRow row) => new CashTransferView(
        row.Id, row.CompanyId, Preparation(row), row.RecordedBy, row.RecordedAtUtc, row.TransferConfirmed,
        VersionText(row.Version));

    private static CashTransferPreviewResult Preview(Refusal refusal) =>
        new CashTransferPreviewResult(refusal.Outcome, null, refusal.Issues);

    private static CashTransferCommandResult Refused(FinanceContext context, Refusal refusal) =>
        new CashTransferCommandResult(refusal.Outcome, null, IsReplay: false, context.CorrelationId, refusal.Issues);

    private static CashTransferCommandResult Refused(
        FinanceContext context,
        CommandOutcome outcome,
        params ValidationIssue[] issues) => new CashTransferCommandResult(
            outcome, null, IsReplay: false, context.CorrelationId, issues);

    private static ReadResult<T> ReadFailure<T>(PermissionDecision decision) where T : class =>
        new ReadResult<T>(
            decision == PermissionDecision.Unavailable ? ReadOutcome.Unavailable : ReadOutcome.Denied,
            null);

    private static string VersionText(long version) => version.ToString(CultureInfo.InvariantCulture);
    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static string AuditJson(CashTransferRow row) => JsonSerializer.Serialize(new
    {
        row.Id,
        version = VersionText(row.Version),
        kind = ToKind(row.Kind).ToString(),
        source = new
        {
            kind = ToAccountKind(row.SourceKind).ToString(),
            accountId = row.SourceAccountId,
            currencyId = row.SourceCurrencyId,
            profile = new { profileId = row.SourceProfileId, version = row.SourceProfileVersion },
        },
        target = new
        {
            kind = ToAccountKind(row.TargetKind).ToString(),
            accountId = row.TargetAccountId,
            currencyId = row.TargetCurrencyId,
            profile = new { profileId = row.TargetProfileId, version = row.TargetProfileVersion },
        },
        row.OccurredOn,
        row.ValueOn,
        row.RecordedBy,
        row.TransferConfirmed,
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private sealed record Refusal(CommandOutcome Outcome, ValidationIssue[] Issues);
}

/// <summary>
/// FNS-D24 fail-closed default: the rate source, the rate/amount consistency (rounding, quote direction) and any
/// allowed deviation are not decided, so no currency exchange can prove its rate. Every exchange is refused as
/// unavailable until the decided rule is registered before this one (NEEDS-SINAN).
/// </summary>
public sealed class UnavailableCurrencyExchangeRateRule : IFinanceCurrencyExchangeRateRule
{
    public Task<CurrencyExchangeRateAnswer> CheckAsync(
        FinanceContext context,
        MoneyText sourceMoney,
        MoneyText targetMoney,
        string rate,
        DateOnly occurredOn,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CurrencyExchangeRateAnswer(CurrencyExchangeRateOutcome.Unavailable,
            CurrencyExchangeRateAnswer.UnavailableCode));
    }
}

/// <summary>
/// FNS-D102: passivation reads the same internal ledger, including approved openings.
/// Zero is proven only under the caller's serializable Finance transaction. Missing
/// account/currency, invalid facts or an unprotected read remain Unavailable.
/// </summary>
public sealed class TreasuryMovementAwareCashAccountBalanceReader(
    FinanceDbContext dbContext, ICompanyContext companyContext) : IFinanceCashAccountBalanceReader
{
    public async Task<CashAccountBalanceState> ReadAsync(
        Guid companyId, Guid cashAccountId, CancellationToken cancellationToken)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId != companyId || companyId == Guid.Empty
            || cashAccountId == Guid.Empty || !dbContext.Database.IsRelational()
            || dbContext.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != System.Data.IsolationLevel.Serializable)
            return CashAccountBalanceState.Unavailable;
        var currency = await dbContext.CashAccounts.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.Id == cashAccountId)
            .Select(row => (Guid?)row.CurrencyId).SingleOrDefaultAsync(cancellationToken);
        if (currency is null) return CashAccountBalanceState.Unavailable;
        var balance = await new FinanceTreasuryLedger(dbContext, companyContext)
            .ReadAsync(companyId, ExpensePaymentSourceKind.Cash, cashAccountId, currency.Value, cancellationToken);
        return balance is null ? CashAccountBalanceState.Unavailable
            : balance.IsZero ? CashAccountBalanceState.Zero : CashAccountBalanceState.NotZero;
    }
}
