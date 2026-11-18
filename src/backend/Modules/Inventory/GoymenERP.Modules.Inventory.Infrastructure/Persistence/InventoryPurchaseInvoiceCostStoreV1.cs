using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Messaging;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>SAT-D385/D404/D413: one immutable invoice share changes the current original-position average, never
/// historical receipt/issue stamps. BS-1 first recognition retains its decided partial-material split separately.</summary>
public sealed partial class InventoryPurchaseInvoiceCostStoreV1(InventoryDbContext db, ICompanyContext company,
    IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter, TimeProvider clock,
    IInventoryFirstCostPrerequisitesV1? firstCostPrerequisites = null) : IInventoryPurchaseInvoiceCostStoreV1
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private static readonly Guid Tl = SettingsCurrencyIdentitiesV1.TurkishLira;

    public async Task ApplyAsync(WorkInboxEventV1 envelope, CancellationToken cancellationToken = default)
    {
        var source = InventoryPurchaseInvoiceCostSourceReaderV1.Read(envelope, company);
        firstCostFencedLabels.Clear();
        if (auditWriter.HasPendingDrafts) throw Unavailable("purchase_invoice_cost_audit_scope_busy");
        var companyId = source.CompanyId;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(envelope.PayloadJson)));
        // The same Serializable transaction locks the invoice identity, acceptance-share ranges and current pools.
        // A competing physical writer must either precede the valuation or observe its committed pool version.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var previous = await db.PurchaseInvoiceCostApplications.SingleOrDefaultAsync(row => row.SourceEventId == envelope.EventId,
                cancellationToken);
            RequireCompany(companyId);
            if (previous is not null)
            {
                if (previous.SourceSha256 != hash) throw Invalid("purchase_invoice_cost_source_conflict");
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            if (await db.PurchaseInvoiceCostApplications.AnyAsync(row => row.InvoiceId == source.InvoiceId
                || row.AdmissionId == source.AdmissionId, cancellationToken)) throw Invalid("purchase_invoice_cost_duplicate_invoice");

            var receipts = new List<InventoryPurchaseInvoiceCostReceiptResultV1>();
            var firstRecognitions = new List<InventoryPurchaseFirstCostRecognitionV1>();
            var firstPlans = new List<FirstCostReceiptPlan>();
            foreach (var share in source.Receipts.OrderBy(value => value.AcceptanceId))
            {
                var priorShares = await db.PurchaseInvoiceReceiptCostEffects.Where(row => row.AcceptanceId == share.AcceptanceId)
                    .ToListAsync(cancellationToken);
                if (priorShares.Any(row => row.AcceptedQuantity != share.AcceptedQuantity)
                    || priorShares.Aggregate(ExactCostRatio.Of(share.MatchedQuantity), (sum, row) => sum.Add(ExactCostRatio.Of(row.MatchedQuantity)))
                        .CompareTo(ExactCostRatio.Of(share.AcceptedQuantity)) > 0)
                    throw Invalid("purchase_invoice_cost_receipt_share_conflict");
                var actual = await InventoryGoodsReceiptSourceProjection.ReadAsync(db, companyId, share.AcceptanceId, cancellationToken)
                    ?? throw Unavailable("purchase_invoice_cost_receipt_unavailable");
                var receipt = actual.OriginalRequest;
                if (receipt.CompanyId != companyId || receipt.ProductId != share.ProductId || receipt.PurchaseUnitId != share.UnitId
                    || receipt.PurchaseOrderId != share.OrderId || receipt.PurchaseOrderLineId != share.OrderLineId
                    || !long.TryParse(receipt.PurchaseOrderVersionAfter, NumberStyles.None, CultureInfo.InvariantCulture, out var orderVersion)
                    || orderVersion > share.OrderVersion || receipt.OwnerKind != StockPartyKind.Company || receipt.OwnerId != companyId
                    || receipt.CustodianKind != StockPartyKind.Company || receipt.CustodianId != companyId)
                    throw Invalid("purchase_invoice_cost_receipt_source_conflict");
                if (await db.GoodsReceiptReversals.AnyAsync(row => row.AcceptanceId == share.AcceptanceId, cancellationToken))
                    throw Invalid("purchase_invoice_cost_receipt_reversed");
                var stamp = await db.MovementCostStamps.AsNoTracking().SingleOrDefaultAsync(row => row.MovementId == receipt.MovementId
                    && row.LineNumber == 1 && row.CurrencyId == Tl, cancellationToken);
                if (stamp is null || stamp.StampVersion < 1) throw Unavailable("purchase_invoice_cost_receipt_unavailable");
                if (stamp.Status != (int)InventoryMovementCostStampStatusV1.Known)
                {
                    firstPlans.Add(await PrepareFirstCostAsync(source, share, receipt.MovementId,
                        receipt.LabelId, receipt.LocationId, receipt.QuantityKg, stamp, cancellationToken));
                    continue;
                }
                var originalUnit = InventoryExactCostRatioV1.Resolve(stamp.UnitCostPerKg, stamp.UnitCostRatioJson is null ? null
                    : JsonSerializer.Deserialize<InventoryExactCostRatioV1>(stamp.UnitCostRatioJson, Json));
                if (originalUnit.Sign < 0) throw Invalid("purchase_invoice_cost_receipt_source_conflict");
                // Physical kg comes from the immutable original Inventory receipt, not current Product conversion.
                var physicalKg = decimal.Parse(receipt.QuantityKg, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                var invoicedKg = ExactCostRatio.Of(physicalKg).Multiply(share.MatchedQuantity).Divide(share.AcceptedQuantity);
                var previousValue = originalUnit.Multiply(invoicedKg);
                var invoiceValue = share.TlNetShare.Value();
                var result = new InventoryPurchaseInvoiceCostReceiptResultV1(share.InvoiceLineId, share.InvoiceLineNo, share.AcceptanceId,
                    receipt.MovementId, 1, receipt.LabelId, share.ProductId, receipt.LocationId, share.MatchedQuantity, share.AcceptedQuantity,
                    Ratio(invoicedKg), Ratio(invoiceValue), Ratio(previousValue), Ratio(invoiceValue.Subtract(previousValue)), stamp.StampVersion);
                receipts.Add(result);
                db.PurchaseInvoiceReceiptCostEffects.Add(new PurchaseInvoiceReceiptCostEffectRow() { CompanyId = companyId, SourceEventId = envelope.EventId,
                    AcceptanceId = share.AcceptanceId, InvoiceLineId = share.InvoiceLineId, MovementId = receipt.MovementId,
                    MovementLineNumber = 1, MatchedQuantity = share.MatchedQuantity, AcceptedQuantity = share.AcceptedQuantity,
                    ResultJson = JsonSerializer.Serialize(result, Json) });
            }

            foreach (var line in firstPlans.GroupBy(x => x.Share.InvoiceLineId).OrderBy(x => x.First().Share.InvoiceLineNo))
            {
                var recognition = await RecognizeFirstCostLineAsync(source, line.ToArray(), cancellationToken);
                firstRecognitions.Add(recognition);
                // Publish each real receipt's normalized quantity only after its aggregate pool additions.
                // Reading a pool midway through those additions must still see the prior costed fraction.
                foreach (var item in line)
                    db.PurchaseInvoiceReceiptCostEffects.Add(new PurchaseInvoiceReceiptCostEffectRow() { CompanyId = companyId, SourceEventId = envelope.EventId,
                        AcceptanceId = item.Share.AcceptanceId, InvoiceLineId = item.Share.InvoiceLineId,
                        MovementId = item.Receipt.ReceiptMovementId, MovementLineNumber = 1,
                        MatchedQuantity = item.Share.MatchedQuantity, AcceptedQuantity = item.Share.AcceptedQuantity,
                        ResultJson = JsonSerializer.Serialize(new { recognition.InvoiceLineId, receipt = item.Receipt,
                            recognition.AllocationVersion }, Json) });
            }

            var positions = new List<InventoryPurchaseInvoiceCostPositionResultV1>();
            foreach (var group in receipts.GroupBy(value => (value.LocationId, value.ProductId)).OrderBy(value => value.Key.LocationId)
                .ThenBy(value => value.Key.ProductId))
            {
                var pool = await InventoryStore.ReadCostPoolAsync(db, company, group.Key.LocationId, group.Key.ProductId, Tl, cancellationToken);
                RequireCompany(companyId);
                if (!pool.IsConsistent || pool.ExactPosition is null) throw Unavailable("purchase_invoice_cost_pool_unavailable");
                var difference = group.Aggregate(ExactCostRatio.Of(0m), (sum, value) => sum.Add(value.SourceDifference.Value()));
                var applied = AverageCostLateInvoiceDifferenceCalculator.ApplyExact(pool.ExactPosition, difference);
                if (applied.Position.AverageCostPerKg >= 100_000_000_000_000m) throw Unavailable("purchase_invoice_cost_pool_overflow");
                var version = applied.AbsorbedDifference.Sign == 0 ? pool.Version
                    : await InventoryStore.WriteCostPoolAsync(db, company, clock, group.Key.LocationId, group.Key.ProductId, Tl,
                        pool, applied.Position, cancellationToken);
                positions.Add(new InventoryPurchaseInvoiceCostPositionResultV1(group.Key.ProductId, group.Key.LocationId, Ratio(difference), Ratio(applied.AbsorbedDifference),
                    Ratio(applied.UnabsorbedDifference), Ratio(applied.RecordedPoolValueEffect), Ratio(applied.AverageRoundingDifference), version));
            }
            var unabsorbed = positions.Aggregate(ExactCostRatio.Of(0m), (sum, value) => sum.Add(value.UnabsorbedDifference.Value()))
                .Add(firstRecognitions.Aggregate(ExactCostRatio.Of(0m), (sum, value) => sum.Add(ExactCostRatio.Of(value.ConsumedTlAmount))));
            // Preserve each nonzero residual even when opposite signed positions net to zero. A real shipment
            // fully returned before first recognition also needs its immutable zero-claim witness; stock alone does not.
            var costEventId = positions.Any(value => value.UnabsorbedDifference.Value().Sign != 0)
                || firstRecognitions.Any(value => value.ConsumedExactTlAmount.Value().Sign != 0)
                || firstPlans.Any(value => value.HasFullyReturnedShipmentWitness) ? Guid.NewGuid() : (Guid?)null;
            var resultOfApplication = new InventoryPurchaseInvoiceCostApplicationResultV1(companyId, source.InvoiceId, source.InvoiceVersion,
                envelope.EventId, source.AdmissionId, source.InvoiceDate, source.OfficialDocument, hash,
                receipts.OrderBy(value => value.InvoiceLineNo).ThenBy(value => value.AcceptanceId).ToArray(), positions, costEventId,
                Ratio(unabsorbed), firstRecognitions.Count == 0 ? null : firstRecognitions);
            var resultJson = JsonSerializer.Serialize(resultOfApplication, Json);
            db.PurchaseInvoiceCostApplications.Add(new PurchaseInvoiceCostApplicationRow() { CompanyId = companyId, SourceEventId = envelope.EventId,
                InvoiceId = source.InvoiceId, InvoiceVersion = source.InvoiceVersion, AdmissionId = source.AdmissionId,
                SourceSha256 = hash, SourceJson = envelope.PayloadJson, ResultJson = resultJson, OccurredAtUtc = envelope.OccurredAtUtc,
                RecordedAtUtc = clock.GetUtcNow(), CorrelationId = envelope.CorrelationId });
            if (costEventId is { } id)
                db.OutboxMessages.Add(new InventoryOutboxRow() { CompanyId = companyId, Id = id, EventType = InventoryPurchaseInvoiceCostDifferenceV1.EventType,
                    SchemaVersion = 1, SourceOperation = InventoryPurchaseInvoiceCostInboxV1.ConsumerName,
                    AggregateType = InventoryPurchaseInvoiceCostDifferenceV1.AggregateType, AggregateId = source.InvoiceId,
                    AggregateVersion = source.InvoiceVersion, Sequence = 1, PayloadJson = resultJson,
                    OccurredAtUtc = envelope.OccurredAtUtc, RecordedAtUtc = clock.GetUtcNow(), CorrelationId = envelope.CorrelationId,
                    CausationId = envelope.EventId.ToString("D") });
            RequireCompany(companyId);
            await db.SaveChangesAsync(cancellationToken);
            RequireCompany(companyId);
            // Enqueue only after the Inventory save: the Audit owner writes this batch in the same transaction.
            var actor = "inbox." + InventoryPurchaseInvoiceCostInboxV1.ConsumerName;
            audit.Enqueue(new AuditEventDraft(companyId, InventoryPurchaseInvoiceCostInboxV1.ConsumerName,
                InventoryPurchaseInvoiceCostDifferenceV1.AggregateType, source.InvoiceId.ToString("D"), "Applied", null, source.InvoiceVersion,
                AuditActorTypes.System, actor, actor, [], "{}", resultJson, "Kesinleşmiş alış faturasının kanıtlı maliyet farkı.",
                null, AuditAiInvolvement.None, null, SourceChannels.System, null, null, envelope.CorrelationId,
                envelope.EventId.ToString("D"), envelope.EventId.ToString("D")));
            RequireCompany(companyId);
            await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), cancellationToken);
            RequireCompany(companyId);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            auditWriter.DiscardPending();
            throw;
        }
    }

    private void RequireCompany(Guid expected)
    {
        if (!company.IsResolved || company.CompanyId != expected) throw Unavailable("purchase_invoice_cost_company_changed");
    }
    private static InventoryExactCostRatioV1 Ratio(ExactCostRatio value) => InventoryExactCostRatioV1.From(value);
    private static OutboxPermanentFailureException Invalid(string code) => new OutboxPermanentFailureException(code, "Alış faturası maliyet kaynağı çelişiyor.");
    private static InvalidOperationException Unavailable(string code) => new InvalidOperationException(code);
}
