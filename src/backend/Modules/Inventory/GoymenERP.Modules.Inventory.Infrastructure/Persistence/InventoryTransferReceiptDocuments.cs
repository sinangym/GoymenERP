using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Transactions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Only document history is written here. Physical receipt remains the existing Inventory command.</summary>
public sealed class InventoryTransferReceiptDocuments(InventoryDbContext database, ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IInventoryTransferReaderV1 transfers, IAuditEventCollector audit,
    IAuditTransactionWriterV1 auditWriter, InventoryKilogramScale scale, TimeProvider clock,
    IInventoryTransferReceiptDocumentCursorV1? cursors = null) : IInventoryTransferReceiptDocumentsV1
{
    internal const string CreateDocumentCommand = "inventory.transfer.receipt-document.create.v1";
    internal const string FinalizeCommand = "inventory.transfer.receipt-document.finalize.v1";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true };

    public async Task<InventoryTransferReceiptDocumentsV1> ListAsync(Guid transferId, string? cursor = null, CancellationToken cancellationToken = default)
    {
        var scope = Capture();
        try
        {
            await Fence(scope, cancellationToken);
            if (transferId == Guid.Empty) throw Error(400, InventoryTransferReceiptDocumentV1.Invalid);
            if (await transfers.ReadAsync(transferId, cancellationToken) is not { } transfer || transfer.CompanyId != scope.CompanyId)
                throw Error(404, InventoryTransferCodesV1.TransferNotFound);
            Guid? after = null;
            if (cursor is not null)
            {
                if (cursors is not { IsAvailable: true }) throw Error(503, InventoryTransferCodesV1.Unavailable);
                after = cursors.Unprotect(scope.CompanyId, scope.ActorId, transferId, cursor);
            }
            var query = database.TransferReceiptDocumentVersions.AsNoTracking().Where(x => x.TransferId == transferId && x.Version == 1);
            if (after.HasValue) query = query.Where(x => x.DocumentId.CompareTo(after.Value) > 0);
            var ids = await query.Select(x => x.DocumentId).OrderBy(x => x).Take(51).ToListAsync(cancellationToken);
            var selected = ids.Take(50).ToArray();
            var rows = await database.TransferReceiptDocumentVersions.AsNoTracking().Where(x => x.TransferId == transferId && selected.Contains(x.DocumentId))
                .OrderBy(x => x.DocumentId).ThenBy(x => x.Version).ToListAsync(cancellationToken);
            var result = rows.GroupBy(x => x.DocumentId).Select(x => Project(x.ToArray())).ToArray();
            await Fence(scope, cancellationToken);
            var more = ids.Count > 50;
            string? next = null;
            if (more)
            {
                if (cursors is not { IsAvailable: true }) throw Error(503, InventoryTransferCodesV1.Unavailable);
                next = cursors.Protect(scope.CompanyId, scope.ActorId, transferId, selected[^1]);
            }
            return new(scope.CompanyId, transferId, result, more, next);
        }
        catch (OperationCanceledException) { throw; }
        catch (InventoryTransferException) { await Fence(scope, cancellationToken); throw; }
        catch { await Fence(scope, cancellationToken); throw Error(503, InventoryTransferCodesV1.Unavailable); }
    }

    public Task<InventoryTransferReceiptDocumentResultV1> CreateAsync(CreateInventoryTransferReceiptDocumentV1 request,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Write(CreateDocumentCommand, request.DocumentId, request, metadata, async scope =>
        {
            if (request.DocumentId == Guid.Empty || request.TransferId == Guid.Empty || !Version(request.ExpectedTransferVersion, out var expected)
                || request.ReceivedAtUtc == default || request.ReceivedAtUtc.Offset != TimeSpan.Zero || request.Lines is null
                || request.Lines.Count is < 1 or > InventoryTransferApiV1.MaximumLines
                || request.Lines.Any(x => x is null || x.DocumentLineId == Guid.Empty || x.TransferLineId == Guid.Empty)
                || request.Lines.Select(x => x.DocumentLineId).Distinct().Count() != request.Lines.Count
                || request.Lines.Select(x => x.TransferLineId).Distinct().Count() != request.Lines.Count)
                throw Error(400, InventoryTransferReceiptDocumentV1.Invalid);
            if (await database.TransferReceiptDocumentVersions.AsNoTracking().AnyAsync(x => x.DocumentId == request.DocumentId, cancellationToken))
                throw Error(409, InventoryTransferReceiptDocumentV1.VersionConflict);
            var transfer = await RequireTransfer(scope, request.TransferId, expected, cancellationToken);
            var lines = request.Lines.Select(input =>
            {
                var original = transfer.Lines.SingleOrDefault(x => x.LineId == input.TransferLineId && x.Status == "InTransit")
                    ?? throw Error(409, InventoryTransferReceiptDocumentV1.BindingConflict);
                if (!Quantity(input.ReceivedKg, out var kg) || !Quantity(original.ActualShippedKg, out var dispatched))
                    throw Error(400, InventoryTransferReceiptDocumentV1.Invalid);
                var reason = input.OverageReason is null ? null : InventoryTransferReceiptApiV1.NormalizeText(input.OverageReason);
                if (kg > dispatched ? reason is null : input.OverageReason is not null)
                    throw Error(422, kg > dispatched ? InventoryTransferReceiptCodesV1.OverageReasonRequired : InventoryTransferReceiptCodesV1.OverageReasonNotAllowed);
                return new InventoryTransferReceiptDocumentLineV1(input.DocumentLineId, original.LineId, original.LabelId,
                    original.ActualShippedKg!, input.ReceivedKg, reason);
            }).ToArray();
            if (transfer.DispatchedAtUtc is null || request.ReceivedAtUtc < transfer.DispatchedAtUtc)
                throw Error(400, InventoryTransferReceiptDocumentV1.Invalid);
            await RequireScale(scope, request.ReceivedAtUtc, lines, cancellationToken);
            var row = new InventoryTransferReceiptDocumentVersionRow { CompanyId = scope.CompanyId, DocumentId = request.DocumentId,
                Version = 1, Status = 1, TransferId = transfer.TransferId, SourceTransferVersion = expected,
                ReceivedAtUtc = request.ReceivedAtUtc, LinesJson = JsonSerializer.Serialize(lines, Json),
                RecordedBy = scope.ActorId, RecordedAtUtc = clock.GetUtcNow() };
            database.TransferReceiptDocumentVersions.Add(row);
            return new Staged(Project([row]), null);
        }, cancellationToken);
    }

    public Task<InventoryTransferReceiptDocumentResultV1> FinalizeAsync(Guid documentId, FinalizeInventoryTransferReceiptDocumentV1 request,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Write(FinalizeCommand, documentId, request, metadata, async scope =>
        {
            var reason = InventoryTransferReceiptApiV1.NormalizeText(request.Reason);
            if (documentId == Guid.Empty || !Version(request.ExpectedDocumentVersion, out var expected) || expected != 1
                || !Version(request.ExpectedTransferVersion, out var transferVersion) || reason is null)
                throw Error(400, InventoryTransferReceiptDocumentV1.Invalid);
            var history = await database.TransferReceiptDocumentVersions.AsNoTracking().Where(x => x.DocumentId == documentId)
                .OrderBy(x => x.Version).ToListAsync(cancellationToken);
            if (history.Count == 0) throw Error(404, InventoryTransferReceiptDocumentV1.NotFound);
            var prior = Project(history);
            if (prior.Status != "Draft" || prior.Version != request.ExpectedDocumentVersion)
                throw Error(409, InventoryTransferReceiptDocumentV1.VersionConflict);
            var transfer = await RequireTransfer(scope, prior.TransferId, transferVersion, cancellationToken);
            if (prior.Lines.Any(line => !transfer.Lines.Any(x => x.LineId == line.TransferLineId && x.LabelId == line.LabelId
                    && x.Status == "InTransit" && x.ActualShippedKg == line.DispatchedKg)))
                throw Error(409, InventoryTransferReceiptDocumentV1.BindingConflict);
            await RequireScale(scope, prior.ReceivedAtUtc, prior.Lines, cancellationToken);
            var draft = history[0];
            var row = new InventoryTransferReceiptDocumentVersionRow { CompanyId = scope.CompanyId, DocumentId = documentId,
                Version = 2, PreviousVersion = 1, Status = 2, TransferId = draft.TransferId, SourceTransferVersion = draft.SourceTransferVersion,
                ReceivedAtUtc = draft.ReceivedAtUtc, LinesJson = draft.LinesJson, RecordedBy = scope.ActorId,
                RecordedAtUtc = clock.GetUtcNow(), FinalizationReason = reason };
            database.TransferReceiptDocumentVersions.Add(row);
            return new Staged(Project([draft, row]), prior);
        }, cancellationToken);
    }

    private async Task<InventoryTransferReceiptDocumentResultV1> Write(string command, Guid documentId, object request,
        InventoryCommandMetadata metadata, Func<Scope, Task<Staged>> stage, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var scope = Capture();
        await Fence(scope, token);
        if (metadata.ActorId != scope.ActorId || metadata.PermissionCode != InventoryPermissionManifestV1.StockTransferWrite
            || !Text(metadata.IdempotencyKey, 128) || !Text(metadata.CorrelationId, 128) || !Text(metadata.SourceChannel, 64))
            throw Error(400, InventoryTransferReceiptDocumentV1.Invalid);
        if (database.Database.CurrentTransaction is not null || Transaction.Current is not null || database.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts)
            throw Error(503, InventoryTransferCodesV1.Unavailable);
        var key = InventoryStore.GenealogyHash(metadata.IdempotencyKey);
        var hash = InventoryStore.GenealogyPayloadHash(new { documentId, request });
        var started = false;
        try
        {
            database.ChangeTracker.Clear();
            await using var transaction = await database.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
            started = true;
            var replay = await Find(command, key, token);
            if (replay is not null)
            {
                await Fence(scope, token);
                var original = Replay(replay, scope, documentId, hash);
                await transaction.CommitAsync(token);
                return original;
            }
            var staged = await stage(scope); var result = staged.Document;
            var grant = await Fence(scope, token);
            database.Commands.Add(new InventoryCommandRow { CompanyId = scope.CompanyId, CommandName = command, IdempotencyKeyHash = key,
                ActorId = scope.ActorId, PayloadHash = hash, TargetId = documentId, ResultJson = JsonSerializer.Serialize(result, Json),
                CorrelationId = metadata.CorrelationId, RecordedAtUtc = clock.GetUtcNow() });
            audit.Enqueue(new AuditEventDraft(scope.CompanyId, command, "Inventory.TransferReceiptDocument", documentId.ToString("D"),
                command == CreateDocumentCommand ? "Create" : "Finalize", command == CreateDocumentCommand ? null : 1, command == CreateDocumentCommand ? 1 : 2,
                AuditActorTypes.User, scope.ActorId, InventoryPermissionManifestV1.StockTransferWrite, grant.RoleCodes,
                staged.Before is null ? "{}" : JsonSerializer.Serialize(staged.Before, Json), JsonSerializer.Serialize(result, Json), "Transfer hedef kabul belgesi sürümü kaydedildi", null,
                AuditAiInvolvement.None, null, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
                metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
            await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), token);
            await Fence(scope, token);
            await database.SaveChangesAsync(token);
            await Fence(scope, token);
            await transaction.CommitAsync(token);
            return new(result, false, metadata.CorrelationId);
        }
        catch (DbUpdateException error) when (error.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            database.ChangeTracker.Clear(); auditWriter.DiscardPending();
            var winner = await Find(command, key, token);
            await Fence(scope, token);
            if (winner is not null) return Replay(winner, scope, documentId, hash);
            throw Error(409, InventoryTransferReceiptDocumentV1.VersionConflict);
        }
        catch (OperationCanceledException) { throw; }
        catch (InventoryTransferException) { await Fence(scope, token); throw; }
        catch { await Fence(scope, token); throw Error(503, InventoryTransferCodesV1.Unavailable); }
        finally { if (started) { database.ChangeTracker.Clear(); auditWriter.DiscardPending(); } }
    }

    private Task<InventoryCommandRow?> Find(string command, string key, CancellationToken token) => database.Commands.AsNoTracking()
        .SingleOrDefaultAsync(x => x.CommandName == command && x.IdempotencyKeyHash == key, token);
    private static InventoryTransferReceiptDocumentResultV1 Replay(InventoryCommandRow row, Scope scope, Guid id, string hash)
    {
        if (row.ActorId != scope.ActorId || row.PayloadHash != hash || row.TargetId != id)
            throw Error(409, InventoryTransferCodesV1.IdempotencyConflict);
        var value = JsonSerializer.Deserialize<InventoryTransferReceiptDocumentViewV1>(row.ResultJson, Json);
        if (row.CompanyId != scope.CompanyId || value is null || value.CompanyId != scope.CompanyId || value.DocumentId != id
            || !Valid(value) || !Text(row.CorrelationId, 128)) throw Error(503, InventoryTransferCodesV1.Unavailable);
        return new(value, true, row.CorrelationId);
    }
    private async Task<InventoryTransferViewV1> RequireTransfer(Scope scope, Guid id, long expected, CancellationToken token)
    {
        var value = await transfers.ReadAsync(id, token);
        await Fence(scope, token);
        if (value is null) throw Error(404, InventoryTransferCodesV1.TransferNotFound);
        if (value.CompanyId != scope.CompanyId || value.TransferId != id) throw Error(503, InventoryTransferCodesV1.Unavailable);
        if (value.Version != expected.ToString(CultureInfo.InvariantCulture)) throw Error(409, InventoryTransferCodesV1.TransferVersionConflict);
        if (value.Status is not ("InTransit" or "PartiallyAccepted")) throw Error(409, InventoryTransferCodesV1.TransferStateConflict);
        return value;
    }
    private async Task RequireScale(Scope scope, DateTimeOffset at, IReadOnlyList<InventoryTransferReceiptDocumentLineV1> lines, CancellationToken token)
    {
        var result = await scale.CheckAsync(scope.CompanyId, DateOnly.FromDateTime(at.UtcDateTime),
            lines.Select(x => Kilograms.Positive(decimal.Parse(x.ReceivedKg, CultureInfo.InvariantCulture))), token);
        await Fence(scope, token);
        if (!result.IsAccepted) throw Error(503, InventoryTransferCodesV1.Unavailable);
    }
    private Scope Capture()
    {
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)
            || !user.CanAccessCompany(company.CompanyId)) throw Error(403, InventoryTransferCodesV1.Forbidden);
        return new(company.CompanyId, user.UserId);
    }
    private async Task<PermissionScope> Fence(Scope scope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Capture() != scope) throw Error(403, InventoryTransferCodesV1.Forbidden);
        PermissionScope grant;
        try { grant = await authority.RevalidatePermissionScopeAsync(InventoryPermissionManifestV1.StockTransferWrite, scope.CompanyId, token); }
        catch (OperationCanceledException) { throw; }
        catch
        {
            if (Capture() != scope) throw Error(403, InventoryTransferCodesV1.Forbidden);
            throw Error(503, InventoryTransferCodesV1.Unavailable);
        }
        if (Capture() != scope || !grant.IsGranted || !grant.IsCompanyWide) throw Error(403, InventoryTransferCodesV1.Forbidden);
        return grant;
    }
    internal static InventoryTransferReceiptDocumentViewV1 Project(IReadOnlyList<InventoryTransferReceiptDocumentVersionRow> rows)
    {
        if (rows.Count is < 1 or > 2 || rows[0].Version != 1 || rows[0].Status != 1 || rows[0].PreviousVersion is not null
            || rows.Count == 2 && (rows[1].Version != 2 || rows[1].Status != 2 || rows[1].PreviousVersion != 1))
            throw Error(503, InventoryTransferCodesV1.Unavailable);
        var first = rows[0]; var last = rows[^1];
        if (rows.Any(x => x.CompanyId != first.CompanyId || x.DocumentId != first.DocumentId || x.TransferId != first.TransferId
            || x.SourceTransferVersion != first.SourceTransferVersion || x.LinesJson != first.LinesJson || x.ReceivedAtUtc != first.ReceivedAtUtc
            || !Text(x.RecordedBy, 128) || x.RecordedAtUtc == default || x.RecordedAtUtc.Offset != TimeSpan.Zero)
            || first.FinalizationReason is not null || last.Version == 2 && InventoryTransferReceiptApiV1.NormalizeText(last.FinalizationReason) is null)
            throw Error(503, InventoryTransferCodesV1.Unavailable);
        var lines = JsonSerializer.Deserialize<InventoryTransferReceiptDocumentLineV1[]>(last.LinesJson, Json);
        var value = new InventoryTransferReceiptDocumentViewV1(first.CompanyId, first.DocumentId, first.TransferId,
            last.Version.ToString(CultureInfo.InvariantCulture), last.Status == 1 ? "Draft" : "Effective",
            first.SourceTransferVersion.ToString(CultureInfo.InvariantCulture), first.ReceivedAtUtc, lines ?? [], first.RecordedBy,
            first.RecordedAtUtc, last.Version == 2 ? last.RecordedBy : null, last.Version == 2 ? last.RecordedAtUtc : null,
            last.FinalizationReason, InventoryTransferReceiptDocumentV1.SourceVersion);
        return Valid(value) ? value : throw Error(503, InventoryTransferCodesV1.Unavailable);
    }
    internal static bool Valid(InventoryTransferReceiptDocumentViewV1 value) => value.CompanyId != Guid.Empty && value.DocumentId != Guid.Empty
        && value.TransferId != Guid.Empty && Version(value.SourceTransferVersion, out _) && value.ReceivedAtUtc != default && value.ReceivedAtUtc.Offset == TimeSpan.Zero
        && value.Lines is { Count: > 0 and <= InventoryTransferApiV1.MaximumLines }
        && value.Lines.All(x => x is not null && x.DocumentLineId != Guid.Empty && x.TransferLineId != Guid.Empty && x.LabelId != Guid.Empty
            && Quantity(x.ReceivedKg, out var kg) && Quantity(x.DispatchedKg, out var dispatched)
            && (kg > dispatched ? InventoryTransferReceiptApiV1.NormalizeText(x.OverageReason) == x.OverageReason && x.OverageReason is not null : x.OverageReason is null))
        && value.Lines.Select(x => x.DocumentLineId).Distinct().Count() == value.Lines.Count
        && value.Lines.Select(x => x.TransferLineId).Distinct().Count() == value.Lines.Count
        && value.SourceVersion == InventoryTransferReceiptDocumentV1.SourceVersion
        && Text(value.CreatedBy, 128) && value.CreatedAtUtc != default && value.CreatedAtUtc.Offset == TimeSpan.Zero
        && (value.Status == "Draft" && value.Version == "1" && value.FinalizedBy is null && value.FinalizedAtUtc is null && value.FinalizationReason is null
            || value.Status == "Effective" && value.Version == "2" && Text(value.FinalizedBy, 128) && value.FinalizedAtUtc is { } at && at.Offset == TimeSpan.Zero
                && at >= value.CreatedAtUtc && InventoryTransferReceiptApiV1.NormalizeText(value.FinalizationReason) == value.FinalizationReason && value.FinalizationReason is not null);
    internal static bool Quantity(string? text, out decimal value)
    {
        value = 0;
        if (text is null || text.Length > 32 || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) return false;
        try { return Kilograms.Positive(value).ToString() == text; } catch (InventoryRuleException) { return false; }
    }
    internal static bool Version(string? text, out long value) => long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value)
        && value > 0 && text == value.ToString(CultureInfo.InvariantCulture);
    private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value == value.Trim()
        && value.Length <= max && !value.Any(char.IsControl);
    private static InventoryTransferException Error(int status, string code) => new(status, code);
    private sealed record Scope(Guid CompanyId, string ActorId);
    private sealed record Staged(InventoryTransferReceiptDocumentViewV1 Document, InventoryTransferReceiptDocumentViewV1? Before);
}
