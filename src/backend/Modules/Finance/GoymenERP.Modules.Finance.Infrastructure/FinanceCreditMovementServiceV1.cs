using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>FNS-D97/D101/D108: source actuals, maker/checker, one transactional bank fact and outbox event.
/// A contract approval or a planned installment never creates an actual movement.</summary>
public sealed class FinanceCreditMovementServiceV1(FinanceDbContext db, ICompanyContext company, ICurrentUser user,
    IFinancePermissionPort permissions, ISettingsPrerequisiteProvider settings, IFinanceOpeningPeriodReader openingPeriod,
    IFinanceCreditAccountingPeriodPortV1 accountingPeriod, IFinanceTreasuryBalanceGuard treasury,
    IAuditEventCollector audit, FinanceCreditAccountEnvelopes envelopes, TimeProvider time,
    IAuditTransactionWriterV1? auditOwner = null, FinanceCreditGroupGateV1? groupGate = null) : IFinanceCreditMovementServiceV1
{
    // FNS-D106: without an explicit gate every group is closed (fail-closed).
    private readonly FinanceCreditGroupGateV1 _groupGate = groupGate ?? FinanceCreditGroupGateV1.NoneApproved;

    public const string ProposeCommand = "finance.credit-movement.propose";
    public const string DecideCommand = "finance.credit-movement.decide";
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();
    private static readonly JsonSerializerOptions AuditJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<CreditMovementResultV1> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        CreditMovementProposeV1 request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        const FinanceAction action = FinanceAction.ProposeCredit;
        if (await Start(context, metadata, action, cancellationToken) is { } denied) return denied;
        var issues = FinanceCreditMovementPolicyV1.Validate(request);
        if (issues.Count != 0) return Failure(context, CommandOutcome.ValidationFailed, [.. issues]);
        var payload = FinanceCreditAccountService.PayloadHash(ProposeCommand, request);
        return await Transaction(context, async () =>
        {
            if (await Prior(context, metadata, action, ProposeCommand, payload, cancellationToken) is { } prior) return prior;
            var account = await db.CreditAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.CreditAccountId, cancellationToken);
            if (await Fence(context, action, cancellationToken) is { } fenced) return fenced;
            if (account is null) return await Terminal(context, metadata, action, ProposeCommand, payload, CommandOutcome.ValidationFailed, cancellationToken, new ValidationIssue("creditAccountId", "credit_account_not_found"));
            if (account.Version.ToString(CultureInfo.InvariantCulture) != request.ExpectedCreditVersion || account.Status != 2)
                return await Terminal(context, metadata, action, ProposeCommand, payload, CommandOutcome.VersionConflict, cancellationToken, new ValidationIssue("creditAccountId", "credit_account_must_be_current_and_approved"));
            if (!_groupGate.MovementsOpen(account.CreditGroup))
                return await Terminal(context, metadata, action, ProposeCommand, payload, CommandOutcome.ValidationFailed, cancellationToken, new ValidationIssue("creditAccountId", FinanceCreditGroupGateV1.GroupClosedCode));
            var validation = await Sources(context, action, request, account, cancellationToken);
            if (validation.Failure is { } invalid) return invalid;
            var sourceHash = SourceHash(request);
            if (await db.CreditMovements.AsNoTracking().AnyAsync(x => x.CreditAccountId == account.Id && x.SourceIdentityHash == sourceHash && (x.Status == 1 || x.Status == 2), cancellationToken))
                return await Terminal(context, metadata, action, ProposeCommand, payload, CommandOutcome.ValidationFailed, cancellationToken, new ValidationIssue("sourceDocument", "credit_movement_source_already_recorded"));
            if (await Opening(context, action, request, cancellationToken) is { } openingFailure) return openingFailure;
            if (await Fence(context, action, cancellationToken) is { } last) return last;
            var row = new FinanceCreditMovementRow
            {
                Id = Guid.NewGuid(), CompanyId = context.CompanyId, CreditAccountId = account.Id, CurrencyId = account.CurrencyId,
                Version = 1, Status = 1, Kind = (int)request.Kind, SourceJson = JsonSerializer.Serialize(request, Json),
                SourceIdentityHash = sourceHash, MoneyProfileJson = JsonSerializer.Serialize(validation.Profile, Json),
                OpeningPeriodVersion = validation.OpeningVersion, BankAccountId = request.BankEffect?.BankAccountId,
                BankAmountText = request.BankEffect?.Amount, ReplacesOpeningMovementId = request.ReplacesOpeningMovementId,
                ProposedBy = context.ActorId, ProposedAtUtc = time.GetUtcNow()
            };
            db.CreditMovements.Add(row);
            Audit(context, metadata, envelopes.Propose, row, "finance.credit-movement.proposed.v1", null, "{}", request.Reason);
            return await Save(context, metadata, action, ProposeCommand, payload, Success(context, row), cancellationToken);
        }, cancellationToken);
    }

    public async Task<CreditMovementResultV1> DecideAsync(FinanceContext context, CommandMetadata metadata,
        CreditMovementDecisionV1 request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        const FinanceAction action = FinanceAction.ApproveCredit;
        if (await Start(context, metadata, action, cancellationToken) is { } denied) return denied;
        if (request.MovementId == Guid.Empty || !FinanceCreditMovementPolicyV1.Text(request.ExpectedVersion, 64)
            || (!request.Approve && !FinanceCreditMovementPolicyV1.Text(request.Reason, 1000)) || (request.Approve && request.Reason is not null))
            return Failure(context, CommandOutcome.ValidationFailed, new ValidationIssue("request", "credit_movement_decision_invalid"));
        var payload = FinanceCreditAccountService.PayloadHash(DecideCommand, request);
        return await Transaction(context, async () =>
        {
            if (await Prior(context, metadata, action, DecideCommand, payload, cancellationToken) is { } prior) return prior;
            var row = await db.CreditMovements.SingleOrDefaultAsync(x => x.Id == request.MovementId, cancellationToken);
            if (await Fence(context, action, cancellationToken) is { } fenced) return fenced;
            if (row is null) return await Terminal(context, metadata, action, DecideCommand, payload, CommandOutcome.ValidationFailed, cancellationToken, new ValidationIssue("movementId", "credit_movement_not_found"));
            if (row.Version.ToString(CultureInfo.InvariantCulture) != request.ExpectedVersion || row.Status != 1)
                return await Terminal(context, metadata, action, DecideCommand, payload, CommandOutcome.VersionConflict, cancellationToken);
            if (row.ProposedBy == context.ActorId)
                return await Terminal(context, metadata, action, DecideCommand, payload, CommandOutcome.ValidationFailed, cancellationToken, new ValidationIssue("actor", "credit_movement_maker_cannot_approve"));
            var source = ReadSource(row);
            if (request.Approve)
            {
                var account = await db.CreditAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.CreditAccountId, cancellationToken);
                if (await Fence(context, action, cancellationToken) is { } afterAccount) return afterAccount;
                if (account is null || account.Status != 2 || account.CurrencyId != row.CurrencyId || account.Version.ToString(CultureInfo.InvariantCulture) != source.ExpectedCreditVersion)
                    return await Terminal(context, metadata, action, DecideCommand, payload, CommandOutcome.VersionConflict, cancellationToken);
                if (!_groupGate.MovementsOpen(account.CreditGroup))
                    return await Terminal(context, metadata, action, DecideCommand, payload, CommandOutcome.ValidationFailed, cancellationToken, new ValidationIssue("creditAccountId", FinanceCreditGroupGateV1.GroupClosedCode));
                var validation = await Sources(context, action, source, account, cancellationToken);
                if (validation.Failure is { } invalid) return invalid;
                if (JsonSerializer.Serialize(validation.Profile, Json) != row.MoneyProfileJson || validation.OpeningVersion != row.OpeningPeriodVersion)
                    return await Terminal(context, metadata, action, DecideCommand, payload, CommandOutcome.VersionConflict, cancellationToken, new ValidationIssue("source", "credit_movement_source_changed"));
                if (await Opening(context, action, source, cancellationToken, row.Id) is { } openingFailure) return openingFailure;
                if (source.Kind == CreditMovementKindV1.Repayment && source.BankEffect is { } bank)
                {
                    var balance = await treasury.CheckOutgoingAsync(context, ExpensePaymentSourceKind.Bank, bank.BankAccountId, new MoneyText(bank.Amount, row.CurrencyId), cancellationToken);
                    if (await Fence(context, action, cancellationToken) is { } afterBalance) return afterBalance;
                    if (balance.Outcome != TreasuryBalanceOutcome.Sufficient)
                        return Failure(context, balance.Outcome == TreasuryBalanceOutcome.Insufficient ? CommandOutcome.ValidationFailed : CommandOutcome.Unavailable,
                            new ValidationIssue("bankEffect", balance.Code ?? TreasuryBalanceAnswer.UnavailableCode));
                }
            }
            var before = JsonSerializer.Serialize(View(row, context.ActorId), Json);
            var version = row.Version;
            row.Status = request.Approve ? 2 : 3; row.Version++; row.DecidedBy = context.ActorId; row.DecidedAtUtc = time.GetUtcNow();
            row.RejectionReason = request.Approve ? null : request.Reason;
            if (request.Approve)
            {
                if (source.ReplacesOpeningMovementId is { } originalId)
                {
                    var original = await db.CreditMovements.SingleAsync(x => x.Id == originalId, cancellationToken);
                    var originalBefore = JsonSerializer.Serialize(View(original, context.ActorId), Json);
                    var originalVersion = original.Version;
                    original.ReversedByMovementId = row.Id; original.Version++;
                    AddEvent(context, original, ReadSource(original), originalId, source.AccountingDate, source.Reason);
                    Audit(context, metadata, envelopes.Approve, original, "finance.credit-opening.reversed.v1", originalVersion, originalBefore, source.Reason);
                }
                AddEvent(context, row, source, null, source.AccountingDate, source.Reason);
            }
            if (await Fence(context, action, cancellationToken) is { } last) { db.ChangeTracker.Clear(); auditOwner?.DiscardPending(); return last; }
            Audit(context, metadata, envelopes.Approve, row, request.Approve ? "finance.credit-movement.approved.v1" : "finance.credit-movement.rejected.v1",
                version, before, request.Reason ?? source.Reason);
            return await Save(context, metadata, action, DecideCommand, payload, Success(context, row), cancellationToken);
        }, cancellationToken);
    }

    public async Task<ReadResult<CreditMovementPageV1>> ListAsync(FinanceContext context, Guid creditAccountId, CancellationToken cancellationToken)
    {
        if (await Fence(context, FinanceAction.ReadCredits, cancellationToken) is { } failure) return new ReadResult<CreditMovementPageV1>(ReadFailure(failure), null);
        var account = await db.CreditAccounts.AsNoTracking().AnyAsync(x => x.Id == creditAccountId, cancellationToken);
        var rows = account ? await db.CreditMovements.AsNoTracking().Where(x => x.CreditAccountId == creditAccountId)
            .OrderByDescending(x => x.ProposedAtUtc).ThenBy(x => x.Id).ToListAsync(cancellationToken) : [];
        if (await Fence(context, FinanceAction.ReadCredits, cancellationToken) is { } after) return new ReadResult<CreditMovementPageV1>(ReadFailure(after), null);
        return account ? new ReadResult<CreditMovementPageV1>(ReadOutcome.Found, new CreditMovementPageV1(context.CompanyId, rows.Select(x => View(x, context.ActorId)).ToArray())) : new ReadResult<CreditMovementPageV1>(ReadOutcome.NotFound, null);
    }

    private async Task<(CreditMovementResultV1? Failure, CreditPlanToleranceSnapshotV1? Profile, string? OpeningVersion)> Sources(
        FinanceContext context, FinanceAction action, CreditMovementProposeV1 source, CreditAccountRow account, CancellationToken ct)
    {
        var shape = FinanceCreditMovementPolicyV1.Validate(source);
        if (shape.Count != 0) return (Failure(context, CommandOutcome.ValidationFailed, [.. shape]), null, null);
        if (account.SourceProfileJson is null) return (Failure(context, CommandOutcome.Unavailable, new ValidationIssue("creditAccountId", "credit_source_profile_required")), null, null);
        var profile = JsonSerializer.Deserialize<CreditAccountProfileV1>(account.SourceProfileJson, Json);
        if (profile is null || (account.CreditGroup == FinanceCreditAccountService.NonCashGroup && source.BankEffect is not null))
            return (Failure(context, CommandOutcome.ValidationFailed, new ValidationIssue("bankEffect", "credit_non_cash_has_no_bank_effect")), null, null);
        MoneyProfileV1? money;
        try { money = await settings.GetMoneyProfileAsync(account.CurrencyId, FinanceCreditAccountService.PlanMoneyOperation, source.TransactionDate, ct); }
        catch (SettingsPrerequisiteException) { money = null; }
        if (await Fence(context, action, ct) is { } afterMoney) return (afterMoney, null, null);
        if (money is null || money.CompanyId != context.CompanyId || money.CurrencyId != account.CurrencyId || !money.IsActive
            || money.Operation != FinanceCreditAccountService.PlanMoneyOperation || money.Profile is null || money.Profile.ProfileId == Guid.Empty
            || money.Profile.Version < 1 || money.Scale is < 0 or > 28 || money.EffectiveFrom > source.TransactionDate || money.EffectiveThrough < source.TransactionDate)
            return (Failure(context, CommandOutcome.Unavailable, new ValidationIssue("profile", "credit_plan_profile_unavailable")), null, null);
        // The source is kept exactly, including its fractional precision. The currency smallest unit is
        // FNS-D99's plan tolerance (FNS-D116), not authorization to round or replace actual bank components.
        if (source.BankEffect is { } bank)
        {
            var eligible = await db.BankAccounts.AsNoTracking().AnyAsync(x => x.Id == bank.BankAccountId && x.CurrencyId == account.CurrencyId
                && x.Status == 2 && x.MoneyMovementEligible == true && x.Iban != null, ct);
            if (await Fence(context, action, ct) is { } afterBank) return (afterBank, null, null);
            if (!eligible) return (Failure(context, CommandOutcome.ValidationFailed, new ValidationIssue("bankEffect", "credit_bank_account_not_eligible")), null, null);
        }
        string? openingVersion = null;
        if (source.Kind == CreditMovementKindV1.OpeningBalance)
        {
            var period = await openingPeriod.ReadAsync(context.CompanyId, ct);
            if (await Fence(context, action, ct) is { } afterPeriod) return (afterPeriod, null, null);
            if (!FinanceCreditMovementPolicyV1.Text(period.Version, 64) || period.State is not (OpeningPeriodState.Open or OpeningPeriodState.Closed))
                return (Failure(context, CommandOutcome.Unavailable, new ValidationIssue("openingPeriod", "credit_opening_period_unavailable")), null, null);
            if (source.ReplacesOpeningMovementId is null && period.State != OpeningPeriodState.Open)
                return (Failure(context, CommandOutcome.ValidationFailed, new ValidationIssue("openingPeriod", "credit_opening_period_closed")), null, null);
            openingVersion = period.Version;
        }
        var accounting = await accountingPeriod.ReadAsync(context, source.AccountingDate, ct);
        if (await Fence(context, action, ct) is { } afterAccounting) return (afterAccounting, null, null);
        if (accounting.Outcome != RecordPeriodOutcome.Open)
            return (Failure(context, accounting.Outcome == RecordPeriodOutcome.NotOpen ? CommandOutcome.ValidationFailed : CommandOutcome.Unavailable,
                new ValidationIssue("accountingDate", accounting.Code ?? RecordPeriodAnswer.RuleUnavailableCode)), null, null);
        return (null, new CreditPlanToleranceSnapshotV1(money.Profile.ProfileId, money.Profile.Version, money.Scale, new FinanceExactDecimalText(1, money.Scale).Text), openingVersion);
    }

    private async Task<CreditMovementResultV1?> Opening(FinanceContext context, FinanceAction action, CreditMovementProposeV1 source,
        CancellationToken ct, Guid? ownProposal = null)
    {
        if (source.Kind != CreditMovementKindV1.OpeningBalance) return null;
        if (source.ReplacesOpeningMovementId is { } originalId)
        {
            var original = await db.CreditMovements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == originalId, ct);
            var pending = await db.CreditMovements.AsNoTracking().AnyAsync(x => x.ReplacesOpeningMovementId == originalId && x.Id != ownProposal && (x.Status == 1 || x.Status == 2), ct);
            if (await Fence(context, action, ct) is { } fenced) return fenced;
            if (original is null || original.CreditAccountId != source.CreditAccountId || original.Kind != 4 || original.Status != 2
                || original.ReversedByMovementId is not null || original.Version.ToString(CultureInfo.InvariantCulture) != source.ExpectedOpeningMovementVersion || pending)
                return Failure(context, CommandOutcome.VersionConflict, new ValidationIssue("replacesOpeningMovementId", "credit_opening_correction_source_changed"));
        }
        else
        {
            var exists = await db.CreditMovements.AsNoTracking().AnyAsync(x => x.CreditAccountId == source.CreditAccountId && x.Kind == 4 && x.Id != ownProposal
                && x.ReplacesOpeningMovementId == null && (x.Status == 1 || x.Status == 2), ct);
            if (await Fence(context, action, ct) is { } fenced) return fenced;
            if (exists) return Failure(context, CommandOutcome.ValidationFailed, new ValidationIssue("creditAccountId", "credit_opening_already_exists"));
        }
        return null;
    }

    private void AddEvent(FinanceContext context, FinanceCreditMovementRow row, CreditMovementProposeV1 source, Guid? reverses,
        DateOnly accountingDate, string reason)
    {
        var eventId = Guid.NewGuid();
        var payload = new FinanceCreditMovementRecordedV1(eventId, context.CompanyId, row.Id, row.CreditAccountId, row.CurrencyId,
            source.Kind, source.SourceDocument, source.TransactionDate, accountingDate, source.Components, source.BankEffect, reverses, context.ActorId, reason);
        // Reversal is explicit, not a negative source number. The original immutable fact remains available.
        db.OutboxMessages.Add(new FinanceOutboxRow() { Id = eventId, CompanyId = context.CompanyId, AggregateId = row.Id, AggregateVersion = row.Version,
            EventType = FinanceCreditMovementEventsV1.Recorded, SchemaVersion = 1, PayloadJson = JsonSerializer.Serialize(payload, Json),
            OccurredAtUtc = time.GetUtcNow(), CorrelationId = context.CorrelationId, CausationId = context.CausationId });
    }
    private async Task<CreditMovementResultV1?> Start(FinanceContext context, CommandMetadata metadata, FinanceAction action, CancellationToken ct)
    {
        if (await Fence(context, action, ct) is { } failure) return failure;
        return !FinanceCreditMovementPolicyV1.Text(metadata.IdempotencyKey, 128) || !FinanceCreditMovementPolicyV1.Text(context.CorrelationId, 128)
            || context.ActorId.Length > 128 || context.CausationId is { Length: > 128 }
            ? Failure(context, CommandOutcome.ValidationFailed, new ValidationIssue("context", "credit_movement_metadata_invalid")) : null;
    }
    private async Task<CreditMovementResultV1?> Fence(FinanceContext context, FinanceAction action, CancellationToken ct)
    {
        if (!Same(context)) return Failure(context, CommandOutcome.Denied);
        var answer = await permissions.CheckAsync(context, action, null, ct);
        if (!Same(context) || answer == PermissionDecision.Denied) return Failure(context, CommandOutcome.Denied);
        return answer == PermissionDecision.Allowed ? null : Failure(context, CommandOutcome.Unavailable);
    }
    private bool Same(FinanceContext context) => user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.UserId)
        && user.UserId == context.ActorId && company.IsResolved && company.CompanyId != Guid.Empty && company.CompanyId == context.CompanyId && user.CanAccessCompany(context.CompanyId);
    private async Task<CreditMovementResultV1?> Prior(FinanceContext context, CommandMetadata metadata, FinanceAction action, string command, string payload, CancellationToken ct)
    {
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var row = await db.CreditMovementCommands.AsNoTracking().SingleOrDefaultAsync(x => x.Command == command && x.KeyHash == key, ct);
        if (await Fence(context, action, ct) is { } fenced) return fenced;
        if (row is null) return null;
        if (row.ActorId != context.ActorId || row.PayloadHash != payload) return Failure(context, CommandOutcome.IdempotencyConflict);
        return (JsonSerializer.Deserialize<CreditMovementResultV1>(row.ResultJson, Json) ?? throw new InvalidOperationException("Kredi hareket makbuzu okunamadı.")) with { IsReplay = true };
    }
    private async Task<CreditMovementResultV1> Save(FinanceContext context, CommandMetadata metadata, FinanceAction action, string command,
        string payload, CreditMovementResultV1 result, CancellationToken ct)
    {
        if (await Fence(context, action, ct) is { } fenced) { db.ChangeTracker.Clear(); auditOwner?.DiscardPending(); return fenced; }
        db.CreditMovementCommands.Add(new FinanceCreditMovementCommandRow() { CompanyId = context.CompanyId, Command = command, KeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            ActorId = context.ActorId, PayloadHash = payload, ResultJson = JsonSerializer.Serialize(result, Json) });
        await db.SaveChangesAsync(ct);
        return result;
    }
    private Task<CreditMovementResultV1> Terminal(FinanceContext context, CommandMetadata metadata, FinanceAction action, string command,
        string payload, CommandOutcome outcome, CancellationToken ct, params ValidationIssue[] issues)
    {
        db.ChangeTracker.Clear(); auditOwner?.DiscardPending();
        return Save(context, metadata, action, command, payload, Failure(context, outcome, issues), ct);
    }
    private Task<CreditMovementResultV1> Transaction(FinanceContext context, Func<Task<CreditMovementResultV1>> action, CancellationToken ct) =>
        FinanceTreasuryTransaction.ExecuteAsync(db, action, async () => await Fence(context, FinanceAction.ReadCredits, ct)
            ?? Failure(context, CommandOutcome.Unavailable, new ValidationIssue("request", "credit_movement_transaction_unavailable")), auditOwner, ct);
    private void Audit(FinanceContext context, CommandMetadata metadata, FinanceAuditEnvelope envelope, FinanceCreditMovementRow row,
        string eventType, long? previousVersion, string before, string reason) => audit.Enqueue(new AuditEventDraft(context.CompanyId, eventType,
            "Finance.CreditMovement", row.Id.ToString("D"), previousVersion is null ? "Propose" : "Decide", previousVersion, row.Version,
            AuditActorTypes.User, context.ActorId, envelope.PermissionCode, envelope.RoleCodes, before, JsonSerializer.Serialize(View(row, context.ActorId), Json),
            reason, JsonSerializer.Serialize(new { rule = "FNS-D97/FNS-D108", row.ProposedBy, row.DecidedBy }, AuditJson), AuditAiInvolvement.None, null,
            envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent, context.CorrelationId, context.CausationId, metadata.IdempotencyKey));
    private static string SourceHash(CreditMovementProposeV1 source) => FinanceCreditAccountService.PayloadHash("credit-actual-source",
        new { source.Kind, source.SourceDocument.Reference, source.SourceDocument.DocumentDate, source.SourceDocument.Revision });
    private static CreditMovementProposeV1 ReadSource(FinanceCreditMovementRow row) => JsonSerializer.Deserialize<CreditMovementProposeV1>(row.SourceJson, Json)
        ?? throw new InvalidOperationException("Kredi hareket kaynağı okunamadı.");
    private static CreditMovementViewV1 View(FinanceCreditMovementRow row, string actor) => new CreditMovementViewV1(row.Id, row.CompanyId, row.Version.ToString(CultureInfo.InvariantCulture),
        (CreditMovementStatusV1)row.Status, ReadSource(row), row.CurrencyId, row.ProposedBy == actor, row.ProposedAtUtc, row.DecidedAtUtc,
        row.RejectionReason, row.ReversedByMovementId, row.OpeningPeriodVersion,
        JsonSerializer.Deserialize<CreditPlanToleranceSnapshotV1>(row.MoneyProfileJson, Json) ?? throw new InvalidOperationException("Kredi para profili okunamadı."));
    private static CreditMovementResultV1 Success(FinanceContext context, FinanceCreditMovementRow row) => new CreditMovementResultV1(CommandOutcome.Succeeded, View(row, context.ActorId), false, context.CorrelationId, []);
    private static CreditMovementResultV1 Failure(FinanceContext context, CommandOutcome outcome, params ValidationIssue[] issues) => new CreditMovementResultV1(outcome, null, false, context.CorrelationId, issues);
    private static ReadOutcome ReadFailure(CreditMovementResultV1 failure) => failure.Outcome == CommandOutcome.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable;
}
