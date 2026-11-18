using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>FNS-D95/D100: source-backed bank/type limit, separate maker/checker, advisory exact-principal assessment.</summary>
public sealed class FinanceCreditLimitServiceV1(FinanceDbContext db, ICompanyContext company, ICurrentUser user,
    IFinancePermissionPort permissions, IFinanceCreditLenderPortV1 lenders, IAuditEventCollector audit,
    FinanceCreditAccountEnvelopes envelopes, TimeProvider time, IAuditTransactionWriterV1? auditOwner = null) : IFinanceCreditLimitServiceV1
{
    public const string ProposeCommand = "finance.credit-limit.propose";
    public const string DecideCommand = "finance.credit-limit.decide";
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<CreditLimitResultV1> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        CreditLimitProposeV1 request, CancellationToken cancellationToken)
    {
        const FinanceAction action = FinanceAction.ProposeCredit;
        if (await Start(context, metadata, action, cancellationToken) is { } refused) return refused;
        if (request.CreditAccountId == Guid.Empty || !Text(request.ExpectedCreditVersion, 64) || !Text(request.Amount, 128)
            || !FinanceExactDecimalText.TryParseNonNegative(request.Amount, out _) || !Text(request.Reason, 1000)
            || request.SourceDocument is not { } source || !Text(source.Reference, 200) || !Text(source.Revision, 100)
            || source.DocumentDate == default || request.ExpectedCurrentLimitId == Guid.Empty
            || (request.ExpectedCurrentLimitId is null ? request.ExpectedCurrentLimitVersion is not null : !Text(request.ExpectedCurrentLimitVersion, 64)))
            return Failure(context, CommandOutcome.ValidationFailed, "credit_limit_source_invalid");
        var hash = FinanceCreditAccountService.PayloadHash(ProposeCommand, request);
        return await Transaction(context, async () =>
        {
            if (await Prior(context, metadata, action, ProposeCommand, hash, cancellationToken) is { } prior) return prior;
            var account = await db.CreditAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.CreditAccountId, cancellationToken);
            if (await Fence(context, action, cancellationToken) is { } fenced) return fenced;
            if (!Usable(account) || account!.Version.ToString(CultureInfo.InvariantCulture) != request.ExpectedCreditVersion)
                return Failure(context, CommandOutcome.VersionConflict, "credit_limit_account_changed");
            if (!await Lender(context, account, cancellationToken)) return Failure(context, CommandOutcome.Unavailable, "credit_lender_unavailable");
            if (await Fence(context, action, cancellationToken) is { } afterOwner) return afterOwner;
            var current = await Key(account).AsNoTracking().Where(x => x.Status == 2).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(cancellationToken);
            if (!Expected(current, request)) return Failure(context, CommandOutcome.VersionConflict, "credit_limit_current_changed");
            if (await Key(account).AnyAsync(x => x.Status == 1, cancellationToken)) return Failure(context, CommandOutcome.VersionConflict, "credit_limit_proposal_exists");
            if (await Fence(context, action, cancellationToken) is { } last) return last;
            var row = new FinanceCreditLimitRow { Id = Guid.NewGuid(), CompanyId = context.CompanyId, CreditAccountId = account.Id,
                LenderPartyId = account.LenderPartyId!.Value, LenderCompanyPartyAccountId = account.LenderCompanyPartyAccountId!.Value,
                CreditGroup = account.CreditGroup, CurrencyId = account.CurrencyId, AmountText = request.Amount, Status = 1,
                Version = 1, Revision = checked((current?.Revision ?? 0) + 1), PreviousLimitId = current?.Id,
                SourceJson = JsonSerializer.Serialize(request, Json), ProposedBy = context.ActorId, ProposedAtUtc = time.GetUtcNow() };
            db.CreditLimits.Add(row);
            Audit(context, metadata, row, envelopes.Propose, "Propose", null, "{}", request.Reason);
            return await Save(context, metadata, action, ProposeCommand, hash, Success(context, row), cancellationToken);
        }, cancellationToken);
    }

    public async Task<CreditLimitResultV1> DecideAsync(FinanceContext context, CommandMetadata metadata,
        CreditLimitDecisionV1 request, CancellationToken cancellationToken)
    {
        const FinanceAction action = FinanceAction.ApproveCredit;
        if (await Start(context, metadata, action, cancellationToken) is { } refused) return refused;
        if (request.LimitId == Guid.Empty || !Text(request.ExpectedVersion, 64)
            || (request.Approve ? request.Reason is not null : !Text(request.Reason, 1000))) return Failure(context, CommandOutcome.ValidationFailed, "credit_limit_decision_invalid");
        var hash = FinanceCreditAccountService.PayloadHash(DecideCommand, request);
        return await Transaction(context, async () =>
        {
            if (await Prior(context, metadata, action, DecideCommand, hash, cancellationToken) is { } prior) return prior;
            var row = await db.CreditLimits.SingleOrDefaultAsync(x => x.Id == request.LimitId, cancellationToken);
            if (await Fence(context, action, cancellationToken) is { } fenced) return fenced;
            if (row is null || row.Status != 1 || row.Version.ToString(CultureInfo.InvariantCulture) != request.ExpectedVersion)
                return Failure(context, CommandOutcome.VersionConflict, "credit_limit_version_conflict");
            if (row.ProposedBy == context.ActorId) return Failure(context, CommandOutcome.ValidationFailed, "credit_limit_maker_cannot_approve");
            var source = Source(row);
            if (request.Approve)
            {
                var account = await db.CreditAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.CreditAccountId, cancellationToken);
                if (await Fence(context, action, cancellationToken) is { } afterAccount) return afterAccount;
                if (!Usable(account) || account!.Version.ToString(CultureInfo.InvariantCulture) != source.ExpectedCreditVersion)
                    return Failure(context, CommandOutcome.VersionConflict, "credit_limit_account_changed");
                if (!await Lender(context, account, cancellationToken)) return Failure(context, CommandOutcome.Unavailable, "credit_lender_unavailable");
                if (await Fence(context, action, cancellationToken) is { } afterLender) return afterLender;
                var current = await Key(account).AsNoTracking().Where(x => x.Status == 2).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(cancellationToken);
                if (!Expected(current, source)) return Failure(context, CommandOutcome.VersionConflict, "credit_limit_current_changed");
            }
            if (await Fence(context, action, cancellationToken) is { } last) return last;
            var before = JsonSerializer.Serialize(View(row, context.ActorId, null), Json);
            row.Status = request.Approve ? 2 : 3; row.Version++; row.DecidedBy = context.ActorId; row.DecidedAtUtc = time.GetUtcNow(); row.RejectionReason = request.Reason;
            Audit(context, metadata, row, envelopes.Approve, "Decide", 1, before, request.Reason ?? source.Reason);
            return await Save(context, metadata, action, DecideCommand, hash, Success(context, row), cancellationToken);
        }, cancellationToken);
    }

    public async Task<ReadResult<CreditLimitWorkspaceV1>> ReadAsync(FinanceContext context, Guid creditAccountId, CancellationToken cancellationToken)
    {
        const FinanceAction action = FinanceAction.ReadCredits;
        ReadResult<CreditLimitWorkspaceV1> Refused(CreditLimitResultV1 answer) => new ReadResult<CreditLimitWorkspaceV1>(answer.Outcome == CommandOutcome.Denied ? ReadOutcome.Denied : ReadOutcome.Unavailable, null);
        if (await Fence(context, action, cancellationToken) is { } refused) return Refused(refused);
        var account = await db.CreditAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == creditAccountId, cancellationToken);
        if (await Fence(context, action, cancellationToken) is { } afterAccount) return Refused(afterAccount);
        if (account is null) return new ReadResult<CreditLimitWorkspaceV1>(ReadOutcome.NotFound, null);
        if (!Usable(account)) return new ReadResult<CreditLimitWorkspaceV1>(ReadOutcome.Unavailable, null);
        var rows = await Key(account).AsNoTracking().OrderByDescending(x => x.Revision).ThenByDescending(x => x.ProposedAtUtc).ToListAsync(cancellationToken);
        var current = rows.FirstOrDefault(x => x.Status == 2);
        var ids = await db.CreditAccounts.AsNoTracking().Where(x => x.Status == 2 && x.LenderPartyId == account.LenderPartyId
            && x.LenderCompanyPartyAccountId == account.LenderCompanyPartyAccountId && x.CreditGroup == account.CreditGroup && x.CurrencyId == account.CurrencyId)
            .Select(x => x.Id).ToArrayAsync(cancellationToken);
        var movements = await db.CreditMovements.AsNoTracking().Where(x => ids.Contains(x.CreditAccountId) && (x.Status == 1 || x.Status == 2)).ToListAsync(cancellationToken);
        if (await Fence(context, action, cancellationToken) is { } afterSources) return Refused(afterSources);
        if (movements.Any(x => x.CompanyId != context.CompanyId || x.CurrencyId != account.CurrencyId)) return new ReadResult<CreditLimitWorkspaceV1>(ReadOutcome.Unavailable, null);
        CreditLimitAssessmentV1? assessment;
        try { assessment = Assess(movements, current?.AmountText); }
        catch (JsonException) { assessment = null; }
        if (assessment is null) return new ReadResult<CreditLimitWorkspaceV1>(ReadOutcome.Unavailable, null);
        return new ReadResult<CreditLimitWorkspaceV1>(ReadOutcome.Found, new CreditLimitWorkspaceV1(context.CompanyId, account.Id, rows.Select(x => View(x, context.ActorId,
            rows.FirstOrDefault(next => next.Status == 2 && next.PreviousLimitId == x.Id)?.Id)).ToArray(), assessment));
    }

    internal static CreditLimitAssessmentV1? Assess(IReadOnlyList<FinanceCreditMovementRow> movements, string? limit)
    {
        var net = new FinanceExactDecimalText(0, 0);
        FinanceExactDecimalText? Principal(FinanceCreditMovementRow row)
        {
            var source = JsonSerializer.Deserialize<CreditMovementProposeV1>(row.SourceJson, Json);
            if (source is null || FinanceCreditMovementPolicyV1.Validate(source).Count != 0 || (int)source.Kind != row.Kind || source.CreditAccountId != row.CreditAccountId) return null;
            var text = source.Components.SingleOrDefault(x => x.Kind == "principal")?.Amount;
            if (text is null) return new FinanceExactDecimalText(0, 0);
            return FinanceExactDecimalText.TryParseNonNegative(text, out var amount) ? amount : null;
        }
        foreach (var row in movements.Where(x => x.Status == 2 && x.ReversedByMovementId is null))
        {
            if (Principal(row) is not { } amount) return null;
            net = net.Add(row.Kind == 2 ? new FinanceExactDecimalText(-amount.Unscaled, amount.Scale) : amount);
        }
        FinanceExactDecimalText? ceiling = null;
        if (limit is not null) { if (!FinanceExactDecimalText.TryParseNonNegative(limit, out var value)) return null; ceiling = value; }
        string Signed(FinanceExactDecimalText amount) => amount.Unscaled.Sign < 0 ? "-" + new FinanceExactDecimalText(-amount.Unscaled, amount.Scale).Text : amount.Text;
        bool? Exceeds(FinanceExactDecimalText amount) => ceiling is { } cap ? amount.CompareTo(cap) > 0 : null;
        var projected = new List<CreditLimitMovementAssessmentV1>();
        foreach (var row in movements.Where(x => x.Status == 1))
        {
            if (Principal(row) is not { } amount) return null;
            var candidate = net.Add(row.Kind == 2 ? new FinanceExactDecimalText(-amount.Unscaled, amount.Scale) : amount);
            if (row.ReplacesOpeningMovementId is { } originalId)
            {
                var original = movements.SingleOrDefault(x => x.Id == originalId && x.Status == 2 && x.ReversedByMovementId is null);
                if (original is null || Principal(original) is not { } replaced) return null;
                candidate = candidate.Add(new FinanceExactDecimalText(-replaced.Unscaled, replaced.Scale));
            }
            projected.Add(new CreditLimitMovementAssessmentV1(row.Id, JsonSerializer.Deserialize<CreditMovementProposeV1>(row.SourceJson, Json)!.SourceDocument.Reference, Signed(candidate), Exceeds(candidate)));
        }
        return new CreditLimitAssessmentV1(Signed(net), Exceeds(net), projected);
    }

    private IQueryable<FinanceCreditLimitRow> Key(CreditAccountRow account) => db.CreditLimits.Where(x => x.LenderPartyId == account.LenderPartyId
        && x.LenderCompanyPartyAccountId == account.LenderCompanyPartyAccountId && x.CreditGroup == account.CreditGroup && x.CurrencyId == account.CurrencyId);
    private static bool Usable(CreditAccountRow? row) => row is { Status: 2, CreditGroup: >= 1 and <= 4, LenderPartyId: not null, LenderCompanyPartyAccountId: not null }
        && row.LenderPartyId != Guid.Empty && row.LenderCompanyPartyAccountId != Guid.Empty && row.CurrencyId != Guid.Empty;
    private async Task<bool> Lender(FinanceContext context, CreditAccountRow account, CancellationToken ct)
    {
        var answer = await lenders.ReadAsync(context.CompanyId, account.LenderPartyId!.Value, account.LenderCompanyPartyAccountId!.Value, ct);
        return answer.CompanyId == context.CompanyId && answer.PartyId == account.LenderPartyId && answer.CompanyPartyAccountId == account.LenderCompanyPartyAccountId
            && answer.Status == FinanceCreditLenderStatusV1.Available;
    }
    private static bool Expected(FinanceCreditLimitRow? current, CreditLimitProposeV1 request) => current is null
        ? request.ExpectedCurrentLimitId is null && request.ExpectedCurrentLimitVersion is null
        : current.Id == request.ExpectedCurrentLimitId && current.Version.ToString(CultureInfo.InvariantCulture) == request.ExpectedCurrentLimitVersion;
    private static bool Text(string? value, int length) => FinanceCreditMovementPolicyV1.Text(value, length);
    private bool Same(FinanceContext context) => company.IsResolved && company.CompanyId == context.CompanyId && context.CompanyId != Guid.Empty
        && user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.UserId) && user.UserId == context.ActorId && user.CanAccessCompany(context.CompanyId);
    private async Task<CreditLimitResultV1?> Fence(FinanceContext context, FinanceAction action, CancellationToken ct)
    {
        if (!Same(context)) return Failure(context, CommandOutcome.Denied);
        var answer = await permissions.CheckAsync(context, action, null, ct);
        if (!Same(context) || answer == PermissionDecision.Denied) return Failure(context, CommandOutcome.Denied);
        return answer == PermissionDecision.Allowed ? null : Failure(context, CommandOutcome.Unavailable);
    }
    private async Task<CreditLimitResultV1?> Start(FinanceContext context, CommandMetadata metadata, FinanceAction action, CancellationToken ct)
    {
        if (await Fence(context, action, ct) is { } denied) return denied;
        return Text(metadata.IdempotencyKey, 128) && Text(context.CorrelationId, 128) && context.ActorId.Length <= 128 && context.CausationId is not { Length: > 128 }
            ? null : Failure(context, CommandOutcome.ValidationFailed, "credit_limit_metadata_invalid");
    }
    private async Task<CreditLimitResultV1?> Prior(FinanceContext context, CommandMetadata metadata, FinanceAction action, string command, string hash, CancellationToken ct)
    {
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var receipt = await db.CreditMovementCommands.AsNoTracking().SingleOrDefaultAsync(x => x.Command == command && x.KeyHash == key, ct);
        if (await Fence(context, action, ct) is { } denied) return denied;
        if (receipt is null) return null;
        if (receipt.ActorId != context.ActorId || receipt.PayloadHash != hash) return Failure(context, CommandOutcome.IdempotencyConflict);
        return (JsonSerializer.Deserialize<CreditLimitResultV1>(receipt.ResultJson, Json) ?? throw new InvalidOperationException("Limit makbuzu okunamadı.")) with { IsReplay = true };
    }
    private async Task<CreditLimitResultV1> Save(FinanceContext context, CommandMetadata metadata, FinanceAction action, string command,
        string hash, CreditLimitResultV1 result, CancellationToken ct)
    {
        if (await Fence(context, action, ct) is { } denied) { db.ChangeTracker.Clear(); auditOwner?.DiscardPending(); return denied; }
        db.CreditMovementCommands.Add(new FinanceCreditMovementCommandRow() { CompanyId = context.CompanyId, Command = command, KeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey),
            ActorId = context.ActorId, PayloadHash = hash, ResultJson = JsonSerializer.Serialize(result, Json) });
        await db.SaveChangesAsync(ct); return result;
    }
    private Task<CreditLimitResultV1> Transaction(FinanceContext context, Func<Task<CreditLimitResultV1>> action, CancellationToken ct) =>
        FinanceTreasuryTransaction.ExecuteAsync(db, action, async () => await Fence(context, FinanceAction.ReadCredits, ct) ?? Failure(context, CommandOutcome.Unavailable), auditOwner, ct);
    private void Audit(FinanceContext context, CommandMetadata metadata, FinanceCreditLimitRow row, FinanceAuditEnvelope envelope,
        string operation, long? beforeVersion, string before, string reason) => audit.Enqueue(new AuditEventDraft(context.CompanyId,
            "finance.credit-limit." + operation.ToLowerInvariant() + ".v1", "Finance.CreditLimit", row.Id.ToString("D"), operation,
            beforeVersion, row.Version, AuditActorTypes.User, context.ActorId, envelope.PermissionCode, envelope.RoleCodes, before,
            JsonSerializer.Serialize(View(row, context.ActorId, null), Json), reason, "{\"rule\":\"FNS-D95/FNS-D100\"}", AuditAiInvolvement.None, null,
            envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent, context.CorrelationId, context.CausationId, metadata.IdempotencyKey));
    private static CreditLimitProposeV1 Source(FinanceCreditLimitRow row) => JsonSerializer.Deserialize<CreditLimitProposeV1>(row.SourceJson, Json)
        ?? throw new InvalidOperationException("Limit kaynağı okunamadı.");
    private static CreditLimitViewV1 View(FinanceCreditLimitRow row, string actor, Guid? superseded) => new CreditLimitViewV1(row.Id, row.CompanyId, row.LenderPartyId,
        row.LenderCompanyPartyAccountId, (CreditAccountGroup)(row.CreditGroup - 1), row.CurrencyId, row.Version.ToString(CultureInfo.InvariantCulture),
        (CreditAccountStatus)(row.Status - 1), Source(row), row.ProposedBy == actor, row.ProposedAtUtc, row.DecidedAtUtc, row.RejectionReason, superseded);
    private static CreditLimitResultV1 Success(FinanceContext context, FinanceCreditLimitRow row) => new CreditLimitResultV1(CommandOutcome.Succeeded, View(row, context.ActorId, null), false, context.CorrelationId, []);
    private static CreditLimitResultV1 Failure(FinanceContext context, CommandOutcome outcome, string? issue = null) => new CreditLimitResultV1(outcome, null, false, context.CorrelationId,
        issue is null ? [] : [new ValidationIssue("request", issue)]);
}
