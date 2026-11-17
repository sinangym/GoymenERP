using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>Finance owns the immutable period carry and its counter-record. The Accounting host
/// supplies its transaction and fresh closing/reopening authority. This session never owns or
/// completes the borrowed connection/transaction and never creates cash or party movements.</summary>
public sealed class FinancePeriodCarryForwardSession : IAsyncDisposable
{
    private readonly FinanceDbContext db;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly ICompanyContext company;
    private readonly ICurrentUser user;
    private readonly IFinancePermissionPort permissions;
    private readonly Func<CancellationToken, Task> authorityFence;
    private readonly IAuditEventCollector audit;
    private readonly IAuditTransactionWriterV1 auditOwner;
    private readonly Guid companyId;
    private readonly string actorId;
    private bool disposed;
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    private FinancePeriodCarryForwardSession(FinanceDbContext db, DbConnection connection, DbTransaction transaction,
        ICompanyContext company, ICurrentUser user, IFinancePermissionPort permissions, Func<CancellationToken, Task> authorityFence,
        IAuditEventCollector audit, IAuditTransactionWriterV1 auditOwner)
    {
        this.db = db; this.connection = connection; this.transaction = transaction; this.company = company;
        this.user = user; this.permissions = permissions; this.authorityFence = authorityFence;
        companyId = company.CompanyId; actorId = user.UserId!;
        this.audit = audit; this.auditOwner = auditOwner;
    }

