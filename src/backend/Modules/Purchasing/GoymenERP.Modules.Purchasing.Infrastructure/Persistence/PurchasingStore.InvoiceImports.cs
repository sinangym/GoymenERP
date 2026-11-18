using System.Text.Json;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore : IPurchaseInvoiceImportReservationStoreV1
{
    async Task<PurchaseInvoiceImportReservationPageV1> IPurchaseInvoiceImportReservationStoreV1.ListAsync(int pageIndex, CancellationToken token)
    {
        const int pageSize = 25;
        if (pageIndex < 0 || pageIndex > int.MaxValue / pageSize) throw new ArgumentOutOfRangeException(nameof(pageIndex));
        var companyId = RequireResolvedCompany();
        var rows = await dbContext.InvoiceImportReservations.AsNoTracking().OrderByDescending(x => x.ReservedAtUtc).ThenBy(x => x.Id)
            .Skip(pageIndex * pageSize).Take(pageSize + 1).ToArrayAsync(token);
        return new(companyId, pageIndex, rows.Length > pageSize, rows.Take(pageSize).Select(row => ImportReservation(row, companyId)).ToArray());
    }

    async Task<PurchaseInvoiceImportReservationV1?> IPurchaseInvoiceImportReservationStoreV1.FindAsync(Guid id, CancellationToken token)
    {
        var companyId = RequireResolvedCompany();
        var row = await dbContext.InvoiceImportReservations.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        return row is null ? null : ImportReservation(row, companyId);
    }

    public async Task<PurchaseInvoiceImportReservationResultV1> ReserveAsync(PurchasingWriteEnvelope envelope,
        Func<CancellationToken, Task> requireStillAuthorized, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(requireStillAuthorized);
        RequireInvoiceTransactionOwnership(); RequireEnvelope(envelope);
        if (envelope.PermissionCode != PurchaseIncomingInvoicePermissionNamesV1.Import) throw new ArgumentException("Import authority required.");
        var companyId = RequireResolvedCompany(); var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        if (await FindInvoiceImportReservationReplayAsync(envelope, companyId, keyHash, requireStillAuthorized, token) is { } prior) return prior;
        var created = new PurchaseInvoiceImportReservationRow
        {
            CompanyId = companyId, Id = Guid.NewGuid(), ActorId = envelope.ActorId.Trim(), IdempotencyKeyHash = keyHash,
            CorrelationId = envelope.CorrelationId.Trim(), ReservedAtUtc = timeProvider.GetUtcNow(),
        };
        var reservation = ImportReservation(created, companyId);
        await requireStillAuthorized(token);
        dbContext.InvoiceImportReservations.Add(created);
        Audit(envelope, companyId, "Purchasing.InvoiceImportReservation", created.Id, "purchasing.invoice-import.reserved.v1", "Reserve",
            null, 1, "{}", JsonSerializer.Serialize(reservation, Json), "Alış faturası kaynak belgesi için kimlik ayrıldı; para etkisi yok");
        try { await SaveAsync(token); }
        catch (PurchasingStoreException exception) when (exception.Code == PurchasingStoreCodes.ConcurrentChange)
        { if (await FindInvoiceImportReservationReplayAsync(envelope, companyId, keyHash, requireStillAuthorized, token) is { } winner) return winner; throw; }
        return new(reservation, false, created.CorrelationId);
    }

    private async Task<PurchaseInvoiceImportReservationResultV1?> FindInvoiceImportReservationReplayAsync(
        PurchasingWriteEnvelope envelope, Guid companyId, string keyHash,
        Func<CancellationToken, Task> requireStillAuthorized, CancellationToken token)
    {
        await requireStillAuthorized(token);
        var row = await dbContext.InvoiceImportReservations.AsNoTracking().SingleOrDefaultAsync(item => item.IdempotencyKeyHash == keyHash, token);
        await requireStillAuthorized(token);
        if (row is null) return null;
        if (row.ActorId != envelope.ActorId.Trim()) throw Refused(PurchasingStoreCodes.SameKeyPayloadMismatch, "İşlem anahtarı başka aktöre ait.");
        return new(ImportReservation(row, companyId), true, row.CorrelationId);
    }

    private static PurchaseInvoiceImportReservationV1 ImportReservation(PurchaseInvoiceImportReservationRow row, Guid companyId)
    {
        if (row.CompanyId != companyId || row.Id == Guid.Empty || string.IsNullOrWhiteSpace(row.ActorId))
            throw new InvalidOperationException("Fatura içe aktarma kimliği şirket kapsamıyla uyuşmuyor.");
        return new(row.Id, row.CompanyId, "1", row.ActorId, row.ReservedAtUtc);
    }
}
