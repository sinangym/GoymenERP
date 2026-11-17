using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Domain.Events;
using GoymenERP.Modules.Logistics.Application.Contracts;
using GoymenERP.Modules.Logistics.Domain.Loading;
using GoymenERP.Modules.Logistics.Domain.Quantities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Logistics.Infrastructure.Persistence;

/// <summary>
/// LOJ-D11 / KLN-D25(7), D27. Genuine owner facts, not an inference from a document number. The irreversible
/// no-exit fence and physical events contend on one versioned row. Inventory's later rollback never reopens it.
/// No GİB-send implementation is manufactured here: a linked document needs its real external owner proof.
/// </summary>
public sealed class LogisticsTransferDepartureOwner(LogisticsDbContext database, ICompanyContext company,
    ICurrentUser user, IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter, TimeProvider clock,
    ILogisticsTransferReferenceSourceV1? transfers = null,
    ILogisticsTransferEDispatchEvidenceSourceV1? officialDocuments = null) : ILogisticsTransferDepartureOwnerV1
{
    internal const string RecordCommand = "logistics.transfer-departure.record.v1";
    internal const string ConfirmCommand = "logistics.transfer-departure.confirm-no-exit.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<bool> SupportsWarehouseAsync(Guid companyId, Guid locationId, CancellationToken cancellationToken = default)
    {
        var actor = Member(companyId, cancellationToken);
        if (locationId == Guid.Empty || transfers is null) return false;
        // Prove the actual owner schema is readable, not merely that an interface is registered. The host separately
        // proves that this exact Organization location is an active internal warehouse. No event is invented.
        try
        {
            _ = await database.TransferDepartureStates.AsNoTracking().Select(x => x.TransferId).Take(1).ToArrayAsync(cancellationToken);
            _ = await database.TransferDepartureEvents.AsNoTracking().Select(x => x.TransferId).Take(1).ToArrayAsync(cancellationToken);
            Still(companyId, actor, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { Still(companyId, actor, cancellationToken); return false; }
    }

    public async Task<LogisticsTransferDepartureFactsV1> ReadAsync(LogisticsTransferDepartureQueryV1 query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var actor = Member(query.CompanyId, cancellationToken);
        await RequireDispatchAsync(query, actor, cancellationToken);
        var row = await database.TransferDepartureStates.AsNoTracking().SingleOrDefaultAsync(x => x.TransferId == query.TransferId,
            cancellationToken);
        Still(query.CompanyId, actor, cancellationToken);
        if (row is null) return Unknown(query);
        Validate(row, query);
        await ValidateHistoryAsync(row, cancellationToken);
        var proof = await OfficialAsync(row, actor, cancellationToken);
        Still(query.CompanyId, actor, cancellationToken);
        return Facts(row, proof);
    }

    public async Task<LogisticsTransferDepartureRecordedV1> RecordAsync(Guid transferId, RecordTransferDepartureV1 request,
        LogisticsWriteEnvelope envelope, Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        var actor = RequireEnvelope(companyId, envelope, LogisticsTransferDepartureV1.RecordPermission, cancellationToken);
        if (transferId == Guid.Empty || !TryVersion(request.ExpectedVersion, out var expected)
            || request.Kind is not (LogisticsTransferDepartureEventKindV1.ExitWeighing or LogisticsTransferDepartureEventKindV1.VehicleExit)
            || request.OccurredAtUtc == default || request.OccurredAtUtc.Offset != TimeSpan.Zero
            || request.OccurredAtUtc > clock.GetUtcNow() || !TryReason(request.Reason, out var reason)) throw Invalid();
        var weighing = Weighing(request);
        var hash = LogisticsRequestHash.Compute(RecordCommand, transferId,
            new { ExpectedVersion = expected, request.Kind, request.OccurredAtUtc, Reason = reason, Weighing = weighing });
        var recorded = await OwnedAsync(RecordCommand, transferId, hash, envelope, freshAuthority, async token =>
        {
            var row = await StateAsync(transferId, token);
            var reference = await ReferenceAsync(companyId, transferId, actor, token);
            var query = Query(reference);
            Validate(row, query);
            if (request.OccurredAtUtc < query.DispatchedAtUtc || row.Version != expected) throw Conflict("logistics_transfer_departure_version_conflict");
            if (row.NoFurtherExitVersion is not null) throw Conflict("logistics_transfer_departure_closed");
            if (request.Kind == LogisticsTransferDepartureEventKindV1.ExitWeighing ? row.ExitWeighingAtUtc is not null : row.VehicleExitAtUtc is not null)
                throw Conflict("logistics_transfer_departure_already_recorded");
            var proof = await OfficialAsync(row, actor, token);
            if (request.Kind == LogisticsTransferDepartureEventKindV1.ExitWeighing) row.ExitWeighingAtUtc = request.OccurredAtUtc;
            else row.VehicleExitAtUtc = request.OccurredAtUtc;
            Advance(row, request.Kind.ToString(), request.OccurredAtUtc, reason, envelope, weighing);
            return Facts(row, proof);
        }, cancellationToken);
        return new(recorded.Value, recorded.IsReplay, recorded.OriginalCorrelationId);
    }

    public async Task<LogisticsTransferCancelConfirmationV1> ConfirmAsync(LogisticsTransferCancelIntentV1 intent,
        LogisticsWriteEnvelope envelope, Func<CancellationToken, Task> freshAuthority, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(intent.Dispatch);
        var actor = RequireEnvelope(intent.Dispatch.CompanyId, envelope, ErpPermissions.InventoryStockTransferCancel, cancellationToken);
        if (!TryVersion(intent.ExpectedTransferVersion, out _) || intent.CancelledAtUtc == default
            || intent.CancelledAtUtc.Offset != TimeSpan.Zero || intent.CancelledAtUtc < intent.Dispatch.DispatchedAtUtc
            || !TryReason(intent.Reason, out var reason)) throw Invalid();
        var hash = LogisticsRequestHash.Compute(ConfirmCommand, intent.Dispatch.TransferId, intent with { Reason = reason });
        var confirmed = await OwnedAsync(ConfirmCommand, intent.Dispatch.TransferId, hash, envelope, freshAuthority, async token =>
        {
            var row = await StateAsync(intent.Dispatch.TransferId, token);
            var reference = await RequireDispatchAsync(intent.Dispatch, actor, token);
            if (Text(reference.Version) != intent.ExpectedTransferVersion) throw Conflict("logistics_transfer_departure_version_conflict");
            Validate(row, intent.Dispatch);
            if (intent.RequireNoExit && (row.ExitWeighingAtUtc is not null || row.VehicleExitAtUtc is not null))
                throw Conflict("logistics_transfer_departure_already_recorded");
            var proof = await OfficialAsync(row, actor, token);
            long? externalConfirmation = null;
            if (intent.RequireEDispatchCancellation)
            {
                if (row.Association != (int)LogisticsTransferEDispatchAssociationV1.Linked || officialDocuments is null)
                    throw Unavailable();
                proof = await officialDocuments.ConfirmCancellationAsync(row.CompanyId, row.TransferId, envelope,
                    token2 => FenceAsync(row.CompanyId, actor, freshAuthority, token2), token);
                await FenceAsync(row.CompanyId, actor, freshAuthority, token);
                if (!LogisticsTransferDepartureProof.IsValid(proof, row.CompanyId, row.TransferId)
                    || !SameOfficialBinding(row, proof!) || proof!.IsSentToGib != false || proof.CancelConfirmationVersion is not > 0) throw Unavailable();
                externalConfirmation = proof.CancelConfirmationVersion;
            }
            // A fence from a failed later Inventory reversal remains closed. Same intent/key replays its original
            // confirmation; a new key can obtain the existing fence, but can never remove it or allow an exit.
            if (intent.RequireNoExit && row.NoFurtherExitVersion is null)
            {
                row.NoFurtherExitVersion = checked(row.Version + 1);
                Advance(row, "NoFurtherExit", clock.GetUtcNow(), reason, envelope);
            }
            return new LogisticsTransferCancelConfirmationV1(Facts(row, proof),
                intent.RequireNoExit ? row.NoFurtherExitVersion : null, externalConfirmation);
        }, cancellationToken);
        return confirmed.Value;
    }

    private async Task<OwnerWrite<T>> OwnedAsync<T>(string command, Guid transferId, string hash, LogisticsWriteEnvelope envelope,
        Func<CancellationToken, Task> freshAuthority, Func<CancellationToken, Task<T>> stage, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(freshAuthority);
        var companyId = company.CompanyId;
        var actor = envelope.ActorId;
        await FenceAsync(companyId, actor, freshAuthority, token);
        if (database.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || database.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts) throw Unavailable();
        // Only unchanged owner cache may be cleared; this method refuses callers' staged work above.
        database.ChangeTracker.Clear();
        var key = LogisticsStore.IdempotencyKeyHash(envelope.IdempotencyKey);
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var prior = await database.TransferDepartureAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.CommandName == command
                && x.IdempotencyKeyHash == key, token);
            if (prior is not null)
            {
                await FenceAsync(companyId, actor, freshAuthority, token);
                var replay = Replay<T>(prior, companyId, transferId, envelope.ActorId, hash);
                await transaction.CommitAsync(token);
                return new(replay, true, prior.CorrelationId);
            }
            var result = await stage(token);
            await FenceAsync(companyId, actor, freshAuthority, token);
            database.TransferDepartureAttempts.Add(new TransferDepartureAttemptRow { CompanyId = companyId, TransferId = transferId,
                CommandName = command, IdempotencyKeyHash = key, RequestHash = hash, ActorId = actor,
                ResultJson = JsonSerializer.Serialize(result, Json), CorrelationId = envelope.CorrelationId, RecordedAtUtc = clock.GetUtcNow() });
            await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), token);
            await FenceAsync(companyId, actor, freshAuthority, token);
            await database.SaveChangesAsync(token);
            await FenceAsync(companyId, actor, freshAuthority, token);
            await transaction.CommitAsync(token);
            return new(result, false, envelope.CorrelationId);
        }
        catch (DbUpdateConcurrencyException) { throw Conflict("logistics_transfer_departure_version_conflict"); }
        catch (Exception exception) when (SqlNumber(exception) is 2601 or 2627 or 1205)
        {
            // The owned transaction has disposed/rolled back. No retry may invoke a physical/official owner callback twice.
            throw Conflict("logistics_transfer_departure_version_conflict");
        }
        finally { database.ChangeTracker.Clear(); auditWriter.DiscardPending(); }
    }

    private async Task<TransferDepartureStateRow> StateAsync(Guid transferId, CancellationToken token)
    {
        var row = await database.TransferDepartureStates.SingleOrDefaultAsync(x => x.TransferId == transferId, token) ?? throw Unavailable();
        await ValidateHistoryAsync(row, token);
        return row;
    }
    private async Task<LogisticsTransferReferenceV1> ReferenceAsync(Guid companyId, Guid transferId, string actor, CancellationToken token)
    {
        var value = transfers is null ? null : await transfers.ReadAsync(companyId, transferId, token);
        Still(companyId, actor, token);
        if (value is null || value.CompanyId != companyId || value.TransferId != transferId || value.SourceLocationId == Guid.Empty
            || value.Version < 1 || value.DispatchedAtUtc is not { } at || at == default || at.Offset != TimeSpan.Zero
            || value.Status is not ("InTransit" or "PartiallyAccepted" or "Accepted" or "DispatchCancelled" or "Closed")) throw Unavailable();
        return value;
    }
    private async Task<LogisticsTransferReferenceV1> RequireDispatchAsync(LogisticsTransferDepartureQueryV1 query, string actor, CancellationToken token)
    {
        var value = await ReferenceAsync(query.CompanyId, query.TransferId, actor, token);
        if (value.SourceLocationId != query.SourceLocationId || value.DispatchedAtUtc != query.DispatchedAtUtc) throw Unavailable();
        return value;
    }
    private async Task<LogisticsTransferEDispatchEvidenceV1?> OfficialAsync(TransferDepartureStateRow row, string actor, CancellationToken token)
    {
        if (row.Association == (int)LogisticsTransferEDispatchAssociationV1.NotLinked) return null;
        if (row.Association != (int)LogisticsTransferEDispatchAssociationV1.Linked || officialDocuments is null) throw Unavailable();
        var proof = await officialDocuments.ReadAsync(row.CompanyId, row.TransferId, token);
        Still(row.CompanyId, actor, token);
        return LogisticsTransferDepartureProof.IsValid(proof, row.CompanyId, row.TransferId) && SameOfficialBinding(row, proof!)
            ? proof : throw Unavailable();
    }
    private void Advance(TransferDepartureStateRow row, string kind, DateTimeOffset at, string reason, LogisticsWriteEnvelope envelope,
        WeighingSnapshot? weighing = null)
    {
        var previous = row.Version;
        row.Version = checked(previous + 1);
        database.TransferDepartureEvents.Add(new TransferDepartureEventRow { CompanyId = row.CompanyId, TransferId = row.TransferId,
            Version = row.Version, Kind = kind, OccurredAtUtc = at, RecordedBy = envelope.ActorId,
            RecordedAtUtc = clock.GetUtcNow(), Reason = reason, CorrelationId = envelope.CorrelationId,
            WeighingDirection = weighing?.Direction, FirstWeighingKg = weighing?.FirstKg,
            SecondWeighingKg = weighing?.SecondKg, NetWeighingKg = weighing?.NetKg });
        audit.Enqueue(new AuditEventDraft(row.CompanyId, "logistics.transfer-departure." + kind.ToLowerInvariant() + ".v1",
            "Logistics.TransferDeparture", row.TransferId.ToString("D"), kind, previous, row.Version, AuditActorTypes.User,
            envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes, JsonSerializer.Serialize(new { version = previous }, Json),
            JsonSerializer.Serialize(new { facts = Facts(row, null), weighing }, Json), reason, null, AuditAiInvolvement.None, null,
            envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent, envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey));
    }
    private string RequireEnvelope(Guid companyId, LogisticsWriteEnvelope envelope, string permission, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var actor = Member(companyId, token);
        if (actor != envelope.ActorId || envelope.PermissionCode != permission || envelope.RoleCodes is null
            || string.IsNullOrWhiteSpace(envelope.IdempotencyKey) || envelope.IdempotencyKey.Length > 128
            || string.IsNullOrWhiteSpace(envelope.CorrelationId) || envelope.CorrelationId.Length > 128
            || string.IsNullOrWhiteSpace(envelope.SourceChannel)) throw new UnauthorizedAccessException();
        return actor;
    }
    private string Member(Guid companyId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId || !user.IsAuthenticated
            || string.IsNullOrWhiteSpace(user.UserId) || !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException();
        return user.UserId;
    }
    private void Still(Guid companyId, string actor, CancellationToken token)
    { if (Member(companyId, token) != actor) throw new UnauthorizedAccessException(); }
    private async Task FenceAsync(Guid companyId, string actor, Func<CancellationToken, Task> fence, CancellationToken token)
    { Still(companyId, actor, token); await fence(token); Still(companyId, actor, token); }
    private static void Validate(TransferDepartureStateRow row, LogisticsTransferDepartureQueryV1 query)
    {
        if (row.CompanyId != query.CompanyId || row.TransferId != query.TransferId || row.SourceLocationId != query.SourceLocationId
            || row.DocumentId == Guid.Empty || row.Version < 1 || row.Association is not (1 or 2)
            || row.NoFurtherExitVersion is { } noExit && (noExit < 1 || noExit > row.Version
                || row.ExitWeighingAtUtc is not null || row.VehicleExitAtUtc is not null)
            || row.ExitWeighingAtUtc is { } weighing && (weighing.Offset != TimeSpan.Zero || weighing < query.DispatchedAtUtc)
            || row.VehicleExitAtUtc is { } exit && (exit.Offset != TimeSpan.Zero || exit < query.DispatchedAtUtc)) throw Unavailable();
    }
    private static LogisticsTransferDepartureQueryV1 Query(LogisticsTransferReferenceV1 reference) =>
        new(reference.CompanyId, reference.TransferId, reference.SourceLocationId, reference.DispatchedAtUtc!.Value);
    private static LogisticsTransferDepartureFactsV1 Facts(TransferDepartureStateRow row, LogisticsTransferEDispatchEvidenceV1? proof) =>
        new(row.CompanyId, row.TransferId, row.SourceLocationId, (LogisticsTransferEDispatchAssociationV1)row.Association,
            row.Version, row.ExitWeighingAtUtc, row.VehicleExitAtUtc, row.NoFurtherExitVersion is not null, proof,
            LogisticsTransferDepartureV1.SourceVersion + "/" + Text(row.Version));
    private static LogisticsTransferDepartureFactsV1 Unknown(LogisticsTransferDepartureQueryV1 query) =>
        new(query.CompanyId, query.TransferId, query.SourceLocationId, LogisticsTransferEDispatchAssociationV1.Unknown,
            null, null, null, false, null, LogisticsTransferDepartureV1.SourceVersion + "/legacy-unknown");
    private static T Replay<T>(TransferDepartureAttemptRow row, Guid companyId, Guid transferId, string actor, string hash)
    {
        if (row.ActorId != actor || row.RequestHash != hash || row.TransferId != transferId)
            throw Conflict("logistics_transfer_departure_idempotency_conflict");
        if (row.CompanyId != companyId || string.IsNullOrWhiteSpace(row.CorrelationId)) throw Unavailable();
        var result = JsonSerializer.Deserialize<T>(row.ResultJson, Json);
        var facts = result switch { LogisticsTransferDepartureFactsV1 value => value,
            LogisticsTransferCancelConfirmationV1 value => value.Facts, _ => null };
        if (facts is null || facts.CompanyId != companyId || facts.TransferId != transferId || facts.Version is not > 0
            || facts.SourceLocationId == Guid.Empty || facts.Association is not (LogisticsTransferEDispatchAssociationV1.NotLinked
                or LogisticsTransferEDispatchAssociationV1.Linked)
            || facts.SourceVersion != LogisticsTransferDepartureV1.SourceVersion + "/" + Text(facts.Version.Value)
            || facts.NoFurtherExit && (facts.ExitWeighingAtUtc is not null || facts.VehicleExitAtUtc is not null)
            || facts.ExitWeighingAtUtc is { } weighing && (weighing == default || weighing.Offset != TimeSpan.Zero)
            || facts.VehicleExitAtUtc is { } exit && (exit == default || exit.Offset != TimeSpan.Zero)
            || facts.Association == LogisticsTransferEDispatchAssociationV1.NotLinked && facts.OfficialDocument is not null
            || facts.Association == LogisticsTransferEDispatchAssociationV1.Linked
                && !LogisticsTransferDepartureProof.IsValid(facts.OfficialDocument, companyId, transferId)) throw Unavailable();
        if (result is LogisticsTransferCancelConfirmationV1 confirmation
            && (confirmation.NoExitConfirmationVersion is { } fence && (!facts.NoFurtherExit || fence < 1 || fence > facts.Version)
                || confirmation.EDispatchCancellationVersion is { } official && (official < 1
                    || facts.OfficialDocument?.IsSentToGib != false || facts.OfficialDocument.CancelConfirmationVersion != official)))
            throw Unavailable();
        return result!;
    }
    private static bool TryVersion(string? value, out long version) => long.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out version) && version > 0 && value == Text(version);
    private static bool TryReason(string? value, out string reason)
    { reason = value?.Trim() ?? ""; return reason.Length is > 0 and <= 4000 && !reason.Any(char.IsControl); }
    private static WeighingSnapshot? Weighing(RecordTransferDepartureV1 request)
    {
        if (request.Kind != LogisticsTransferDepartureEventKindV1.ExitWeighing)
            return request.Weighing is null ? null : throw Invalid();
        if (request.Weighing is not { } weighing || !Kg(weighing.FirstKg, out var first) || !Kg(weighing.SecondKg, out var second)) throw Invalid();
        try
        {
            var net = WeighbridgeCalculator.Net((WeighingDirection)weighing.Direction,
                Quantity.Of(first, QuantityUnit.Kilogram), Quantity.Of(second, QuantityUnit.Kilogram));
            return new(weighing.Direction, first, second, net.Value);
        }
        catch (ArgumentException) { throw Invalid(); }
        catch (GoymenERP.Modules.Logistics.Domain.LogisticsRuleException) { throw Invalid(); }
    }
    private async Task ValidateHistoryAsync(TransferDepartureStateRow row, CancellationToken token)
    {
        if (!await database.TransferOutgoingDocuments.AsNoTracking().AnyAsync(x => x.DocumentId == row.DocumentId
            && x.TransferId == row.TransferId, token)) throw Unavailable();
        var history = await database.TransferDepartureEvents.AsNoTracking().Where(x => x.TransferId == row.TransferId)
            .OrderBy(x => x.Version).Take(5).ToArrayAsync(token);
        if (history.Length != row.Version || history.Length is < 1 or > 3 || history[0].Kind != "AssociationRecorded"
            || history[0].OccurredAtUtc != row.CreatedAtUtc || history.Any(x => x.CompanyId != row.CompanyId)
            || history.Where((item, index) => item.Version != index + 1).Any()) throw Unavailable();
        var weighing = history.SingleOrDefault(x => x.Kind == "ExitWeighing");
        var exit = history.SingleOrDefault(x => x.Kind == "VehicleExit");
        var fence = history.SingleOrDefault(x => x.Kind == "NoFurtherExit");
        if (weighing?.OccurredAtUtc != row.ExitWeighingAtUtc || exit?.OccurredAtUtc != row.VehicleExitAtUtc
            || fence?.Version != row.NoFurtherExitVersion) throw Unavailable();
        if (weighing is not null)
        {
            if (weighing.WeighingDirection is not { } direction || weighing.FirstWeighingKg is not { } first
                || weighing.SecondWeighingKg is not { } second || weighing.NetWeighingKg is not { } recorded) throw Unavailable();
            var actual = Weighing(new("1", LogisticsTransferDepartureEventKindV1.ExitWeighing, weighing.OccurredAtUtc,
                weighing.Reason, new(direction, first.ToString(CultureInfo.InvariantCulture), second.ToString(CultureInfo.InvariantCulture))));
            if (actual?.NetKg != recorded) throw Unavailable();
        }
    }
    private static bool Kg(string? value, out decimal amount) => decimal.TryParse(value, NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out amount) && amount > 0 && amount < 1000000000000000m && decimal.Round(amount, 3) == amount;
    private sealed record WeighingSnapshot(int Direction, decimal FirstKg, decimal SecondKg, decimal NetKg);
    private sealed record OwnerWrite<T>(T Value, bool IsReplay, string OriginalCorrelationId);
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static bool SameOfficialBinding(TransferDepartureStateRow row, LogisticsTransferEDispatchEvidenceV1 proof) =>
        row.OfficialDocumentId == proof.OfficialDocumentId && row.InitialOfficialDocumentVersion is > 0
        && proof.Version >= row.InitialOfficialDocumentVersion && row.OfficialLinkedAtUtc == proof.LinkedAtUtc;
    private static int? SqlNumber(Exception exception)
    { for (Exception? item = exception; item is not null; item = item.InnerException) if (item is SqlException sql) return sql.Number; return null; }
    private static LogisticsTransferDepartureException Unavailable() => new(503, "logistics_transfer_departure_unavailable");
    private static LogisticsTransferDepartureException Invalid() => new(422, "logistics_transfer_departure_request_invalid");
    private static LogisticsTransferDepartureException Conflict(string code) => new(409, code);
}
