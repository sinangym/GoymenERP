using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Finance.Contracts;
using GoymenERP.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Finance.Infrastructure;

/// <summary>
/// FIN-S14 / #1189/6960684942. Documents-backed carrying value, explicitly entered by Accounting and approved by
/// another Accounting actor. Serializable Finance-owned proof transaction; no balance movement or journal posting.
/// </summary>
public sealed class CashExchangeBookValueServiceV1(FinanceDbContext db, ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, ICashExchangeBookValueEvidencePortV1 evidence,
    IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter, ICorrelationContext correlation,
    TimeProvider time) : ICashExchangeBookValueServiceV1
{
    public const string ProposeCommand = "finance.cash-exchange.book-value.propose.v1";
    public const string DecideCommand = "finance.cash-exchange.book-value.decide.v1";
    public const int PageSize = 25;
    private static readonly JsonSerializerOptions Json = FinanceJson.CreateOptions();

    public async Task<ReadResult<CashExchangeBookValueCandidatePageV1>> ListAsync(FinanceContext context,
        Guid? afterId, CancellationToken ct)
    {
        if (!await Allowed(context, null, ct)) return new(ReadOutcome.Denied, null);
        if (afterId == Guid.Empty) return new(ReadOutcome.NotFound, null);
        try
        {
            var query = db.CashTransfers.AsNoTracking().Where(x => x.Kind == CashTransferKinds.CurrencyExchange);
            if (afterId is { } cursor) query = query.Where(x => x.Id.CompareTo(cursor) > 0);
            var rows = await query.OrderBy(x => x.Id).Take(PageSize + 1).ToArrayAsync(ct);
            if (!await Allowed(context, null, ct)) return new(ReadOutcome.Denied, null);
            if (rows.Any(x => x.CompanyId != context.CompanyId)) return new(ReadOutcome.Unavailable, null);
            var views = rows.Take(PageSize).Select(CashTransferService.View).ToArray();
            return new(ReadOutcome.Found, new(context.CompanyId, views, rows.Length > PageSize ? views[^1].Id : null));
        }
        catch (Exception ex) when (ex is DbException or JsonException or InvalidOperationException)
        { return new(await Allowed(context, null, ct) ? ReadOutcome.Unavailable : ReadOutcome.Denied, null); }
    }

    public async Task<ReadResult<CashExchangeBookValueWorkspaceV1>> GetAsync(FinanceContext context, Guid transferId, CancellationToken ct)
    {
        if (!await Allowed(context, null, ct)) return new(ReadOutcome.Denied, null);
        try
        {
            var transfer = await db.CashTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == transferId, ct);
            if (!await Allowed(context, null, ct)) return new(ReadOutcome.Denied, null);
            if (transfer is null || transfer.Kind != CashTransferKinds.CurrencyExchange) return new(ReadOutcome.NotFound, null);
            var proposals = await db.CashExchangeBookValueProposals.AsNoTracking().Where(x => x.TransferId == transferId)
                .OrderByDescending(x => x.PreparedAtUtc).ThenBy(x => x.Id).ToArrayAsync(ct);
            if (!await Allowed(context, null, ct)) return new(ReadOutcome.Denied, null);
            var decisions = await db.CashExchangeBookValueDecisions.AsNoTracking().Where(x => x.TransferId == transferId).ToArrayAsync(ct);
            if (!await Allowed(context, null, ct)) return new(ReadOutcome.Denied, null);
            return new(ReadOutcome.Found, new(context.CompanyId, CashTransferService.View(transfer),
                proposals.Select(x => View(x, decisions.SingleOrDefault(d => d.ProposalId == x.Id))).ToArray()));
        }
        catch (Exception ex) when (ex is DbException or JsonException or InvalidOperationException)
        { return new(await Allowed(context, null, ct) ? ReadOutcome.Unavailable : ReadOutcome.Denied, null); }
    }

    public async Task<CashExchangeBookValueCommandResultV1> ProposeAsync(FinanceContext context, CommandMetadata metadata,
        ProposeCashExchangeBookValueV1 request, CancellationToken ct)
    {
        const string permission = CashExchangeBookValuePermissionsV1.Prepare;
        if (!await Allowed(context, permission, ct)) return Failure(context, CommandOutcome.Denied);
        if (!Metadata(context, metadata) || request is null) return Failure(context, CommandOutcome.ValidationFailed, "cash_exchange_book_value_invalid");
        return await Transaction(context, permission, async () =>
        {
            var payloadHash = Hash(JsonSerializer.Serialize(request, Json));
            if (await Replay(context, metadata, ProposeCommand, payloadHash, permission, ct) is { } replay) return replay;
            if (!Valid(request)) return Failure(context, CommandOutcome.ValidationFailed, "cash_exchange_book_value_invalid");
            var transfer = await db.CashTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.TransferId, ct);
            if (!await Allowed(context, permission, ct)) return Failure(context, CommandOutcome.Denied);
            if (transfer is null || transfer.Kind != CashTransferKinds.CurrencyExchange) return Failure(context, CommandOutcome.VersionConflict, "cash_exchange_not_found");
            var source = CashTransferService.View(transfer);
            if (source.Version != request.ExpectedTransferVersion) return Failure(context, CommandOutcome.VersionConflict, "cash_exchange_source_changed");
            if (source.Exchange is null) return Failure(context, CommandOutcome.Unavailable, "cash_exchange_legacy_source_unreadable");
            var all = await db.CashExchangeBookValueProposals.AsNoTracking().Where(x => x.TransferId == transfer.Id).Select(x => x.Id).ToArrayAsync(ct);
            if (!await Allowed(context, permission, ct)) return Failure(context, CommandOutcome.Denied);
            var decisions = await db.CashExchangeBookValueDecisions.AsNoTracking().Where(x => x.TransferId == transfer.Id).ToArrayAsync(ct);
            if (!await Allowed(context, permission, ct)) return Failure(context, CommandOutcome.Denied);
            if (decisions.Any(x => x.Approved) || all.Any(id => decisions.All(x => x.ProposalId != id)))
                return Failure(context, CommandOutcome.VersionConflict, "cash_exchange_book_value_exists");
            if (await Evidence(context, request.Evidence, permission, ct) is { } denied) return denied;
            var sourceJson = JsonSerializer.Serialize(source, Json);
            var row = new CashExchangeBookValueProposalRow { Id = Guid.NewGuid(), CompanyId = context.CompanyId,
                TransferId = source.Id, TransferSourceJson = sourceJson, TransferSourceHash = Hash(sourceJson),
                PreparationJson = JsonSerializer.Serialize(request, Json), PreparedBy = context.ActorId, PreparedAtUtc = time.GetUtcNow() };
            db.CashExchangeBookValueProposals.Add(row);
            return await Save(context, metadata, ProposeCommand, permission, payloadHash, View(row, null), request.Reason, ct, null);
        }, ct);
    }

    public async Task<CashExchangeBookValueCommandResultV1> DecideAsync(FinanceContext context, CommandMetadata metadata,
        DecideCashExchangeBookValueV1 request, CancellationToken ct)
    {
        const string permission = CashExchangeBookValuePermissionsV1.Approve;
        if (!await Allowed(context, permission, ct)) return Failure(context, CommandOutcome.Denied);
        if (!Metadata(context, metadata) || request is null) return Failure(context, CommandOutcome.ValidationFailed, "cash_exchange_book_value_invalid");
        return await Transaction(context, permission, async () =>
        {
            var payloadHash = Hash(JsonSerializer.Serialize(request, Json));
            if (await Replay(context, metadata, DecideCommand, payloadHash, permission, ct) is { } replay) return replay;
            if (request.ProposalId == Guid.Empty || request.ExpectedVersion != "1"
                || (request.Approve ? request.Reason is not null : !Text(request.Reason, 1000)))
                return Failure(context, CommandOutcome.ValidationFailed, "cash_exchange_book_value_invalid");
            var row = await db.CashExchangeBookValueProposals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ProposalId, ct);
            if (!await Allowed(context, permission, ct)) return Failure(context, CommandOutcome.Denied);
            if (row is null) return Failure(context, CommandOutcome.VersionConflict, "cash_exchange_book_value_not_found");
            if (row.PreparedBy == context.ActorId) return Failure(context, CommandOutcome.ValidationFailed, "cash_exchange_book_value_maker_cannot_approve");
            var decisions = await db.CashExchangeBookValueDecisions.AsNoTracking().Where(x => x.TransferId == row.TransferId).ToArrayAsync(ct);
            if (!await Allowed(context, permission, ct)) return Failure(context, CommandOutcome.Denied);
            if (decisions.Any(x => x.ProposalId == row.Id || x.Approved)) return Failure(context, CommandOutcome.VersionConflict, "cash_exchange_book_value_decided");
            var pending = View(row, null); // Strictly decode and bind the original immutable proof before any append.
            var source = JsonSerializer.Deserialize<CashTransferView>(row.TransferSourceJson, Json) ?? throw new JsonException();
            if (request.Approve)
            {
                var transfer = await db.CashTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == row.TransferId, ct);
                if (!await Allowed(context, permission, ct)) return Failure(context, CommandOutcome.Denied);
                if (transfer is null || Hash(JsonSerializer.Serialize(CashTransferService.View(transfer), Json)) != row.TransferSourceHash)
                    return Failure(context, CommandOutcome.VersionConflict, "cash_exchange_source_changed");
                if (await Evidence(context, pending.Evidence, permission, ct) is { } denied) return denied;
            }
            var decision = new CashExchangeBookValueDecisionRow { CompanyId = context.CompanyId, ProposalId = row.Id,
                TransferId = row.TransferId, Approved = request.Approve, PreparedBy = row.PreparedBy,
                DecidedBy = context.ActorId, DecidedAtUtc = time.GetUtcNow(), Reason = request.Reason };
            db.CashExchangeBookValueDecisions.Add(decision);
            var result = View(row, decision);
            if (request.Approve) FinanceFinancialMovementOutboxV1.ExchangeBookValue(db, context, source, result);
            return await Save(context, metadata, DecideCommand, permission, payloadHash, result, request.Reason ?? pending.Reason, ct, pending);
        }, ct);
    }

    private async Task<CashExchangeBookValueCommandResultV1?> Evidence(FinanceContext context, CashExchangeBookValueEvidenceV1 input, string permission, CancellationToken ct)
    {
        ReadOutcome result;
        try { result = await evidence.CheckAsync(context, input, ct); }
        catch (Exception) when (!ct.IsCancellationRequested)
        { return Failure(context, await Allowed(context, permission, ct) ? CommandOutcome.Unavailable : CommandOutcome.Denied); }
        if (!await Allowed(context, permission, ct) || result == ReadOutcome.Denied) return Failure(context, CommandOutcome.Denied);
        return result == ReadOutcome.Found ? null : Failure(context, result == ReadOutcome.NotFound ? CommandOutcome.ValidationFailed : CommandOutcome.Unavailable,
            "cash_exchange_book_value_evidence_unavailable");
    }

    private async Task<CashExchangeBookValueCommandResultV1> Save(FinanceContext context, CommandMetadata metadata,
        string command, string permission, string payloadHash, CashExchangeBookValueViewV1 value, string reason, CancellationToken ct,
        CashExchangeBookValueViewV1? before)
    {
        var result = new CashExchangeBookValueCommandResultV1(CommandOutcome.Succeeded, value, false, context.CorrelationId, null);
        db.CashExchangeBookValueCommands.Add(new() { CompanyId = context.CompanyId, Command = command,
            KeyHash = FinanceCommandKeys.Hash(metadata.IdempotencyKey), ActorId = context.ActorId, PayloadHash = payloadHash,
            ResultJson = JsonSerializer.Serialize(result, Json) });
        if (!await Allowed(context, permission, ct)) return Failure(context, CommandOutcome.Denied);
        // Keep the legacy Finance interceptor empty; Audit owns its table append in this SAME transaction.
        if (auditWriter.HasPendingDrafts) return Failure(context, CommandOutcome.Unavailable);
        await db.SaveChangesAsync(ct);
        if (!await Allowed(context, permission, ct)) return Failure(context, CommandOutcome.Denied);
        var scope = await authority.RevalidatePermissionScopeAsync(permission, context.CompanyId, ct);
        if (!Same(context) || !scope.IsGranted || !scope.IsCompanyWide) return Failure(context, CommandOutcome.Denied);
        audit.Enqueue(new AuditEventDraft(context.CompanyId, command, "Finance.CashExchangeBookValue", value.Id.ToString("D"),
            command == ProposeCommand ? "Propose" : "Decide", command == ProposeCommand ? null : 1, command == ProposeCommand ? 1 : 2,
            AuditActorTypes.User, context.ActorId, permission, scope.RoleCodes, before is null ? "{}" : JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(value, Json), reason,
            "{\"decision\":\"1189/6960684942\",\"bookValueIsNotTcmbReference\":true}", AuditAiInvolvement.None, null,
            correlation.SourceChannel, correlation.ClientIpAddress, correlation.UserAgent, context.CorrelationId, context.CausationId, metadata.IdempotencyKey));
        await auditWriter.WritePendingAsync(db.Database.CurrentTransaction!.GetDbTransaction(), ct);
        return await Allowed(context, permission, ct) ? result : Failure(context, CommandOutcome.Denied);
    }

    private async Task<CashExchangeBookValueCommandResultV1?> Replay(FinanceContext context, CommandMetadata metadata,
        string command, string hash, string permission, CancellationToken ct)
    {
        var key = FinanceCommandKeys.Hash(metadata.IdempotencyKey);
        var row = await db.CashExchangeBookValueCommands.AsNoTracking().SingleOrDefaultAsync(x => x.Command == command && x.KeyHash == key, ct);
        if (!await Allowed(context, permission, ct)) return Failure(context, CommandOutcome.Denied);
        if (row is null) return null;
        if (row.ActorId != context.ActorId || row.PayloadHash != hash) return Failure(context, CommandOutcome.IdempotencyConflict);
        var stored = JsonSerializer.Deserialize<CashExchangeBookValueCommandResultV1>(row.ResultJson, Json) ?? throw new JsonException();
        if (stored.Outcome != CommandOutcome.Succeeded || stored.Resource is not { } value || value.CompanyId != context.CompanyId
            || !Text(stored.OriginalCorrelationId, 128) || (command == ProposeCommand ? value.PreparedBy != context.ActorId || value.Version != "1"
                : value.DecidedBy != context.ActorId || value.Version != "2")) throw new JsonException();
        return stored with { IsReplay = true }; // Original pending result remains pending after the later decision.
    }

    private async Task<CashExchangeBookValueCommandResultV1> Transaction(FinanceContext context, string permission,
        Func<Task<CashExchangeBookValueCommandResultV1>> operation, CancellationToken ct)
    {
        if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null || db.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts)
            return Failure(context, CommandOutcome.Unavailable);
        IDbContextTransaction transaction;
        try { transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct); }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        { return Failure(context, await Allowed(context, permission, ct) ? CommandOutcome.Unavailable : CommandOutcome.Denied); }
        await using var tx = transaction;
        var committed = false;
        try
        {
            var result = await operation();
            if (result.Outcome != CommandOutcome.Succeeded || !await Allowed(context, permission, ct))
            {
                await tx.RollbackAsync(CancellationToken.None);
                return await Allowed(context, permission, ct) ? result : Failure(context, CommandOutcome.Denied);
            }
            await tx.CommitAsync(ct); committed = true; return result;
        }
        catch (Exception ex) when (ex is DbException or DbUpdateException or JsonException or InvalidOperationException)
        {
            await Rollback();
            return Failure(context, await Allowed(context, permission, ct) ? CommandOutcome.Unavailable : CommandOutcome.Denied);
        }
        finally
        {
            if (!committed) await Rollback();
            db.ChangeTracker.Clear(); auditWriter.DiscardPending();
        }
        async Task Rollback()
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception ex) when (ex is DbException or InvalidOperationException) { /* Already aborted or disconnected. No success is returned. */ }
        }
    }

    private bool Same(FinanceContext c) => c.CompanyId != Guid.Empty && company.IsResolved && company.CompanyId == c.CompanyId
        && user.IsAuthenticated && user.UserId == c.ActorId && Text(c.ActorId, 128) && user.CanAccessCompany(c.CompanyId);
    private async Task<bool> Allowed(FinanceContext c, string? permission, CancellationToken ct)
    {
        if (!Same(c)) return false;
        var prepare = await authority.RevalidatePermissionScopeAsync(permission ?? CashExchangeBookValuePermissionsV1.Prepare, c.CompanyId, ct);
        if (!Same(c)) return false;
        if (prepare.IsGranted && prepare.IsCompanyWide) return true;
        if (permission is not null) return false;
        var approve = await authority.RevalidatePermissionScopeAsync(CashExchangeBookValuePermissionsV1.Approve, c.CompanyId, ct);
        return Same(c) && approve.IsGranted && approve.IsCompanyWide;
    }
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    internal static bool Valid(ProposeCashExchangeBookValueV1 r) => r.TransferId != Guid.Empty && r.ExpectedTransferVersion == "1"
        && Text(r.SourceBookValueTl, 128) && FinanceExactDecimalText.TryParsePositive(r.SourceBookValueTl, out var amount) && amount.Text == r.SourceBookValueTl
        && Text(r.Reason, 1000) && r.Evidence is { DocumentId: var id, VersionNo: > 0, Sha256: var hash } && id != Guid.Empty
        && hash is { Length: 64 } && hash.All(char.IsAsciiHexDigit);
    private static bool Metadata(FinanceContext c, CommandMetadata m) => Text(m.IdempotencyKey, 128) && Text(c.CorrelationId, 128)
        && (c.CausationId is null || Text(c.CausationId, 128));
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max
        && value == value.Trim() && !value.Any(char.IsControl);
    private static CashExchangeBookValueCommandResultV1 Failure(FinanceContext c, CommandOutcome outcome, string? code = null) => new(outcome, null, false, c.CorrelationId, code);
    internal static CashExchangeBookValueViewV1 View(CashExchangeBookValueProposalRow p, CashExchangeBookValueDecisionRow? d)
    {
        var preparation = JsonSerializer.Deserialize<ProposeCashExchangeBookValueV1>(p.PreparationJson, Json) ?? throw new JsonException();
        var source = JsonSerializer.Deserialize<CashTransferView>(p.TransferSourceJson, Json) ?? throw new JsonException();
        if (!Valid(preparation) || preparation.TransferId != p.TransferId || source.Id != p.TransferId || source.CompanyId != p.CompanyId
            || source.Version != preparation.ExpectedTransferVersion || p.TransferSourceHash != Hash(p.TransferSourceJson)
            || source.Preparation.Kind != CashTransferKind.CurrencyExchange || !source.TransferConfirmed || source.Exchange is null
            || !Text(p.PreparedBy, 128) || p.PreparedAtUtc == default || p.PreparedAtUtc.Offset != TimeSpan.Zero
            || (d is not null && (d.CompanyId != p.CompanyId || d.ProposalId != p.Id || d.TransferId != p.TransferId
                || d.PreparedBy != p.PreparedBy || !Text(d.DecidedBy, 128) || d.DecidedBy == p.PreparedBy || d.DecidedAtUtc < p.PreparedAtUtc
                || d.DecidedAtUtc.Offset != TimeSpan.Zero || (d.Approved ? d.Reason is not null : !Text(d.Reason, 1000))))) throw new JsonException();
        return new(p.Id, p.CompanyId, p.TransferId, source.Version, p.TransferSourceHash, preparation.SourceBookValueTl,
            preparation.Evidence, preparation.Reason, p.PreparedBy, p.PreparedAtUtc,
            d is null ? CashExchangeBookValueStatusV1.Pending : d.Approved ? CashExchangeBookValueStatusV1.Approved : CashExchangeBookValueStatusV1.Rejected,
            d is null ? "1" : "2", d?.DecidedBy, d?.DecidedAtUtc, d?.Reason);
    }
}
