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
    private const string DifferenceEvent = "inventory.manufacturing-cost-difference.proposed.v1";
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
        if (companyId == Guid.Empty || connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection))
            throw new InvalidOperationException("Üretim maliyet kaydı sahibin açık işlemini ve şirketini ister.");
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
                label.Version = checked(label.Version + 1); label.UpdatedAtUtc = clock.GetUtcNow();
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
                label.Version = checked(label.Version + 1); label.UpdatedAtUtc = clock.GetUtcNow();
                results.Add(await OutputEffectAsync(new(output.OutputId, output.ReceiptMovementId, output.LabelId, output.NetQuantityKg,
                    output.TlUnitCostPerKg, output.UsdUnitCostPerKg, output.OriginalTlAmount),
                    before.State == InventoryManufacturingCostGenerationStateV1.Recognized, reverse: true,
                    row.Id, before.InputFingerprint, metadata, cancellationToken));
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
        bool recognized, bool reverse, Guid generationId, string sourceVersion, InventoryCommandMetadata metadata, CancellationToken token)
    {
        var balances = await db.Balances.Where(row => row.LabelId == output.LabelId && row.QuantityKg > 0m).ToListAsync(token);
        var remaining = balances.Sum(row => row.QuantityKg);
        if (remaining > output.NetQuantityKg) throw Error(503, "inventory_manufacturing_cost_physical_source_unreadable");
        var consumed = output.NetQuantityKg - remaining;
        if (recognized)
        {
            var disposition = await InventoryLabelDispositionReader.ReadAsync(db, company.CompanyId, output.LabelId, token);
            var shipped = disposition.ShippedKg - disposition.CustomerReturnedKg;
            // Missing balances include transit and downstream transformations. Only a real net definite shipment is
            // evidence for this SMM proposal. Other lineage needs its owner cost propagation; no missing-kg guess.
            if (shipped < 0m || consumed != shipped)
                throw Error(503, "inventory_manufacturing_cost_consumption_source_unreadable");
        }
        var tlAmount = recognized ? output.TlAmount : null;
        // Decimal values remain JSON decimals at full precision. No SQL money-column scale or new cent policy is imposed.
        // The exact residual preserves original = remaining + consumed, even when the 4dp recorded unit differs.
        var remainingTl = tlAmount is { } amount ? output.NetQuantityKg == 0m ? 0m : amount * remaining / output.NetQuantityKg : (decimal?)null;
        var consumedTl = tlAmount - remainingTl;
        var sign = reverse ? -1m : 1m;
        var positions = new List<InventoryManufacturingCostPositionEffectV1>();
        var groups = balances.GroupBy(row => (row.LocationId, Included: row.OwnerKind == (int)StockPartyKind.Company
                     && row.OwnerId == company.CompanyId && row.CustodianKind == (int)StockPartyKind.Company && row.CustodianId == company.CompanyId))
                 .OrderBy(group => group.Key.LocationId).ThenBy(group => group.Key.Included).ToArray();
        var allocatedValue = 0m;
        for (var index = 0; index < groups.Length; index++)
        {
            var group = groups[index];
            var kg = group.Sum(row => row.QuantityKg);
            var value = index == groups.Length - 1 ? (remainingTl ?? 0m) - allocatedValue
                : tlAmount is { } fullAmount && output.NetQuantityKg > 0m ? fullAmount * kg / output.NetQuantityKg : 0m;
            allocatedValue += value;
            if (recognized) positions.Add(new(group.Key.LocationId, kg, sign * value, group.Key.Included));
            if (!recognized || !group.Key.Included) continue;
            var product = group.Select(row => row.ProductId).Distinct().Single();
            await ApplyPoolEffectAsync(group.Key.LocationId, product, SettingsCurrencyIdentitiesV1.TurkishLira, kg,
                output.TlUnitCostPerKg!.Value, reverse, token);
            if (output.UsdUnitCostPerKg is > 0m)
                await ApplyPoolEffectAsync(group.Key.LocationId, product, ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"), kg,
                    output.UsdUnitCostPerKg.Value, reverse, token);
        }
        if (recognized && !reverse && output.NetQuantityKg > 0m)
        {
            await AddRecognitionAsync(SettingsCurrencyIdentitiesV1.TurkishLira, output.TlUnitCostPerKg!.Value);
            if (output.UsdUnitCostPerKg is > 0m) await AddRecognitionAsync(ExchangeRateCurrencyIdentitiesV1.ForIsoCode("USD"), output.UsdUnitCostPerKg.Value);
        }
        return new(output.OutputId, output.ReceiptMovementId, output.LabelId, output.NetQuantityKg, remaining, consumed,
            recognized ? output.TlUnitCostPerKg : null, sign * remainingTl, sign * consumedTl,
            recognized ? output.UsdUnitCostPerKg : null, positions, recognized ? output.TlAmount : null);

        async Task AddRecognitionAsync(Guid currency, decimal unit)
        {
            var pending = await db.PendingMovementCosts.SingleOrDefaultAsync(row => row.MovementId == output.ReceiptMovementId
                && row.LineNumber == 1 && row.CurrencyId == currency, token);
            if (pending is null) throw Error(503, "inventory_manufacturing_cost_pending_source_missing");
            db.MovementCostRecognitions.Add(new() { Id = Guid.NewGuid(), CompanyId = company.CompanyId,
                MovementId = output.ReceiptMovementId, LineNumber = 1, CurrencyId = currency, GenerationId = generationId,
                UnitCostPerKg = unit, RemainingQuantityKg = remaining, ConsumedQuantityKg = consumed,
                SourceVersion = sourceVersion, ResultJson = JsonSerializer.Serialize(new { output, remaining, consumed }, Json),
                RecordedAtUtc = clock.GetUtcNow(), CorrelationId = metadata.CorrelationId });
        }
    }

    private readonly Dictionary<(Guid, Guid, Guid), InventoryStore.CostPool> pools = [];
    private async Task ApplyPoolEffectAsync(Guid location, Guid product, Guid currency, decimal kg, decimal unit, bool reverse, CancellationToken token)
    {
        var key = (location, product, currency);
        if (!pools.TryGetValue(key, out var pool)) pool = await InventoryStore.ReadCostPoolAsync(db, company, location, product, currency, token);
        if (!pool.IsConsistent || unit <= 0m) throw Error(503, "inventory_manufacturing_cost_ledger_unreadable");
        var after = AverageCostCalculator.Apply(pool.Position, new(reverse ? AverageCostMovementKind.PurchaseReturn : AverageCostMovementKind.PurchaseReceipt,
            kg, pool.CostedKg, unit));
        if (after.Status != AverageCostStatus.Known) throw Error(503, "inventory_manufacturing_cost_ledger_unreadable");
        var version = await InventoryStore.WriteCostPoolAsync(db, company, clock, location, product, currency, pool, after, token);
        pools[key] = pool with { Position = after, Version = version, CostedKg = pool.CostedKg + (reverse ? -kg : kg),
            TlRow = currency == SettingsCurrencyIdentitiesV1.TurkishLira ? db.AverageCostLedger.Local.Single(row => row.LocationId == location && row.ProductId == product) : null,
            ForeignRow = currency != SettingsCurrencyIdentitiesV1.TurkishLira ? db.CurrencyAverageCostLedger.Local.Single(row => row.LocationId == location && row.ProductId == product && row.CurrencyId == currency) : null };
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
            var disposition = await InventoryLabelDispositionReader.ReadAsync(db, company.CompanyId, output.LabelId, token);
            if (fact is null || disposition.Output is not { } state || state.ManufacturingOrderId != command.ManufacturingOrderId
                || state.OutputId != output.OutputId || state.ProducedKg - state.ReversedKg != output.NetQuantityKg)
                throw Error(503, "inventory_manufacturing_cost_physical_source_unreadable");
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
                AggregateVersion = row.Version, Sequence = row.Version, PayloadJson = json, OccurredAtUtc = occurredAt,
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
        return (JsonSerializer.Deserialize<InventoryManufacturingCostGenerationResultV1>(row.ResultJson, Json)
            ?? throw Error(503, "inventory_manufacturing_cost_unavailable")) with { IsReplay = true, OriginalCorrelationId = row.CorrelationId };
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
                && (output.TlUnitCostPerKg is not > 0m || output.TlAmount is not > 0m))
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
        return result;
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
