using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Expenses;
using GoymenERP.Modules.Purchasing.Domain.Services;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed class PurchaseAdditionalCostStoreV1(PurchasingDbContext database, ICompanyContext company,
    IPurchasingStore orders, IPurchaseAcceptanceSourceV1 goodsAcceptances, IPurchaseServiceOrderStoreV1 serviceOrders,
    IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter) : IPurchaseAdditionalCostStoreV1
{
    internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    public async Task<PurchaseAdditionalCostSourceV1?> ReadSourceAsync(PurchaseAdditionalCostSourceKind kind, Guid orderId, Guid? acceptanceId, CancellationToken token)
    {
        var scope = Company();
        if (!Enum.IsDefined(kind) || orderId == Guid.Empty || acceptanceId == Guid.Empty) throw new ArgumentException();
        if (kind == PurchaseAdditionalCostSourceKind.GoodsOrder)
        {
            var source = await orders.FindOrderAsync(orderId, token); Same(scope);
            if (source is null) return null;
            if (source.Order.CompanyId != scope || source.Order.Id != orderId || source.Order.Money is not { } money)
                throw new InvalidOperationException("Sipariş para kaynağı okunamadı.");
            Guid? line = null;
            if (acceptanceId is { } accepted)
            {
                var fact = await goodsAcceptances.ReadAsync(orderId, accepted, token); Same(scope);
                if (fact is null) return null;
                if (fact.CompanyId != scope || fact.PurchaseOrderId != orderId || fact.AcceptanceId != accepted) throw new InvalidOperationException();
                line = fact.PurchaseOrderLineId;
            }
            return new PurchaseAdditionalCostSourceV1(scope, orderId, source.Order.Version, source.DocumentNumber, kind, acceptanceId, line,
                new PurchaseServiceMoney(money.CurrencyId, money.CurrencyCode, money.ProfileId, money.ProfileVersion, money.Scale, money.UnitPriceScale))
            { Acceptances = source.Order.Acceptances.Select(x => new PurchaseAdditionalCostAcceptanceOptionV1(x.AcceptanceId, x.LineId,
                PurchaseAcceptanceParticipantContractV1.FormatQuantity(x.Quantity.Value), x.RecordedAtUtc)).ToArray() };
        }
        var service = await serviceOrders.ReadAsync(orderId, token); Same(scope);
        if (service is null) return null;
        if (service.CompanyId != scope || service.OrderId != orderId) throw new InvalidOperationException();
        var serviceAcceptance = acceptanceId is null ? null : service.Acceptances.SingleOrDefault(x => x.AcceptanceId == acceptanceId);
        if (acceptanceId is not null && serviceAcceptance is null) return null;
        return new PurchaseAdditionalCostSourceV1(scope, orderId, service.Version, service.DocumentNumber, kind, acceptanceId, serviceAcceptance?.LineId, service.Money)
        { Acceptances = service.Acceptances.Select(x => new PurchaseAdditionalCostAcceptanceOptionV1(x.AcceptanceId, x.LineId,
            PurchaseAcceptanceParticipantContractV1.FormatQuantity(x.Quantity), x.RecordedAtUtc)).ToArray() };
    }
    public async Task<IReadOnlyList<PurchaseAdditionalCostState>> ListAsync(PurchaseAdditionalCostSourceKind kind, Guid orderId, CancellationToken token)
    {
        var scope = Company();
        var rows = await database.AdditionalCosts.AsNoTracking().Where(x => x.OrderId == orderId && x.SourceKind == (int)kind).OrderByDescending(x => x.RecordedAtUtc)
            .ThenBy(x => x.CostId).Take(100).ToArrayAsync(token); Same(scope);
        return rows.Select(row => { var value = Restore(row.SnapshotJson); if (value.CompanyId != scope || value.OrderId != orderId || value.SourceKind != kind || value.CostId != row.CostId)
            throw new InvalidOperationException(); return value; }).ToArray();
    }
    public async Task<PurchaseAdditionalCostWriteV1?> ReplayAsync(PurchasingWriteEnvelope envelope, string hash, CancellationToken token)
    {
        Envelope(envelope, hash); var scope = Company(); var key = Key(envelope.IdempotencyKey);
        var row = await database.AdditionalCostAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.KeyHash == key, token); Same(scope);
        if (row is null) return null;
        if (row.ActorId != envelope.ActorId || row.RequestHash != hash)
            throw new PurchasingStoreException(PurchasingStoreCodes.SameKeyPayloadMismatch, "Aynı anahtar farklı içerik veya aktörle kullanılamaz.");
        return new PurchaseAdditionalCostWriteV1(row.SnapshotJson is null ? null : Restore(row.SnapshotJson), row.RefusalJson is null ? null
            : JsonSerializer.Deserialize<PurchasingTerminalRefusal>(row.RefusalJson, Json) ?? throw new InvalidOperationException(), true, row.CorrelationId);
    }
    public async Task<PurchaseAdditionalCostWriteV1> RecordAsync(Guid costId, PurchasingWriteEnvelope envelope, string hash,
        Func<CancellationToken, Task<PurchaseAdditionalCostState>> create, Func<CancellationToken, Task> requireStill, CancellationToken token)
    {
        Envelope(envelope, hash);
        if (costId == Guid.Empty) throw new ArgumentException();
        if (database.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || database.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts) throw new InvalidOperationException("Ek masraf kendi temiz işlemini gerektirir.");
        var scope = Company();
        await using var tx = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await requireStill(token);
            var replay = await ReplayAsync(envelope, hash, token);
            if (replay is not null) { await requireStill(token); return replay; }
            PurchaseAdditionalCostState? value = null; PurchasingTerminalRefusal? refusal = null;
            try { value = await create(token); }
            catch (PurchaseAdditionalCostException exception) { refusal = new PurchasingTerminalRefusal(409, exception.Code, []); }
            await requireStill(token); Same(scope);
            string? snapshot = null;
            if (value is not null)
            {
                PurchaseAdditionalCost.Validate(value);
                if (value.CompanyId != scope || value.CostId != costId || value.RecordedBy != envelope.ActorId) throw new InvalidOperationException();
                snapshot = JsonSerializer.Serialize(value, Json);
                database.AdditionalCosts.Add(new PurchaseAdditionalCostRow() { CompanyId = scope, CostId = costId, OrderId = value.OrderId, SourceKind = (int)value.SourceKind,
                    NormalOrderId = value.SourceKind == PurchaseAdditionalCostSourceKind.GoodsOrder ? value.OrderId : null,
                    NormalLineId = value.SourceKind == PurchaseAdditionalCostSourceKind.GoodsOrder ? value.AcceptanceLineId : null,
                    NormalAcceptanceId = value.SourceKind == PurchaseAdditionalCostSourceKind.GoodsOrder ? value.AcceptanceId : null,
                    ServiceOrderId = value.SourceKind == PurchaseAdditionalCostSourceKind.ServiceOrder ? value.OrderId : null,
                    ServiceLineId = value.SourceKind == PurchaseAdditionalCostSourceKind.ServiceOrder ? value.AcceptanceLineId : null,
                    ServiceAcceptanceId = value.SourceKind == PurchaseAdditionalCostSourceKind.ServiceOrder ? value.AcceptanceId : null,
                    RecordedAtUtc = value.RecordedAtUtc, SnapshotJson = snapshot });
            }
            database.AdditionalCostAttempts.Add(new PurchaseAdditionalCostAttemptRow() { CompanyId = scope, KeyHash = Key(envelope.IdempotencyKey), RequestHash = hash,
                ActorId = envelope.ActorId, CorrelationId = envelope.CorrelationId, SnapshotJson = snapshot,
                RefusalJson = refusal is null ? null : JsonSerializer.Serialize(refusal, Json) });
            await requireStill(token); await database.SaveChangesAsync(token);
            audit.Enqueue(new AuditEventDraft(scope, "purchasing.additional-cost.recorded.v1", "Purchasing.AdditionalCost", costId.ToString("D"),
                refusal is null ? "Record" : "Refuse", null, value is null ? 0 : 1, AuditActorTypes.User, envelope.ActorId,
                envelope.PermissionCode, envelope.RoleCodes, "{}", snapshot ?? JsonSerializer.Serialize(refusal, Json), value?.Reason ?? "Ek masraf kaynak sürümü reddedildi",
                null, AuditAiInvolvement.None, null, envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent,
                envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey));
            await auditWriter.WritePendingAsync(tx.GetDbTransaction(), token); await requireStill(token); Same(scope);
            await tx.CommitAsync(token); return new PurchaseAdditionalCostWriteV1(value, refusal, false, envelope.CorrelationId);
        }
        catch (DbUpdateException exception) when (exception is DbUpdateConcurrencyException || exception.InnerException is SqlException { Number: 2601 or 2627 })
        { await tx.RollbackAsync(CancellationToken.None); database.ChangeTracker.Clear(); auditWriter.DiscardPending(); throw new PurchasingServiceException(409, PurchasingProblemCodesV1.ConcurrentChange); }
        catch
        { await tx.RollbackAsync(CancellationToken.None); database.ChangeTracker.Clear(); auditWriter.DiscardPending(); throw; }
    }
    private Guid Company() => company.IsResolved && company.CompanyId != Guid.Empty ? company.CompanyId : throw new InvalidOperationException("Şirket bağlamı gerekli.");
    private void Same(Guid id) { if (Company() != id) throw new InvalidOperationException("Şirket bağlamı değişti."); }
    private static string Key(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static PurchaseAdditionalCostState Restore(string json)
    { var value = JsonSerializer.Deserialize<PurchaseAdditionalCostState>(json, Json) ?? throw new InvalidOperationException(); PurchaseAdditionalCost.Validate(value); return value; }
    private static void Envelope(PurchasingWriteEnvelope value, string hash)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.ActorId) || value.ActorId.Length > 128 || value.PermissionCode != PurchasingPermissionManifestV1.OrdersRevise
            || string.IsNullOrWhiteSpace(value.CorrelationId) || value.CorrelationId.Length > 128 || string.IsNullOrWhiteSpace(value.IdempotencyKey)
            || value.IdempotencyKey.Length > 200 || value.IdempotencyKey.Trim() != value.IdempotencyKey || hash is null || hash.Length != 64 || hash.Any(x => !char.IsAsciiHexDigit(x)))
            throw new ArgumentException("Masraf işlem zarfı geçersiz.");
    }
}
