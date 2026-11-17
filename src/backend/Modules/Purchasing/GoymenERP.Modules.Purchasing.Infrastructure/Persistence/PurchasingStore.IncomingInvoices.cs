using System.Text.Json;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Domain;
using GoymenERP.Modules.Purchasing.Domain.Invoices;
using GoymenERP.Modules.Purchasing.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Purchasing.Infrastructure.Persistence;

public sealed partial class PurchasingStore : IPurchasingIncomingInvoiceStoreV1
{
    public async Task<PurchaseIncomingInvoiceSnapshot?> FindIncomingInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken = default)
    {
        var company = RequireResolvedCompany();
        var row = await dbContext.IncomingInvoiceRevisions.AsNoTracking().Where(item => item.InvoiceId == invoiceId)
            .OrderByDescending(item => item.Version).FirstOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var snapshot = ReadInvoiceSnapshot(row.SnapshotJson);
        if (snapshot.CompanyId != company || snapshot.Id != invoiceId || snapshot.Version != row.Version || (int)snapshot.Status != row.Status)
            throw new InvalidOperationException("Fatura geçmişi şirket ve sürüm kaydıyla uyuşmuyor.");
        return snapshot;
    }

    public async Task<PurchasingIncomingInvoiceWriteResult?> FindIncomingInvoiceAttemptAsync(string command,
        PurchasingWriteEnvelope envelope, string requestHash, CancellationToken cancellationToken = default)
    {
        RequireInvoiceCommand(command); RequireEnvelope(envelope); RequireHash(requestHash);
        var company = RequireResolvedCompany(); var keyHash = IdempotencyKeyHash(envelope.IdempotencyKey);
        var row = await dbContext.IncomingInvoiceAttempts.AsNoTracking()
            .SingleOrDefaultAsync(item => item.CommandName == command && item.IdempotencyKeyHash == keyHash, cancellationToken);
        if (row is null) return null;
        if (row.CompanyId != company || row.ActorId != envelope.ActorId.Trim() || row.RequestHash != requestHash)
            throw Refused(PurchasingStoreCodes.SameKeyPayloadMismatch, "Aynı işlem anahtarı farklı içerik veya aktörle kullanılamaz.");
        return new(row.SnapshotJson is null ? null : ReadInvoiceSnapshot(row.SnapshotJson),
            row.RefusalJson is null ? null : JsonSerializer.Deserialize<PurchasingTerminalRefusal>(row.RefusalJson, Json)
                ?? throw new InvalidOperationException("Fatura ret kaydı okunamadı."), true, row.CorrelationId);
    }

    public async Task<PurchasingIncomingInvoiceWriteResult> RecordIncomingInvoiceAsync(PurchaseIncomingInvoice invoice,
        PurchasingWriteEnvelope envelope, string requestHash, Func<CancellationToken, Task> requireStillAuthorized,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice); ArgumentNullException.ThrowIfNull(requireStillAuthorized);
        RequireInvoiceTransactionOwnership();
        await requireStillAuthorized(cancellationToken);
        var replay = await FindIncomingInvoiceAttemptAsync(PurchaseIncomingInvoiceCommandsV1.Record, envelope, requestHash, cancellationToken);
        if (replay is not null) { await requireStillAuthorized(cancellationToken); return replay; }
        var company = RequireResolvedCompany();
        if (invoice.CompanyId != company || invoice.Version != 1 || invoice.Status != PurchaseIncomingInvoiceStatus.Recorded
            || invoice.RecordedBy != envelope.ActorId.Trim()) throw new InvalidOperationException("Fatura kayıt kapsamı uyuşmuyor.");
        var now = timeProvider.GetUtcNow();
        var snapshot = PurchaseIncomingInvoiceSnapshot.Capture(invoice); var json = JsonSerializer.Serialize(snapshot, Json);
        await requireStillAuthorized(cancellationToken);
        dbContext.IncomingInvoices.Add(new()
        {
            CompanyId = company, InvoiceId = invoice.Id, SupplierPartyId = invoice.SupplierPartyId, Ettn = invoice.Source.Ettn,
            OfficialNumber = invoice.Source.OfficialNumber, DocumentId = invoice.Source.DocumentId, DocumentVersion = invoice.Source.DocumentVersion,
            DocumentSha256 = invoice.Source.Sha256,
        });
        AddInvoiceRevision(snapshot, json, now);
        AddInvoiceAttempt(company, PurchaseIncomingInvoiceCommandsV1.Record, envelope, requestHash, json, null, now);
        Audit(envelope, company, "Purchasing.IncomingInvoice", invoice.Id, "purchasing.incoming-invoice.recorded.v1", "Record",
            null, invoice.Version, "{}", json, "Resmî alış faturası içe aktarıldı");
        await SaveAsync(cancellationToken);
        return new(snapshot, null, false, envelope.CorrelationId.Trim());
    }

    public async Task<PurchasingIncomingInvoiceWriteResult?> MutateIncomingInvoiceAsync(Guid invoiceId, string command,
        PurchasingWriteEnvelope envelope, string requestHash, IReadOnlyList<Guid> orderIdsToLock,
        Func<PurchaseIncomingInvoice, CancellationToken, Task> mutate, Func<CancellationToken, Task> requireStillAuthorized,
        CancellationToken cancellationToken = default)
    {
        RequireInvoiceCommand(command); ArgumentNullException.ThrowIfNull(orderIdsToLock);
        RequireInvoiceTransactionOwnership();
        ArgumentNullException.ThrowIfNull(mutate); ArgumentNullException.ThrowIfNull(requireStillAuthorized);
        if (command == PurchaseIncomingInvoiceCommandsV1.Record) throw new ArgumentException("Kayıt komutu ayrı uygulanır.", nameof(command));
        await requireStillAuthorized(cancellationToken);
        var replay = await FindIncomingInvoiceAttemptAsync(command, envelope, requestHash, cancellationToken);
        if (replay is not null) { await requireStillAuthorized(cancellationToken); return replay; }
        var company = RequireResolvedCompany();
        await using var owned = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var lockedOrders = orderIdsToLock.Distinct().OrderBy(value => value).ToArray();
        if (lockedOrders.Contains(Guid.Empty)) throw new ArgumentException("Sipariş kimliği boş olamaz.");
        var generalLineIds = await dbContext.OrderLines.AsNoTracking()
            .Where(line => lockedOrders.Contains(line.OrderId) && line.Kind == (int)PurchaseOrderLineKind.General)
            .Select(line => line.LineId).ToArrayAsync(cancellationToken);
        try
        {
            await PurchaseLineSerializationLock.Bind(dbContext.Database.GetDbConnection(), owned.GetDbTransaction(), company)
                .AcquireAllAsync(generalLineIds, cancellationToken);
        }
        catch (PurchaseLineBusyException exception)
        { throw Refused(PurchasingStoreCodes.LineBusy, "Sipariş satırı başka bir işlem tarafından kilitli; hiçbir şey kaydedilmedi.", exception); }
        var before = await FindIncomingInvoiceAsync(invoiceId, cancellationToken);
        if (before is null) return null;
        var invoice = before.Restore();
        await requireStillAuthorized(cancellationToken); await mutate(invoice, cancellationToken);
        if (invoice.Version != before.Version + 1 || !ValidInvoiceTransition(command, before.Status, invoice.Status))
            throw new InvalidOperationException("Fatura komutu tek bir geçerli sürüm adımı yazmalıdır.");
        Action? stageBindings = command == PurchaseIncomingInvoiceCommandsV1.Match
            ? await PrepareIncomingInvoiceReceiptBindingsAsync(invoice, lockedOrders, cancellationToken) : null;
        var now = timeProvider.GetUtcNow(); var after = PurchaseIncomingInvoiceSnapshot.Capture(invoice);
        var json = JsonSerializer.Serialize(after, Json);
        await requireStillAuthorized(cancellationToken);
        stageBindings?.Invoke();
        if (command == PurchaseIncomingInvoiceCommandsV1.Finalize)
        {
            var payload = PurchaseIncomingInvoiceFinalizationV1.Event(invoice);
            dbContext.OutboxMessages.Add(new()
            {
                Id = Guid.NewGuid(), CompanyId = company, EventType = PurchaseInvoiceFinalizedEventV1.EventType,
                SchemaVersion = PurchaseInvoiceFinalizedEventV1.SchemaVersion, SourceOperation = command,
                AggregateType = "Purchasing.IncomingInvoice", AggregateId = invoice.Id, AggregateVersion = payload.Version, Sequence = 1,
                PayloadJson = JsonSerializer.Serialize(payload, Json), OccurredAtUtc = invoice.FinalizedAtUtc!.Value,
                CorrelationId = envelope.CorrelationId.Trim(), CausationId = envelope.CausationId, RecordedAtUtc = now,
            });
        }
        AddInvoiceRevision(after, json, now); AddInvoiceAttempt(company, command, envelope, requestHash, json, null, now);
        Audit(envelope, company, "Purchasing.IncomingInvoice", invoice.Id, command.Replace(".v1", ".recorded.v1", StringComparison.Ordinal),
            command, before.Version, after.Version, JsonSerializer.Serialize(before, Json), json, "Alış faturası işlem geçmişi");
        await SaveAsync(cancellationToken);
        await owned.CommitAsync(cancellationToken);
        return new(after, null, false, envelope.CorrelationId.Trim());
    }

    public async Task<PurchasingIncomingInvoiceWriteResult> RecordIncomingInvoiceRefusalAsync(string command,
        PurchasingWriteEnvelope envelope, string requestHash, PurchasingTerminalRefusal refusal,
        Func<CancellationToken, Task> requireStillAuthorized, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(refusal); ArgumentNullException.ThrowIfNull(requireStillAuthorized);
        RequireInvoiceTransactionOwnership();
        if (refusal.Status is not (409 or 422) || string.IsNullOrWhiteSpace(refusal.Code))
            throw new ArgumentException("Yalnız kalıcı iş kuralı retleri kaydedilir.", nameof(refusal));
        await requireStillAuthorized(cancellationToken);
        var replay = await FindIncomingInvoiceAttemptAsync(command, envelope, requestHash, cancellationToken);
        if (replay is not null) { await requireStillAuthorized(cancellationToken); return replay; }
        await requireStillAuthorized(cancellationToken);
        AddInvoiceAttempt(RequireResolvedCompany(), command, envelope, requestHash, null, JsonSerializer.Serialize(refusal, Json), timeProvider.GetUtcNow());
        await SaveAsync(cancellationToken);
        return new(null, refusal, false, envelope.CorrelationId.Trim());
    }

    private async Task<Action> PrepareIncomingInvoiceReceiptBindingsAsync(PurchaseIncomingInvoice invoice, Guid[] lockedOrders, CancellationToken token)
    {
        var receipts = new List<PurchaseInvoiceReceiptBindingRow>();
        foreach (var match in invoice.Matches)
        {
            if (!lockedOrders.Contains(match.OrderId)) throw new InvalidOperationException("Fatura bağı sipariş kilidi gerektirir.");
            var order = await dbContext.Orders.AsNoTracking().SingleOrDefaultAsync(row => row.Id == match.OrderId, token);
            var line = await dbContext.OrderLines.AsNoTracking().SingleOrDefaultAsync(row => row.OrderId == match.OrderId && row.LineId == match.OrderLineId, token);
            var receipt = await dbContext.Acceptances.AsNoTracking().SingleOrDefaultAsync(row => row.AcceptanceId == match.AcceptanceId, token);
            var invoiceLine = invoice.Lines.Single(item => item.Id == match.InvoiceLineId);
            if (order is null || line is null || receipt is null || order.Version != match.OrderVersion
                || order.SupplierPartyId != invoice.SupplierPartyId || order.CompanyAccountId != invoice.CompanyAccountId
                || order.CurrencyId != invoice.CurrencyId || line.UnitId != match.UnitId || line.ProductId != invoiceLine.ProductId
                || receipt.OrderId != order.Id || receipt.LineId != line.LineId)
                throw new PurchasingRuleException(PurchaseIncomingInvoice.SourceMismatch, "Faturalanan kabul kaynağı değişti.");
            var undone = await dbContext.ReceiptAdjustments.AsNoTracking().Where(row => row.AcceptanceId == match.AcceptanceId)
                .SumAsync(row => (decimal?)row.UndoQuantity, token) ?? 0m;
            var billed = await dbContext.InvoiceReceiptBindings.AsNoTracking().Where(row => row.AcceptanceId == match.AcceptanceId)
                .SumAsync(row => (decimal?)row.Quantity, token) ?? 0m;
            if (match.Quantity > receipt.Quantity - undone - billed)
                throw new PurchasingRuleException(PurchaseIncomingInvoice.SourceMismatch, "Kabulün faturalanabilir payı aşıldı.");
            receipts.Add(new()
            {
                CompanyId = invoice.CompanyId, InvoiceId = invoice.Id, InvoiceLineId = match.InvoiceLineId,
                AcceptanceId = match.AcceptanceId, OrderId = match.OrderId, OrderLineId = match.OrderLineId, Quantity = match.Quantity,
            });
        }
        var orders = invoice.Matches.DistinctBy(item => (item.InvoiceLineId, item.OrderLineId)).Select(match => new PurchaseInvoiceOrderBindingRow
            {
                CompanyId = invoice.CompanyId, InvoiceId = invoice.Id, InvoiceLineId = match.InvoiceLineId,
                OrderId = match.OrderId, OrderLineId = match.OrderLineId, OrderVersion = match.OrderVersion,
                SupplierPartyId = invoice.SupplierPartyId, RecordedAtUtc = timeProvider.GetUtcNow(),
            }).ToArray();
        return () => { dbContext.InvoiceReceiptBindings.AddRange(receipts); dbContext.InvoiceOrderBindings.AddRange(orders); };
    }

    private void AddInvoiceRevision(PurchaseIncomingInvoiceSnapshot snapshot, string json, DateTimeOffset now) => dbContext.IncomingInvoiceRevisions.Add(new()
    {
        CompanyId = snapshot.CompanyId, InvoiceId = snapshot.Id, Version = snapshot.Version, Status = (int)snapshot.Status,
        SnapshotJson = json, RecordedAtUtc = now,
    });
    private void AddInvoiceAttempt(Guid company, string command, PurchasingWriteEnvelope envelope, string requestHash,
        string? snapshotJson, string? refusalJson, DateTimeOffset now) => dbContext.IncomingInvoiceAttempts.Add(new()
    {
        CompanyId = company, CommandName = command, IdempotencyKeyHash = IdempotencyKeyHash(envelope.IdempotencyKey),
        RequestHash = requestHash, ActorId = envelope.ActorId.Trim(), CorrelationId = envelope.CorrelationId.Trim(),
        SnapshotJson = snapshotJson, RefusalJson = refusalJson, RecordedAtUtc = now,
    });
    private static PurchaseIncomingInvoiceSnapshot ReadInvoiceSnapshot(string json)
    {
        var snapshot = JsonSerializer.Deserialize<PurchaseIncomingInvoiceSnapshot>(json, Json)
            ?? throw new InvalidOperationException("Fatura geçmişi okunamadı.");
        _ = snapshot.Restore(); return snapshot;
    }
    private static void RequireInvoiceCommand(string command)
    {
        if (!PurchaseIncomingInvoiceCommandsV1.IsKnown(command)) throw new ArgumentException("Bilinmeyen fatura komutu.", nameof(command));
    }
    // ALS-D64: Purchasing commits its own pending invoice first; Finance is a separate idempotent owner call.
    // Refuse ambient/borrowed transactions rather than depending on SQL savepoints (which MARS may disable).
    private void RequireInvoiceTransactionOwnership()
    {
        if (dbContext.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Alış faturası yazımı kendi işlemini tamamlar; başka bir transaction'a katılmaz.");
    }
    private static bool ValidInvoiceTransition(string command, PurchaseIncomingInvoiceStatus before, PurchaseIncomingInvoiceStatus after) => command switch
    {
        PurchaseIncomingInvoiceCommandsV1.Match => before == PurchaseIncomingInvoiceStatus.Recorded && after == PurchaseIncomingInvoiceStatus.Matched,
        PurchaseIncomingInvoiceCommandsV1.ApproveVariance => before == PurchaseIncomingInvoiceStatus.Matched && after == before,
        PurchaseIncomingInvoiceCommandsV1.Finalize => before == PurchaseIncomingInvoiceStatus.Matched && after == PurchaseIncomingInvoiceStatus.FinancePending,
        PurchaseIncomingInvoiceCommandsV1.AcknowledgeFinance => before == PurchaseIncomingInvoiceStatus.FinancePending && after == PurchaseIncomingInvoiceStatus.FinanceAccepted,
        _ => false,
    };
}
