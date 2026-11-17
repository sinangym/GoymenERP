using System.Data;
using System.Globalization;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Accounting.Application;
using GoymenERP.Modules.Accounting.Domain.Journals;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Accounting.Infrastructure.Persistence;

/// <summary>
/// Appends physical attribution of actual posted money inside the posting owner's serializable transaction.
/// The same own-table ranges fence journal reversals, later signed source components and initial invoice claims.
/// Inventory history is immutable provenance, not a claim that the physical generation is still current.
/// </summary>
public sealed class ManufacturingPostedCostAttributionStoreV1(AccountingDbContext db, ICompanyContext company,
    IManufacturingAttributionLineageReaderV1 lineage, AccountingAmountScaleVerifier amounts, TimeProvider clock)
    : IPostedManufacturingShipmentCostReaderV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RecordPostedAsync(Guid sourceEventId, Func<CancellationToken, Task> freshFence,
        CancellationToken token = default)
    {
        var captured = Company(); RequireTransaction();
        await Fence(captured, freshFence, token);
        var row = await db.ManufacturingCostDifferences.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceEventId, token)
            ?? throw Refuse("manufacturing_attribution_source_missing");
        await Fence(captured, freshFence, token);
        if (row.Status != 3 || row.JournalId is null || row.CurrencyId is null || row.AccountingDate is null
            || row.PreparedBy is null || row.ApprovedBy is null || row.PreparedBy == row.ApprovedBy)
            throw Refuse("manufacturing_attribution_unposted_source");
        var source = Decode<ManufacturingCostDifferenceEventV1>(row.SourceJson);
        // Pre-lineage sources retain their real posting. They cannot prove an invoice's physical share retroactively.
        if (source.ConsumedLineageReference is null) return;
        if (source.CompanyId != captured || source.CostDifferenceEventId != row.Id || source.GenerationId != row.GenerationId
            || source.GenerationVersion != row.GenerationVersion) throw Refuse("manufacturing_attribution_source_invalid");
        var frozen = await lineage.ReadAsync(source, freshFence, token);
        await Fence(captured, freshFence, token);
        var journal = await ActualJournal(row.JournalId.Value, sourceEventId, captured, freshFence, token);
        await Fence(captured, freshFence, token);
        var profile = await amounts.ReadProfileAsync(captured, journal.Header.CurrencyId, journal.Header.AccountingDate, token);
        await Fence(captured, freshFence, token);
        var exactTotal = source.Outputs.Aggregate(AccountingInventoryCostAmountsV1.FromDecimal(0m), (sum, output) =>
            AccountingInventoryCostAmountsV1.Add(sum, AccountingInventoryCostAmountsV1.FromDecimal(output.ConsumedTlDifference!.Value)));
        if (!AccountingInventoryCostAmountsV1.TryExactDecimal(exactTotal, 28, out var total))
            throw Refuse("manufacturing_attribution_posted_amount_mismatch");
        if (total == 0 || Math.Abs(total) != journal.Debit.Debit || journal.Header.CurrencyId != row.CurrencyId
            || Text(total) != row.SignedTlDifference || frozen.SourceEventId != sourceEventId || frozen.CompanyId != captured
            || frozen.GenerationId != source.GenerationId || frozen.GenerationVersion != source.GenerationVersion
            || frozen.HistorySha256 != source.ConsumedLineageReference.HistorySha256 || frozen.Outputs.Count != source.Outputs.Count)
            throw Refuse("manufacturing_attribution_posted_amount_mismatch");
        foreach (var output in source.Outputs)
        {
            var proof = frozen.Outputs.SingleOrDefault(x => x.OutputId == output.OutputId)
                ?? throw Refuse("manufacturing_attribution_output_missing");
            var existing = await db.ManufacturingCostAttributions.AsNoTracking().SingleOrDefaultAsync(x =>
                x.SourceEventId == sourceEventId && x.OutputId == output.OutputId, token);
            await Fence(captured, freshFence, token);
            if (existing is not null)
            {
                if (existing.SourceHash != row.SourceHash || existing.LineageHash != proof.LineageSha256
                    || existing.JournalId != row.JournalId) throw Refuse("manufacturing_attribution_replay_conflict");
                continue; // The original attribution's profile and money remain frozen across subsequent profile changes.
            }
            if (!AccountingInventoryCostAmountsV1.TryRead(proof.ConsumedRootQuantityKg, out var consumed, out _)
                || proof.ReceiptMovementId != output.ReceiptMovementId || proof.LabelId != output.LabelId
                || proof.NetQuantityKg != output.NetQuantityKg || proof.Shipments.Any(x => !ManufacturingAttributionShipmentEvidenceV1.IsValid(x)))
                throw Refuse("manufacturing_attribution_output_invalid");
            PostedCostAttributionCalculationV1? calculated;
            if (consumed == 0)
            {
                if (output.ConsumedTlDifference != 0 || proof.Shipments.Any(x => x.NetRootQuantityKg.Numerator != "0"))
                    throw Refuse("manufacturing_attribution_output_invalid");
                calculated = new(0, journal.Header.CurrencyId, profile.Scale, proof.ConsumedRootQuantityKg, []);
            }
            else
            {
                var weighted = proof.Shipments.Where(x => x.NetRootQuantityKg.Numerator != "0").Select(x =>
                    new PostedCostAttributionWeightV1(x.ShipmentId, x.MovementId, x.MovementLineNumber, x.NetRootQuantityKg,
                        x.RecordedAtUtc, x.ProvenOriginalSequence)).ToArray();
                if (!PostedManufacturingCostAttributionCalculatorV1.TryCalculate(output.ConsumedTlDifference!.Value,
                        journal.Header.CurrencyId, profile.Scale, proof.ConsumedRootQuantityKg, weighted, out calculated, out var reason))
                    throw Refuse(reason ?? "manufacturing_attribution_amount_unavailable");
            }
            // A completely returned shipment has a proved zero net share. Retain that evidence without
            // assigning any money or participating in the positive-weight rounding/remainder order.
            var zero = AccountingInventoryCostAmountsV1.FromDecimal(0m);
            calculated = calculated! with { Shares = calculated!.Shares.Concat(proof.Shipments.Where(x => x.NetRootQuantityKg == zero)
                .Select(x => new PostedCostAttributionAmountV1(new(x.ShipmentId, x.MovementId, x.MovementLineNumber,
                    zero, x.RecordedAtUtc, x.ProvenOriginalSequence), zero, 0, 0)))
                .OrderBy(x => x.Source.ShipmentId).ThenBy(x => x.Source.MovementLineNumber).ToArray() };
            var at = clock.GetUtcNow();
            db.ManufacturingCostAttributions.Add(new() { CompanyId = captured, SourceEventId = sourceEventId,
                OutputId = output.OutputId, GenerationId = source.GenerationId, GenerationVersion = source.GenerationVersion,
                ReceiptMovementId = output.ReceiptMovementId, JournalId = journal.Header.Id, CurrencyId = journal.Header.CurrencyId,
                MoneyProfileId = profile.Profile.ProfileId, MoneyProfileVersion = profile.Profile.Version, MoneyScale = profile.Scale,
                SourceHash = row.SourceHash, LineageHash = proof.LineageSha256,
                ViewJson = Encode(new { Proof = proof, Calculation = calculated, Profile = profile, frozen.HistorySha256 }), RecordedAtUtc = at });
            foreach (var share in calculated!.Shares)
            {
                var physical = proof.Shipments.Single(x => x.MovementId == share.Source.MovementId
                    && x.MovementLineNumber == share.Source.MovementLineNumber);
                var value = new PostedManufacturingCostShareV1(captured, sourceEventId, output.OutputId,
                    source.GenerationId, source.GenerationVersion, output.ReceiptMovementId, journal.Header.Id, journal.Header.CurrencyId,
                    total > 0 ? journal.Debit.AccountId : journal.Credit.AccountId,
                    total > 0 ? journal.Credit.AccountId : journal.Debit.AccountId, Text(journal.Debit.Debit), Text(share.RoundedSignedAmount),
                    share.ExactSignedAmount, Text(share.SignedResidue), profile.Profile.ProfileId, profile.Profile.Version, profile.Scale,
                    journal.Header.AccountingDate, physical, row.SourceHash, proof.LineageSha256, frozen.HistorySha256, "");
                value = value with { Fingerprint = Hash(Encode(value)) };
                db.ManufacturingCostAttributionShares.Add(new() { CompanyId = captured, SourceEventId = sourceEventId,
                    OutputId = output.OutputId, MovementId = physical.MovementId, MovementLineNumber = physical.MovementLineNumber,
                    ShipmentId = physical.ShipmentId, ProductId = physical.ProductId, SourceHash = value.Fingerprint,
                    ViewJson = Encode(value), RecordedAtUtc = at });
            }
        }
        await Fence(captured, freshFence, token);
    }

    public Task<PostedManufacturingShipmentCostV1?> ReadAsync(Guid companyId, Guid shipmentId, Guid movementId,
        int movementLineNumber, Guid productId, decimal quantityKg, Func<CancellationToken, Task> freshConsumerFence,
        CancellationToken cancellationToken = default) =>
        Read(companyId, shipmentId, movementId, movementLineNumber, productId, quantityKg, freshConsumerFence, true, true, cancellationToken);

    public Task<PostedManufacturingShipmentCostV1?> ReadProvenSharesAsync(Guid companyId, Guid shipmentId, Guid movementId,
        int movementLineNumber, Guid productId, decimal quantityKg, Func<CancellationToken, Task> freshConsumerFence,
        CancellationToken cancellationToken = default) =>
        Read(companyId, shipmentId, movementId, movementLineNumber, productId, quantityKg, freshConsumerFence, false, true, cancellationToken);

    public Task<PostedManufacturingShipmentCostV1?> ReadPostedDeltasAsync(Guid companyId, Guid shipmentId, Guid movementId,
        int movementLineNumber, Guid productId, decimal quantityKg, Func<CancellationToken, Task> freshConsumerFence,
        CancellationToken cancellationToken = default) =>
        Read(companyId, shipmentId, movementId, movementLineNumber, productId, quantityKg, freshConsumerFence, false, false, cancellationToken);

    private async Task<PostedManufacturingShipmentCostV1?> Read(Guid companyId, Guid shipmentId, Guid movementId,
        int movementLineNumber, Guid productId, decimal quantityKg, Func<CancellationToken, Task> freshConsumerFence,
        bool requireWholeLine, bool requireRecognitionWitness, CancellationToken cancellationToken)
    {
        RequireTransaction();
        if (companyId != Company() || shipmentId == Guid.Empty || movementId == Guid.Empty || movementLineNumber < 1
            || productId == Guid.Empty || quantityKg <= 0) throw Refuse("manufacturing_attribution_query_invalid");
        await Fence(companyId, freshConsumerFence, cancellationToken);
        var rows = await db.ManufacturingCostAttributionShares.AsNoTracking().Where(x => x.MovementId == movementId
            && x.MovementLineNumber == movementLineNumber).ToListAsync(cancellationToken);
        await Fence(companyId, freshConsumerFence, cancellationToken);
        if (rows.Count == 0) return null;
        var shares = new List<PostedManufacturingCostShareV1>();
        var recognizedRoots = new HashSet<(Guid OutputId, Guid ReceiptMovementId)>();
        var sourceOrders = new HashSet<Guid>();
        foreach (var row in rows)
        {
            var share = Decode<PostedManufacturingCostShareV1>(row.ViewJson);
            var physical = share.Shipment;
            if (row.CompanyId != companyId || row.ShipmentId != shipmentId || row.ProductId != productId
                || share.CompanyId != companyId || share.SourceEventId != row.SourceEventId || share.OutputId != row.OutputId
                || physical.ShipmentId != shipmentId || physical.MovementId != movementId || physical.MovementLineNumber != movementLineNumber
                || physical.ProductId != productId || physical.PhysicalQuantityKg != quantityKg
                || !ManufacturingAttributionShipmentEvidenceV1.IsValid(physical)
                || share.Fingerprint != row.SourceHash || Hash(Encode(share with { Fingerprint = "" })) != share.Fingerprint)
                throw Refuse("manufacturing_attribution_physical_share_unavailable");
            var journal = await ActualJournal(share.JournalId, share.SourceEventId, companyId, freshConsumerFence, cancellationToken);
            await Fence(companyId, freshConsumerFence, cancellationToken);
            var signed = decimal.Parse(share.SignedAmount, CultureInfo.InvariantCulture);
            var source = await db.ManufacturingCostDifferences.AsNoTracking().SingleAsync(x => x.Id == share.SourceEventId, cancellationToken);
            await Fence(companyId, freshConsumerFence, cancellationToken);
            var sourceSign = Math.Sign(decimal.Parse(source.SignedTlDifference, CultureInfo.InvariantCulture));
            var sourceProof = Decode<ManufacturingCostDifferenceEventV1>(source.SourceJson);
            var sourceOutput = sourceProof.Outputs.SingleOrDefault(x => x.OutputId == share.OutputId);
            if (source.Status != 3 || source.JournalId != share.JournalId || source.SourceHash != share.SourcePayloadSha256
                || sourceProof.CompanyId != companyId || sourceProof.CostDifferenceEventId != share.SourceEventId
                || sourceProof.GenerationId != share.GenerationId || sourceProof.GenerationVersion != share.GenerationVersion
                || sourceOutput?.ReceiptMovementId != share.ReceiptMovementId || sourceProof.State is not (3 or 4)
                || journal.Header.CurrencyId != share.CurrencyId || Text(journal.Debit.Debit) != share.JournalTotal
                || share.CogsAccountId != (sourceSign > 0 ? journal.Debit.AccountId : journal.Credit.AccountId)
                || share.CounterAccountId != (sourceSign > 0 ? journal.Credit.AccountId : journal.Debit.AccountId)
                || signed != 0 && Math.Sign(signed) != sourceSign) throw Refuse("manufacturing_attribution_posted_component_unavailable");
            // A rounded zero is not evidence of recognition: only the actual State=3 source establishes a root.
            // A State=4 reversal may legitimately round to zero, but cannot replace missing positive history.
            if (sourceProof.State == 3) recognizedRoots.Add((share.OutputId, share.ReceiptMovementId));
            sourceOrders.Add(source.ManufacturingOrderId);
            shares.Add(share);
        }
        await RequireCompletePostedHistory(shares, sourceOrders, companyId, freshConsumerFence, cancellationToken);
        // Each original root is counted once for physical coverage, while every signed posted generation remains money evidence.
        var coverage = AccountingInventoryCostAmountsV1.FromDecimal(0m);
        foreach (var root in shares.GroupBy(x => (x.OutputId, x.ReceiptMovementId)))
        {
            if (root.Select(x => x.Shipment.RootShare).Distinct().Count() != 1
                || requireRecognitionWitness && !recognizedRoots.Contains(root.Key))
                throw Refuse("manufacturing_attribution_root_incomplete");
            coverage = AccountingInventoryCostAmountsV1.Add(coverage, root.First().Shipment.RootShare);
        }
        if (!AccountingInventoryCostAmountsV1.TryRead(coverage, out var covered, out var denominator) || covered <= 0 || covered > denominator
            || requireWholeLine && covered != denominator) throw Refuse("manufacturing_attribution_root_incomplete");
        await Fence(companyId, freshConsumerFence, cancellationToken);
        return new(companyId, shipmentId, movementId, movementLineNumber, productId, quantityKg,
            shares.OrderBy(x => x.SourceEventId).ThenBy(x => x.OutputId).ToArray());
    }

    private async Task RequireCompletePostedHistory(IReadOnlyList<PostedManufacturingCostShareV1> shares,
        IReadOnlyCollection<Guid> sourceOrders, Guid captured, Func<CancellationToken, Task> fence, CancellationToken token)
    {
        var roots = shares.Select(x => (x.OutputId, x.ReceiptMovementId)).ToHashSet();
        foreach (var order in sourceOrders)
        {
            // Targeted own-source history, not a cross-company JSON search or reconstruction from today's graph.
            // A later recognized generation must not hide a posted legacy positive component missing from this ledger.
            var history = await db.ManufacturingCostDifferences.AsNoTracking()
                .Where(x => x.ManufacturingOrderId == order && x.Status == 3).Take(10001).ToListAsync(token);
            await Fence(captured, fence, token);
            if (history.Count > 10000) throw Refuse("manufacturing_attribution_history_unavailable");
            foreach (var row in history)
            {
                var source = Decode<ManufacturingCostDifferenceEventV1>(row.SourceJson);
                if (Hash(row.SourceJson) != row.SourceHash || source.CompanyId != captured || source.ManufacturingOrderId != order
                    || source.CostDifferenceEventId != row.Id || source.State is not (3 or 4))
                    throw Refuse("manufacturing_attribution_history_unavailable");
                foreach (var output in source.Outputs.Where(x => roots.Contains((x.OutputId, x.ReceiptMovementId))))
                {
                    if (output.ConsumedTlDifference == 0) continue;
                    var evidence = await db.ManufacturingCostAttributions.AsNoTracking().SingleOrDefaultAsync(x =>
                        x.SourceEventId == row.Id && x.OutputId == output.OutputId, token);
                    await Fence(captured, fence, token);
                    if (output.ConsumedTlDifference is null || evidence is null || evidence.ReceiptMovementId != output.ReceiptMovementId
                        || evidence.SourceHash != row.SourceHash || evidence.JournalId != row.JournalId
                        || evidence.GenerationId != source.GenerationId || evidence.GenerationVersion != source.GenerationVersion)
                        throw Refuse("manufacturing_attribution_history_incomplete");
                }
            }
        }
    }

    private async Task<(JournalRow Header, JournalLineRow Debit, JournalLineRow Credit)> ActualJournal(Guid id, Guid sourceEventId,
        Guid captured, Func<CancellationToken, Task> freshFence, CancellationToken token)
    {
        var header = await db.Journals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, token);
        await Fence(captured, freshFence, token);
        var lines = await db.JournalLines.AsNoTracking().Where(x => x.JournalId == id).OrderBy(x => x.LineNumber).ToListAsync(token);
        await Fence(captured, freshFence, token);
        var reversed = await db.Journals.AnyAsync(x => x.LinkedJournalId == id && x.Kind == (int)JournalKind.Reversal, token);
        await Fence(captured, freshFence, token);
        var reversing = await db.JournalReversalProposals.AnyAsync(x => x.SourceJournalId == id && x.Status == 1, token);
        await Fence(captured, freshFence, token);
        if (header is null || header.CompanyId != Company() || header.SourceId != sourceEventId
            || header.Kind != (int)JournalKind.Original || header.PostingPurpose != ManufacturingCostDifferenceInboxV1.PostingPurpose
            || lines.Count != 2 || lines[0].LineNumber != 1 || lines[1].LineNumber != 2 || lines[0].Debit <= 0
            || lines[0].Credit != 0 || lines[1].Debit != 0 || lines[1].Credit != lines[0].Debit || lines[0].AccountId == lines[1].AccountId
            || reversed || reversing)
            throw Refuse("manufacturing_attribution_posted_journal_unavailable");
        return (header, lines[0], lines[1]);
    }
    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw Refuse("manufacturing_attribution_transaction_required");
    }
    private Guid Company() => company.IsResolved && company.CompanyId != Guid.Empty ? company.CompanyId : throw Refuse("company_context_mismatch");
    private async Task Fence(Guid captured, Func<CancellationToken, Task> fence, CancellationToken token)
    { ArgumentNullException.ThrowIfNull(fence); await fence(token); if (Company() != captured) throw Refuse("company_context_mismatch"); }
    private static T Decode<T>(string text) => JsonSerializer.Deserialize<T>(text, Json) ?? throw Refuse("manufacturing_attribution_proof_unreadable");
    private static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    private static string Hash(string text) => AccountingInventoryCostSourceValidationV1.Sha256(text);
    private static string Text(decimal value) => value.ToString("0.############################", CultureInfo.InvariantCulture);
    private static AccountingStoreException Refuse(string code) => new(code, "Kaydedilmiş üretim maliyeti sevk payı kanıtı bekleniyor.");
}
