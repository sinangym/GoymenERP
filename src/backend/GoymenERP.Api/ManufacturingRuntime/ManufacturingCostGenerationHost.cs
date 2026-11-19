using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Api.ManufacturingRuntime;

internal delegate Task<IInventoryManufacturingCostGenerationParticipantV1> ManufacturingInventoryCostGenerationFactoryV1(
    DbConnection connection, DbTransaction transaction, Guid companyId, IAuditEventCollector audit,
    IAuditTransactionWriterV1 auditWriter, CancellationToken cancellationToken);

/// <summary>
/// URT-D36/56/58/67: the original physical close and its cost generation share one transaction. Pending costs carry
/// no price. Later recognition is the same close's technical completion; it never posts an Accounting voucher.
/// Accounting owns the consumed-output proposal, selected open period/date and different-person approval.
/// </summary>
internal sealed class ManufacturingCostGenerationHost(ManufacturingDbContext db, ManufacturingStore store,
    ICompanyContext company, ICurrentUser user, IErpAuthorityRevalidationService authority,
    IManufacturingScrapCostDistributionSourceV1 source, IManufacturingCostMovementReadPortV1 movements,
    IAuditEventCollector audit, IAuditTransactionWriterV1 auditWriter, TimeProvider clock,
    ManufacturingInventoryCostGenerationFactoryV1 factory)
    : IManufacturingClosedOrderCostRecognitionPortV1, IManufacturingOrderReopeningInventoryPortV1
{
    internal const string CalculatorVersion = "production-cost-rounding:v1";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    internal async Task StageClosedAsync(Guid orderId, ManufacturingCommandMetadata metadata, CancellationToken token)
    {
        var companyId = Company(metadata);
        var transaction = db.Database.CurrentTransaction ?? throw new InvalidOperationException("Kapanış maliyeti ortak işlem gerektirir.");
        var proposedGenerationId = Guid.NewGuid();
        try
        {
            await using var owner = await factory(db.Database.GetDbConnection(), transaction.GetDbTransaction(), companyId, audit, auditWriter, token);
            await StageAsync(orderId, null, null, "Üretim emri kapanışı", metadata with { Completion = null }, owner, token,
                proposedGenerationId: proposedGenerationId);
        }
        catch (InventoryManufacturingCostGenerationException fault) when (MayDeferInitialRecognition(fault))
        {
            // Owner guarantee: this exact C# refusal occurs during the complete output-read/effect preparation loop,
            // BEFORE every Save/audit/outbox write. Disposing that participant discards its tracker only; the physical
            // Manufacturing callback remains exactly once in this still-uncommitted transaction. No SQL/audit/commit
            // error or late recognition takes this path. URT-D67: money readiness is not a new physical-close rule.
            await using var pending = await factory(db.Database.GetDbConnection(), transaction.GetDbTransaction(), companyId, audit, auditWriter, token);
            try
            {
                await StageAsync(orderId, null, null, "Üretim emri kapanışı; stok maliyet etkisi kaynağı bekleniyor",
                    metadata with { Completion = null }, pending, token, forcePendingConsumptionProof: true, proposedGenerationId: proposedGenerationId);
            }
            catch (InventoryManufacturingCostGenerationException fallback) { throw Map(fallback); }
            catch (Exception fault) when (fault is DbException || IsCostGenerationSqlConcurrencyFailure(fault)) { throw Unavailable(); }
        }
        catch (InventoryManufacturingCostGenerationException fault) { throw Map(fault); }
        catch (Exception fault) when (fault is DbException || IsCostGenerationSqlConcurrencyFailure(fault)) { throw Unavailable(); }
    }

    internal static bool MayDeferInitialRecognition(InventoryManufacturingCostGenerationException fault) =>
        fault.Status == 503 && fault.Code == "inventory_manufacturing_cost_consumption_source_unreadable";

    public async Task<ManufacturingCostGenerationV1> RecognizeAsync(Guid orderId, long expectedOrderVersion,
        long expectedGenerationRevision, string reason, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken)
    {
        var companyId = Company(metadata);
        if (db.Database.CurrentTransaction is not null || auditWriter.HasPendingDrafts)
            throw Unavailable(); // A refused entry never clears another caller's pending state.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await using var owner = await factory(db.Database.GetDbConnection(), transaction.GetDbTransaction(), companyId, audit, auditWriter, cancellationToken);
            var result = await StageAsync(orderId, expectedOrderVersion, expectedGenerationRevision, reason, metadata, owner, cancellationToken);
            await FenceAsync(companyId, metadata, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (InventoryManufacturingCostGenerationException fault)
        {
            await RollbackAsync(transaction); throw Map(fault);
        }
        catch (Exception fault) when (IsCostGenerationSqlConcurrencyFailure(fault)) { await RollbackAsync(transaction); throw Concurrent(); }
        catch (DbException) { await RollbackAsync(transaction); throw Unavailable(); }
        catch { await RollbackAsync(transaction); throw; }
        finally { auditWriter.DiscardPending(); db.ChangeTracker.Clear(); }
    }

    public async Task<ProductionOrder> ReopenAsync(ManufacturingOrderReopeningCommandV1 command,
        Func<CancellationToken, Task<ProductionOrder>> writeReopening, ManufacturingCommandMetadata metadata, CancellationToken cancellationToken)
    {
        var companyId = Company(metadata);
        if (command.CompanyId != companyId || db.Database.CurrentTransaction is not null || auditWriter.HasPendingDrafts) throw Unavailable();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var order = await store.FindOrderAsync(command.OrderId, cancellationToken) ?? throw Unavailable();
            order.RequireReopenable(command.ExpectedOrderVersion);
            await FenceAsync(companyId, metadata, cancellationToken);
            await using var owner = await factory(db.Database.GetDbConnection(), transaction.GetDbTransaction(), companyId, audit, auditWriter, cancellationToken);
            var previous = (await store.ReadCostGenerationsAsync(order.Id, cancellationToken)).LastOrDefault();
            var active = await owner.ReadActiveAsync(order.Id, cancellationToken);
            if (previous is null)
            {
                // Legacy orders with no money generation are reversible only when the owner independently proves absence.
                if (active is not null) throw Unavailable();
            }
            else
            {
                RequireMatching(previous, active, companyId, order.Id, order.Version);
                var result = await owner.ReverseAsync(new InventoryManufacturingCostGenerationReversalV1(companyId, order.Id, previous.GenerationId, previous.InventoryVersion,
                    Text(order.Version), command.Reason ?? throw Unavailable(), clock.GetUtcNow()), InventoryMetadata(metadata, previous.GenerationId, "reverse"), cancellationToken);
                RequireResult(result, companyId, order.Id, previous.GenerationId, order.Version, ManufacturingCostGenerationStateV1.Reversed,
                    previous.InputFingerprint, JsonSerializer.Deserialize<InventoryManufacturingCostGenerationResultV1>(previous.ResultJson, Json)!.Outputs);
                await store.AppendCostGenerationAsync(previous with
                {
                    Revision = previous.Revision + 1, InventoryVersion = result.GenerationVersion,
                    State = ManufacturingCostGenerationStateV1.Reversed, ResultJson = JsonSerializer.Serialize(result, Json),
                    RecordedAtUtc = clock.GetUtcNow(), Reason = command.Reason!, CorrelationId = metadata.CorrelationId.Trim(),
                }, previous.Revision, metadata with { Completion = null }, cancellationToken);
            }
            await FenceAsync(companyId, metadata, cancellationToken);
            var reopened = await writeReopening(cancellationToken);
            if (reopened is null || reopened.CompanyId != companyId || reopened.Id != order.Id
                || reopened.Version != command.ExpectedOrderVersion + 1 || reopened.Status != OrderStatus.Open) throw Unavailable();
            await FenceAsync(companyId, metadata, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return reopened;
        }
        catch (InventoryManufacturingCostGenerationException fault) { await RollbackAsync(transaction); throw Map(fault); }
        catch (Exception fault) when (IsCostGenerationSqlConcurrencyFailure(fault)) { await RollbackAsync(transaction); throw Concurrent(); }
        catch (DbException) { await RollbackAsync(transaction); throw Unavailable(); }
        catch { await RollbackAsync(transaction); throw; }
        finally { auditWriter.DiscardPending(); db.ChangeTracker.Clear(); }
    }

    private async Task<ManufacturingCostGenerationV1> StageAsync(Guid orderId, long? expectedOrderVersion,
        long? expectedGenerationRevision, string reason, ManufacturingCommandMetadata metadata,
        IInventoryManufacturingCostGenerationParticipantV1 owner, CancellationToken token,
        bool forcePendingConsumptionProof = false, Guid? proposedGenerationId = null)
    {
        var companyId = Company(metadata);
        var order = await store.FindOrderAsync(orderId, token) ?? throw Unavailable();
        if (expectedOrderVersion is { } expected && expected != order.Version)
            throw new GoymenERP.Modules.Manufacturing.Domain.Common.ManufacturingVersionConflictException(expected, order.Version);
        if (order.Status != OrderStatus.Closed) throw Unavailable();
        await FenceAsync(companyId, metadata, token);
        var previous = (await store.ReadCostGenerationsAsync(orderId, token)).LastOrDefault(item => item.ClosedOrderVersion == order.Version);
        if (expectedGenerationRevision is { } revision && revision != (previous?.Revision ?? 0)) throw Concurrent();
        var active = await owner.ReadActiveAsync(orderId, token);
        if (previous is null) { if (active is not null) throw Unavailable(); }
        else RequireMatching(previous, active, companyId, orderId, order.Version);

        var read = await source.ReadAsync(companyId, orderId, token)
            ?? new ManufacturingScrapCostDistributionSourceReadV1(ManufacturingScrapCostDistributionSourceStatusV1.Unavailable, companyId, orderId, null);
        if (read.CompanyId != companyId || read.OrderId != orderId) throw Unavailable();
        var stock = read.MovementEvidence ?? await movements.ReadAsync(companyId, orderId, token);
        if (stock is null || stock.CompanyId != companyId || stock.OrderId != orderId || string.IsNullOrWhiteSpace(stock.SourceVersion)
            || stock.Outputs is null || !stock.Outputs.Select(item => item.FactId).ToHashSet().SetEquals(
                order.Outputs.Where(item => item.Class != OutputClass.ProcessLoss).Select(item => item.OutputId))) throw Unavailable();
        var cost = await new ManufacturingProductionCostPort(new FrozenSource(read)).ReadAsync(companyId, orderId, token);
        var usdCost = read.UsdInput is null ? null : await new ManufacturingProductionCostPort(
            new FrozenSource(read with { Input = read.UsdInput })).ReadAsync(companyId, orderId, token);
        var state = cost.Status switch
        {
            ManufacturingScrapCostDistributionStatusV1.Available => ManufacturingCostGenerationStateV1.Recognized,
            ManufacturingScrapCostDistributionStatusV1.Pending => ManufacturingCostGenerationStateV1.Pending,
            _ => ManufacturingCostGenerationStateV1.Unreadable,
        };
        if (forcePendingConsumptionProof)
        {
            if (previous is not null || expectedOrderVersion is not null) throw Unavailable();
            state = ManufacturingCostGenerationStateV1.Pending;
        }
        // Only the calculator's exact input fingerprint represents a recognized cost. Owner outages carry no money.
        if (state == ManufacturingCostGenerationStateV1.Recognized && (read.Provenance is null
            || read.Provenance.OrderVersion != order.Version || read.Provenance.MovementSourceVersion != stock.SourceVersion)) throw Unavailable();
        var evidence = JsonSerializer.Serialize(new { read, cost, physical = stock.Outputs, consumptionProofPending = forcePendingConsumptionProof }, Json);
        var fingerprint = read.Provenance?.InputFingerprint ?? Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new { companyId, orderId, order.Version, stock.SourceVersion, state, cost.ReasonCode }, Json))).ToLowerInvariant();
        if (previous is { State: ManufacturingCostGenerationStateV1.Recognized })
        {
            if (previous.InputFingerprint != fingerprint) throw Concurrent();
            return previous; // Exact same recognized source has no second Inventory monetary effect.
        }
        if (previous is not null && previous.InputFingerprint == fingerprint && previous.State == state) return previous;
        var generationId = previous?.GenerationId ?? proposedGenerationId ?? Guid.NewGuid();
        var outputLines = new List<InventoryManufacturingCostGenerationOutputV1>();
        foreach (var output in order.Outputs.Where(item => item.Class != OutputClass.ProcessLoss))
        {
            var physical = stock.Outputs.Single(item => item.FactId == output.OutputId);
            if (physical.ParentLabelId != output.ParentRollId || physical.DeclaredMass != output.Mass
                || physical.LabelId == Guid.Empty || physical.MovementId == Guid.Empty) throw Unavailable();
            var net = order.NetMassOf(output.OutputId).Value;
            var line = cost.Lines.SingleOrDefault(item => item.OutputLabelId == physical.LabelId);
            var usdLine = usdCost?.Status == ManufacturingScrapCostDistributionStatusV1.Available
                ? usdCost.Lines.SingleOrDefault(item => item.OutputLabelId == physical.LabelId) : null;
            if (state == ManufacturingCostGenerationStateV1.Recognized && net > 0 && output.Class != OutputClass.Scrap
                && (line is null || line.Kilograms != net || line.RecordedUnitCostPerKgTl is null || line.RecordedAmountTl is null)) throw Unavailable();
            outputLines.Add(new InventoryManufacturingCostGenerationOutputV1(output.OutputId, physical.MovementId, physical.LabelId, net,
                state == ManufacturingCostGenerationStateV1.Recognized ? line?.RecordedUnitCostPerKgTl : null,
                state == ManufacturingCostGenerationStateV1.Recognized ? usdLine?.RecordedUnitCostPerKgTl : null,
                state == ManufacturingCostGenerationStateV1.Recognized ? line?.RecordedAmountTl : null));
        }
        await FenceAsync(companyId, metadata, token);
        var fresh = await store.FindOrderAsync(orderId, token);
        if (fresh is null || fresh.Version != order.Version || fresh.Status != OrderStatus.Closed) throw Concurrent();
        var staged = await owner.StageAsync(new InventoryManufacturingCostGenerationCommandV1(companyId, orderId, generationId, previous?.InventoryVersion ?? "0", Text(order.Version),
            (InventoryManufacturingCostGenerationStateV1)state, fingerprint, CalculatorVersion, evidence, outputLines, reason,
            clock.GetUtcNow()), InventoryMetadata(metadata, generationId, RecognitionOperation(fingerprint, previous?.InventoryVersion, state)), token);
        RequireResult(staged, companyId, orderId, generationId, order.Version, state, fingerprint, outputLines.Select(item =>
            new InventoryManufacturingCostGenerationOutputResultV1(item.OutputId, item.ReceiptMovementId, item.LabelId, item.NetQuantityKg,
                0, 0, item.TlUnitCostPerKg, null, null, item.UsdUnitCostPerKg, [], item.TlAmount)).ToArray());
        if (staged.IsReplay || staged.GenerationVersion == previous?.InventoryVersion) throw Unavailable();
        await FenceAsync(companyId, metadata, token);
        var generation = new ManufacturingCostGenerationV1(companyId, orderId, generationId, order.Version, (previous?.Revision ?? 0) + 1,
            staged.GenerationVersion, state, fingerprint, CalculatorVersion, evidence, JsonSerializer.Serialize(staged, Json),
            clock.GetUtcNow(), reason, metadata.CorrelationId.Trim());
        return await store.AppendCostGenerationAsync(generation, previous?.Revision ?? 0, metadata, token);
    }

    internal static void RequireResult(InventoryManufacturingCostGenerationResultV1? value, Guid companyId, Guid orderId,
        Guid generationId, long closedVersion, ManufacturingCostGenerationStateV1 state, string fingerprint,
        IReadOnlyList<InventoryManufacturingCostGenerationOutputResultV1> outputs)
    {
        if (value is null || value.CompanyId != companyId || value.ManufacturingOrderId != orderId || value.GenerationId != generationId
            || value.ClosedOrderVersion != Text(closedVersion) || (int)value.State != (int)state || value.InputFingerprint != fingerprint
            || string.IsNullOrWhiteSpace(value.GenerationVersion) || value.GenerationVersion.Length > 128 || value.Outputs is null
            || value.Outputs.Count != outputs.Count || value.Outputs.Any(item => item is null)
            || state is ManufacturingCostGenerationStateV1.Pending or ManufacturingCostGenerationStateV1.Unreadable
                && value.CostDifferenceEventId is not null
            || value.Outputs.Select(item => item.OutputId).Distinct().Count() != outputs.Count
            || value.Outputs.Any(item => !outputs.Any(expected => expected.OutputId == item.OutputId && expected.ReceiptMovementId == item.ReceiptMovementId
                && expected.LabelId == item.LabelId && expected.NetQuantityKg == item.NetQuantityKg)
                || !ExactOutputEvidence(item)
                || state == ManufacturingCostGenerationStateV1.Recognized && !outputs.Any(expected => expected.OutputId == item.OutputId
                    && expected.TlUnitCostPerKg == item.TlUnitCostPerKg && expected.UsdUnitCostPerKg == item.UsdUnitCostPerKg && expected.OriginalTlAmount == item.OriginalTlAmount)
                || state is ManufacturingCostGenerationStateV1.Pending or ManufacturingCostGenerationStateV1.Unreadable
                    && (item.TlUnitCostPerKg is not null || item.RemainingTlValue is not null || item.ConsumedTlDifference is not null
                        || item.UsdUnitCostPerKg is not null || item.OriginalTlAmount is not null || item.Positions.Count != 0
                        || item.PoolEffects is { Count: > 0 })))
            throw Unavailable();
    }

    private static bool ExactOutputEvidence(InventoryManufacturingCostGenerationOutputResultV1 output)
    {
        try
        {
            var remaining = InventoryExactCostRatioV1.Resolve(output.RemainingQuantityKg, output.RemainingQuantityKgRatio);
            var consumed = InventoryExactCostRatioV1.Resolve(output.ConsumedQuantityKg, output.ConsumedQuantityKgRatio);
            if (remaining.Sign < 0 || consumed.Sign < 0 || remaining.Add(consumed).CompareTo(
                    GoymenERP.Modules.Inventory.Domain.Costing.ExactCostRatio.Of(output.NetQuantityKg)) != 0 || output.Positions is null)
                return false;
            foreach (var position in output.Positions)
            {
                if (position is null || position.LocationId == Guid.Empty
                    || InventoryExactCostRatioV1.Resolve(position.QuantityKg, position.QuantityKgRatio).Sign < 0) return false;
                _ = InventoryExactCostRatioV1.Resolve(position.TlValue, position.TlValueRatio);
            }
            foreach (var pool in output.PoolEffects ?? [])
            {
                if (pool is null || pool.CurrencyId == Guid.Empty || pool.LocationId == Guid.Empty || pool.ProductId == Guid.Empty) return false;
                _ = InventoryExactCostRatioV1.Resolve(pool.QuantityKg, pool.QuantityKgRatio);
                _ = InventoryExactCostRatioV1.Resolve(pool.BeforeQuantityKg, pool.BeforeQuantityKgRatio);
                _ = InventoryExactCostRatioV1.Resolve(pool.AfterQuantityKg, pool.AfterQuantityKgRatio);
                _ = InventoryExactCostRatioV1.Resolve(pool.AuthoritativeValue, pool.AuthoritativeValueRatio);
                _ = InventoryExactCostRatioV1.Resolve(pool.RecordedPoolValueEffect, pool.RecordedPoolValueEffectRatio);
                _ = InventoryExactCostRatioV1.Resolve(pool.AverageRoundingDifference, pool.AverageRoundingDifferenceRatio);
            }
            return true;
        }
        catch (InvalidOperationException) { return false; }
    }

    private static void RequireMatching(ManufacturingCostGenerationV1 previous, InventoryManufacturingCostGenerationResultV1? active,
        Guid companyId, Guid orderId, long closedVersion)
    {
        if (previous.State == ManufacturingCostGenerationStateV1.Reversed || previous.ClosedOrderVersion != closedVersion
            || active is null || active.GenerationVersion != previous.InventoryVersion) throw Unavailable();
        var recorded = JsonSerializer.Deserialize<InventoryManufacturingCostGenerationResultV1>(previous.ResultJson, Json) ?? throw Unavailable();
        RequireResult(active, companyId, orderId, previous.GenerationId, closedVersion, previous.State, previous.InputFingerprint, recorded.Outputs);
    }

    private Guid Company(ManufacturingCommandMetadata metadata)
    {
        if (!company.IsResolved || company.CompanyId == Guid.Empty || !user.IsAuthenticated || user.UserId != metadata.ActorId
            || !user.CanAccessCompany(company.CompanyId)) throw new ManufacturingInventoryRealizationPortException(ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied);
        return company.CompanyId;
    }
    private async Task FenceAsync(Guid companyId, ManufacturingCommandMetadata metadata, CancellationToken token)
    {
        if (Company(metadata) != companyId) throw Unavailable();
        var scope = await authority.RevalidatePermissionScopeAsync(metadata.PermissionCode, companyId, token);
        if (!scope.IsGranted || !scope.IsCompanyWide || Company(metadata) != companyId)
            throw new ManufacturingInventoryRealizationPortException(ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied);
    }
    // URT-D67: a pending physical-close observation and its later recognition are different transitions,
    // even when the authoritative financial input fingerprint is unchanged. Keep the historical first
    // observation key; digest later transition identity so the complete Inventory key stays within 128 chars.
    internal static string RecognitionOperation(string fingerprint, string? expectedInventoryVersion,
        ManufacturingCostGenerationStateV1 state) => "recognize:" + (expectedInventoryVersion is null ? fingerprint
        : Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            keyVersion = 2, expectedInventoryVersion, state = (int)state, inputFingerprint = fingerprint,
        }, Json))).ToLowerInvariant());

    private static InventoryCommandMetadata InventoryMetadata(ManufacturingCommandMetadata value, Guid id, string operation) => new InventoryCommandMetadata(value.ActorId,
        value.PermissionCode, value.RoleCodes, value.CorrelationId, value.CausationId, $"manufacturing-cost:{id:N}:{operation}",
        value.SourceChannel, value.ClientIpAddress, value.UserAgent);
    private async Task RollbackAsync(IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); }
        catch { throw Unavailable(); }
    }
    internal static ManufacturingInventoryRealizationPortException Map(InventoryManufacturingCostGenerationException fault) =>
        new ManufacturingInventoryRealizationPortException(fault.Status == 409 ? ManufacturingApiStatus.Conflict : ManufacturingApiStatus.Unavailable,
            fault.Status == 409 && fault.Code == "inventory_manufacturing_cost_transfer_reversal_required"
                ? ManufacturingStoreCodes.CostTransferReversalRequired
                : fault.Status == 409 ? ManufacturingStoreCodes.ConcurrentChange : "cost_source_unavailable");
    internal static bool IsCostGenerationSqlConcurrencyFailure(Exception error) => error is SqlException { Number: 1205 or 1222 }
        || error is DbUpdateException { InnerException: SqlException { Number: 1205 or 1222 } }
        || error is InvalidOperationException { InnerException: SqlException { Number: 1205 or 1222 } }
        || error is InvalidOperationException
            { InnerException: DbUpdateException { InnerException: SqlException { Number: 1205 or 1222 } } };

    private static ManufacturingInventoryRealizationPortException Unavailable() => new ManufacturingInventoryRealizationPortException(ManufacturingApiStatus.Unavailable, "cost_source_unavailable");
    private static ManufacturingInventoryRealizationPortException Concurrent() => new ManufacturingInventoryRealizationPortException(ManufacturingApiStatus.Conflict, ManufacturingStoreCodes.ConcurrentChange);
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
    private sealed class FrozenSource(ManufacturingScrapCostDistributionSourceReadV1 value) : IManufacturingScrapCostDistributionSourceV1
    {
        public Task<ManufacturingScrapCostDistributionSourceReadV1> ReadAsync(Guid companyId, Guid orderId, CancellationToken cancellationToken) => Task.FromResult(value);
    }
}
