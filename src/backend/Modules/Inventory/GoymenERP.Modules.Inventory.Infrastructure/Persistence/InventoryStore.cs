using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Opening;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// The Inventory store of the server-resolved company (INV-2). Every write is one save: the
/// stock fact, the balances it changes, the label versions it bumps and its audit event commit
/// together or not at all.
/// <list type="bullet">
/// <item><b>Lot (DEC-PRD-003):</b> a new label names a lot of this company and of its product; a
/// first arrival stores the lot and its labels in one save (<see cref="SaveLotAsync"/>). A lot's
/// facts never change here.</item>
/// <item><b>Company:</b> a record of any other company than the resolved context is refused
/// before anything is read (<see cref="InventoryStoreCodes.CompanyContextMismatch"/>); every read
/// runs under the tenant filter.</item>
/// <item><b>Replay (R3):</b> the movement id is the idempotency identity. The same id with the same
/// canonical content is a replay with no second effect; with another content it is refused.</item>
/// <item><b>Source:</b> one source (or source line) causes at most one movement of each kind —
/// checked first for a clear answer, and held by two filtered unique indexes under a race. A source
/// is answered whole or line by line per kind, never both: the source's grain row, written with its
/// first movement and referenced by every movement (<see cref="InventoryStoreCodes.SourceGrainMismatch"/>).</item>
/// <item><b>Label code:</b> one comparison, the code column's collation: another spelling of a stored
/// code is <see cref="InventoryStoreCodes.LabelCodeTaken"/> whether it comes with a new id (the
/// unique index) or with the same id (the same comparison, asked of SQL).</item>
/// <item><b>Race:</b> every write bumps the version of each label it touches. Two writes that read
/// the same stock of one piece cannot both commit; the second is refused whole
/// (<see cref="InventoryStoreCodes.ConcurrentChange"/>) and may be retried.</item>
/// <item><b>Rules:</b> the domain ledger decides, seeded from the stored balances: no negative
/// stock, one physical place per label, no re-receipt of a stock-bearing label.</item>
/// <item><b>Scale:</b> quantities are checked against the company's approved kilogram profile
/// (<see cref="InventoryKilogramScale"/>) on the movement's date; fail-closed.</item>
/// <item><b>Audit:</b> ADR-009 envelope from <see cref="InventoryCommandMetadata"/>, in the same save.</item>
/// </list>
/// </summary>
public sealed partial class InventoryStore(
    InventoryDbContext dbContext,
    ICompanyContext companyContext,
    IAuditEventCollector auditEvents,
    InventoryKilogramScale kilogramScale,
    TimeProvider timeProvider) : IInventoryLotStore, IInventoryLabelStore, IInventoryMovementStore, IInventoryOpeningStore,
    IInventoryPhysicalAllocationWriterV1, IInventoryLabelGenealogyWriterV1, IInventoryLabelGenealogyReaderV1
{
    private const string LotEntityType = "Inventory.MaterialLot";
    private const string LabelEntityType = "Inventory.MaterialLabel";
    private const string MovementEntityType = "Inventory.StockMovement";
    private const string OpeningCountEntityType = "Inventory.OpeningCount";
    private const int ActorMaximumLength = 128;
    private const int ReasonMaximumLength = 1000;
    private const int DocumentKindMaximumLength = 64;
    private const int CostScaleMaximum = 18;

    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public async Task<InventoryWriteOutcome> SaveLotAsync(MaterialLot lot, IReadOnlyList<MaterialLabel> labels,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lot);
        ArgumentNullException.ThrowIfNull(labels);
        RequireMetadata(metadata);
        RequireCompany(lot.CompanyId);
        RequireLotFits(lot);
        foreach (var label in labels)
        {
            ArgumentNullException.ThrowIfNull(label);
            if (label.LotId != lot.Id)
            {
                throw Refused(InventoryStoreCodes.LotNotFound, "Etiket bu kayıtta verilen lotu adlandırmıyor.");
            }

            RequireCompany(label.CompanyId);
            RequireLabelFits(label);
        }

        if (labels.Select(label => label.Id).Distinct().Count() != labels.Count)
        {
            throw Refused(InventoryStoreCodes.LabelConflict, "Aynı etiket bu kayıtta iki kez verildi.");
        }

        var recorded = false;
        var stored = await dbContext.Lots.AsNoTracking().SingleOrDefaultAsync(row => row.Id == lot.Id, cancellationToken);
        if (stored is null)
        {
            dbContext.Lots.Add(new LotRow
            {
                Id = lot.Id,
                CompanyId = lot.CompanyId,
                ProductId = lot.ProductId,
                HeatNumber = lot.HeatNumber,
                CoilNumber = lot.CoilNumber,
                Version = 1,
                CreatedAtUtc = timeProvider.GetUtcNow(),
                CreatedBy = metadata.ActorId.Trim(),
            });
            Audit(metadata, lot.CompanyId, LotEntityType, lot.Id, "inventory.lot.registered.v1", "Register",
                versionBefore: null, versionAfter: 1, before: "{}", after: LotAudit(lot), "Lot kaydı");
            recorded = true;
        }
        else if (!SameLot(stored, lot))
        {
            throw Refused(InventoryStoreCodes.LotConflict, "Aynı lot kimliği farklı lot bilgileriyle verildi.");
        }

        foreach (var label in labels)
        {
            // The lot of this call: just staged or already stored with the same facts (checked above).
            recorded |= await StageLabelAsync(label, metadata, lot, cancellationToken);
        }

        if (!recorded)
        {
            return InventoryWriteOutcome.Replayed;
        }

        await SaveAsync(cancellationToken);
        return InventoryWriteOutcome.Recorded;
    }

    public async Task<MaterialLot?> FindLotAsync(Guid lotId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.Lots.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == lotId, cancellationToken);
        return row is null ? null : ToLot(row);
    }

    public async Task<InventoryWriteOutcome> SaveLabelAsync(MaterialLabel label, InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(label);
        RequireMetadata(metadata);
        RequireCompany(label.CompanyId);
        RequireLabelFits(label);
        if (!await StageLabelAsync(label, metadata, lotOfThisCall: null, cancellationToken))
        {
            return InventoryWriteOutcome.Replayed;
        }

        await SaveAsync(cancellationToken);
        return InventoryWriteOutcome.Recorded;
    }

    /// <summary>
    /// Adds what saving <paramref name="label"/> changes to the context, without saving: a new label
    /// (its lot of the same product), or the prints and attachments that extend a stored one. False
    /// when nothing is new (a replay). The lot is <paramref name="lotOfThisCall"/> when the caller is
    /// saving it in the same call; otherwise it is read through the tenant-filtered query — never from
    /// the change tracker, which the tenant filter does not cover.
    /// </summary>
    private async Task<bool> StageLabelAsync(MaterialLabel label, InventoryCommandMetadata metadata,
        MaterialLot? lotOfThisCall, CancellationToken cancellationToken)
    {
        var stored = await dbContext.Labels.SingleOrDefaultAsync(row => row.Id == label.Id, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (stored is null)
        {
            RequireNewLabelFits(label);
            var lotProductId = lotOfThisCall is not null && lotOfThisCall.Id == label.LotId
                ? lotOfThisCall.ProductId
                : await dbContext.Lots.AsNoTracking().Where(row => row.Id == label.LotId)
                    .Select(row => (Guid?)row.ProductId).SingleOrDefaultAsync(cancellationToken)
                    ?? throw Refused(InventoryStoreCodes.LotNotFound, "Etiketin lotu bu şirkette kayıtlı değil.");
            if (lotProductId != label.ProductId)
            {
                throw Refused(InventoryStoreCodes.LotProductMismatch, "Etiket ile lotu farklı ürünleri adlandırıyor.");
            }

            dbContext.Labels.Add(new LabelRow
            {
                Id = label.Id,
                CompanyId = label.CompanyId,
                Code = label.Code,
                ProductId = label.ProductId,
                ProductVersion = label.ProductVersion,
                LotId = label.LotId,
                HeatNumber = label.HeatNumber,
                CoilNumber = label.CoilNumber,
                RequiredDocumentsJson = JsonSerializer.Serialize(label.RequiredDocuments.Order(StringComparer.Ordinal), Json),
                Version = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
            AddPrints(label, fromSequence: 1);
            AddDocuments(label, fromSequence: 1);
            Audit(metadata, label.CompanyId, LabelEntityType, label.Id, "inventory.label.registered.v1", "Register",
                versionBefore: null, versionAfter: 1, before: "{}", after: LabelAudit(label), "Etiket kaydı");
            return true;
        }

        if (!string.Equals(stored.Code, label.Code, StringComparison.Ordinal)
            && await dbContext.Labels.AnyAsync(row => row.Id == label.Id && row.Code == label.Code, cancellationToken))
        {
            // The column's collation calls it the stored code, written another way: the same answer
            // a new label with this spelling gets from the unique index.
            throw Refused(InventoryStoreCodes.LabelCodeTaken, "Bu kod kayıtlı bir etiket kodunun başka yazımı.");
        }

        if (!SameIdentity(stored, label))
        {
            throw Refused(InventoryStoreCodes.LabelConflict, "Aynı etiket kimliği farklı kimlik bilgileriyle verildi.");
        }

        var storedPrints = await dbContext.LabelPrints.Where(row => row.LabelId == label.Id)
            .OrderBy(row => row.Sequence).ToListAsync(cancellationToken);
        var storedDocuments = await dbContext.LabelDocuments.Where(row => row.LabelId == label.Id)
            .OrderBy(row => row.Sequence).ToListAsync(cancellationToken);
        if (storedPrints.Count > label.Prints.Count || storedDocuments.Count > label.DocumentAttachments.Count
            || storedPrints.Where((row, index) => ToPrint(row) != label.Prints[index]).Any()
            || storedDocuments.Where((row, index) => ToAttachment(row) != label.DocumentAttachments[index]).Any())
        {
            throw Refused(InventoryStoreCodes.LabelConflict, "Etiketin baskı ve belge geçmişi kayıtlı geçmişi uzatmıyor.");
        }

        if (storedPrints.Count == label.Prints.Count && storedDocuments.Count == label.DocumentAttachments.Count)
        {
            return false;
        }

        AddPrints(label, fromSequence: storedPrints.Count + 1);
        AddDocuments(label, fromSequence: storedDocuments.Count + 1);
        var before = stored.Version;
        stored.Version = checked(before + 1);
        stored.UpdatedAtUtc = now;
        Audit(metadata, label.CompanyId, LabelEntityType, label.Id, "inventory.label.history-extended.v1", "Extend",
            versionBefore: before, versionAfter: stored.Version,
            before: JsonSerializer.Serialize(new { prints = storedPrints.Count, documents = storedDocuments.Count }, Json),
            after: LabelAudit(label), "Etiket baskı/belge geçmişi");
        return true;
    }

    public async Task<MaterialLabel?> FindLabelAsync(Guid labelId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var rows = await dbContext.Labels.AsNoTracking().Where(row => row.Id == labelId).ToListAsync(cancellationToken);
        return rows.Count == 0 ? null : (await RestoreLabelsAsync(rows, cancellationToken))[labelId];
    }

    public async Task<InventoryWriteOutcome> RecordMovementAsync(StockMovement movement, InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var outcome = await StageMovementAsync(movement, metadata, recordAudit: true, cancellationToken);
        if (outcome == InventoryWriteOutcome.Recorded)
        {
            await SaveAsync(cancellationToken);
        }

        return outcome;
    }

    /// <summary>
    /// Stages one movement into this context without saving it. Only an enlisted owner participant
    /// may use this to flush the movement with its allocation, command and outbox rows.
    /// </summary>
    internal async Task<InventoryWriteOutcome> StageMovementAsync(StockMovement movement,
        InventoryCommandMetadata metadata, bool recordAudit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(movement);
        RequireMetadata(metadata);
        RequireCompany(movement.CompanyId);
        RequireCostFits(movement.Cost);

        var payloadHash = PayloadHash(movement);
        var existing = await dbContext.Movements.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == movement.Id, cancellationToken);
        if (existing is not null)
        {
            return string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal)
                ? InventoryWriteOutcome.Replayed
                : throw Refused(InventoryStoreCodes.MovementIdConflict, "Aynı hareket kimliği farklı içerikle verildi.");
        }

        if (await SourceTakenAsync(movement, cancellationToken))
        {
            throw Refused(InventoryStoreCodes.DuplicateSource, "Bu kaynak bu türde zaten bir hareket doğurdu.");
        }

        var grain = (int)movement.Source.Grain;
        var storedGrain = await SourceGrainAsync(movement, cancellationToken);
        if (storedGrain is { } fixedGrain && fixedGrain != grain)
        {
            throw Refused(InventoryStoreCodes.SourceGrainMismatch,
                "Bu kaynak bu türde başka ayrıntı düzeyinde (bütün/satır) karşılandı; aynı miktar iki kez sayılmaz.");
        }

        var scale = await kilogramScale.CheckAsync(movement.CompanyId, DateOnly.FromDateTime(movement.OccurredAtUtc.UtcDateTime),
            movement.Lines.Select(line => line.Quantity), cancellationToken);
        if (!scale.IsAccepted)
        {
            throw Refused(scale.Code!, "Miktar şirketin onaylı kilogram profiline uymuyor.");
        }

        var labelIds = movement.Lines.Select(line => line.Position.LabelId).Distinct().ToArray();
        var labels = await dbContext.Labels.Where(row => labelIds.Contains(row.Id)).ToListAsync(cancellationToken);
        foreach (var line in movement.Lines)
        {
            var label = labels.SingleOrDefault(row => row.Id == line.Position.LabelId)
                ?? throw Refused(InventoryStoreCodes.LabelNotRegistered, "Hareket kayıtlı olmayan bir etiketi adlandırıyor.");
            if (label.ProductId != line.Position.ProductId)
            {
                throw Refused(InventoryStoreCodes.LabelConflict, "Hareketteki etiket kayıtlı etiketle aynı ürün değil.");
            }
        }

        var balanceRows = await dbContext.Balances.Where(row => labelIds.Contains(row.LabelId)).ToListAsync(cancellationToken);
        var labelObjects = labels.ToDictionary(row => row.Id, IdentityOnly);
        var ledger = StockLedger.FromStoredBalances(balanceRows.Select(row =>
            new StockBalance(Position(row.CompanyId, row.OwnerKind, row.OwnerId, row.CustodianKind, row.CustodianId,
                row.LocationId, labelObjects[row.LabelId], row.Block), Kilograms.Of(row.QuantityKg))));

        // The domain decides; nothing below runs for a refused movement.
        ledger.Apply(movement);

        foreach (var position in movement.Lines.Select(line => line.Position).Distinct())
        {
            var quantity = ledger.QuantityAt(position).Value;
            var row = balanceRows.SingleOrDefault(candidate => Matches(candidate, position));
            if (row is null)
            {
                dbContext.Balances.Add(new BalanceRow
                {
                    Id = Guid.NewGuid(),
                    CompanyId = position.CompanyId,
                    OwnerKind = (int)position.Owner.Kind,
                    OwnerId = position.Owner.Id,
                    CustodianKind = (int)position.Custodian.Kind,
                    CustodianId = position.Custodian.Id,
                    LocationId = position.LocationId,
                    LabelId = position.LabelId,
                    ProductId = position.ProductId,
                    Block = (int)position.Block,
                    QuantityKg = quantity,
                });
            }
            else
            {
                row.QuantityKg = quantity;
            }
        }

        var now = timeProvider.GetUtcNow();
        foreach (var label in labels)
        {
            label.Version = checked(label.Version + 1);
            label.UpdatedAtUtc = now;
        }

        if (storedGrain is null)
        {
            dbContext.SourceGrains.Add(new SourceGrainRow
            {
                CompanyId = movement.CompanyId,
                Kind = (int)movement.Kind,
                SourceKind = movement.Source.Kind,
                SourceId = movement.Source.Id,
                Grain = grain,
            });
        }

        dbContext.Movements.Add(new MovementRow
        {
            Id = movement.Id,
            CompanyId = movement.CompanyId,
            Kind = (int)movement.Kind,
            OccurredAtUtc = movement.OccurredAtUtc,
            SourceKind = movement.Source.Kind,
            SourceId = movement.Source.Id,
            SourceLineId = movement.Source.LineId,
            SourceGrain = grain,
            CostStatus = (int)movement.Cost.Status,
            CostAmount = movement.Cost.Amount,
            CostCurrencyId = movement.Cost.CurrencyId,
            PayloadHash = payloadHash,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        });
        var number = 0;
        foreach (var line in movement.Lines)
        {
            var position = line.Position;
            dbContext.MovementLines.Add(new MovementLineRow
            {
                CompanyId = movement.CompanyId,
                MovementId = movement.Id,
                LineNumber = ++number,
                Direction = (int)line.Direction,
                OwnerKind = (int)position.Owner.Kind,
                OwnerId = position.Owner.Id,
                CustodianKind = (int)position.Custodian.Kind,
                CustodianId = position.Custodian.Id,
                LocationId = position.LocationId,
                LabelId = position.LabelId,
                ProductId = position.ProductId,
                Block = (int)position.Block,
                QuantityKg = line.Quantity.Value,
            });
        }

        if (recordAudit)
        {
            Audit(metadata, movement.CompanyId, MovementEntityType, movement.Id, "inventory.movement.recorded.v1", "Record",
                versionBefore: null, versionAfter: 1, before: "{}", after: MovementAudit(movement, payloadHash),
                $"Stok hareketi ({movement.Kind})");
        }

        return InventoryWriteOutcome.Recorded;
    }

    internal void AuditDefiniteShipment(InventoryCommandMetadata metadata, Guid companyId, Guid allocationId,
        long versionBefore, long versionAfter, string before, string after)
    {
        Audit(metadata, companyId, "Inventory.StockAllocation", allocationId,
            InventoryDefiniteShipmentOutboxV1.EventType, "ConsumeForDefiniteShipment",
            versionBefore, versionAfter, before, after, "Kesin sevk stok çıkışı ve tahsis tüketimi");
    }

    /// <summary>
    /// INV-OPEN-1 (decision A, #66 5738113623 §2): the opening stock of one physical count, in ONE save —
    /// the count and its lines, each piece's lot (new, or stored with the same facts), its NEW label, its one
    /// receipt with the resulting balance, and the audit events. No second approver; the recorder is the
    /// audited actor. Book = count at opening: every balance written here is exactly its counted kilograms.
    /// </summary>
    public async Task<InventoryWriteOutcome> RecordAsync(OpeningCount count, InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(count);
        RequireMetadata(metadata);
        RequireCompany(count.CompanyId);
        foreach (var line in count.Lines)
        {
            RequireLotFits(line.Lot);
            RequireLabelFits(line.Label);
        }

        // R3: the count id is the identity; the same content is a replay with no second effect.
        var hash = OpeningHash(count);
        var stored = await dbContext.OpeningCounts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == count.Id, cancellationToken);
        if (stored is not null)
        {
            if (string.Equals(stored.ContentHash, hash, StringComparison.Ordinal))
            {
                return InventoryWriteOutcome.Replayed;
            }

            if (string.Equals(stored.ContentHash, LegacyOpeningHash(count), StringComparison.Ordinal)
                && await IsLegacyOpeningWithoutProductVersionAsync(count.Id, count.Lines.Count, cancellationToken))
            {
                return InventoryWriteOutcome.Replayed;
            }

            throw Refused(InventoryStoreCodes.OpeningConflict, "Aynı açılış sayımı kimliği farklı içerikle verildi.");
        }

        foreach (var line in count.Lines)
        {
            RequireNewLabelFits(line.Label);
        }

        // Counted kilograms against the company's approved kg profile on the counted-on date (fail-closed).
        var scale = await kilogramScale.CheckAsync(count.CompanyId, count.CountedOn,
            count.Lines.Select(line => line.Quantity), cancellationToken);
        if (!scale.IsAccepted)
        {
            throw Refused(scale.Code!, "Sayılan miktar şirketin onaylı kilogram profiline uymuyor.");
        }

        // An opening registers NEW pieces only: a stored label is never opened again.
        var labelIds = count.Lines.Select(line => line.Label.Id).ToArray();
        var knownLabels = await dbContext.Labels.AsNoTracking().Where(row => labelIds.Contains(row.Id))
            .Select(row => row.Id).ToListAsync(cancellationToken);
        if (knownLabels.Count > 0)
        {
            if (await dbContext.Balances.AsNoTracking()
                    .AnyAsync(row => knownLabels.Contains(row.LabelId) && row.QuantityKg != 0m, cancellationToken))
            {
                // Opening is never an adjustment: a book/count difference is ST-05 with its second approval.
                throw new InventoryRuleException(InventoryRuleCodes.LabelAlreadyInStock,
                    "Stok taşıyan bir etiket açılış sayımıyla yeniden girilemez; fark sayımı ayrı bir süreçtir.");
            }

            throw Refused(InventoryStoreCodes.LabelConflict, "Açılış sayımı yalnız yeni parça kaydeder; bu etiket kayıtlı.");
        }

        var now = timeProvider.GetUtcNow();
        foreach (var lot in count.Lines.Select(line => line.Lot).DistinctBy(lot => lot.Id))
        {
            var storedLot = await dbContext.Lots.AsNoTracking().SingleOrDefaultAsync(row => row.Id == lot.Id, cancellationToken);
            if (storedLot is null)
            {
                dbContext.Lots.Add(new LotRow
                {
                    Id = lot.Id,
                    CompanyId = lot.CompanyId,
                    ProductId = lot.ProductId,
                    HeatNumber = lot.HeatNumber,
                    CoilNumber = lot.CoilNumber,
                    Version = 1,
                    CreatedAtUtc = now,
                    CreatedBy = metadata.ActorId.Trim(),
                });
                Audit(metadata, lot.CompanyId, LotEntityType, lot.Id, "inventory.lot.registered.v1", "Register",
                    versionBefore: null, versionAfter: 1, before: "{}", after: LotAudit(lot), "Lot kaydı (açılış sayımı)");
            }
            else if (!SameLot(storedLot, lot))
            {
                throw Refused(InventoryStoreCodes.LotConflict, "Aynı lot kimliği farklı lot bilgileriyle verildi.");
            }
        }

        var number = 0;
        foreach (var line in count.Lines)
        {
            var label = line.Label;
            dbContext.Labels.Add(new LabelRow
            {
                Id = label.Id,
                CompanyId = label.CompanyId,
                Code = label.Code,
                ProductId = label.ProductId,
                ProductVersion = label.ProductVersion,
                LotId = label.LotId,
                HeatNumber = label.HeatNumber,
                CoilNumber = label.CoilNumber,
                RequiredDocumentsJson = JsonSerializer.Serialize(label.RequiredDocuments.Order(StringComparer.Ordinal), Json),
                Version = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
            AddPrints(label, fromSequence: 1);
            AddDocuments(label, fromSequence: 1);
            Audit(metadata, label.CompanyId, LabelEntityType, label.Id, "inventory.label.registered.v1", "Register",
                versionBefore: null, versionAfter: 1, before: "{}", after: LabelAudit(label), "Etiket kaydı (açılış sayımı)");

            // The one receipt of the piece: the domain ledger decides (a new piece has no stored balance).
            var movement = count.ReceiptOf(line);
            new StockLedger().Apply(movement);
            var position = count.PositionOf(line);
            var grain = (int)movement.Source.Grain;
            if (number == 0)
            {
                dbContext.SourceGrains.Add(new SourceGrainRow
                {
                    CompanyId = count.CompanyId,
                    Kind = (int)movement.Kind,
                    SourceKind = movement.Source.Kind,
                    SourceId = movement.Source.Id,
                    Grain = grain,
                });
            }

            var payloadHash = PayloadHash(movement);
            dbContext.Movements.Add(new MovementRow
            {
                Id = movement.Id,
                CompanyId = movement.CompanyId,
                Kind = (int)movement.Kind,
                OccurredAtUtc = movement.OccurredAtUtc,
                SourceKind = movement.Source.Kind,
                SourceId = movement.Source.Id,
                SourceLineId = movement.Source.LineId,
                SourceGrain = grain,
                CostStatus = (int)movement.Cost.Status,
                CostAmount = movement.Cost.Amount,
                CostCurrencyId = movement.Cost.CurrencyId,
                PayloadHash = payloadHash,
                RecordedAtUtc = now,
                RecordedBy = metadata.ActorId.Trim(),
                CorrelationId = metadata.CorrelationId.Trim(),
            });
            dbContext.MovementLines.Add(new MovementLineRow
            {
                CompanyId = position.CompanyId,
                MovementId = movement.Id,
                LineNumber = 1,
                Direction = (int)StockDirection.In,
                OwnerKind = (int)position.Owner.Kind,
                OwnerId = position.Owner.Id,
                CustodianKind = (int)position.Custodian.Kind,
                CustodianId = position.Custodian.Id,
                LocationId = position.LocationId,
                LabelId = position.LabelId,
                ProductId = position.ProductId,
                Block = (int)position.Block,
                QuantityKg = line.Quantity.Value,
            });
            dbContext.Balances.Add(new BalanceRow
            {
                Id = Guid.NewGuid(),
                CompanyId = position.CompanyId,
                OwnerKind = (int)position.Owner.Kind,
                OwnerId = position.Owner.Id,
                CustodianKind = (int)position.Custodian.Kind,
                CustodianId = position.Custodian.Id,
                LocationId = position.LocationId,
                LabelId = position.LabelId,
                ProductId = position.ProductId,
                Block = (int)position.Block,
                QuantityKg = line.Quantity.Value,
            });
            Audit(metadata, movement.CompanyId, MovementEntityType, movement.Id, "inventory.movement.recorded.v1", "Record",
                versionBefore: null, versionAfter: 1, before: "{}", after: MovementAudit(movement, payloadHash),
                "Açılış sayımı girişi");

            dbContext.OpeningCountLines.Add(new OpeningCountLineRow
            {
                CompanyId = count.CompanyId,
                CountId = count.Id,
                LineNumber = ++number,
                LineId = line.Id,
                LabelId = label.Id,
                LotId = line.Lot.Id,
                QuantityKg = line.Quantity.Value,
            });
        }

        dbContext.OpeningCounts.Add(new OpeningCountRow
        {
            Id = count.Id,
            CompanyId = count.CompanyId,
            LocationId = count.LocationId,
            CountedOn = count.CountedOn,
            SheetReference = count.SheetReference,
            ContentHash = hash,
            RecordedAtUtc = now,
            RecordedBy = metadata.ActorId.Trim(),
            CorrelationId = metadata.CorrelationId.Trim(),
        });
        Audit(metadata, count.CompanyId, OpeningCountEntityType, count.Id, "inventory.opening-count.recorded.v1", "Record",
            versionBefore: null, versionAfter: 1, before: "{}", after: OpeningAudit(count, hash), "Açılış sayımı");
        await SaveAsync(cancellationToken);
        return InventoryWriteOutcome.Recorded;
    }

    public async Task<OpeningCount?> ReadCountAsync(Guid countId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var row = await dbContext.OpeningCounts.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == countId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var lines = await dbContext.OpeningCountLines.AsNoTracking().Where(line => line.CountId == countId)
            .OrderBy(line => line.LineNumber).ToListAsync(cancellationToken);
        var labelIds = lines.Select(line => line.LabelId).ToArray();
        var labels = await RestoreLabelsAsync(
            await dbContext.Labels.AsNoTracking().Where(label => labelIds.Contains(label.Id)).ToListAsync(cancellationToken),
            cancellationToken);
        var lotIds = lines.Select(line => line.LotId).Distinct().ToArray();
        var lots = (await dbContext.Lots.AsNoTracking().Where(lot => lotIds.Contains(lot.Id)).ToListAsync(cancellationToken))
            .ToDictionary(lot => lot.Id, ToLot);
        return new OpeningCount(row.Id, row.CompanyId, row.LocationId, row.CountedOn, row.SheetReference,
            lines.Select(line => new OpeningCountLine(line.LineId, lots[line.LotId], labels[line.LabelId],
                Kilograms.Positive(line.QuantityKg))));
    }

    public async Task<IReadOnlyList<StockBalance>> ReadBalancesAsync(Guid labelId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var label = await dbContext.Labels.AsNoTracking().SingleOrDefaultAsync(row => row.Id == labelId, cancellationToken);
        if (label is null)
        {
            return [];
        }

        var identity = IdentityOnly(label);
        var rows = await dbContext.Balances.AsNoTracking()
            .Where(row => row.LabelId == labelId && row.QuantityKg != 0m)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new StockBalance(Position(row.CompanyId, row.OwnerKind, row.OwnerId, row.CustodianKind,
            row.CustodianId, row.LocationId, identity, row.Block), Kilograms.Of(row.QuantityKg)))];
    }

    public async Task<IReadOnlyList<StockMovement>> ReadMovementsAsync(Guid labelId, CancellationToken cancellationToken = default)
    {
        RequireResolvedCompany();
        var movementIds = await dbContext.MovementLines.AsNoTracking()
            .Where(line => line.LabelId == labelId)
            .Select(line => line.MovementId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var movements = await dbContext.Movements.AsNoTracking()
            .Where(row => movementIds.Contains(row.Id))
            .OrderBy(row => row.RecordedAtUtc).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var lines = await dbContext.MovementLines.AsNoTracking()
            .Where(line => movementIds.Contains(line.MovementId))
            .ToListAsync(cancellationToken);
        var touchedLabels = lines.Select(line => line.LabelId).Distinct().ToArray();
        var labels = (await dbContext.Labels.AsNoTracking().Where(row => touchedLabels.Contains(row.Id)).ToListAsync(cancellationToken))
            .ToDictionary(row => row.Id, IdentityOnly);

        return [.. movements.Select(row => new StockMovement(row.Id, row.CompanyId, (StockMovementKind)row.Kind, row.OccurredAtUtc,
            new StockSourceReference(row.SourceKind, row.SourceId, row.SourceLineId),
            lines.Where(line => line.MovementId == row.Id).OrderBy(line => line.LineNumber).Select(line => new StockMovementLine(
                Position(line.CompanyId, line.OwnerKind, line.OwnerId, line.CustodianKind, line.CustodianId, line.LocationId,
                    labels[line.LabelId], line.Block),
                (StockDirection)line.Direction, Kilograms.Positive(line.QuantityKg))),
            Cost(row)))];
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        // A primary-key collision on a lot or label id (#324 P3-1) is either two first arrivals of this
        // company racing — a retry reads the stored record — or an id another company holds, invisible
        // here under the tenant filter: a retry would collide forever, so that is a conflict with no
        // retry advice, and nothing about the other record is disclosed. The colliding id is the one SQL
        // names as the duplicate key in that key's own error; if it cannot be read, the answer stays the retryable one.
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw Refused(InventoryStoreCodes.ConcurrentChange,
                "Aynı parçanın stoğu başka bir yazmayla değişti; hiçbir şey kaydedilmedi.", exception);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            var code = sql.Message switch
            {
                var message when message.Contains("UX_Movements_Source", StringComparison.Ordinal) => InventoryStoreCodes.DuplicateSource,
                var message when message.Contains("IX_Labels_CompanyId_Code", StringComparison.Ordinal) => InventoryStoreCodes.LabelCodeTaken,
                var message when message.Contains("PK_Movements", StringComparison.Ordinal) => InventoryStoreCodes.ConcurrentChange,
                var message when message.Contains("'PK_Lots'", StringComparison.Ordinal) =>
                    DuplicateKey(Errors(sql), "'PK_Lots'") is { } lotId
                    && !await dbContext.Lots.AsNoTracking().AnyAsync(row => row.Id == lotId, cancellationToken)
                        ? InventoryStoreCodes.LotConflict
                        : InventoryStoreCodes.ConcurrentChange,
                var message when message.Contains("'PK_Labels'", StringComparison.Ordinal) =>
                    DuplicateKey(Errors(sql), "'PK_Labels'") is { } labelId
                    && !await dbContext.Labels.AsNoTracking().AnyAsync(row => row.Id == labelId, cancellationToken)
                        ? InventoryStoreCodes.LabelConflict
                        : InventoryStoreCodes.ConcurrentChange,
                // The same for an opening count id (SİPARİŞ #337 P3): raced here, or held by another company.
                var message when message.Contains("'PK_OpeningCounts'", StringComparison.Ordinal) =>
                    DuplicateKey(Errors(sql), "'PK_OpeningCounts'") is { } countId
                    && !await dbContext.OpeningCounts.AsNoTracking().AnyAsync(row => row.Id == countId, cancellationToken)
                        ? InventoryStoreCodes.OpeningConflict
                        : InventoryStoreCodes.ConcurrentChange,
                // Two first movements of one source raced for its grain: retried, the loser reads it.
                var message when message.Contains("PK_SourceGrains", StringComparison.Ordinal) => InventoryStoreCodes.ConcurrentChange,
                _ => InventoryStoreCodes.ConcurrentChange,
            };
            throw Refused(code, code is InventoryStoreCodes.LotConflict or InventoryStoreCodes.LabelConflict
                or InventoryStoreCodes.OpeningConflict
                ? "Bu kimlik bu şirkette kullanılamıyor; hiçbir şey kaydedilmedi, yeniden denemek aynı cevabı verir."
                : "Kayıt tekillik kuralına takıldı; hiçbir şey kaydedilmedi.", exception);
        }
    }

    /// <summary>
    /// The duplicate key of one primary key: read from that key's OWN error, never from the combined message.
    /// One save can collide on several keys (a lot and its label in the same batch, #333 SİPARİŞ P3-1); the
    /// combined message then carries several GUIDs. Exactly one duplicate-key error (2601/2627) naming
    /// <paramref name="constraint"/> must exist, and its message must carry exactly one GUID (a single-column
    /// key); anything else is null, and the answer stays the retryable one.
    /// </summary>
    internal static Guid? DuplicateKey(IEnumerable<(int Number, string Message)> errors, string constraint)
    {
        var own = errors.Where(error => error.Number is 2601 or 2627
            && error.Message.Contains(constraint, StringComparison.Ordinal)).ToArray();
        if (own.Length != 1)
        {
            return null;
        }

        var matches = GuidPattern().Matches(own[0].Message);
        return matches.Count == 1 && Guid.TryParseExact(matches[0].Value, "D", out var id) ? id : null;
    }

    private static IEnumerable<(int Number, string Message)> Errors(SqlException sql) =>
        sql.Errors.Cast<SqlError>().Select(error => (error.Number, error.Message));

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    [GeneratedRegex("[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex GuidPattern();

    private async Task<bool> SourceTakenAsync(StockMovement movement, CancellationToken cancellationToken)
    {
        var source = movement.Source;
        var kind = (int)movement.Kind;
        return source.LineId is { } lineId
            ? await dbContext.Movements.AsNoTracking().AnyAsync(row => row.Kind == kind && row.SourceKind == source.Kind
                && row.SourceId == source.Id && row.SourceLineId == lineId, cancellationToken)
            : await dbContext.Movements.AsNoTracking().AnyAsync(row => row.Kind == kind && row.SourceKind == source.Kind
                && row.SourceId == source.Id && row.SourceLineId == null, cancellationToken);
    }

    private async Task<int?> SourceGrainAsync(StockMovement movement, CancellationToken cancellationToken)
    {
        var source = movement.Source;
        var kind = (int)movement.Kind;
        return await dbContext.SourceGrains.AsNoTracking()
            .Where(row => row.Kind == kind && row.SourceKind == source.Kind && row.SourceId == source.Id)
            .Select(row => (int?)row.Grain)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<Dictionary<Guid, MaterialLabel>> RestoreLabelsAsync(IReadOnlyList<LabelRow> rows,
        CancellationToken cancellationToken)
    {
        var ids = rows.Select(row => row.Id).ToArray();
        var prints = await dbContext.LabelPrints.AsNoTracking().Where(row => ids.Contains(row.LabelId)).ToListAsync(cancellationToken);
        var documents = await dbContext.LabelDocuments.AsNoTracking().Where(row => ids.Contains(row.LabelId))
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(row => row.Id, row => MaterialLabel.Restore(row.Id, row.CompanyId, row.Code, row.ProductId,
            row.LotId, row.ProductVersion, JsonSerializer.Deserialize<string[]>(row.RequiredDocumentsJson, Json)
                ?? throw new InvalidOperationException("Kayıtlı etiketin belge gereksinimi okunamadı."),
            row.HeatNumber, row.CoilNumber,
            prints.Where(print => print.LabelId == row.Id).OrderBy(print => print.Sequence).Select(ToPrint),
            documents.Where(document => document.LabelId == row.Id).OrderBy(document => document.Sequence).Select(ToAttachment)));
    }

    private void AddPrints(MaterialLabel label, int fromSequence)
    {
        foreach (var print in label.Prints.Where(print => print.Sequence >= fromSequence))
        {
            dbContext.LabelPrints.Add(new LabelPrintRow
            {
                CompanyId = label.CompanyId,
                LabelId = label.Id,
                Sequence = print.Sequence,
                PrintedAtUtc = print.PrintedAtUtc,
                PrintedBy = print.PrintedBy,
                Reason = print.Reason,
            });
        }
    }

    private void AddDocuments(MaterialLabel label, int fromSequence)
    {
        var sequence = 0;
        foreach (var attachment in label.DocumentAttachments)
        {
            if (++sequence < fromSequence)
            {
                continue;
            }

            dbContext.LabelDocuments.Add(new LabelDocumentRow
            {
                CompanyId = label.CompanyId,
                LabelId = label.Id,
                Sequence = sequence,
                Kind = attachment.Kind,
                AttachedAtUtc = attachment.AttachedAtUtc,
                AttachedBy = attachment.AttachedBy,
            });
        }
    }

    private void Audit(InventoryCommandMetadata metadata, Guid companyId, string entityType, Guid entityId, string eventType,
        string operation, long? versionBefore, long? versionAfter, string before, string after, string reason)
    {
        auditEvents.Enqueue(new AuditEventDraft(
            companyId,
            eventType,
            entityType,
            entityId.ToString("D"),
            operation,
            versionBefore,
            versionAfter,
            AuditActorTypes.User,
            metadata.ActorId,
            metadata.PermissionCode,
            metadata.RoleCodes,
            before,
            after,
            reason,
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));
    }

    private void RequireCompany(Guid companyId)
    {
        if (!companyContext.IsResolved || companyContext.CompanyId != companyId)
        {
            throw Refused(InventoryStoreCodes.CompanyContextMismatch, "Kayıt aktif şirket bağlamının şirketine ait değil.");
        }
    }

    private void RequireResolvedCompany()
    {
        if (!companyContext.IsResolved)
        {
            throw Refused(InventoryStoreCodes.CompanyContextMismatch, "Aktif şirket bağlamı olmadan stok okunmaz.");
        }
    }

    private static void RequireMetadata(InventoryCommandMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(metadata.ActorId) || string.IsNullOrWhiteSpace(metadata.PermissionCode)
            || string.IsNullOrWhiteSpace(metadata.CorrelationId) || string.IsNullOrWhiteSpace(metadata.IdempotencyKey)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel) || metadata.RoleCodes is null)
        {
            throw new ArgumentException("Stok yazması aktör, izin, correlation, idempotency ve kanal bilgisi ister.", nameof(metadata));
        }

        if (metadata.ActorId.Trim().Length > ActorMaximumLength || metadata.CorrelationId.Trim().Length > ActorMaximumLength)
        {
            throw Refused(InventoryStoreCodes.ValueDoesNotFit, "Aktör ya da correlation değeri kayıt alanına sığmıyor.");
        }
    }

    private static void RequireLabelFits(MaterialLabel label)
    {
        var tooLong = (label.HeatNumber?.Length ?? 0) > 64 || (label.CoilNumber?.Length ?? 0) > 64
            || label.RequiredDocuments.Any(kind => kind.Length > DocumentKindMaximumLength)
            || label.Prints.Any(print => print.PrintedBy.Length > ActorMaximumLength || (print.Reason?.Length ?? 0) > ReasonMaximumLength)
            || label.DocumentAttachments.Any(document => document.Kind.Length > DocumentKindMaximumLength
                || document.AttachedBy.Length > ActorMaximumLength);
        if (tooLong)
        {
            throw Refused(InventoryStoreCodes.ValueDoesNotFit, "Etiketin bir metni kayıt alanına sığmıyor; kesilmez, reddedilir.");
        }
    }

    private static void RequireNewLabelFits(MaterialLabel label)
    {
        RequireLabelFits(label);
        if (label.ProductVersion is null)
        {
            throw Refused(InventoryStoreCodes.ValueDoesNotFit,
                "Etiket yeni bir stok etkisine kaydedilirken ürün sürümü zorunludur.");
        }
    }

    private async Task<bool> IsLegacyOpeningWithoutProductVersionAsync(Guid countId, int expectedLineCount,
        CancellationToken cancellationToken)
    {
        var labelIds = await dbContext.OpeningCountLines.AsNoTracking()
            .Where(row => row.CountId == countId)
            .Select(row => row.LabelId)
            .ToListAsync(cancellationToken);
        if (labelIds.Count != expectedLineCount || labelIds.Count == 0
            || labelIds.Distinct().Count() != labelIds.Count)
        {
            return false;
        }

        var productVersions = await dbContext.Labels.AsNoTracking()
            .Where(row => labelIds.Contains(row.Id))
            .Select(row => row.ProductVersion)
            .ToListAsync(cancellationToken);
        return productVersions.Count == labelIds.Count
            && productVersions.All(productVersion => productVersion is null);
    }

    private static void RequireLotFits(MaterialLot lot)
    {
        if ((lot.HeatNumber?.Length ?? 0) > MaterialLot.MaximumTraceLength
            || (lot.CoilNumber?.Length ?? 0) > MaterialLot.MaximumTraceLength)
        {
            throw Refused(InventoryStoreCodes.ValueDoesNotFit, "Lotun heat/coil numarası kayıt alanına sığmıyor; kesilmez, reddedilir.");
        }
    }

    private static void RequireCostFits(CostResult cost)
    {
        // Not a money policy: the stored column holds 18 decimals and 20 integer digits, and a value
        // that does not fit is refused rather than rounded by the database.
        if (cost.Amount is { } amount && (Scale(amount) > CostScaleMaximum || Math.Abs(amount) >= 100_000_000_000_000_000_000m))
        {
            throw Refused(InventoryStoreCodes.ValueDoesNotFit, "Maliyet tutarı kayıt alanına sığmıyor; yuvarlanmaz, reddedilir.");
        }
    }

    private static int Scale(decimal value) => (decimal.GetBits(value)[3] >> 16) & 0xFF;

    /// <summary>A replay repeats the stored facts exactly; the code's spelling included.</summary>
    private static bool SameIdentity(LabelRow row, MaterialLabel label) =>
        row.CompanyId == label.CompanyId && string.Equals(row.Code, label.Code, StringComparison.Ordinal)
        && row.ProductId == label.ProductId
        && string.Equals(row.ProductVersion, label.ProductVersion, StringComparison.Ordinal)
        && row.LotId == label.LotId
        && string.Equals(row.HeatNumber, label.HeatNumber, StringComparison.Ordinal)
        && string.Equals(row.CoilNumber, label.CoilNumber, StringComparison.Ordinal)
        && string.Equals(row.RequiredDocumentsJson,
            JsonSerializer.Serialize(label.RequiredDocuments.Order(StringComparer.Ordinal), Json), StringComparison.Ordinal);

    /// <summary>A replay repeats the stored lot facts exactly.</summary>
    private static bool SameLot(LotRow row, MaterialLot lot) =>
        row.CompanyId == lot.CompanyId && row.ProductId == lot.ProductId
        && string.Equals(row.HeatNumber, lot.HeatNumber, StringComparison.Ordinal)
        && string.Equals(row.CoilNumber, lot.CoilNumber, StringComparison.Ordinal);

    /// <summary>Rebuilds a stored lot through the domain: a stored row that breaks a rule is refused, not repaired.</summary>
    private static MaterialLot ToLot(LotRow row) =>
        new MaterialLot(row.Id, row.CompanyId, row.ProductId, row.HeatNumber, row.CoilNumber);

    /// <summary>The label facts a position needs (company, product, id); prints and documents do not matter there.</summary>
    private static MaterialLabel IdentityOnly(LabelRow row) =>
        new(row.Id, row.CompanyId, row.Code, row.ProductId, row.LotId, row.ProductVersion);

    private static StockPosition Position(Guid companyId, int ownerKind, Guid ownerId, int custodianKind, Guid custodianId,
        Guid locationId, MaterialLabel label, int block) =>
        new StockPosition(companyId, new StockOwner((StockPartyKind)ownerKind, ownerId),
            new StockCustodian((StockPartyKind)custodianKind, custodianId), locationId, label, (BlockState)block);

    private static bool Matches(BalanceRow row, StockPosition position) =>
        row.CompanyId == position.CompanyId && row.OwnerKind == (int)position.Owner.Kind && row.OwnerId == position.Owner.Id
        && row.CustodianKind == (int)position.Custodian.Kind && row.CustodianId == position.Custodian.Id
        && row.LocationId == position.LocationId && row.LabelId == position.LabelId
        && row.ProductId == position.ProductId && row.Block == (int)position.Block;

    private static LabelPrint ToPrint(LabelPrintRow row) => new LabelPrint(row.Sequence, row.PrintedAtUtc, row.PrintedBy, row.Reason);

    private static LabelDocumentAttachment ToAttachment(LabelDocumentRow row) => new LabelDocumentAttachment(row.Kind, row.AttachedAtUtc, row.AttachedBy);

    private static CostResult Cost(MovementRow row) => (CostStatus)row.CostStatus switch
    {
        CostStatus.NotCalculated => CostResult.NotCalculated,
        CostStatus.WaitingPolicy => CostResult.WaitingPolicy,
        CostStatus.Known => CostResult.Known(row.CostAmount!.Value, row.CostCurrencyId!.Value),
        _ => throw new InvalidOperationException("Kayıtlı hareketin maliyet durumu tanımsız."),
    };

    /// <summary>
    /// The canonical content of a movement: kind, time, source, cost and every line in order. Numbers
    /// are written without trailing zeros, so 5.00 and 5 are the same content (#293 SİPARİŞ P3-2).
    /// </summary>
    internal static string PayloadHash(StockMovement movement)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            kind = (int)movement.Kind,
            occurredAtUtc = movement.OccurredAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            source = new { kind = movement.Source.Kind, id = movement.Source.Id, lineId = movement.Source.LineId },
            cost = new
            {
                status = (int)movement.Cost.Status,
                amount = movement.Cost.Amount is { } known
                    ? (known / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture)
                    : null,
                currencyId = movement.Cost.CurrencyId,
            },
            lines = movement.Lines.Select(line => new
            {
                direction = (int)line.Direction,
                ownerKind = (int)line.Position.Owner.Kind,
                ownerId = line.Position.Owner.Id,
                custodianKind = (int)line.Position.Custodian.Kind,
                custodianId = line.Position.Custodian.Id,
                locationId = line.Position.LocationId,
                labelId = line.Position.LabelId,
                productId = line.Position.ProductId,
                block = (int)line.Position.Block,
                quantityKg = line.Quantity.ToString(),
            }),
        }, Json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    /// <summary>
    /// The canonical content of an opening count: location, date, sheet reference and every line with its
    /// piece, lot and kilograms (trailing zeros normalised), in order. Its hash is the R3 comparison.
    /// </summary>
    internal static string OpeningHash(OpeningCount count)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            locationId = count.LocationId,
            countedOn = count.CountedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            sheetReference = count.SheetReference,
            lines = count.Lines.Select(line => new
            {
                id = line.Id,
                quantityKg = line.Quantity.ToString(),
                lot = new { id = line.Lot.Id, productId = line.Lot.ProductId, heat = line.Lot.HeatNumber, coil = line.Lot.CoilNumber },
                label = new
                {
                    id = line.Label.Id,
                    code = line.Label.Code,
                    productVersion = line.Label.ProductVersion,
                    heat = line.Label.HeatNumber,
                    coil = line.Label.CoilNumber,
                    requiredDocuments = line.Label.RequiredDocuments.Order(StringComparer.Ordinal),
                    prints = line.Label.Prints.Count,
                    documents = line.Label.DocumentAttachments.Count,
                },
            }),
        }, Json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    /// <summary>
    /// Canonical opening identity used before label ProductVersion became durable. It is accepted only when the
    /// stored opening's labels still have null provenance; it is never written for a new opening or used to backfill.
    /// </summary>
    internal static string LegacyOpeningHash(OpeningCount count)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            locationId = count.LocationId,
            countedOn = count.CountedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            sheetReference = count.SheetReference,
            lines = count.Lines.Select(line => new
            {
                id = line.Id,
                quantityKg = line.Quantity.ToString(),
                lot = new { id = line.Lot.Id, productId = line.Lot.ProductId, heat = line.Lot.HeatNumber, coil = line.Lot.CoilNumber },
                label = new
                {
                    id = line.Label.Id,
                    code = line.Label.Code,
                    heat = line.Label.HeatNumber,
                    coil = line.Label.CoilNumber,
                    requiredDocuments = line.Label.RequiredDocuments.Order(StringComparer.Ordinal),
                    prints = line.Label.Prints.Count,
                    documents = line.Label.DocumentAttachments.Count,
                },
            }),
        }, Json);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string OpeningAudit(OpeningCount count, string hash) => JsonSerializer.Serialize(new
    {
        count.Id,
        count.LocationId,
        countedOn = count.CountedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        count.SheetReference,
        lines = count.Lines.Count,
        contentHash = hash,
    }, Json);

    private static string LotAudit(MaterialLot lot) => JsonSerializer.Serialize(new
    {
        lot.Id,
        lot.ProductId,
        lot.HeatNumber,
        lot.CoilNumber,
    }, Json);

    private static string LabelAudit(MaterialLabel label) => JsonSerializer.Serialize(new
    {
        label.Id,
        label.Code,
        label.ProductId,
        label.ProductVersion,
        label.LotId,
        requiredDocuments = label.RequiredDocuments.Order(StringComparer.Ordinal),
        documentStatus = label.DocumentStatus.ToString(),
        prints = label.Prints.Count,
        documents = label.DocumentAttachments.Count,
    }, Json);

    private static string MovementAudit(StockMovement movement, string payloadHash) => JsonSerializer.Serialize(new
    {
        movement.Id,
        kind = movement.Kind.ToString(),
        movement.OccurredAtUtc,
        source = new { movement.Source.Kind, movement.Source.Id, movement.Source.LineId },
        costStatus = movement.Cost.Status.ToString(),
        lines = movement.Lines.Count,
        payloadHash,
    }, Json);

    private static InventoryStoreException Refused(string code, string message, Exception? inner = null) => new InventoryStoreException(code, message, inner);
}
