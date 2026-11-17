using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

/// <summary>
/// LOJ-D11: actual per-warehouse append-only policy. NotConfigured Off is a decided default, not a seeded version;
/// explicit Off remains distinguishable. A failing owner does not turn into default Off. No departure is recorded here.
/// </summary>
public sealed class SettingsWarehouseTransferScalePolicy(SettingsDbContext database, ICompanyContext company,
    ICurrentUser user, IErpAuthorizationService authorization, IErpAuthorityRevalidationService authority,
    IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter, TimeProvider clock,
    ISettingsTransferWarehouseSourceV1? warehouses = null, ISettingsTransferDepartureCapabilityV1? departure = null)
    : ISettingsWarehouseTransferScaleProviderV1, ISettingsWarehouseTransferScaleAdministrationV1
{
    private const string Command = "settings.warehouse-transfer-scale.record.v1";
    private const string EntityType = "Settings.WarehouseTransferScale";
    private const string InvalidCode = "settings_warehouse_transfer_scale_request_invalid";
    private const string VersionConflict = "settings_warehouse_transfer_scale_version_conflict";
    private const string KeyConflict = "settings_warehouse_transfer_scale_idempotency_conflict";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SettingsWarehouseTransferScalePolicyV1> GetAsync(Guid companyId, Guid locationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var actor = Member(companyId, cancellationToken);
        try
        {
            await WarehouseAsync(companyId, locationId, actor, cancellationToken);
            var row = await LatestAsync(locationId, cancellationToken);
            Still(companyId, actor, cancellationToken);
            if (row is not null) Validate(row, companyId, locationId);
            return Policy(companyId, locationId, row);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            Still(companyId, actor, cancellationToken);
            return new(SettingsWarehouseTransferScaleStatusV1.Unavailable, companyId, locationId, null, null,
                SettingsWarehouseTransferScaleV1.SourceVersion + "/unavailable");
        }
    }

    public async Task<WarehouseTransferScaleViewV1> ReadAsync(Guid locationId, CancellationToken cancellationToken = default)
    {
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        var actor = Member(companyId, cancellationToken);
        await FenceAsync(companyId, actor, fresh: false, cancellationToken);
        try
        {
            await WarehouseAsync(companyId, locationId, actor, cancellationToken);
            var rows = await database.WarehouseTransferScaleVersions.AsNoTracking().Where(x => x.LocationId == locationId)
                .OrderByDescending(x => x.Version).ToArrayAsync(cancellationToken);
            await FenceAsync(companyId, actor, fresh: true, cancellationToken);
            foreach (var row in rows) Validate(row, companyId, locationId);
            if (rows.Select(x => x.Version).Where((version, index) => version != rows.Length - index).Any()) throw Unavailable();
            return new(companyId, locationId, Policy(companyId, locationId, rows.FirstOrDefault()), rows.Select(View).ToArray());
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        catch (SettingsPrerequisiteException) { throw; }
        catch { Still(companyId, actor, cancellationToken); throw Unavailable(); }
    }

    public async Task<CompanyPolicyCommandResultV1> RecordAsync(Guid locationId, RecordWarehouseTransferScaleV1 request,
        SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(metadata);
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty;
        var actor = Member(companyId, cancellationToken);
        await FenceAsync(companyId, actor, fresh: false, cancellationToken);
        if (locationId == Guid.Empty || metadata.CompanyId != companyId || metadata.ActorId != actor
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || metadata.IdempotencyKey.Length > 128
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.SourceChannel))
            return new(Invalid("Şirket, depo ve komut bilgileri geçerli olmalıdır."), false, metadata.CorrelationId);
        var reason = request.Reason?.Trim() ?? string.Empty;
        if (!long.TryParse(request.ExpectedVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var expected)
            || expected < 0 || request.ExpectedVersion != Text(expected) || request.RequireExitWeighing is not { } enabled
            || reason.Length is 0 or > SettingsWarehouseTransferScaleV1.MaximumReasonLength || reason.Any(char.IsControl))
            return new(Invalid("Beklenen sürüm, açık depo ayarı ve gerekçe zorunludur."), false, metadata.CorrelationId);
        if (database.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || database.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts) throw Unavailable();
        var key = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var hash = IdempotencyHash.ComputePayload(new { LocationId = locationId, ExpectedVersion = expected,
            RequireExitWeighing = enabled, Reason = reason });
        var started = false;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            started = true;
            if (await FindAsync(key, cancellationToken) is { } prior)
            {
                await FenceAsync(companyId, actor, fresh: true, cancellationToken);
                var replay = Replay(prior, hash, metadata);
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }
            var warehouse = await WarehouseAsync(companyId, locationId, actor, cancellationToken);
            if (enabled)
            {
                if (departure is null || !await departure.IsSupportedAsync(companyId, locationId, cancellationToken)) throw Unavailable();
                await FenceAsync(companyId, actor, fresh: true, cancellationToken);
            }
            var latest = await LatestAsync(locationId, cancellationToken);
            if (latest is not null) Validate(latest, companyId, locationId);
            CompanyPolicyCommandAnswerV1 answer;
            if (expected != (latest?.Version ?? 0)) answer = new(409, VersionConflict, "Kayıt değişmiş.", "Güncel depo ayarını okuyup yeniden deneyin.");
            else
            {
                answer = new(204, null, "Kaydedildi.", "Transfer çıkış tartısı depo ayarı kaydedildi.");
                // Explicit first Off is a real recorded choice; it is not collapsed into virgin configuration.
                if (latest is null || latest.RequireExitWeighing != enabled)
                {
                    var row = new WarehouseTransferScaleVersionRow { CompanyId = companyId, LocationId = locationId,
                        Version = checked(expected + 1), RecordId = Guid.NewGuid(), RequireExitWeighing = enabled,
                        LocationVersion = warehouse.LocationVersion, RecordedBy = actor, RecordedAtUtc = clock.GetUtcNow(), Reason = reason };
                    Validate(row, companyId, locationId);
                    database.WarehouseTransferScaleVersions.Add(row);
                    var scope = await FenceAsync(companyId, actor, fresh: true, cancellationToken);
                    audit.Enqueue(new AuditEventDraft(companyId, "settings.warehouse-transfer-scale.recorded.v1", EntityType,
                        row.RecordId.ToString("D"), "RecordVersion", latest?.Version, row.Version, AuditActorTypes.User,
                        actor, SettingsWarehouseTransferScaleV1.Manage, scope.RoleCodes,
                        latest is null ? "{}" : JsonSerializer.Serialize(View(latest), Json), JsonSerializer.Serialize(View(row), Json),
                        "Transfer çıkış tartısı depo ayarı sürümü kaydedildi", null, AuditAiInvolvement.None, null,
                        metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId,
                        metadata.CausationId, metadata.IdempotencyKey));
                }
            }
            database.CommandExecutions.Add(CommandExecution.Complete(new CommandExecutionData(Guid.NewGuid(), companyId,
                Command, key, hash, actor, answer.Code ?? "Succeeded", answer.StatusCode, JsonSerializer.Serialize(answer, Json),
                metadata.CorrelationId, clock.GetUtcNow(), 1)));
            await FenceAsync(companyId, actor, fresh: true, cancellationToken);
            // Drain through the Audit owner before Settings' legacy interceptor can consume this new workflow's drafts.
            await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), cancellationToken);
            await FenceAsync(companyId, actor, fresh: true, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            await FenceAsync(companyId, actor, fresh: true, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(answer, false, metadata.CorrelationId);
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
        {
            database.ChangeTracker.Clear(); auditWriter.DiscardPending();
            var winner = await FindAsync(key, cancellationToken);
            await FenceAsync(companyId, actor, fresh: true, cancellationToken);
            if (winner is not null) return Replay(winner, hash, metadata);
            throw Unavailable();
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        catch (SettingsPrerequisiteException) { throw; }
        catch { Still(companyId, actor, cancellationToken); throw Unavailable(); }
        finally { if (started) { database.ChangeTracker.Clear(); auditWriter.DiscardPending(); } }
    }

    private async Task<SettingsTransferWarehouseV1> WarehouseAsync(Guid companyId, Guid locationId, string actor, CancellationToken token)
    {
        if (warehouses is null || locationId == Guid.Empty) throw Unavailable();
        var value = await warehouses.ReadAsync(companyId, locationId, token);
        Still(companyId, actor, token);
        if (value is null || value.CompanyId != companyId || value.LocationId != locationId || value.IsExternal
            || !value.IsActive || value.LocationVersion < 1) throw Unavailable();
        return value;
    }
    private Task<WarehouseTransferScaleVersionRow?> LatestAsync(Guid locationId, CancellationToken token) =>
        database.WarehouseTransferScaleVersions.AsNoTracking().Where(x => x.LocationId == locationId)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(token);
    private Task<CommandExecution?> FindAsync(string key, CancellationToken token) => database.CommandExecutions.AsNoTracking()
        .SingleOrDefaultAsync(x => x.CommandName == Command && x.IdempotencyKeyHash == key, token);
    private string Member(Guid companyId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId || !user.IsAuthenticated
            || string.IsNullOrWhiteSpace(user.UserId) || !user.CanAccessCompany(companyId)) throw new UnauthorizedAccessException();
        return user.UserId;
    }
    private void Still(Guid companyId, string actor, CancellationToken token)
    { if (Member(companyId, token) != actor) throw new UnauthorizedAccessException(); }
    private async Task<PermissionScope> FenceAsync(Guid companyId, string actor, bool fresh, CancellationToken token)
    {
        Still(companyId, actor, token);
        var scope = fresh ? await authority.RevalidatePermissionScopeAsync(SettingsWarehouseTransferScaleV1.Manage, companyId, token)
            : await authorization.GetPermissionScopeAsync(SettingsWarehouseTransferScaleV1.Manage, companyId, token);
        Still(companyId, actor, token);
        return scope is { IsGranted: true, IsCompanyWide: true } ? scope : throw new UnauthorizedAccessException();
    }
    private static void Validate(WarehouseTransferScaleVersionRow row, Guid companyId, Guid locationId)
    {
        if (row.CompanyId != companyId || row.LocationId != locationId || row.RecordId == Guid.Empty || row.Version < 1
            || row.LocationVersion < 1 || row.RecordedAtUtc.Offset != TimeSpan.Zero || row.RecordedAtUtc == default
            || string.IsNullOrWhiteSpace(row.RecordedBy) || row.RecordedBy.Length > 128 || string.IsNullOrWhiteSpace(row.Reason)
            || row.Reason.Length > SettingsWarehouseTransferScaleV1.MaximumReasonLength) throw Unavailable();
    }
    private static WarehouseTransferScaleVersionV1 View(WarehouseTransferScaleVersionRow row) => new(row.RecordId, Text(row.Version),
        row.RequireExitWeighing, row.LocationVersion, row.RecordedBy, row.RecordedAtUtc, row.Reason);
    private static SettingsWarehouseTransferScalePolicyV1 Policy(Guid companyId, Guid locationId, WarehouseTransferScaleVersionRow? row) =>
        new(row is null ? SettingsWarehouseTransferScaleStatusV1.NotConfigured : SettingsWarehouseTransferScaleStatusV1.Configured,
            companyId, locationId, row?.RequireExitWeighing ?? false, row?.Version,
            SettingsWarehouseTransferScaleV1.SourceVersion + "/" + (row is null ? "not-configured" : Text(row.Version)));
    private static CompanyPolicyCommandResultV1 Replay(CommandExecution row, string hash, SettingsCommandMetadataV1 metadata)
    {
        if (row.ActorId != metadata.ActorId || row.RequestHash != hash) return new(new(409, KeyConflict,
            "İşlem anahtarı kullanılmış.", "Bu anahtar başka bir istek veya aktörle kullanıldı."), false, metadata.CorrelationId);
        var answer = JsonSerializer.Deserialize<CompanyPolicyCommandAnswerV1>(row.ResponseJson, Json);
        if (row.SchemaVersion != 1 || row.CompanyId != metadata.CompanyId || answer is null || answer.StatusCode != row.HttpStatusCode
            || (answer.Code ?? "Succeeded") != row.Outcome || !(answer.StatusCode == 204 && answer.Code is null
                || answer.StatusCode == 409 && answer.Code == VersionConflict)) throw Unavailable();
        return new(answer, true, row.OriginalCorrelationId);
    }
    private static CompanyPolicyCommandAnswerV1 Invalid(string detail) => new(400, InvalidCode, "İstek geçersiz.", detail);
    private static SettingsPrerequisiteException Unavailable() => new("settings_provider_unavailable");
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}
