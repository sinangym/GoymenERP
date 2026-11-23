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
/// The centrally approved Settings money operation whose profile governs an expense payment amount. Null or blank
/// refuses every preparation as unavailable (no default scale).
/// </summary>
public sealed record ExpensePaymentBinding(string? MoneyOperation);

public sealed record ExpensePaymentAuditEnvelopes(
    FinanceAuditEnvelope Draft,
    FinanceAuditEnvelope Approve,
    FinanceAuditEnvelope Record);

/// <summary>
/// FNS-D25 (API-25): the payment not tied to a party, paid from an approved bank account or an approved cash
/// account, naming the single expense item by identity and carrying a mandatory description. Draft and approval
/// move no money. The approver differs from the preparer. Only a successful, explicitly confirmed final Record is
/// the money fact: the recorded row itself (no second movement row is written for it). Before the record the
/// treasury balance guard must answer Sufficient (FNS-D11 / FNS-D22: refused, never clipped); an unavailable
/// answer refuses the record.
/// FNS-D50: a cash account whose passivation waits for approval pays nothing new — the draft save, the approval
/// (FNS-D65, also for a draft prepared before the proposal) and the record refuse it (<c>expense_payment_treasury_account_passivation_pending</c>) and the draft context does not offer it.
/// A rejected passivation is no longer pending, so the account pays again. (A bank account has no passivation yet.)
/// </summary>
public sealed class ExpensePaymentService(
    FinanceDbContext dbContext,
    ICompanyContext companyContext,
    ICurrentUser currentUser,
    IFinancePermissionPort permissions,
    IFinanceExpenseItemCatalog expenseItems,
    IFinanceTreasuryBalanceGuard balanceGuard,
    ISettingsPrerequisiteProvider settings,
    IFinanceRecordPeriodRule periodRule,
    IAuditEventCollector auditEvents,
    ExpensePaymentBinding binding,
    ExpensePaymentAuditEnvelopes envelopes,
    TimeProvider timeProvider,
    IAuditTransactionWriterV1? auditOwner = null,
    IFinanceDocumentNumberReservationV1? documentNumbers = null) : IExpensePaymentService
{
    public const string DraftCommand = "finance.expense-payment.draft.save";
    public const string ApproveCommand = "finance.expense-payment.approve";
    public const string RecordCommand = "finance.expense-payment.record";

    private const string EntityType = "Finance.ExpensePayment";
    private const int ApprovedTreasuryAccountStatus = 2;
    private const int CashAccountPassivationChange = 1;
    private const int CashAccountChangePending = 1;
    private const int IdentityMaximumLength = 128;
    private const int PageSize = 50;
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<ExpensePaymentCommandResult> DraftSaveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ExpensePaymentDraftSaveRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.SaveExpensePaymentDraft;
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        if (await FenceAsync(context, action, request.PaymentId, cancellationToken) is { } denied) return denied;
        if (!ValidMetadata(context, metadata))
            return Refused(context, CommandOutcome.ValidationFailed, Issue("idempotencyKey", "idempotency_key_invalid"));

        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payloadHash = Hash(request);
        if (await PriorAsync(context, DraftCommand, action, keyHash, payloadHash, cancellationToken) is { } prior)
            return prior;
        if (!ExpensePaymentValidator.IsValidDraftIdentity(request) || request.Preparation is null
            || request.Preparation.Money is null || request.Preparation.Profile is null)
            return Refused(context, CommandOutcome.ValidationFailed, Issue("paymentId", "payment_identity_invalid"));
        var preparation = request.Preparation;
        var shape = ExpensePaymentValidator.ValidateShape(preparation);
        if (shape.Count > 0) return Refused(context, CommandOutcome.ValidationFailed, [.. shape]);
        if (!KnownExpenseItem(preparation.ExpenseItemId))
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expenseItemId", "expense_item_unknown"));

        ExpensePaymentRow? row = null;
        if (request.PaymentId is { } paymentId)
        {
            row = await dbContext.ExpensePayments.SingleOrDefaultAsync(candidate => candidate.Id == paymentId,
                cancellationToken);
            if (await FenceAsync(context, action, paymentId, cancellationToken) is { } afterRead) return afterRead;
            if (row is null)
                return await TerminalAsync(context, DraftCommand, action, keyHash, payloadHash, null,
                    Refused(context, CommandOutcome.ValidationFailed, Issue("paymentId", "payment_not_found")),
                    cancellationToken);
            if (VersionText(row.Version) != request.ExpectedVersion)
                return await TerminalAsync(context, DraftCommand, action, keyHash, payloadHash, row.Id,
                    Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                    cancellationToken);
            if (row.Status != ExpensePaymentStatuses.Draft)
                return Refused(context, CommandOutcome.ValidationFailed, Issue("paymentId", "payment_not_draft"));
        }

        if (await RequireEligibleTreasuryAsync(context, preparation, action, request.PaymentId, cancellationToken)
            is { } treasuryFailure) return treasuryFailure;
        if (await ValidateMoneyProfileAsync(context, preparation, request.PaymentId, action, cancellationToken)
            is { } invalid) return invalid;

        var now = timeProvider.GetUtcNow();
        var before = row is null ? "{}" : AuditJson(row);
        var versionBefore = row?.Version;
        if (row is null)
        {
            row = new ExpensePaymentRow
            {
                Id = Guid.NewGuid(),
                CompanyId = context.CompanyId,
                Version = 1,
                Status = ExpensePaymentStatuses.Draft,
                PreparedBy = context.ActorId,
                PreparedAtUtc = now,
            };
            Apply(row, preparation);
            dbContext.ExpensePayments.Add(row);
        }
        else
        {
            Apply(row, preparation);
            row.Version = checked(row.Version + 1);
        }

        var result = Succeeded(context, row);
        AddCommand(context, DraftCommand, keyHash, payloadHash, row.Id, result, now);
        Audit(context, metadata, row, envelopes.Draft,
            versionBefore is null ? "finance.expense-payment.created.v1" : "finance.expense-payment.updated.v1",
            versionBefore is null ? "Create" : "Update", versionBefore, before);
        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } beforeSave) return beforeSave;
        return await SaveAsync(context, DraftCommand, action, keyHash, payloadHash, cancellationToken) ?? result;
    }

    public async Task<ExpensePaymentCommandResult> ApproveAsync(
        FinanceContext context,
        CommandMetadata metadata,
        ApproveExpensePaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.ApproveExpensePayment;
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        if (await FenceAsync(context, action, request.PaymentId, cancellationToken) is { } denied) return denied;
        if (!ValidMetadata(context, metadata))
            return Refused(context, CommandOutcome.ValidationFailed, Issue("idempotencyKey", "idempotency_key_invalid"));

        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payloadHash = Hash(request);
        if (await PriorAsync(context, ApproveCommand, action, keyHash, payloadHash, cancellationToken) is { } prior)
            return prior;
        var row = await dbContext.ExpensePayments.SingleOrDefaultAsync(candidate => candidate.Id == request.PaymentId,
            cancellationToken);
        if (await FenceAsync(context, action, request.PaymentId, cancellationToken) is { } afterRead) return afterRead;
        if (row is null)
            return await TerminalAsync(context, ApproveCommand, action, keyHash, payloadHash, null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("paymentId", "payment_not_found")),
                cancellationToken);
        if (VersionText(row.Version) != request.ExpectedVersion)
            return await TerminalAsync(context, ApproveCommand, action, keyHash, payloadHash, row.Id,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                cancellationToken);
        if (!ExpensePaymentValidator.CanApprove(ToStatus(row.Status), row.PreparedBy, context.ActorId, request))
            return Refused(context, CommandOutcome.ValidationFailed, Issue("paymentId", "payment_approval_invalid"));
        var revisedByApprover = await RevisedByAsync(row.Id, context.ActorId, cancellationToken);
        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } afterRevisers) return afterRevisers;
        if (revisedByApprover)
            return Refused(context, CommandOutcome.ValidationFailed, Issue("paymentId", "payment_approval_invalid"));
        // FNS-D65: while a passivation of the paying cash account waits for its decision, no payment from it is
        // approved — not even a draft prepared before the proposal. Not recorded as terminal: once the passivation is
        // rejected the same approval may be asked again.
        if (row.SourceKind == ExpensePaymentSourceKinds.Cash)
        {
            var cash = await dbContext.CashAccounts.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == row.TreasuryAccountId, cancellationToken);
            if (await FenceAsync(context, action, row.Id, cancellationToken) is { } afterCash) return afterCash;
            if (cash is null || PassivationPending(cash))
                return Refused(context, CommandOutcome.ValidationFailed, Issue("treasuryAccountId",
                    cash is null ? "expense_payment_treasury_account_ineligible"
                        : "expense_payment_treasury_account_passivation_pending"));
        }

        var before = AuditJson(row);
        var versionBefore = row.Version;
        var now = timeProvider.GetUtcNow();
        row.Status = ExpensePaymentStatuses.Approved;
        row.ApprovedBy = context.ActorId;
        row.ApprovedAtUtc = now;
        row.Version = checked(row.Version + 1);
        var result = Succeeded(context, row);
        AddCommand(context, ApproveCommand, keyHash, payloadHash, row.Id, result, now);
        Audit(context, metadata, row, envelopes.Approve, "finance.expense-payment.approved.v1", "Approve",
            versionBefore, before);
        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } beforeSave) return beforeSave;
        return await SaveAsync(context, ApproveCommand, action, keyHash, payloadHash, cancellationToken) ?? result;
    }

    public async Task<ExpensePaymentCommandResult> RecordAsync(
        FinanceContext context,
        CommandMetadata metadata,
        RecordExpensePaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        if (await FenceAsync(context, FinanceAction.RecordExpensePayment, request.PaymentId, cancellationToken)
            is { } denied) return denied;
        return await FinanceTreasuryTransaction.ExecuteAsync(dbContext,
            async () =>
            {
                var result = await RecordTreasuryCoreAsync(context, metadata, request, cancellationToken);
                return result.Outcome == CommandOutcome.Succeeded
                    ? await FenceAsync(context, FinanceAction.RecordExpensePayment, request.PaymentId, cancellationToken) ?? result : result;
            },
            async () => await FenceAsync(context, FinanceAction.RecordExpensePayment, request.PaymentId, cancellationToken)
                ?? Refused(context, CommandOutcome.Unavailable, Issue("treasuryAccountId", TreasuryBalanceAnswer.UnavailableCode)), auditOwner, cancellationToken, result => result.Outcome == CommandOutcome.Succeeded);
    }

    private async Task<ExpensePaymentCommandResult> RecordTreasuryCoreAsync(
        FinanceContext context,
        CommandMetadata metadata,
        RecordExpensePaymentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        const FinanceAction action = FinanceAction.RecordExpensePayment;
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        if (await FenceAsync(context, action, request.PaymentId, cancellationToken) is { } denied) return denied;
        if (!ValidMetadata(context, metadata))
            return Refused(context, CommandOutcome.ValidationFailed, Issue("idempotencyKey", "idempotency_key_invalid"));

        var keyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var payloadHash = Hash(request);
        if (await PriorAsync(context, RecordCommand, action, keyHash, payloadHash, cancellationToken) is { } prior)
            return prior;
        var row = await dbContext.ExpensePayments.SingleOrDefaultAsync(candidate => candidate.Id == request.PaymentId,
            cancellationToken);
        if (await FenceAsync(context, action, request.PaymentId, cancellationToken) is { } afterRead) return afterRead;
        if (row is null)
            return await TerminalAsync(context, RecordCommand, action, keyHash, payloadHash, null,
                Refused(context, CommandOutcome.ValidationFailed, Issue("paymentId", "payment_not_found")),
                cancellationToken);
        if (VersionText(row.Version) != request.ExpectedVersion)
            return await TerminalAsync(context, RecordCommand, action, keyHash, payloadHash, row.Id,
                Refused(context, CommandOutcome.VersionConflict, Issue("expectedVersion", "version_conflict")),
                cancellationToken);
        var recordIssues = ExpensePaymentValidator.ValidateRecord(ToStatus(row.Status), request);
        if (recordIssues.Count > 0) return Refused(context, CommandOutcome.ValidationFailed, [.. recordIssues]);

        var preparation = Preparation(row);
        if (!KnownExpenseItem(preparation.ExpenseItemId))
            return Refused(context, CommandOutcome.ValidationFailed, Issue("expenseItemId", "expense_item_unknown"));
        if (await RequireEligibleTreasuryAsync(context, preparation, action, row.Id, cancellationToken)
            is { } treasuryFailure) return treasuryFailure;
        if (await ValidateMoneyProfileAsync(context, preparation, row.Id, action, cancellationToken)
            is { } invalid) return invalid;

        var now = timeProvider.GetUtcNow();
        var period = await periodRule.CheckAsync(context, row.OccurredOn, row.ValueOn, now, cancellationToken);
        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } afterPeriod) return afterPeriod;
        if (period.Outcome != RecordPeriodOutcome.Open)
            return Refused(context, period.Outcome == RecordPeriodOutcome.Unavailable
                ? CommandOutcome.Unavailable : CommandOutcome.ValidationFailed,
                Issue("occurredOn", period.Code ?? (period.Outcome == RecordPeriodOutcome.Unavailable
                    ? RecordPeriodAnswer.RuleUnavailableCode : RecordPeriodAnswer.PeriodNotOpenCode)));

        // FNS-D11 / FNS-D22: the CURRENT balance at record time must cover the payment; never clipped.
        TreasuryBalanceAnswer? balance;
        try
        {
            balance = await balanceGuard.CheckOutgoingAsync(context, preparation.SourceKind,
                preparation.TreasuryAccountId, preparation.Money, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            balance = null;
        }

        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } afterBalance) return afterBalance;
        if (balance is null || balance.Outcome == TreasuryBalanceOutcome.Unavailable)
            return Refused(context, CommandOutcome.Unavailable,
                Issue("treasuryAccountId", TreasuryBalanceAnswer.UnavailableCode));
        if (balance.Outcome != TreasuryBalanceOutcome.Sufficient)
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("money.amount", TreasuryBalanceAnswer.InsufficientCode));

        var before = AuditJson(row);
        var numbering = await FinanceDocumentNumbering.ReserveAsync(dbContext, documentNumbers, context, metadata,
            envelopes.Record, SettingsFinanceDocumentPurposeV1.ExpensePaymentRecord, row.Id, row.OccurredOn, cancellationToken);
        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } afterNumber) return afterNumber;
        if (numbering.Number is not { } number) return Refused(context,
            numbering.Denied ? CommandOutcome.Denied : CommandOutcome.Unavailable,
            Issue("documentNumber", numbering.Code ?? SettingsDocumentNumberSeriesProblemCodesV1.Unavailable));
        row.DocumentNumber = number.Number;
        row.DocumentNumberFormatVersion = number.FormatVersion;

        var versionBefore = row.Version;
        row.Status = ExpensePaymentStatuses.Recorded;
        row.RecordedBy = context.ActorId;
        row.RecordedAtUtc = now;
        row.PaymentConfirmed = true;
        row.Version = checked(row.Version + 1);
        var result = Succeeded(context, row);
        AddCommand(context, RecordCommand, keyHash, payloadHash, row.Id, result, now);
        Audit(context, metadata, row, envelopes.Record, "finance.expense-payment.recorded.v1", "Record",
            versionBefore, before);
        FinanceFinancialMovementOutboxV1.Expense(dbContext, context, View(row));
        if (await FenceAsync(context, action, row.Id, cancellationToken) is { } beforeSave) return beforeSave;
        return await SaveAsync(context, RecordCommand, action, keyHash, payloadHash, cancellationToken) ?? result;
    }

    public async Task<ReadResult<ExpensePaymentPage>> ListAuthorizedAsync(
        FinanceContext context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return new ReadResult<ExpensePaymentPage>(ReadOutcome.Denied, null);
        var decision = await permissions.CheckAsync(context, FinanceAction.ReadExpensePayments, null, cancellationToken);
        if (decision != PermissionDecision.Allowed || !SameContext(context))
            return ReadFailure<ExpensePaymentPage>(decision);
        var query = dbContext.ExpensePayments.AsNoTracking();
        if (cursor is not null)
        {
            if (DraftListCursor.Decode(cursor, context.CompanyId) is not { } after)
                return new ReadResult<ExpensePaymentPage>(ReadOutcome.NotFound, null);
            query = query.Where(row => row.PreparedAtUtc < after.CreatedAtUtc
                || (row.PreparedAtUtc == after.CreatedAtUtc && row.Id.CompareTo(after.Id) < 0));
        }

        var rows = await query.OrderByDescending(row => row.PreparedAtUtc).ThenByDescending(row => row.Id)
            .Take(PageSize + 1).ToListAsync(cancellationToken);
        var again = await permissions.CheckAsync(context, FinanceAction.ReadExpensePayments, null, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context)) return ReadFailure<ExpensePaymentPage>(again);
        var page = rows.Take(PageSize).ToArray();
        var next = rows.Count > PageSize
            ? DraftListCursor.Encode(context.CompanyId, page[^1].PreparedAtUtc, page[^1].Id)
            : null;
        return new ReadResult<ExpensePaymentPage>(
            ReadOutcome.Found,
            new ExpensePaymentPage(context.CompanyId, page.Select(View).ToArray(), next));
    }

    public async Task<ReadResult<ExpensePaymentView>> DetailAuthorizedAsync(
        FinanceContext context,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return new ReadResult<ExpensePaymentView>(ReadOutcome.Denied, null);
        var decision = await permissions.CheckAsync(context, FinanceAction.ReadExpensePayments, paymentId,
            cancellationToken);
        if (decision != PermissionDecision.Allowed || !SameContext(context))
            return ReadFailure<ExpensePaymentView>(decision);
        var row = await dbContext.ExpensePayments.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == paymentId, cancellationToken);
        var again = await permissions.CheckAsync(context, FinanceAction.ReadExpensePayments, paymentId,
            cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context)) return ReadFailure<ExpensePaymentView>(again);
        return row is null
            ? new ReadResult<ExpensePaymentView>(ReadOutcome.NotFound, null)
            : new ReadResult<ExpensePaymentView>(ReadOutcome.Found, View(row));
    }

    public async Task<ReadResult<ExpenseItemCatalogView>> ExpenseItemsAuthorizedAsync(
        FinanceContext context,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return new ReadResult<ExpenseItemCatalogView>(ReadOutcome.Denied, null);
        var decision = await permissions.CheckAsync(context, FinanceAction.ReadExpensePayments, null, cancellationToken);
        if (decision != PermissionDecision.Allowed || !SameContext(context))
            return ReadFailure<ExpenseItemCatalogView>(decision);
        var items = expenseItems.Items.ToArray();
        var again = await permissions.CheckAsync(context, FinanceAction.ReadExpensePayments, null, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context))
            return ReadFailure<ExpenseItemCatalogView>(again);
        return new ReadResult<ExpenseItemCatalogView>(
            ReadOutcome.Found, new ExpenseItemCatalogView(context.CompanyId, items));
    }

    public async Task<ReadResult<ExpensePaymentDraftContextView>> DraftContextAuthorizedAsync(
        FinanceContext context,
        ExpensePaymentSourceKind sourceKind,
        Guid treasuryAccountId,
        DateOnly occurredOn,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return new ReadResult<ExpensePaymentDraftContextView>(ReadOutcome.Denied, null);
        const FinanceAction action = FinanceAction.SaveExpensePaymentDraft;
        var decision = await permissions.CheckAsync(context, action, null, cancellationToken);
        if (decision != PermissionDecision.Allowed || !SameContext(context))
            return ReadFailure<ExpensePaymentDraftContextView>(decision);
        if (!Enum.IsDefined(sourceKind) || treasuryAccountId == Guid.Empty)
            return new ReadResult<ExpensePaymentDraftContextView>(ReadOutcome.NotFound, null);

        var currencyId = await EligibleCurrencyAsync(sourceKind, treasuryAccountId, cancellationToken);
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
                    return ReadFailure<ExpensePaymentDraftContextView>(afterRefusal);
                return new ReadResult<ExpensePaymentDraftContextView>(
                    exception.Code == "settings_access_denied" ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
            }
        }

        var again = await permissions.CheckAsync(context, action, null, cancellationToken);
        if (again != PermissionDecision.Allowed || !SameContext(context))
            return ReadFailure<ExpensePaymentDraftContextView>(again);
        if (currencyId is not { } eligibleCurrency)
            return new ReadResult<ExpensePaymentDraftContextView>(ReadOutcome.NotFound, null);
        if (profileUnavailable || profile?.Profile is null || profile.Profile.ProfileId == Guid.Empty
            || profile.Profile.Version <= 0 || profile.CompanyId != context.CompanyId
            || profile.CurrencyId != eligibleCurrency
            || !string.Equals(profile.Operation, binding.MoneyOperation, StringComparison.Ordinal) || !profile.IsActive
            || occurredOn < profile.EffectiveFrom
            || (profile.EffectiveThrough is { } through && occurredOn > through))
            return new ReadResult<ExpensePaymentDraftContextView>(ReadOutcome.Unavailable, null);
        return new ReadResult<ExpensePaymentDraftContextView>(ReadOutcome.Found, new ExpensePaymentDraftContextView(
            context.CompanyId, sourceKind, treasuryAccountId, eligibleCurrency,
            new ProfileReference(profile.Profile.ProfileId.ToString("D"),
                profile.Profile.Version.ToString(CultureInfo.InvariantCulture)),
            occurredOn));
    }

    /// <summary>The source account's currency when it may pay (the same rule as the draft save), else null.</summary>
    private async Task<Guid?> EligibleCurrencyAsync(
        ExpensePaymentSourceKind sourceKind,
        Guid treasuryAccountId,
        CancellationToken cancellationToken)
    {
        if (sourceKind == ExpensePaymentSourceKind.Bank)
        {
            var bank = await dbContext.BankAccounts.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == treasuryAccountId, cancellationToken);
            return bank is not null && bank.Status == ApprovedTreasuryAccountStatus
                && bank.MoneyMovementEligible == true && !string.IsNullOrWhiteSpace(bank.Iban)
                && bank.CurrencyId != Guid.Empty
                    ? bank.CurrencyId
                    : null;
        }

        var cash = await dbContext.CashAccounts.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == treasuryAccountId, cancellationToken);
        return cash is not null && cash.Status == ApprovedTreasuryAccountStatus && cash.CurrencyId != Guid.Empty
            && !PassivationPending(cash)
            ? cash.CurrencyId
            : null;
    }

    /// <summary>FNS-D50: the cash account's passivation is proposed and not yet decided.</summary>
    private static bool PassivationPending(CashAccountRow cash) =>
        cash.ChangeKind == CashAccountPassivationChange && cash.ChangeStatus == CashAccountChangePending;

    internal static string Hash<T>(T request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, Json))))
            .ToLowerInvariant();

    private bool KnownExpenseItem(Guid expenseItemId) =>
        expenseItemId != Guid.Empty && expenseItems.Items.Any(item => item.Id == expenseItemId);

    /// <summary>
    /// A bank source must be an approved, money-movement-eligible account with an IBAN; a cash source an approved
    /// cash account whose passivation is not pending (FNS-D50). Both must hold the payment currency. Each read is
    /// Finance's own table.
    /// </summary>
    private async Task<ExpensePaymentCommandResult?> RequireEligibleTreasuryAsync(
        FinanceContext context,
        ExpensePaymentPreparation preparation,
        FinanceAction action,
        Guid? paymentId,
        CancellationToken cancellationToken)
    {
        bool eligible;
        var passivationPending = false;
        if (preparation.SourceKind == ExpensePaymentSourceKind.Bank)
        {
            var bank = await dbContext.BankAccounts.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == preparation.TreasuryAccountId, cancellationToken);
            eligible = bank is not null && bank.Status == ApprovedTreasuryAccountStatus
                && bank.MoneyMovementEligible == true && bank.CurrencyId == preparation.Money.CurrencyId
                && !string.IsNullOrWhiteSpace(bank.Iban);
        }
        else
        {
            var cash = await dbContext.CashAccounts.AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Id == preparation.TreasuryAccountId, cancellationToken);
            eligible = cash is not null && cash.Status == ApprovedTreasuryAccountStatus
                && cash.CurrencyId == preparation.Money.CurrencyId;
            passivationPending = eligible && PassivationPending(cash!);
        }

        if (await FenceAsync(context, action, paymentId, cancellationToken) is { } fenced) return fenced;
        if (!eligible)
            return Refused(context, CommandOutcome.ValidationFailed,
                Issue("treasuryAccountId", "expense_payment_treasury_account_ineligible"));
        return passivationPending
            ? Refused(context, CommandOutcome.ValidationFailed,
                Issue("treasuryAccountId", "expense_payment_treasury_account_passivation_pending"))
            : null;
    }

    private async Task<ExpensePaymentCommandResult?> ValidateMoneyProfileAsync(
        FinanceContext context,
        ExpensePaymentPreparation preparation,
        Guid? paymentId,
        FinanceAction action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(binding.MoneyOperation))
            return Refused(context, CommandOutcome.Unavailable,
                Issue("profile", "finance_operation_mapping_unavailable"));
        try
        {
            var profile = await settings.GetMoneyProfileAsync(preparation.Money.CurrencyId, binding.MoneyOperation,
                preparation.OccurredOn, cancellationToken);
            if (await FenceAsync(context, action, paymentId, cancellationToken) is { } fenced) return fenced;
            if (profile is null)
                return Refused(context, CommandOutcome.Unavailable, Issue("profile", "settings_provider_unavailable"));
            var issues = ExpensePaymentValidator.ValidateMoneyProfile(preparation, context.CompanyId, profile,
                binding.MoneyOperation);
            return issues.Count == 0 ? null : Refused(context, CommandOutcome.ValidationFailed, [.. issues]);
        }
        catch (SettingsPrerequisiteException exception)
        {
            if (await FenceAsync(context, action, paymentId, cancellationToken) is { } fenced) return fenced;
            return exception.Code == "settings_access_denied"
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable, Issue("profile", exception.Code));
        }
    }

    private async Task<ExpensePaymentCommandResult?> PriorAsync(
        FinanceContext context,
        string command,
        FinanceAction action,
        string keyHash,
        string payloadHash,
        CancellationToken cancellationToken)
    {
        var prior = await dbContext.ExpensePaymentCommands.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CommandName == command && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (await FenceAsync(context, action, prior?.PaymentId, cancellationToken) is { } fenced) return fenced;
        if (prior is null) return null;
        if (prior.PayloadHash != payloadHash || prior.ActorId != context.ActorId)
            return Refused(context, CommandOutcome.IdempotencyConflict);
        var stored = JsonSerializer.Deserialize<ExpensePaymentCommandResult>(prior.ResultJson, Json)
            ?? throw new InvalidOperationException("Kayitli masraf odemesi komut sonucu okunamadi.");
        return stored with { IsReplay = true };
    }

    /// <summary>
    /// SINAN_KARARLARI_2026-10-21 P6 (maker != approver) with No 103: whoever revised the draft is a maker as well as
    /// its first preparer, so a successful draft save by the actor for this payment bars that actor from approving it.
    /// The successful saves are the module's own command rows (one per accepted create/update, written with the row).
    /// </summary>
    private async Task<bool> RevisedByAsync(Guid paymentId, string actorId, CancellationToken cancellationToken)
    {
        var results = await dbContext.ExpensePaymentCommands.AsNoTracking()
            .Where(command => command.CommandName == DraftCommand && command.PaymentId == paymentId
                && command.ActorId == actorId)
            .Select(command => command.ResultJson)
            .ToListAsync(cancellationToken);
        return results.Any(json =>
            (JsonSerializer.Deserialize<ExpensePaymentCommandResult>(json, Json)
                ?? throw new InvalidOperationException("Kayitli masraf odemesi komut sonucu okunamadi.")).Outcome
            == CommandOutcome.Succeeded);
    }

    private async Task<ExpensePaymentCommandResult?> SaveAsync(
        FinanceContext context,
        string command,
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
            return await PriorAsync(context, command, action, keyHash, payloadHash, cancellationToken)
                ?? Refused(context, CommandOutcome.VersionConflict, Issue("request", "concurrent_change"));
        }
    }

    private async Task<ExpensePaymentCommandResult> TerminalAsync(
        FinanceContext context,
        string command,
        FinanceAction action,
        string keyHash,
        string payloadHash,
        Guid? paymentId,
        ExpensePaymentCommandResult refusal,
        CancellationToken cancellationToken)
    {
        AddCommand(context, command, keyHash, payloadHash, paymentId, refusal, timeProvider.GetUtcNow());
        if (await FenceAsync(context, action, paymentId, cancellationToken) is { } fenced) return fenced;
        return await SaveAsync(context, command, action, keyHash, payloadHash, cancellationToken) ?? refusal;
    }

    private async Task<ExpensePaymentCommandResult?> FenceAsync(
        FinanceContext context,
        FinanceAction action,
        Guid? paymentId,
        CancellationToken cancellationToken)
    {
        if (!SameContext(context)) return Refused(context, CommandOutcome.Denied);
        var decision = await permissions.CheckAsync(context, action, paymentId, cancellationToken);
        if (decision != PermissionDecision.Allowed)
            return decision == PermissionDecision.Denied
                ? Refused(context, CommandOutcome.Denied)
                : Refused(context, CommandOutcome.Unavailable,
                    Issue("permission", "finance_permission_mapping_unavailable"));
        return SameContext(context) ? null : Refused(context, CommandOutcome.Denied);
    }

    private void AddCommand(
        FinanceContext context,
        string command,
        string keyHash,
        string payloadHash,
        Guid? paymentId,
        ExpensePaymentCommandResult result,
        DateTimeOffset now) => dbContext.ExpensePaymentCommands.Add(new ExpensePaymentCommandRow
        {
            Id = Guid.NewGuid(),
            CompanyId = context.CompanyId,
            CommandName = command,
            IdempotencyKeyHash = keyHash,
            ActorId = context.ActorId,
            PayloadHash = payloadHash,
            PaymentId = paymentId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = context.CorrelationId,
            RecordedAtUtc = now,
        });

    private void Audit(
        FinanceContext context,
        CommandMetadata metadata,
        ExpensePaymentRow row,
        FinanceAuditEnvelope envelope,
        string eventType,
        string operation,
        long? versionBefore,
        string before) => auditEvents.Enqueue(new AuditEventDraft(
            context.CompanyId,
            eventType,
            EntityType,
            row.Id.ToString("D"),
            operation,
            versionBefore,
            row.Version,
            AuditActorTypes.User,
            context.ActorId,
            envelope.PermissionCode,
            envelope.RoleCodes,
            before,
            AuditJson(row),
            "Cariye bagli olmayan masraf odemesi",
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            envelope.SourceChannel,
            envelope.ClientIpAddress,
            envelope.UserAgent,
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

    private static ExpensePaymentStatus ToStatus(int status) => status switch
    {
        ExpensePaymentStatuses.Draft => ExpensePaymentStatus.Draft,
        ExpensePaymentStatuses.Approved => ExpensePaymentStatus.Approved,
        ExpensePaymentStatuses.Recorded => ExpensePaymentStatus.Recorded,
        _ => throw new InvalidOperationException("Bilinmeyen masraf odemesi durumu."),
    };

    private static ExpensePaymentSourceKind ToSourceKind(int sourceKind) => sourceKind switch
    {
        ExpensePaymentSourceKinds.Bank => ExpensePaymentSourceKind.Bank,
        ExpensePaymentSourceKinds.Cash => ExpensePaymentSourceKind.Cash,
        _ => throw new InvalidOperationException("Bilinmeyen masraf odemesi kaynagi."),
    };

    private static int FromSourceKind(ExpensePaymentSourceKind sourceKind) => sourceKind switch
    {
        ExpensePaymentSourceKind.Bank => ExpensePaymentSourceKinds.Bank,
        ExpensePaymentSourceKind.Cash => ExpensePaymentSourceKinds.Cash,
        _ => throw new InvalidOperationException("Bilinmeyen masraf odemesi kaynagi."),
    };

    private static void Apply(ExpensePaymentRow row, ExpensePaymentPreparation preparation)
    {
        row.SourceKind = FromSourceKind(preparation.SourceKind);
        row.TreasuryAccountId = preparation.TreasuryAccountId;
        row.ExpenseItemId = preparation.ExpenseItemId;
        row.CurrencyId = preparation.Money.CurrencyId;
        row.AmountText = preparation.Money.Amount;
        row.ProfileId = preparation.Profile.ProfileId;
        row.ProfileVersion = preparation.Profile.Version;
        row.OccurredOn = preparation.OccurredOn;
        row.ValueOn = preparation.ValueOn;
        row.Reference = preparation.Reference;
        row.Description = preparation.Description;
    }

    private static ExpensePaymentPreparation Preparation(ExpensePaymentRow row) => new ExpensePaymentPreparation(
        ToSourceKind(row.SourceKind), row.TreasuryAccountId, row.ExpenseItemId,
        new MoneyText(row.AmountText, row.CurrencyId), new ProfileReference(row.ProfileId, row.ProfileVersion),
        row.OccurredOn, row.ValueOn, row.Reference, row.Description);

    private static ExpensePaymentView View(ExpensePaymentRow row) => new ExpensePaymentView(
        row.Id, row.CompanyId, ToStatus(row.Status), Preparation(row),
        row.PreparedBy, row.PreparedAtUtc,
        row.ApprovedBy, row.ApprovedAtUtc, row.RecordedBy, row.RecordedAtUtc, row.PaymentConfirmed,
        VersionText(row.Version), FinanceDocumentNumbering.View(row));

    private static ExpensePaymentCommandResult Succeeded(FinanceContext context, ExpensePaymentRow row) =>
        new ExpensePaymentCommandResult(CommandOutcome.Succeeded, View(row), IsReplay: false, context.CorrelationId, []);

    private static ExpensePaymentCommandResult Refused(
        FinanceContext context,
        CommandOutcome outcome,
        params ValidationIssue[] issues) => new ExpensePaymentCommandResult(
            outcome, null, IsReplay: false, context.CorrelationId, issues);

    private static ReadResult<T> ReadFailure<T>(PermissionDecision decision) where T : class =>
        new ReadResult<T>(
            decision == PermissionDecision.Unavailable ? ReadOutcome.Unavailable : ReadOutcome.Denied,
            null);

    private static string VersionText(long version) => version.ToString(CultureInfo.InvariantCulture);
    private static ValidationIssue Issue(string field, string code) => new ValidationIssue(field, code);

    private static string AuditJson(ExpensePaymentRow row) => JsonSerializer.Serialize(new
    {
        row.Id,
        documentNumber = FinanceDocumentNumbering.View(row),
        version = VersionText(row.Version),
        status = ToStatus(row.Status).ToString(),
        sourceKind = ToSourceKind(row.SourceKind).ToString(),
        row.TreasuryAccountId,
        row.ExpenseItemId,
        row.CurrencyId,
        row.OccurredOn,
        row.ValueOn,
        profile = new { row.ProfileId, row.ProfileVersion },
        row.PreparedBy,
        row.ApprovedBy,
        row.RecordedBy,
        row.PaymentConfirmed,
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

/// <summary>
/// FNS-D25: the single "Masraf" expense item. Its identity is fixed so every payment recorded before the expense
/// structure is specified (NEEDS-SPEC) names the same item and can be carried over. The client shows the localized
/// label for <see cref="GeneralExpenseCode"/>.
/// </summary>
public sealed class SingleExpenseItemCatalog : IFinanceExpenseItemCatalog
{
    public const string GeneralExpenseCode = "general-expense";
    public static readonly Guid GeneralExpenseId = Guid.Parse("50c73c05-2987-4eb1-ba65-69342f8acbcf");

    private static readonly IReadOnlyList<ExpenseItemView> Catalog = [new ExpenseItemView(GeneralExpenseId, GeneralExpenseCode)];

    public IReadOnlyList<ExpenseItemView> Items => Catalog;
}

/// <summary>
/// FNS-D11 fail-closed default: no treasury balance source (opening balances, bank and cash movements) is defined
/// yet, so no record can prove the balance covers it. Every final record is refused as unavailable until the slice
/// that owns the balance registers the real guard before this one.
/// </summary>
public sealed class UnavailableTreasuryBalanceGuard : IFinanceTreasuryBalanceGuard
{
    public Task<TreasuryBalanceAnswer> CheckOutgoingAsync(
        FinanceContext context,
        ExpensePaymentSourceKind sourceKind,
        Guid treasuryAccountId,
        MoneyText money,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new TreasuryBalanceAnswer(TreasuryBalanceOutcome.Unavailable,
            TreasuryBalanceAnswer.UnavailableCode));
    }
}

/// <summary>
/// The cash account passivation balance seam once cash can leave a cash account: a recorded cash expense payment
/// means the balance is no longer provably zero, so the reading is Unavailable (passivation refused) for that
/// account; an account with no recorded cash movement still reads Zero. Only Finance's own rows of the
/// server-resolved company are read.
/// </summary>
public sealed class ExpensePaymentAwareCashAccountBalanceReader(FinanceDbContext dbContext)
    : IFinanceCashAccountBalanceReader
{
    public async Task<CashAccountBalanceState> ReadAsync(
        Guid companyId,
        Guid cashAccountId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || cashAccountId == Guid.Empty) return CashAccountBalanceState.Unavailable;
        var moved = await dbContext.ExpensePayments.AsNoTracking().AnyAsync(row =>
            row.CompanyId == companyId && row.SourceKind == ExpensePaymentSourceKinds.Cash
            && row.TreasuryAccountId == cashAccountId && row.Status == ExpensePaymentStatuses.Recorded,
            cancellationToken);
        return moved ? CashAccountBalanceState.Unavailable : CashAccountBalanceState.Zero;
    }
}
