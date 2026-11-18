using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>Inventory-owned cost recognition; the Manufacturing host alone commits or rolls back borrowed handles.</summary>
public sealed class InventoryManufacturingCostGenerationParticipant : IInventoryManufacturingCostGenerationParticipantV1
{
    private const string StageCommand = "inventory.manufacturing-cost.stage.v1";
    private const string ReverseCommand = "inventory.manufacturing-cost.reverse.v1";
    private const string DifferenceEvent = InventoryOutboxContractV1.ManufacturingCostDifferenceProposed;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly InventoryDbContext db;
    private readonly CompanyContext company;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly IAuditEventCollector audit;
    private readonly IAuditTransactionWriterV1 auditWriter;
    private readonly TimeProvider clock;
    private bool disposed;
    private int started;

    private InventoryManufacturingCostGenerationParticipant(InventoryDbContext db, CompanyContext company,
        DbConnection connection, DbTransaction transaction, IAuditEventCollector audit,
        IAuditTransactionWriterV1 auditWriter, TimeProvider clock)
    { this.db = db; this.company = company; this.connection = connection; this.transaction = transaction;
        this.audit = audit; this.auditWriter = auditWriter; this.clock = clock; }

    public static async Task<IInventoryManufacturingCostGenerationParticipantV1> EnlistAsync(DbConnection connection,
        DbTransaction transaction, Guid companyId, IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter,
        CancellationToken cancellationToken = default, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(audit); ArgumentNullException.ThrowIfNull(auditWriter);
        if (companyId == Guid.Empty || connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || transaction.IsolationLevel != IsolationLevel.Serializable)
            throw new InvalidOperationException("Üretim maliyet kaydı sahibin açık Serializable işlemini ve şirketini ister.");
        if (auditWriter.HasPendingDrafts) throw Error(503, "inventory_manufacturing_cost_unavailable");
        var company = new CompanyContext(); company.Set(companyId);
        var db = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false).Options, company);
        try
        {
            await db.Database.UseTransactionAsync(transaction, cancellationToken);
            return new InventoryManufacturingCostGenerationParticipant(db, company, connection, transaction, audit,
                auditWriter, clock ?? TimeProvider.System);
        }
        catch { await db.DisposeAsync(); throw; }
    }

    public async Task<InventoryManufacturingCostGenerationResultV1?> ReadActiveAsync(Guid manufacturingOrderId,
        CancellationToken cancellationToken = default)
    {
        EnsureActive();
        if (manufacturingOrderId == Guid.Empty) throw Error(400, "inventory_manufacturing_cost_invalid_request");
        var row = await db.ManufacturingCostGenerations.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.ManufacturingOrderId == manufacturingOrderId && candidate.State != (int)InventoryManufacturingCostGenerationStateV1.Reversed,
            cancellationToken);
        return row is null ? null : ResultOf(row);
    }

    public async Task<InventoryManufacturingCostGenerationResultV1> StageAsync(InventoryManufacturingCostGenerationCommandV1 command,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        Begin(command.CompanyId, metadata);
        var completed = false;
        try
        {
            Validate(command);
            if (await ReplayAsync(StageCommand, command, metadata, cancellationToken) is { } replay) { completed = true; return replay; }
            var row = await db.ManufacturingCostGenerations.SingleOrDefaultAsync(candidate => candidate.Id == command.GenerationId,
                cancellationToken);
            var before = row is null ? null : ResultOf(row);
            var facts = Facts(command.Outputs);
            if (row is not null && (row.ManufacturingOrderId != command.ManufacturingOrderId
                || row.ClosedOrderVersion != command.ClosedOrderVersion || row.OutputFactsJson != facts
                || row.State == (int)InventoryManufacturingCostGenerationStateV1.Reversed))
                throw Error(409, "inventory_manufacturing_cost_generation_conflict");
            if (before is not null && before.State == command.State && before.InputFingerprint == command.InputFingerprint)
            {
                // A fingerprint is only a dedup key when the complete authoritative amount/provenance also agrees.
                var history = await db.ManufacturingCostGenerationHistory.AsNoTracking().SingleAsync(candidate =>
                    candidate.GenerationId == row!.Id && candidate.Version == row.Version, cancellationToken);
                if (history.InputEvidenceJson != Evidence(command)) throw Error(409, "inventory_manufacturing_cost_generation_conflict");
                var unchanged = before with { IsReplay = false, OriginalCorrelationId = metadata.CorrelationId };
                db.Commands.Add(new() { CompanyId = company.CompanyId, CommandName = StageCommand,
                    IdempotencyKeyHash = Hash(metadata.IdempotencyKey), PayloadHash = Hash(JsonSerializer.Serialize(command, Json)),
                    ActorId = metadata.ActorId, TargetId = row!.Id, ResultJson = JsonSerializer.Serialize(unchanged, Json),
                    CorrelationId = metadata.CorrelationId, RecordedAtUtc = clock.GetUtcNow() });
                audit.Enqueue(new AuditEventDraft(company.CompanyId, StageCommand, "Inventory.ManufacturingCostGeneration", row.Id.ToString("D"),
                    "ObserveUnchanged", row.Version, row.Version, AuditActorTypes.User, metadata.ActorId, metadata.PermissionCode,
                    metadata.RoleCodes, row.LastResultJson, row.LastResultJson, command.Reason, ApprovalContextJson: null,
                    AuditAiInvolvement.None, AiContextJson: null, metadata.SourceChannel, metadata.ClientIpAddress, metadata.UserAgent,
                    metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
                await db.SaveChangesAsync(cancellationToken);
                await auditWriter.WritePendingAsync(transaction, cancellationToken);
                completed = true; return unchanged;
            }
            if (Text(row?.Version ?? 0) != command.ExpectedGenerationVersion || before?.State == InventoryManufacturingCostGenerationStateV1.Recognized)
                throw Error(409, "inventory_manufacturing_cost_version_conflict");
            if (row is null && await db.ManufacturingCostGenerations.AnyAsync(candidate => candidate.ManufacturingOrderId == command.ManufacturingOrderId
                && candidate.State != (int)InventoryManufacturingCostGenerationStateV1.Reversed, cancellationToken))
                throw Error(409, "inventory_manufacturing_cost_generation_conflict");
            var factual = await ValidateOutputsAsync(command, cancellationToken);
            var results = new List<InventoryManufacturingCostGenerationOutputResultV1>();
            foreach (var output in command.Outputs.OrderBy(value => value.LabelId))
            {
                var label = factual[output.LabelId];
                // Version + rowversion fence with every physical writer, including transfers after this read.
                _ = label; // Every actual positive-share descendant is fenced by OutputEffectAsync.
                results.Add(await OutputEffectAsync(output, command.State == InventoryManufacturingCostGenerationStateV1.Recognized,
                    reverse: false, command.GenerationId, command.InputFingerprint, metadata, cancellationToken));
            }
            row ??= NewGeneration(command, facts);
            var version = checked(row.Version + 1);
            var eventId = command.State == InventoryManufacturingCostGenerationStateV1.Recognized
                && results.Any(result => result.ConsumedTlDifference is not null and not 0m) ? Guid.NewGuid() : (Guid?)null;
            var result = new InventoryManufacturingCostGenerationResultV1(company.CompanyId, command.ManufacturingOrderId,
                row.Id, Text(version), command.ClosedOrderVersion, command.State, command.InputFingerprint, results,
                eventId, false, metadata.CorrelationId);
            await SaveAsync(row, result, command, StageCommand, command.Reason, command.OccurredAtUtc, before, metadata,
                cancellationToken);
            completed = true; return result;
        }
        finally { if (!completed) auditWriter.DiscardPending(); }
    }

    public async Task<InventoryManufacturingCostGenerationResultV1> ReverseAsync(InventoryManufacturingCostGenerationReversalV1 command,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        Begin(command.CompanyId, metadata);
        var completed = false;
        try
        {
            if (command.ManufacturingOrderId == Guid.Empty || command.GenerationId == Guid.Empty || !Version(command.ExpectedOrderVersion)
                || !Version(command.ExpectedGenerationVersion) || string.IsNullOrWhiteSpace(command.Reason) || command.OccurredAtUtc.Offset != TimeSpan.Zero)
                throw Error(400, "inventory_manufacturing_cost_invalid_request");
            if (await ReplayAsync(ReverseCommand, command, metadata, cancellationToken) is { } replay) { completed = true; return replay; }
            var row = await db.ManufacturingCostGenerations.SingleOrDefaultAsync(candidate => candidate.Id == command.GenerationId,
                cancellationToken) ?? throw Error(404, "inventory_manufacturing_cost_generation_not_found");
            var before = ResultOf(row);
            if (row.ManufacturingOrderId != command.ManufacturingOrderId || Text(row.Version) != command.ExpectedGenerationVersion
                || row.State == (int)InventoryManufacturingCostGenerationStateV1.Reversed)
                throw Error(409, "inventory_manufacturing_cost_version_conflict");
            var results = new List<InventoryManufacturingCostGenerationOutputResultV1>();
            foreach (var output in before.Outputs.OrderBy(value => value.LabelId))
            {
                var label = await db.Labels.SingleOrDefaultAsync(candidate => candidate.Id == output.LabelId, cancellationToken)
                    ?? throw Error(503, "inventory_manufacturing_cost_unavailable");
                _ = label; // Every actual positive-share descendant is fenced by OutputEffectAsync.
                results.Add(await OutputEffectAsync(new(output.OutputId, output.ReceiptMovementId, output.LabelId, output.NetQuantityKg,
                    output.TlUnitCostPerKg, output.UsdUnitCostPerKg, output.OriginalTlAmount),
                    before.State == InventoryManufacturingCostGenerationStateV1.Recognized, reverse: true,
                    row.Id, before.InputFingerprint, metadata, cancellationToken, output.PoolEffects, output.Positions));
            }
            var eventId = before.State == InventoryManufacturingCostGenerationStateV1.Recognized
                && results.Any(result => result.ConsumedTlDifference is not null and not 0m) ? Guid.NewGuid() : (Guid?)null;
            var result = before with { GenerationVersion = Text(checked(row.Version + 1)), State = InventoryManufacturingCostGenerationStateV1.Reversed,
                Outputs = results, CostDifferenceEventId = eventId, IsReplay = false, OriginalCorrelationId = metadata.CorrelationId };
            await SaveAsync(row, result, command, ReverseCommand, command.Reason, command.OccurredAtUtc, before, metadata, cancellationToken);
            completed = true; return result;
        }
        finally { if (!completed) auditWriter.DiscardPending(); }
    }

    private async Task<InventoryManufacturingCostGenerationOutputResultV1> OutputEffectAsync(InventoryManufacturingCostGenerationOutputV1 output,
        bool recognized, bool reverse, Guid generationId, string sourceVersion, InventoryCommandMetadata metadata, CancellationToken token,
        IReadOnlyList<InventoryManufacturingCostPoolEffectV1>? originalPoolEffects = null,
        IReadOnlyList<InventoryManufacturingCostPositionEffectV1>? originalPositions = null)
    {
        var root = await db.Labels.SingleAsync(row => row.Id == output.LabelId, token);
        var lineage = await InventoryCostLineageReader.ReadAsync(db, company.CompanyId, root.ProductId, output.LabelId, token);
        if (lineage.Outcome != InventoryCostLineageOutcome.Value || lineage.RemainingKg is null || lineage.NetShippedKg is null)
            throw Error(503, "inventory_manufacturing_cost_physical_source_unreadable");
        await FenceLineageAsync(lineage, token);
        var hasMoney = output.TlAmount is not null || output.UsdUnitCostPerKg is > 0m;
        if (recognized && reverse && hasMoney)
            await InventoryManufacturingTransferReversalProof.RequireReversibleAsync(db, lineage, output, generationId,
                sourceVersion, originalPositions ?? throw Error(503, InventoryManufacturingTransferReversalProof.UnavailableCode), token);
        var transferScope = recognized && !reverse && hasMoney
            ? await InventoryManufacturingTransferReversalProof.CaptureAsync(db, lineage, output, generationId, sourceVersion, token) : null;
        var remaining = lineage.RemainingKg;
        var consumed = ExactCostRatio.Of(output.NetQuantityKg).Subtract(remaining);
        if (consumed.Sign < 0) throw Error(503, "inventory_manufacturing_cost_physical_source_unreadable");
        if (recognized && output.TlAmount is not null && (lineage.NetShippedKg.Sign < 0 || consumed.CompareTo(lineage.NetShippedKg) != 0))
            // Transit or a non-shipment exit is not a sale. This exact refusal is still before every save.
            throw Error(503, "inventory_manufacturing_cost_consumption_source_unreadable");
        var split = recognized && output.TlAmount is { } amount
            ? InventoryManufacturingCostSplitCalculatorV1.Split(amount, output.NetQuantityKg, consumed) : null;
        var remainingTl = split?.RemainingTlAmount;
        var consumedTl = split?.ConsumedTlAmount;
        var sign = reverse ? -1m : 1m;
        var positions = new List<InventoryManufacturingCostPositionEffectV1>();
        var poolEffects = new List<InventoryManufacturingCostPoolEffectV1>();
        var groups = lineage.Labels.Where(label => label.RootShare.Sign > 0)
            .SelectMany(label => label.Positions.Where(position => position.QuantityKg > 0m).Select(position => new
            {
                position.LocationId, Included = position.Owner.Kind == StockPartyKind.Company && position.Owner.Id == company.CompanyId
                    && position.Custodian.Kind == StockPartyKind.Company && position.Custodian.Id == company.CompanyId,
                Quantity = label.RootShare.Multiply(position.QuantityKg),
            })).GroupBy(position => (position.LocationId, position.Included))
            .OrderBy(group => group.Key.LocationId).ThenBy(group => group.Key.Included).ToArray();
        var allocatedValue = ExactCostRatio.Of(0m);
        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            var kg = group.Aggregate(ExactCostRatio.Of(0m), (sum, row) => sum.Add(row.Quantity));
            var value = index == groups.Length - 1 ? ExactCostRatio.Of(remainingTl ?? 0m).Subtract(allocatedValue)
                : remainingTl is { } fullRemaining && remaining.Sign > 0 ? ExactCostRatio.Of(fullRemaining).Multiply(kg).Divide(remaining) : ExactCostRatio.Of(0m);
            allocatedValue = allocatedValue.Add(value);
            var signedValue = value.Multiply(sign);
            if (recognized && output.TlAmount is not null) positions.Add(new(group.Key.LocationId, Scalar(kg), Scalar(signedValue),
                group.Key.Included, Ratio(kg), Ratio(signedValue)));
            if (!recognized || !group.Key.Included) continue;
            if (output.TlAmount is not null)
                poolEffects.Add(await ApplyPoolEffectAsync(group.Key.LocationId, root.ProductId, SettingsCurrencyIdentitiesV1.TurkishLira,
                    kg, value, reverse, Original(SettingsCurrencyIdentitiesV1.TurkishLira), token));
            if (output.UsdUnitCostPerKg is > 0m)
                poolEffects.Add(await ApplyPoolEffectAsync(group.Key.LocationId, root.ProductId, ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"),
                    kg, kg.Multiply(output.UsdUnitCostPerKg.Value), reverse, Original(ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD")), token));
            InventoryManufacturingCostPoolEffectV1? Original(Guid currency) => originalPoolEffects?.SingleOrDefault(effect =>
                effect.LocationId == group.Key.LocationId && effect.ProductId == root.ProductId && effect.CurrencyId == currency);
        }
        if (recognized && !reverse && output.NetQuantityKg > 0m)
        {
            if (output.TlAmount is not null) await AddRecognitionAsync(SettingsCurrencyIdentitiesV1.TurkishLira, output.TlUnitCostPerKg!.Value);
            if (output.UsdUnitCostPerKg is > 0m) await AddRecognitionAsync(ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"), output.UsdUnitCostPerKg.Value);
        }
        var consumedLineage = InventoryManufacturingConsumedCostLineageEvidenceV1.Create(company.CompanyId, output.OutputId,
            output.ReceiptMovementId, output.LabelId, output.NetQuantityKg, Ratio(lineage.NetShippedKg),
            Array.AsReadOnly(lineage.Shipments.Select(shipment => new InventoryManufacturingConsumedCostShipmentV1(
                shipment.ShipmentId, shipment.MovementId, shipment.LineNumber, shipment.LoadingInstructionLineId,
                shipment.LabelId, shipment.ProductId, shipment.ProductVersion, shipment.Owner.Kind, shipment.Owner.Id,
                shipment.Custodian.Kind, shipment.Custodian.Id, shipment.LocationId, shipment.Block, shipment.QuantityKg,
                Ratio(shipment.RootShare), Ratio(shipment.NetRootQuantityKg), Array.AsReadOnly(shipment.Returns.Select(item =>
                    new InventoryManufacturingConsumedCostReturnV1(item.ReturnId, item.MovementId, item.LineNumber,
                        item.SourceIssueMovementId, item.QuantityKg, item.RecordedAtUtc)).ToArray()), shipment.RecordedAtUtc)).ToArray()));
        return new(output.OutputId, output.ReceiptMovementId, output.LabelId, output.NetQuantityKg, Scalar(remaining), Scalar(consumed),
            recognized ? output.TlUnitCostPerKg : null, sign * remainingTl, sign * consumedTl,
            recognized ? output.UsdUnitCostPerKg : null, positions, recognized ? output.TlAmount : null, poolEffects, Ratio(remaining), Ratio(consumed),
            consumedLineage);

        async Task AddRecognitionAsync(Guid currency, decimal unit)
        {
            var pending = await db.PendingMovementCosts.SingleOrDefaultAsync(row => row.MovementId == output.ReceiptMovementId
                && row.LineNumber == 1 && row.CurrencyId == currency, token);
            if (pending is null) throw Error(503, "inventory_manufacturing_cost_pending_source_missing");
            db.MovementCostRecognitions.Add(new() { Id = Guid.NewGuid(), CompanyId = company.CompanyId,
                MovementId = output.ReceiptMovementId, LineNumber = 1, CurrencyId = currency, GenerationId = generationId,
                UnitCostPerKg = unit, RemainingQuantityKg = MassScalar(remaining), ConsumedQuantityKg = MassScalar(consumed),
                RemainingQuantityRatioJson = JsonSerializer.Serialize(Ratio(remaining), Json), ConsumedQuantityRatioJson = JsonSerializer.Serialize(Ratio(consumed), Json),
                SourceVersion = sourceVersion, ResultJson = JsonSerializer.Serialize(new { output, remaining = Ratio(remaining), consumed = Ratio(consumed), transferScope }, Json),
                RecordedAtUtc = clock.GetUtcNow(), CorrelationId = metadata.CorrelationId });
        }
    }

    private readonly HashSet<Guid> fencedLineage = [];
    private async Task FenceLineageAsync(InventoryCostLineageSnapshot lineage, CancellationToken token)
    {
        foreach (var label in lineage.Labels.Where(label => label.RootShare.Sign > 0).OrderBy(label => label.LabelId))
        {
            var tracked = await db.Labels.SingleOrDefaultAsync(row => row.Id == label.LabelId, token)
                ?? throw Error(503, "inventory_manufacturing_cost_physical_source_unreadable");
            if (db.Entry(tracked).Property(row => row.Version).OriginalValue != label.Version
                || tracked.ProductId != lineage.ProductId || tracked.ProductVersion != label.ProductVersion)
                throw Error(409, "inventory_manufacturing_cost_version_conflict");
            if (fencedLineage.Add(label.LabelId))
            { tracked.Version = checked(tracked.Version + 1); tracked.UpdatedAtUtc = clock.GetUtcNow(); }
        }
    }

    private static decimal? Scalar(ExactCostRatio value) => InventoryExactCostRatioV1.ScalarOf(value);
    private static decimal? MassScalar(ExactCostRatio value) => Scalar(value) is { } scalar && decimal.Round(scalar, 3) == scalar ? scalar : null;
    private static InventoryExactCostRatioV1 Ratio(ExactCostRatio value) => InventoryExactCostRatioV1.From(value);
    private readonly Dictionary<(Guid, Guid, Guid), InventoryStore.CostPool> pools = [];
    private async Task<InventoryManufacturingCostPoolEffectV1> ApplyPoolEffectAsync(Guid location, Guid product, Guid currency,
        ExactCostRatio kg, ExactCostRatio authoritativeValue, bool reverse, InventoryManufacturingCostPoolEffectV1? original, CancellationToken token)
    {
        var key = (location, product, currency);
        if (!pools.TryGetValue(key, out var pool)) pool = await InventoryStore.ReadCostPoolAsync(db, company, location, product, currency, token);
        if (!pool.IsConsistent || authoritativeValue.Sign < 0) throw Error(503, "inventory_manufacturing_cost_ledger_unreadable");
        var current = pool.ExactPosition ?? new(ExactCostRatio.Of(0m), 0m);
        ExactAverageCostPool after;
        if (!reverse) after = current.Add(kg, authoritativeValue);
        else
        {
            if (original is null) throw Error(503, "inventory_manufacturing_cost_reversal_effect_unreadable");
            var originalKg = InventoryExactCostRatioV1.Resolve(original.QuantityKg, original.QuantityKgRatio);
            if (originalKg.Sign <= 0 || originalKg.CompareTo(kg) < 0) throw Error(503, "inventory_manufacturing_cost_reversal_effect_unreadable");
            var actual = InventoryExactCostRatioV1.Resolve(original.RecordedPoolValueEffect, original.RecordedPoolValueEffectRatio)
                .Multiply(kg).Divide(originalKg);
            try { after = current.Remove(kg, actual); }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentOutOfRangeException)
            { throw Error(503, "inventory_manufacturing_cost_reversal_effect_unreadable"); }
        }
        var version = await InventoryStore.WriteCostPoolAsync(db, company, clock, location, product, currency, pool, after, token);
        pools[key] = pool with { ExactPosition = after, Version = version, CostedKg = after.QuantityKg,
            ExactRow = db.ExactCostPools.Local.Single(row => row.LocationId == location && row.ProductId == product && row.CurrencyId == currency),
            TlRow = currency == SettingsCurrencyIdentitiesV1.TurkishLira ? db.AverageCostLedger.Local.Single(row => row.LocationId == location && row.ProductId == product) : null,
            ForeignRow = currency != SettingsCurrencyIdentitiesV1.TurkishLira ? db.CurrencyAverageCostLedger.Local.Single(row => row.LocationId == location && row.ProductId == product && row.CurrencyId == currency) : null };
        var actualDelta = after.Value.Subtract(current.Value);
        var signedAuthoritative = authoritativeValue.Multiply(reverse ? -1m : 1m);
        var difference = actualDelta.Subtract(signedAuthoritative);
        return new(currency, location, product, Scalar(kg), Scalar(current.QuantityKg), pool.ExactPosition?.AverageCostPerKg,
            Scalar(after.QuantityKg), after.AverageCostPerKg, Scalar(signedAuthoritative), Scalar(actualDelta), Scalar(difference),
            Ratio(kg), Ratio(current.QuantityKg), Ratio(after.QuantityKg), Ratio(signedAuthoritative), Ratio(actualDelta), Ratio(difference));
    }

    private async Task<Dictionary<Guid, LabelRow>> ValidateOutputsAsync(InventoryManufacturingCostGenerationCommandV1 command, CancellationToken token)
    {
        var read = await new InventoryManufacturingCostReadPort(db, company).GetAsync(company.CompanyId, command.ManufacturingOrderId, token);
        if (read.Outcome != InventoryManufacturingCostReadOutcomeV1.Available || read.Outputs.Count != command.Outputs.Count)
            throw Error(503, "inventory_manufacturing_cost_physical_source_unreadable");
        foreach (var output in command.Outputs)
        {
            var fact = read.Outputs.SingleOrDefault(row => row.FactId == output.OutputId && row.ReceiptMovementId == output.ReceiptMovementId
                && row.LabelId == output.LabelId);
            var reversals = await (from movement in db.Movements.AsNoTracking()
                join line in db.MovementLines.AsNoTracking() on movement.Id equals line.MovementId
                where line.LabelId == output.LabelId && movement.SourceId == command.ManufacturingOrderId
                    && movement.SourceKind == InventoryManufacturingOutputReversalContractV1.OutputReversalSourceKind
                    && line.Direction == (int)GoymenERP.Modules.Inventory.Domain.Movements.StockDirection.Out
                select (decimal?)line.QuantityKg).SumAsync(token) ?? 0m;
            if (fact is null || fact.DeclaredQuantityKg - reversals != output.NetQuantityKg)
                throw Error(503, "inventory_manufacturing_cost_physical_source_unreadable");
            if (command.State == InventoryManufacturingCostGenerationStateV1.Recognized && output.NetQuantityKg > 0m && output.TlAmount is null)
            {
                var source = await db.Commands.AsNoTracking().Where(row => row.TargetId == output.OutputId
                    && (row.CommandName == InventoryManufacturingRealizationContractV1.OutputCommandName
                        || row.CommandName == InventoryManufacturingRealizationContractV2.OutputCommandName))
                    .Select(row => row.ResultJson).SingleAsync(token);
                var original = JsonSerializer.Deserialize<InventoryManufacturingOutputResultV1>(source, Json);
                if (original?.OutputClass != InventoryManufacturingOutputClassificationV2.Scrap || output.UsdUnitCostPerKg is not null)
                    throw Error(422, "inventory_manufacturing_cost_source_not_readable");
            }
        }
        var labels = command.Outputs.Select(output => output.LabelId).ToArray();
        return await db.Labels.Where(row => labels.Contains(row.Id)).OrderBy(row => row.Id).ToDictionaryAsync(row => row.Id, token);
    }

    private ManufacturingCostGenerationRow NewGeneration(InventoryManufacturingCostGenerationCommandV1 command, string facts)
    {
        var row = new ManufacturingCostGenerationRow { CompanyId = company.CompanyId, Id = command.GenerationId,
            ManufacturingOrderId = command.ManufacturingOrderId, ClosedOrderVersion = command.ClosedOrderVersion,
            OutputFactsJson = facts, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        db.ManufacturingCostGenerations.Add(row); return row;
    }

    private async Task SaveAsync<T>(ManufacturingCostGenerationRow row, InventoryManufacturingCostGenerationResultV1 result, T command,
        string operation, string reason, DateTimeOffset occurredAt, InventoryManufacturingCostGenerationResultV1? before,
        InventoryCommandMetadata metadata, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(result, Json);
        row.Version = long.Parse(result.GenerationVersion, CultureInfo.InvariantCulture); row.State = (int)result.State;
        row.LastResultJson = json; row.UpdatedAtUtc = clock.GetUtcNow();
        db.ManufacturingCostGenerationHistory.Add(new() { CompanyId = company.CompanyId, GenerationId = row.Id,
            Version = row.Version, InputFingerprint = result.InputFingerprint, InputEvidenceJson = Evidence(command), ResultJson = json,
            ActorId = metadata.ActorId, CorrelationId = metadata.CorrelationId, RecordedAtUtc = clock.GetUtcNow() });
        db.Commands.Add(new() { CompanyId = company.CompanyId, CommandName = operation, IdempotencyKeyHash = Hash(metadata.IdempotencyKey),
            PayloadHash = Hash(JsonSerializer.Serialize(command, Json)), ActorId = metadata.ActorId, TargetId = row.Id,
            ResultJson = json, CorrelationId = metadata.CorrelationId, RecordedAtUtc = clock.GetUtcNow() });
        audit.Enqueue(new AuditEventDraft(company.CompanyId, operation, "Inventory.ManufacturingCostGeneration", row.Id.ToString("D"),
            result.State.ToString(), before is null ? 0 : long.Parse(before.GenerationVersion, CultureInfo.InvariantCulture), row.Version,
            AuditActorTypes.User, metadata.ActorId, metadata.PermissionCode, metadata.RoleCodes, before is null ? "{}" : JsonSerializer.Serialize(before, Json),
            json, reason, ApprovalContextJson: null, AuditAiInvolvement.None, AiContextJson: null, metadata.SourceChannel,
            metadata.ClientIpAddress, metadata.UserAgent, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey));
        if (result.CostDifferenceEventId is { } eventId)
            db.OutboxMessages.Add(new() { CompanyId = company.CompanyId, Id = eventId, EventType = DifferenceEvent, SchemaVersion = 1,
                SourceOperation = operation, AggregateType = "Inventory.ManufacturingCostGeneration", AggregateId = row.Id,
                AggregateVersion = row.Version, Sequence = row.Version,
                PayloadJson = JsonSerializer.Serialize(InventoryManufacturingConsumedCostLineageEvidenceV1.ForOutbox(result, json), Json), OccurredAtUtc = occurredAt,
                CorrelationId = metadata.CorrelationId, CausationId = metadata.CausationId, RecordedAtUtc = clock.GetUtcNow() });
        try { await db.SaveChangesAsync(token); await auditWriter.WritePendingAsync(transaction, token); }
        catch (DbUpdateConcurrencyException) { throw Error(409, "inventory_manufacturing_cost_version_conflict"); }
    }

    private async Task<InventoryManufacturingCostGenerationResultV1?> ReplayAsync<T>(string operation, T command,
        InventoryCommandMetadata metadata, CancellationToken token)
    {
        var hash = Hash(metadata.IdempotencyKey);
        var row = await db.Commands.AsNoTracking().SingleOrDefaultAsync(row => row.CommandName == operation && row.IdempotencyKeyHash == hash, token);
        if (row is null) return null;
        if (row.ActorId != metadata.ActorId || row.PayloadHash != Hash(JsonSerializer.Serialize(command, Json)))
            throw Error(409, "inventory_manufacturing_cost_idempotency_conflict");
        var result = JsonSerializer.Deserialize<InventoryManufacturingCostGenerationResultV1>(row.ResultJson, Json)
            ?? throw Error(503, "inventory_manufacturing_cost_unavailable");
        if (result.CompanyId != company.CompanyId || result.GenerationId != row.TargetId)
            throw Error(503, "inventory_manufacturing_cost_unavailable");
        ValidateResultExactValues(result);
        return result with { IsReplay = true, OriginalCorrelationId = row.CorrelationId };
    }

    private static void Validate(InventoryManufacturingCostGenerationCommandV1 command)
    {
        if (command.ManufacturingOrderId == Guid.Empty || command.GenerationId == Guid.Empty || !Version(command.ClosedOrderVersion)
            || !(command.ExpectedGenerationVersion == "0" || Version(command.ExpectedGenerationVersion))
            || command.State is not (InventoryManufacturingCostGenerationStateV1.Pending or InventoryManufacturingCostGenerationStateV1.Unreadable or InventoryManufacturingCostGenerationStateV1.Recognized)
            || string.IsNullOrWhiteSpace(command.InputFingerprint) || command.InputFingerprint.Length > 160
            || string.IsNullOrWhiteSpace(command.CalculatorVersion) || string.IsNullOrWhiteSpace(command.Reason)
            || command.OccurredAtUtc.Offset != TimeSpan.Zero || command.Outputs is null
            || command.Outputs.Select(output => output.OutputId).Distinct().Count() != command.Outputs.Count
            || command.Outputs.Select(output => output.LabelId).Distinct().Count() != command.Outputs.Count)
            throw Error(400, "inventory_manufacturing_cost_invalid_request");
        JsonDocument evidence;
        try { evidence = JsonDocument.Parse(command.EvidenceJson); }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        { throw Error(400, "inventory_manufacturing_cost_invalid_request"); }
        using var evidenceLifetime = evidence;
        if (evidence.RootElement.ValueKind != JsonValueKind.Object) throw Error(400, "inventory_manufacturing_cost_invalid_request");
        foreach (var output in command.Outputs)
        {
            if (output.OutputId == Guid.Empty || output.ReceiptMovementId == Guid.Empty || output.LabelId == Guid.Empty
                || output.NetQuantityKg < 0m || decimal.Round(output.NetQuantityKg, 3) != output.NetQuantityKg
                || output.UsdUnitCostPerKg is < 0m || output.TlUnitCostPerKg is < 0m || output.TlAmount is < 0m)
                throw Error(400, "inventory_manufacturing_cost_invalid_request");
            if (output.TlAmount is { } amount && decimal.Round(amount, 2) != amount
                || output.TlUnitCostPerKg is { } unit && decimal.Round(unit, 4) != unit
                || output.UsdUnitCostPerKg is { } usd && decimal.Round(usd, 4) != usd)
                throw Error(400, "inventory_manufacturing_cost_invalid_request");
            if (command.State == InventoryManufacturingCostGenerationStateV1.Recognized && output.NetQuantityKg > 0m
                && ((output.TlUnitCostPerKg is null) != (output.TlAmount is null)
                    || output.TlUnitCostPerKg is <= 0m || output.TlAmount is <= 0m))
                throw Error(422, "inventory_manufacturing_cost_source_not_readable");
            if (command.State != InventoryManufacturingCostGenerationStateV1.Recognized
                && (output.TlUnitCostPerKg is not null || output.TlAmount is not null || output.UsdUnitCostPerKg is not null))
                throw Error(400, "inventory_manufacturing_cost_invalid_request");
        }
    }

    private void Begin(Guid companyId, InventoryCommandMetadata metadata)
    {
        EnsureActive();
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("Maliyet katılımcısı tek komut girişimidir.");
        if (companyId != company.CompanyId || string.IsNullOrWhiteSpace(metadata.ActorId) || string.IsNullOrWhiteSpace(metadata.PermissionCode)
            || string.IsNullOrWhiteSpace(metadata.IdempotencyKey) || string.IsNullOrWhiteSpace(metadata.CorrelationId)
            || string.IsNullOrWhiteSpace(metadata.SourceChannel) || auditWriter.HasPendingDrafts)
            throw Error(503, "inventory_manufacturing_cost_unavailable");
    }
    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
            throw Error(503, "inventory_manufacturing_cost_unavailable");
    }
    private static InventoryManufacturingCostGenerationResultV1 ResultOf(ManufacturingCostGenerationRow row)
    {
        var result = JsonSerializer.Deserialize<InventoryManufacturingCostGenerationResultV1>(row.LastResultJson, Json);
        if (result is null || result.CompanyId != row.CompanyId || result.GenerationId != row.Id || result.ManufacturingOrderId != row.ManufacturingOrderId
            || result.GenerationVersion != Text(row.Version) || (int)result.State != row.State)
            throw Error(503, "inventory_manufacturing_cost_unavailable");
        ValidateResultExactValues(result);
        return result;
    }
    private static void ValidateResultExactValues(InventoryManufacturingCostGenerationResultV1 result)
    {
        try
        {
            foreach (var output in result.Outputs)
            {
                var remaining = InventoryExactCostRatioV1.Resolve(output.RemainingQuantityKg, output.RemainingQuantityKgRatio);
                var consumed = InventoryExactCostRatioV1.Resolve(output.ConsumedQuantityKg, output.ConsumedQuantityKgRatio);
                if (remaining.Sign < 0 || consumed.Sign < 0 || remaining.Add(consumed).CompareTo(ExactCostRatio.Of(output.NetQuantityKg)) != 0)
                    throw new InvalidOperationException();
                foreach (var position in output.Positions)
                {
                    if (InventoryExactCostRatioV1.Resolve(position.QuantityKg, position.QuantityKgRatio).Sign < 0)
                        throw new InvalidOperationException();
                    _ = InventoryExactCostRatioV1.Resolve(position.TlValue, position.TlValueRatio);
                }
                foreach (var effect in output.PoolEffects ?? [])
                {
                    var quantity = InventoryExactCostRatioV1.Resolve(effect.QuantityKg, effect.QuantityKgRatio);
                    var before = InventoryExactCostRatioV1.Resolve(effect.BeforeQuantityKg, effect.BeforeQuantityKgRatio);
                    var after = InventoryExactCostRatioV1.Resolve(effect.AfterQuantityKg, effect.AfterQuantityKgRatio);
                    var authoritative = InventoryExactCostRatioV1.Resolve(effect.AuthoritativeValue, effect.AuthoritativeValueRatio);
                    var actual = InventoryExactCostRatioV1.Resolve(effect.RecordedPoolValueEffect, effect.RecordedPoolValueEffectRatio);
                    var difference = InventoryExactCostRatioV1.Resolve(effect.AverageRoundingDifference, effect.AverageRoundingDifferenceRatio);
                    if (quantity.Sign < 0 || before.Sign < 0 || after.Sign < 0 || actual.Subtract(authoritative).CompareTo(difference) != 0
                        || after.Multiply(effect.AfterAverageCostPerKg).Subtract(before.Multiply(effect.BeforeAverageCostPerKg ?? 0m)).CompareTo(actual) != 0)
                        throw new InvalidOperationException();
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or NullReferenceException)
        { throw Error(503, "inventory_manufacturing_cost_unavailable"); }
    }
    private static string Facts(IReadOnlyList<InventoryManufacturingCostGenerationOutputV1> outputs) => JsonSerializer.Serialize(outputs
        .OrderBy(output => output.OutputId).Select(output => new { output.OutputId, output.ReceiptMovementId, output.LabelId, output.NetQuantityKg }), Json);
    private static string Evidence<T>(T command) => command is InventoryManufacturingCostGenerationCommandV1 stage
        ? JsonSerializer.Serialize(new { stage.CalculatorVersion, stage.EvidenceJson, stage.Outputs }, Json)
        : JsonSerializer.Serialize(command, Json);
    private static bool Version(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var version) && version > 0 && Text(version) == value;
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static InventoryManufacturingCostGenerationException Error(int status, string code) => new(status, code);
    public async ValueTask DisposeAsync() { if (disposed) return; disposed = true; await db.DisposeAsync(); }
}
