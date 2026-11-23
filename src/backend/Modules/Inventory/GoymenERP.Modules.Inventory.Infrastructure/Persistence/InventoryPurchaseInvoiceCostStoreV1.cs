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
    IInventoryFirstCostPrerequisitesV1? firstCostPrerequisites = null, bool? remainingShareAccepted = null) : IInventoryPurchaseInvoiceCostStoreV1
{
    /// <summary>
    /// ALS-D122/D123/D124: production composition passes nothing and reads
    /// <see cref="PurchaseInvoiceKilogramShare.PurchaseInvoiceRemainingShareGoldenAccepted"/>; a test pins either state explicitly.
    /// </summary>
    private readonly bool remainingShareAccepted = remainingShareAccepted ?? PurchaseInvoiceKilogramShare.PurchaseInvoiceRemainingShareGoldenAccepted;
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
                var basis = await ReadReceiptCostBasisAsync(receipt, share, cancellationToken);
                if (share.MeasuredQuantityKg is { } weighedKg)
                {
                    // AK-3b (ALS-D121): Purchasing's piece-line share must be THIS immutable piece receipt with exactly the
                    // kilograms it weighed; the kg share is then weighed kg × matched pieces ÷ accepted pieces.
                    if (receipt.PieceCount is not { } receivedPieces || receivedPieces != share.AcceptedQuantity
                        || basis.OriginalKg.CompareTo(ExactCostRatio.Of(weighedKg)) != 0)
                        throw Invalid("purchase_invoice_cost_receipt_source_conflict");
                    // A return or reversal that did not leave in the receipt's own kg-per-piece proportion makes "remaining kg ×
                    // matched ÷ remaining pieces" (ALS-D122) differ from ALS-D121. ALS-D122 applies only behind its switch (golden
                    // pending); until then the source stays pending.
                    if (!PurchaseInvoiceKilogramShare.PieceShareIsDecided(basis.OriginalKg, share.AcceptedQuantity,
                            basis.EligibleKg, basis.EligibleQuantity, remainingShareAccepted))
                        throw Unavailable("purchase_invoice_cost_piece_reversal_basis_unavailable");
                }
                if (basis.Cohort is { } cohort ? ExactCostRatio.Of(share.MatchedQuantity).CompareTo(cohort.PendingQuantity) > 0
                    : priorShares.Aggregate(ExactCostRatio.Of(share.MatchedQuantity),
                        (sum, row) => sum.Add(ExactCostRatio.Of(row.MatchedQuantity))).CompareTo(basis.EligibleQuantity) > 0)
                    throw Invalid("purchase_invoice_cost_receipt_share_conflict");
                var stamp = await db.MovementCostStamps.AsNoTracking().SingleOrDefaultAsync(row => row.MovementId == receipt.MovementId
                    && row.LineNumber == 1 && row.CurrencyId == Tl, cancellationToken);
                if (stamp is null || stamp.StampVersion < 1) throw Unavailable("purchase_invoice_cost_receipt_unavailable");
                if (stamp.Status != (int)InventoryMovementCostStampStatusV1.Known)
                {
                    // A later return changes the priced/unpriced cohort after an earlier recognition. Its economic
                    // allocation is not decided by this fix; preserve the earlier evidence and keep the new source retryable.
                    if (basis.Cohort is null && priorShares.Any(prior => !(FirstCostPhysicalSource(prior).ReceiptReversals ?? [])
                        .OrderBy(x => x.ReversalId).SequenceEqual(basis.Reversals.OrderBy(x => x.ReversalId))))
                        throw Unavailable("purchase_invoice_cost_first_reversal_basis_changed");
                    firstPlans.Add(await PrepareFirstCostAsync(source, share, receipt.MovementId,
                        receipt.LabelId, receipt.LocationId, receipt.QuantityKg, stamp, basis, cancellationToken,
                        PieceShareOf(basis, share, priorShares)));
                    continue;
                }
                var originalUnit = InventoryExactCostRatioV1.Resolve(stamp.UnitCostPerKg, stamp.UnitCostRatioJson is null ? null
                    : JsonSerializer.Deserialize<InventoryExactCostRatioV1>(stamp.UnitCostRatioJson, Json));
                if (originalUnit.Sign < 0) throw Invalid("purchase_invoice_cost_receipt_source_conflict");
                // The existing late-difference contract retains the original stamped kg/purchase-unit basis.
                // A non-proportional measured undo cannot silently reinterpret that historical cost contract.
                // ALS-D122 (switch on): a piece share is decided by its own rule below, so this guard no longer holds it back.
                if (!(remainingShareAccepted && share.MeasuredQuantityKg is not null)
                    && basis.EligibleKg.Divide(basis.EligibleQuantity)
                    .CompareTo(basis.OriginalKg.Divide(share.AcceptedQuantity)) != 0)
                    throw Unavailable("purchase_invoice_cost_known_reversal_basis_unavailable");
                // Physical kg comes from the immutable original Inventory receipt, not current Product conversion.
                var invoicedKg = PieceShareOf(basis, share, priorShares)
                    ?? PurchaseInvoiceKilogramShare.Of(basis.EligibleKg, share.MatchedQuantity, basis.EligibleQuantity);
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

    /// <summary>
    /// The piece-line kilogram share (null on a kilogram line or with the switch closed). Without an explicit supplier-return source
    /// the remaining figures are the receipt's after its returns less the earlier invoices (<see cref="RemainingPieceShare"/>); with
    /// one (an AK-8b cohort, which exists only after a return) they are the cohort's pending root kg and pending pieces — the part no
    /// invoice took yet after every return — so the same ALS-D122/D123/D124 rule applies: three decimals, the last invoice takes the
    /// remaining kg (golden EK1 E26, E28).
    /// </summary>
    private ExactCostRatio? PieceShareOf(ReceiptCostBasis basis, InventoryPurchaseInvoiceCostReceiptV1 share,
        IReadOnlyList<PurchaseInvoiceReceiptCostEffectRow> priorShares) => basis.Cohort is { } cohort
        ? CohortPieceShare(remainingShareAccepted, share.MeasuredQuantityKg, cohort.PendingRootKg, cohort.PendingQuantity,
            share.MatchedQuantity)
        : RemainingPieceShare(basis, share, priorShares);

    /// <summary>ALS-D122/D123/D124 over an explicit-source cohort's pending figures; nothing pending keeps the source waiting.</summary>
    internal static ExactCostRatio? CohortPieceShare(bool remainingShareAccepted, decimal? measuredQuantityKg,
        ExactCostRatio pendingRootKg, ExactCostRatio pendingPieces, decimal invoicedPieces)
    {
        if (!remainingShareAccepted || measuredQuantityKg is null) return null;
        try
        {
            return PurchaseInvoiceKilogramShare.RemainingShare(pendingRootKg, pendingPieces, invoicedPieces);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Unavailable("purchase_invoice_cost_piece_remaining_share_unavailable");
        }
    }

    /// <summary>
    /// ALS-D122/D123/D124 (golden EK1 E22–E29), only while the switch is on and only for a piece-line share (null otherwise: the caller keeps today's formula). The remaining figures are current: the
    /// receipt's kg and pieces after its returns (<paramref name="basis"/>) less the kg and pieces of the earlier invoices recorded
    /// for the same acceptance. An earlier effect whose kilograms cannot be read keeps the source pending; nothing is estimated.
    /// </summary>
    private ExactCostRatio? RemainingPieceShare(ReceiptCostBasis basis, InventoryPurchaseInvoiceCostReceiptV1 share,
        IReadOnlyList<PurchaseInvoiceReceiptCostEffectRow> priorShares)
    {
        if (!remainingShareAccepted || share.MeasuredQuantityKg is null) return null;
        var remainingKg = priorShares.Aggregate(basis.EligibleKg, (left, prior) => left.Subtract(PriorInvoicedKg(prior)));
        var remainingPieces = priorShares.Aggregate(basis.EligibleQuantity, (left, prior) => left.Subtract(ExactCostRatio.Of(prior.MatchedQuantity)));
        if (remainingKg.Sign <= 0 || remainingPieces.Sign <= 0 || ExactCostRatio.Of(share.MatchedQuantity).CompareTo(remainingPieces) > 0)
            throw Unavailable("purchase_invoice_cost_piece_remaining_share_unavailable");
        return PurchaseInvoiceKilogramShare.PieceShare(basis.OriginalKg, share.AcceptedQuantity, basis.Reversals.Count > 0,
            remainingKg, remainingPieces, share.MatchedQuantity);
    }

    /// <summary>The kilograms an earlier invoice took from the same acceptance, from its immutable effect (either recognition shape).</summary>
    private static ExactCostRatio PriorInvoicedKg(PurchaseInvoiceReceiptCostEffectRow effect)
    {
        try
        {
            using var document = JsonDocument.Parse(effect.ResultJson);
            var root = document.RootElement;
            if ((root.TryGetProperty("invoicedKg", out var kilograms)
                    || root.TryGetProperty("receipt", out var receipt) && receipt.TryGetProperty("invoicedPhysicalKg", out kilograms))
                && kilograms.TryGetProperty("numerator", out var numerator)
                && kilograms.TryGetProperty("denominator", out var denominator)
                && ExactCostRatio.TryParse(numerator.GetString(), denominator.GetString(), out var value) && value is { Sign: > 0 })
                return value;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // An unreadable earlier effect: fall through to the pending answer below.
        }
        throw Unavailable("purchase_invoice_cost_piece_remaining_share_unavailable");
    }

    private void RequireCompany(Guid expected)
    {
        if (!company.IsResolved || company.CompanyId != expected) throw Unavailable("purchase_invoice_cost_company_changed");
    }
    private static InventoryExactCostRatioV1 Ratio(ExactCostRatio value) => InventoryExactCostRatioV1.From(value);
    private static OutboxPermanentFailureException Invalid(string code) => new OutboxPermanentFailureException(code, "Alış faturası maliyet kaynağı çelişiyor.");
    private static InvalidOperationException Unavailable(string code) => new InvalidOperationException(code);
}