    public static async Task<FinancePeriodCarryForwardSession> EnlistAsync(DbConnection connection, DbTransaction transaction,
        ICompanyContext company, ICurrentUser user, IFinancePermissionPort permissions,
        Func<CancellationToken, Task> authorityFence, IAuditEventCollector audit, IAuditTransactionWriterV1 auditOwner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(company); ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(permissions); ArgumentNullException.ThrowIfNull(authorityFence);
        ArgumentNullException.ThrowIfNull(audit); ArgumentNullException.ThrowIfNull(auditOwner);
        if (auditOwner.HasPendingDrafts) throw new InvalidOperationException("A carry attempt cannot consume another operation's audit batch.");
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.IsAuthenticated
            || !Guid.TryParse(user.UserId, out var actor) || actor == Guid.Empty || !user.CanAccessCompany(company.CompanyId))
            throw new UnauthorizedAccessException();
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Finance carry requires the caller's active Serializable transaction.");
        var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false).Options, company);
        try
        {
            await db.Database.UseTransactionAsync(transaction, cancellationToken);
            var session = new FinancePeriodCarryForwardSession(db, connection, transaction, company, user, permissions, authorityFence, audit, auditOwner);
            await session.FenceAsync(cancellationToken); return session;
        }
        catch { await db.DisposeAsync(); throw; }
    }

    public async Task<FinancePeriodCarryVoucherV1> RecordAsync(FinanceContext context,
        FinancePeriodCarrySourceV1 source, CommandMetadata metadata, FinanceAuditEnvelope envelope, CancellationToken cancellationToken)
    {
        await FenceAsync(cancellationToken);
        if (source.CompanyId != companyId || context.CompanyId != companyId || context.ActorId != actorId
            || !Guid.TryParse(actorId, out var actor) || source.ActorId != actor || source.ClosureId == Guid.Empty || source.SourcePeriodId == Guid.Empty
            || source.TargetPeriodId == Guid.Empty || source.TargetPeriodId == source.SourcePeriodId || source.ClosedPeriodVersion < 2
            || source.SourceStartsOn == default || source.SourceEndsOn < source.SourceStartsOn || source.SourceEndsOn == DateOnly.MaxValue
            || source.TargetStartsOn <= source.SourceEndsOn || source.TargetEndsOn < source.TargetStartsOn
            || source.RecordedAtUtc == default || source.RecordedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("Finance carry source is incomplete or belongs to another context.");
        if (await db.PeriodCarryVouchers.AnyAsync(x => x.ClosureId == source.ClosureId
            || (x.SourcePeriodId == source.SourcePeriodId && x.ClosedPeriodVersion == source.ClosedPeriodVersion), cancellationToken))
            throw new InvalidOperationException("A closure already owns a carry voucher.");

        var lines = await SnapshotAsync(context, source.SourceEndsOn, cancellationToken);
        var sourceJson = JsonSerializer.Serialize(source, Json);
        var linesJson = JsonSerializer.Serialize(lines, Json);
        var sourceHash = Hash(sourceJson + "|" + linesJson);
        var row = new FinancePeriodCarryVoucherRow { Id = Guid.NewGuid(), CompanyId = companyId,
            ClosureId = source.ClosureId, SourcePeriodId = source.SourcePeriodId, ClosedPeriodVersion = source.ClosedPeriodVersion,
            SourceEndsOn = source.SourceEndsOn, TargetPeriodId = source.TargetPeriodId, TargetStartsOn = source.TargetStartsOn,
            SourceJson = sourceJson, SourceHash = sourceHash, LinesJson = linesJson,
            RecordedBy = source.ActorId, RecordedAtUtc = source.RecordedAtUtc };
        db.PeriodCarryVouchers.Add(row);
        await FenceAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await FenceAsync(cancellationToken);
        var result = new FinancePeriodCarryVoucherV1(row.Id, companyId, source, sourceHash, lines, null);
        await AuditAsync(context, metadata, envelope, row.Id, "finance.period-carry.recorded.v1", "Record",
            JsonSerializer.Serialize(result, Json), "Dönem sınırındaki mevcut bakiyeler devir fişine kaydedildi; para hareketi oluşturulmadı.", cancellationToken);
        return result;
    }

    public async Task ReverseAsync(FinanceContext context, FinancePeriodCarryReversalV1 reversal,
        CommandMetadata metadata, FinanceAuditEnvelope envelope, CancellationToken cancellationToken)
    {
        await FenceAsync(cancellationToken);
        if (reversal.CompanyId != companyId || context.CompanyId != companyId || context.ActorId != actorId
            || !Guid.TryParse(actorId, out var actor) || reversal.ActorId != actor || reversal.Id == Guid.Empty
            || reversal.VoucherId == Guid.Empty || reversal.ClosureId == Guid.Empty || string.IsNullOrWhiteSpace(reversal.Reason)
            || reversal.Reason.Length > 1000 || reversal.RecordedAtUtc == default || reversal.RecordedAtUtc.Offset != TimeSpan.Zero)
            throw new InvalidOperationException("A carry reversal requires its exact source and approval.");
        var row = await db.PeriodCarryVouchers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == reversal.VoucherId
            && x.ClosureId == reversal.ClosureId, cancellationToken)
            ?? throw new InvalidOperationException("The Finance carry voucher was not found.");
        if (Hash(row.SourceJson + "|" + row.LinesJson) != row.SourceHash)
            throw new InvalidOperationException("The immutable carry source is inconsistent.");
        if (await db.PeriodCarryReversals.AnyAsync(x => x.VoucherId == row.Id || x.Id == reversal.Id, cancellationToken))
            throw new InvalidOperationException("The Finance carry voucher was already reversed.");
        db.PeriodCarryReversals.Add(new FinancePeriodCarryReversalRow { Id = reversal.Id, CompanyId = companyId,
            ClosureId = row.ClosureId, VoucherId = row.Id, RecordedBy = reversal.ActorId,
            RecordedAtUtc = reversal.RecordedAtUtc, Reason = reversal.Reason, SourceHash = row.SourceHash });
        await FenceAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await FenceAsync(cancellationToken);
        await AuditAsync(context, metadata, envelope, reversal.Id, "finance.period-carry.reversed.v1", "Reverse",
            JsonSerializer.Serialize(reversal, Json), reversal.Reason, cancellationToken);
    }

    private async Task<IReadOnlyList<FinancePeriodCarryLineV1>> SnapshotAsync(FinanceContext context, DateOnly through,
        CancellationToken cancellationToken)
    {
        var lines = new List<FinancePeriodCarryLineV1>();
        var treasury = new FinanceTreasuryLedger(db, company);
        var banks = await db.BankAccounts.AsNoTracking().Select(x => new { x.Id, x.CurrencyId }).ToListAsync(cancellationToken);
        var cash = await db.CashAccounts.AsNoTracking().Select(x => new { x.Id, x.CurrencyId }).ToListAsync(cancellationToken);
        foreach (var bank in banks)
            AddTreasury("Bank", bank.Id, bank.CurrencyId, await treasury.ReadAsync(companyId, ExpensePaymentSourceKind.Bank,
                bank.Id, bank.CurrencyId, cancellationToken, through));
        foreach (var account in cash)
            AddTreasury("Cash", account.Id, account.CurrencyId, await treasury.ReadAsync(companyId, ExpensePaymentSourceKind.Cash,
                account.Id, account.CurrencyId, cancellationToken, through));

        var parties = await db.MoneyEvents.AsNoTracking().Select(x => new { x.PartyId, AccountId = x.CompanyAccountId })
            .Union(db.OutgoingMoneyEvents.Select(x => new { x.PartyId, AccountId = x.CompanyAccountId }))
            .Union(db.OpenItems.Select(x => new { x.PartyId, AccountId = x.CompanyAccountId }))
            .Union(db.OpeningBalanceMovements.Where(x => x.TargetKind == FinanceOpeningBalanceService.PartyAccountKind && x.PartyId.HasValue)
                .Select(x => new { PartyId = x.PartyId!.Value, AccountId = x.TargetId }))
            .Union(db.PartyBalanceTransfers.Select(x => new { PartyId = x.PredecessorPartyId, AccountId = x.PredecessorAccountId }))
            .Union(db.PartyBalanceTransfers.Select(x => new { PartyId = x.SuccessorPartyId, AccountId = x.SuccessorAccountId }))
            .ToListAsync(cancellationToken);
        var ledger = new PartyLedgerService(db, company, user, permissions);
        foreach (var party in parties.OrderBy(x => x.PartyId).ThenBy(x => x.AccountId))
        {
            var answer = await ledger.ReadBoundaryAuthorizedAsync(context, party.PartyId, party.AccountId, through, cancellationToken);
            if (answer.Outcome != PartyLedgerOutcome.Found || answer.Ledger is not { } value)
                throw new InvalidOperationException("The complete, authorized party ledger source is unavailable.");
            foreach (var currency in value.Currencies)
            {
                var balance = currency.ClosingBalance;
                if (balance.Amount.Disclosure != AmountDisclosure.Visible || balance.Amount.Money is not { } amount
                    || amount.CurrencyId != currency.CurrencyId || !FinanceExactDecimalText.TryParseNonNegative(amount.Amount, out var parsed)
                    || balance.Side is null) throw new InvalidOperationException("The exact party balance is unavailable.");
                if (parsed.IsZero) continue;
                if (balance.Side is not (PartyLedgerBalanceSide.Debit or PartyLedgerBalanceSide.Credit))
                    throw new InvalidOperationException("A nonzero party balance must have its actual debit or credit side.");
                lines.Add(new("Party", party.AccountId, party.PartyId, currency.CurrencyId,
                    balance.Side == PartyLedgerBalanceSide.Debit ? parsed.Text : "0",
                    balance.Side == PartyLedgerBalanceSide.Credit ? parsed.Text : "0"));
            }
        }
        await FenceAsync(cancellationToken);
        return lines.OrderBy(x => x.AccountKind, StringComparer.Ordinal).ThenBy(x => x.AccountId)
            .ThenBy(x => x.PartyId).ThenBy(x => x.CurrencyId).ToArray();

        void AddTreasury(string kind, Guid id, Guid currencyId, FinanceTreasuryBalance? balance)
        {
            if (balance is null) throw new InvalidOperationException("The dated treasury balance source is unavailable.");
            var comparison = balance.Incoming.CompareTo(balance.Outgoing);
            if (comparison == 0) return;
            lines.Add(new(kind, id, null, currencyId,
                comparison > 0 ? balance.Incoming.Subtract(balance.Outgoing).Text : "0",
                comparison < 0 ? balance.Outgoing.Subtract(balance.Incoming).Text : "0"));
        }
    }

    private async Task FenceAsync(CancellationToken token)
    {
        RequireActive(); await authorityFence(token); RequireActive();
    }
    private async Task AuditAsync(FinanceContext context, CommandMetadata metadata, FinanceAuditEnvelope envelope,
        Guid id, string eventType, string action, string after, string reason, CancellationToken token)
    {
        if (context.CompanyId != companyId || context.ActorId != actorId || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || string.IsNullOrWhiteSpace(envelope.PermissionCode)) throw new UnauthorizedAccessException();
        audit.Enqueue(new AuditEventDraft(companyId, eventType, "Finance.PeriodCarry", id.ToString("D"), action,
            null, 1, AuditActorTypes.User, actorId, envelope.PermissionCode, envelope.RoleCodes, "{}", after, reason,
            null, AuditAiInvolvement.None, null, envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent,
            context.CorrelationId, context.CausationId, metadata.IdempotencyKey));
        try { await auditOwner.WritePendingAsync(transaction, token); await FenceAsync(token); }
        finally { auditOwner.DiscardPending(); }
    }
    private void RequireActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!company.IsResolved || company.CompanyId != companyId || !user.IsAuthenticated || user.UserId != actorId
            || !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException();
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || transaction.IsolationLevel != IsolationLevel.Serializable || db.Database.CurrentTransaction is not { } current
            || !ReferenceEquals(current.GetDbTransaction(), transaction))
            throw new InvalidOperationException("The Finance carry host transaction is no longer active.");
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public async ValueTask DisposeAsync() { if (disposed) return; disposed = true; await db.DisposeAsync(); }
}
