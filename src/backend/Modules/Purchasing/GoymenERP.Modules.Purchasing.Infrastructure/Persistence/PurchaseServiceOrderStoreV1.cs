using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain.Services;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

/// <summary>Own Serializable transaction, immutable snapshots/attempts and Audit owner append. Borrowed state is never cleared.</summary>
public sealed partial class PurchaseServiceOrderStoreV1(PurchasingDbContext database, ICompanyContext company,
    IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter, ISettingsDocumentNumberSeriesV1 numbers) : IPurchaseServiceOrderStoreV1,
    IPurchaseSubcontractOrderReadPortV1, IPurchaseServiceAcceptanceReadPortV1
{
    internal static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    public Task<PurchaseServiceOrderState?> ReadAsync(Guid orderId, CancellationToken token) =>
        ReadOwnerAsync(orderId, null, token);

    public Task<PurchaseServiceOrderState?> ReadForApprovalPreparationAsync(Guid orderId,
        Func<CancellationToken, Task> freshFence, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(freshFence);
        return ReadOwnerAsync(orderId, freshFence, token);
    }

    private async Task<PurchaseServiceOrderState?> ReadOwnerAsync(Guid orderId,
        Func<CancellationToken, Task>? freshFence, CancellationToken token)
    {
        var scope = Company();
        if (freshFence is not null) await Fence();
        var parent = await database.ServiceOrders.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == orderId, token);
        if (freshFence is not null) await Fence();
        if (parent is null) return null;
        if (freshFence is not null) await Fence();
        var revision = await database.ServiceOrderRevisions.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == orderId && x.Version == parent.Version, token);
        if (freshFence is not null) await Fence();
        if (revision is null) throw new InvalidOperationException("Hizmet siparişi sürümü okunamadı.");
        var value = Restore(revision.SnapshotJson);
        if (Company() != scope || value.CompanyId != scope || value.OrderId != orderId || value.Version != parent.Version)
            throw new InvalidOperationException("Hizmet siparişi kapsamı değişti.");
        return value;

        async Task Fence()
        {
            token.ThrowIfCancellationRequested();
            if (Company() != scope) throw new InvalidOperationException("Hizmet siparişi kapsamı değişti.");
            await freshFence!(token);
            token.ThrowIfCancellationRequested();
            if (Company() != scope) throw new InvalidOperationException("Hizmet siparişi kapsamı değişti.");
        }
    }
    public async Task<IReadOnlyList<PurchaseServiceOrderState>> ListAsync(CancellationToken token)
    {
        var scope = Company();
        var rows = await database.ServiceOrders.AsNoTracking().OrderBy(x => x.OrderId).Take(100).ToArrayAsync(token);
        var values = new List<PurchaseServiceOrderState>();
        foreach (var row in rows) values.Add(await ReadAsync(row.OrderId, token) ?? throw new InvalidOperationException());
        if (Company() != scope) throw new InvalidOperationException(); return values;
    }
    public async Task<bool> HasApprovedPhysicalReferencesAsync(PurchaseServiceOrderState order, CancellationToken token)
    {
        var scope = Company();
        if (order.CompanyId != scope || order.OrderId == Guid.Empty || database.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Fiziksel onay kanıtı aynı sahip işleminde okunur.");
        var rows = await database.ServiceOrderRevisions.AsNoTracking().Where(x => x.OrderId == order.OrderId && x.Version < order.Version)
            .OrderByDescending(x => x.Version).ToArrayAsync(token);
        foreach (var row in rows)
        {
            var approved = Restore(row.SnapshotJson);
            if (Company() != scope || row.CompanyId != scope || approved.CompanyId != scope || approved.OrderId != order.OrderId || approved.Version != row.Version)
                throw new InvalidOperationException("Fiziksel onay geçmişi kapsamı uyuşmuyor.");
            if (approved.Status == PurchaseServiceOrderStatus.Approved && approved.Kind == order.Kind
                && approved.SupplierPartyId == order.SupplierPartyId && approved.CompanyAccountId == order.CompanyAccountId
                && approved.Materials.SequenceEqual(order.Materials) && approved.ExpectedOutputs.SequenceEqual(order.ExpectedOutputs)) return true;
        }
        return false;
    }
    public async Task<bool> HasApprovedTaxSnapshotAsync(PurchaseServiceOrderState order, Guid lineId, CancellationToken token)
    {
        var scope = Company();
        if (order.CompanyId != scope || database.Database.CurrentTransaction is null) throw new InvalidOperationException();
        var line = order.Lines.Single(x => x.LineId == lineId);
        var rows = await database.ServiceOrderRevisions.AsNoTracking().Where(x => x.OrderId == order.OrderId && x.Version < order.Version)
            .OrderByDescending(x => x.Version).ToArrayAsync(token);
        foreach (var row in rows)
        {
            var previous = Restore(row.SnapshotJson);
            if (Company() != scope || row.CompanyId != scope || previous.CompanyId != scope || previous.OrderId != order.OrderId || previous.Version != row.Version)
                throw new InvalidOperationException("Vergi onay geçmişi kapsamı uyuşmuyor.");
            if (previous.Status == PurchaseServiceOrderStatus.Approved && previous.Lines.SingleOrDefault(x => x.LineId == lineId)?.Tax == line.Tax) return true;
        }
        return false;
    }
    public async Task<PurchaseServiceOrderWriteV1?> ReplayAsync(string command, PurchasingWriteEnvelope envelope, string hash, CancellationToken token)
    {
        RequireEnvelope(command, envelope, hash); var scope = Company(); var key = Hash(envelope.IdempotencyKey);
        var row = await database.ServiceOrderAttempts.AsNoTracking().SingleOrDefaultAsync(x => x.Command == command && x.KeyHash == key, token);
        if (Company() != scope) throw new InvalidOperationException();
        if (row is null) return null;
        if (row.ActorId != envelope.ActorId || row.RequestHash != hash)
            throw new PurchasingStoreException(PurchasingStoreCodes.SameKeyPayloadMismatch, "Aynı anahtar farklı içerikle kullanılamaz.");
        return new PurchaseServiceOrderWriteV1(row.SnapshotJson is null ? null : Restore(row.SnapshotJson), row.RefusalJson is null ? null
            : JsonSerializer.Deserialize<PurchasingTerminalRefusal>(row.RefusalJson, Json) ?? throw new InvalidOperationException(), true, row.CorrelationId);
    }
    public async Task<PurchaseServiceOrderWriteV1?> WriteAsync(Guid orderId, string command, PurchasingWriteEnvelope envelope, string hash, string reason,
        Func<PurchaseServiceOrderState?, CancellationToken, Task<PurchaseServiceOrderState>> mutate, Func<CancellationToken, Task> requireStill, CancellationToken token)
    {
        RequireEnvelope(command, envelope, hash);
        // Refuse before touching somebody else's transaction, tracked entities or Audit drafts.
        if (database.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || database.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts) throw new InvalidOperationException("Hizmet komutu kendi temiz işlemini gerektirir.");
        var scope = Company();
        var linkedPrices = command == PurchaseServiceOrderCommandsV1.SetSupplierRevisionPrices;
        var linkedCommitted = false;
        if (linkedPrices)
        {
            token.ThrowIfCancellationRequested(); await requireStill(token); token.ThrowIfCancellationRequested();
            if (Company() != scope || database.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
                || database.ChangeTracker.HasChanges() || auditWriter.HasPendingDrafts) throw new PurchasingServiceException(503, "service_unavailable");
        }
        await using var transaction = await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            await requireStill(token);
            var replay = await ReplayAsync(command, envelope, hash, token);
            if (replay is not null) { await requireStill(token); return replay; }
            if (command == PurchaseServiceOrderCommandsV1.Approve)
            {
                var bound = await PurchasingStore.ReadMixedOwnerAsync(() => database.OrderServiceMemberships.AsNoTracking()
                    .AnyAsync(x => x.CompanyId == scope && x.ServiceOrderId == orderId, token), requireStill, token);
                if (bound) throw new PurchasingServiceException(503, "service_unavailable");
            }
            var before = command == PurchaseServiceOrderCommandsV1.SetSupplierRevisionPrices
                ? await ReadOwnerAsync(orderId, requireStill, token) : await ReadAsync(orderId, token);
            if (before is null && command != PurchaseServiceOrderCommandsV1.Create) return null;
            if (command == PurchaseServiceOrderCommandsV1.RevisePrices)
                await RequireBoundPriceRevisionDraftAsync(orderId, before, requireStill, token);
            if (command == PurchaseServiceOrderCommandsV1.SetSupplierRevisionPrices)
            {
                // Read-only reuse of the same parent's original-artifact codec, with the SAME Db/collector/writer/TX.
                // The child-snapshot locator alone grants nothing; no parent command or second transaction is invoked.
                var originalOwner = new PurchasingStore(database, company, audit, TimeProvider.System, decisionAuditWriter: auditWriter);
                await originalOwner.RequireMixedSupplierOriginAsync(before!, requireStill, token);
                await requireStill(token);
            }
            PurchaseServiceOrderState? after = null; PurchasingTerminalRefusal? refusal = null;
            try { after = await mutate(before, token); }
            catch (PurchaseServiceOrderException exception) { refusal = new PurchasingTerminalRefusal(409, exception.Code, []); }
            await requireStill(token);
            if (Company() != scope) throw new InvalidOperationException();
            string? snapshot = null;
            if (after is not null)
            {
                PurchaseServiceOrder.Validate(after);
                if (after.CompanyId != scope || after.OrderId != orderId || after.Version != (before?.Version ?? 0) + 1)
                    throw new InvalidOperationException("Hizmet siparişi sürüm kanıtı uyuşmuyor.");
                if (before is null)
                {
                    var format = await numbers.GetPolicyAsync(scope, SettingsDocumentSeriesV1.PurchaseOrder, token);
                    await requireStill(token);
                    var reserved = await numbers.ReservePurchaseOrderNumberAsync(transaction.GetDbTransaction(), scope, orderId, after.BusinessDate,
                        format.Version, new SettingsCommandMetadataV1(scope, envelope.ActorId, envelope.CorrelationId, envelope.CausationId, envelope.SourceChannel,
                            envelope.ClientIpAddress, envelope.UserAgent, envelope.IdempotencyKey), token);
                    await requireStill(token);
                    if (reserved.CompanyId != scope || reserved.Series != SettingsDocumentSeriesV1.PurchaseOrder || reserved.DocumentId != orderId
                        || reserved.BusinessDate != after.BusinessDate || reserved.FormatVersion != format.Version || reserved.Sequence < 1
                        || string.IsNullOrWhiteSpace(reserved.Number) || reserved.Number.Length > 64 || reserved.Number.Any(char.IsControl))
                        throw new InvalidOperationException("Numara kaynağı doğrulanamadı.");
                    after = after with { DocumentNumber = reserved.Number };
                }
                snapshot = await StageStateAsync(database, scope, orderId, before, after, token, linkedPrices ? requireStill : null);
            }
            database.ServiceOrderAttempts.Add(new PurchaseServiceOrderAttemptRow() { CompanyId = scope, Command = command, KeyHash = Hash(envelope.IdempotencyKey),
                RequestHash = hash, ActorId = envelope.ActorId, CorrelationId = envelope.CorrelationId,
                SnapshotJson = snapshot, RefusalJson = refusal is null ? null : JsonSerializer.Serialize(refusal, Json) });
            await requireStill(token);
            if (linkedPrices) token.ThrowIfCancellationRequested();
            await database.SaveChangesAsync(token);
            if (linkedPrices) { token.ThrowIfCancellationRequested(); await requireStill(token); token.ThrowIfCancellationRequested(); }
            audit.Enqueue(new AuditEventDraft(scope, command.Replace(".v1", ".recorded.v1", StringComparison.Ordinal),
                "Purchasing.ServiceOrder", orderId.ToString("D"), refusal is null ? command : "Refuse", before?.Version,
                after?.Version ?? before?.Version ?? 0, AuditActorTypes.User, envelope.ActorId, envelope.PermissionCode,
                envelope.RoleCodes, before is null ? "{}" : JsonSerializer.Serialize(before, Json), snapshot ?? JsonSerializer.Serialize(refusal, Json),
                reason, null, AuditAiInvolvement.None, null, envelope.SourceChannel, envelope.ClientIpAddress, envelope.UserAgent,
                envelope.CorrelationId, envelope.CausationId, envelope.IdempotencyKey));
            if (linkedPrices) { await requireStill(token); token.ThrowIfCancellationRequested(); }
            await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), token);
            await requireStill(token);
            if (linkedPrices) token.ThrowIfCancellationRequested();
            await transaction.CommitAsync(token);
            if (linkedPrices) { linkedCommitted = true; token.ThrowIfCancellationRequested(); await requireStill(token); token.ThrowIfCancellationRequested(); }
            return new PurchaseServiceOrderWriteV1(after, refusal, false, envelope.CorrelationId);
        }
        catch (DbUpdateConcurrencyException)
        { if (!linkedCommitted) await transaction.RollbackAsync(CancellationToken.None); database.ChangeTracker.Clear(); auditWriter.DiscardPending(); throw new PurchasingServiceException(409, PurchasingProblemCodesV1.ConcurrentChange); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        { if (!linkedCommitted) await transaction.RollbackAsync(CancellationToken.None); database.ChangeTracker.Clear(); auditWriter.DiscardPending(); throw new PurchasingServiceException(409, PurchasingProblemCodesV1.ConcurrentChange); }
        catch
        { if (!linkedCommitted) await transaction.RollbackAsync(CancellationToken.None); database.ChangeTracker.Clear(); auditWriter.DiscardPending(); throw; }
    }
    public async Task<PurchaseSubcontractOrderAnswerV1> GetAsync(Guid companyId, Guid orderId, CancellationToken token = default)
    {
        if (!IsCompany(companyId)) return new PurchaseSubcontractOrderAnswerV1(PurchaseSubcontractOrderOutcomeV1.Unavailable, null);
        try
        {
            var value = await ReadAsync(orderId, token);
            if (!IsCompany(companyId)) return new PurchaseSubcontractOrderAnswerV1(PurchaseSubcontractOrderOutcomeV1.Unavailable, null);
            if (value is null) return new PurchaseSubcontractOrderAnswerV1(PurchaseSubcontractOrderOutcomeV1.NotFound, null);
            if (value.Kind != PurchaseServiceOrderKind.OwnMaterialSubcontract) return new PurchaseSubcontractOrderAnswerV1(PurchaseSubcontractOrderOutcomeV1.NotSubcontract, null);
            return new PurchaseSubcontractOrderAnswerV1(PurchaseSubcontractOrderOutcomeV1.Available, new PurchaseSubcontractOrderV1(value.CompanyId, value.OrderId, Text(value.Version), value.SupplierPartyId,
                value.CompanyAccountId, value.Status == PurchaseServiceOrderStatus.Approved, value.IsOpen,
                value.Lines.Select(x => new PurchaseSubcontractServiceLineV1(x.LineId, x.ServiceTypeId, Text(x.ServiceTypeVersion), x.UnitId,
                    Text(x.Quantity), Text(x.AcceptedQuantity))).ToArray(), value.Materials, value.ExpectedOutputs));
        }
        catch (OperationCanceledException) { throw; }
        catch { return new PurchaseSubcontractOrderAnswerV1(PurchaseSubcontractOrderOutcomeV1.Unavailable, null); }
    }
    public async Task<PurchaseServiceAcceptanceAnswerV1> GetAsync(Guid companyId, Guid orderId, Guid acceptanceId, CancellationToken token = default)
    {
        if (!IsCompany(companyId)) return new PurchaseServiceAcceptanceAnswerV1(PurchaseServiceAcceptanceOutcomeV1.Unavailable, null);
        try
        {
            var value = await ReadAsync(orderId, token);
            if (!IsCompany(companyId)) return new PurchaseServiceAcceptanceAnswerV1(PurchaseServiceAcceptanceOutcomeV1.Unavailable, null);
            var acceptance = value?.Acceptances.SingleOrDefault(x => x.AcceptanceId == acceptanceId);
            if (value is null || acceptance is null) return new PurchaseServiceAcceptanceAnswerV1(PurchaseServiceAcceptanceOutcomeV1.NotFound, null);
            var line = value.Lines.Single(x => x.LineId == acceptance.LineId);
            return new PurchaseServiceAcceptanceAnswerV1(PurchaseServiceAcceptanceOutcomeV1.Available, new PurchaseServiceAcceptanceSourceV1(value.CompanyId, orderId, Text(value.Version), value.SupplierPartyId,
                value.CompanyAccountId, acceptanceId, line.LineId, line.ServiceTypeId, line.UnitId, Text(acceptance.Quantity), acceptance.AcceptedOn,
                value.Money.CurrencyId, line.UnitPrice, $"purchasing.service-acceptance:{orderId:D}:{acceptanceId:D}:{acceptance.OrderVersionAfter}"));
        }
        catch (OperationCanceledException) { throw; }
        catch { return new PurchaseServiceAcceptanceAnswerV1(PurchaseServiceAcceptanceOutcomeV1.Unavailable, null); }
    }
    private Guid Company() => company.IsResolved && company.CompanyId != Guid.Empty ? company.CompanyId : throw new InvalidOperationException("Şirket bağlamı gerekli.");
    private bool IsCompany(Guid id) => id != Guid.Empty && company.IsResolved && company.CompanyId == id;
    private static PurchaseServiceOrderState Restore(string json)
    { var v = JsonSerializer.Deserialize<PurchaseServiceOrderState>(json, Json) ?? throw new InvalidOperationException(); PurchaseServiceOrder.Validate(v);
      if (string.IsNullOrWhiteSpace(v.DocumentNumber) || v.DocumentNumber.Length > 64) throw new InvalidOperationException("Kayıtlı belge numarası okunamadı."); return v; }
    private static string Text(decimal value) => value.ToString("0.############################", System.Globalization.CultureInfo.InvariantCulture);
    private static string Text(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static void RequireEnvelope(string command, PurchasingWriteEnvelope envelope, string hash)
    {
        if (!PurchaseServiceOrderCommandsV1.IsKnown(command) || string.IsNullOrWhiteSpace(envelope.ActorId) || envelope.ActorId.Length > 128
            || string.IsNullOrWhiteSpace(envelope.PermissionCode) || string.IsNullOrWhiteSpace(envelope.IdempotencyKey)
            || envelope.IdempotencyKey.Length > 200 || envelope.IdempotencyKey != envelope.IdempotencyKey.Trim()
            || string.IsNullOrWhiteSpace(envelope.CorrelationId) || envelope.CorrelationId.Length > 128 || hash.Length != 64) throw new ArgumentException();
    }
}
