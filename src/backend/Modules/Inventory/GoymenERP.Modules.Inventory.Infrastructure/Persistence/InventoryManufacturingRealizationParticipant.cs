using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Balances;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// Inventory's single-use participant in a Manufacturing transaction. It stages and flushes only
/// Inventory facts. The host remains the sole owner of commit, rollback and borrowed handles.
/// </summary>
public sealed class InventoryManufacturingRealizationParticipant
    : IInventoryManufacturingRealizationParticipantV1
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private readonly InventoryDbContext context;
    private readonly InventoryStore store;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly Guid companyId;
    private readonly TimeProvider clock;
    private readonly Func<CancellationToken, Task>? onStaged;
    private int started;
    private bool disposed;

    private InventoryManufacturingRealizationParticipant(InventoryDbContext context, InventoryStore store,
        DbConnection connection, DbTransaction transaction, Guid companyId, TimeProvider clock,
        Func<CancellationToken, Task>? onStaged)
    {
        this.context = context;
        this.store = store;
        this.connection = connection;
        this.transaction = transaction;
        this.companyId = companyId;
        this.clock = clock;
        this.onStaged = onStaged;
    }

    public static async Task<InventoryManufacturingRealizationParticipant> EnlistAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid companyId,
        ISettingsPrerequisiteProvider settings,
        CancellationToken cancellationToken = default,
        TimeProvider? clock = null,
        Func<CancellationToken, Task>? onStaged = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(settings);
        if (companyId == Guid.Empty || connection.State != ConnectionState.Open
            || !ReferenceEquals(transaction.Connection, connection))
        {
            throw new InvalidOperationException("Inventory requires a company and the owner's open transaction.");
        }

        var company = new CompanyContext();
        company.Set(companyId);
        var audit = new AuditEventCollector();
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(connection, contextOwnsConnection: false)
            .AddInterceptors(new AuditSaveChangesInterceptor(audit, company))
            .Options;
        var context = new InventoryDbContext(options, company);
        try
        {
            await context.Database.UseTransactionAsync(transaction, cancellationToken);
            var effectiveClock = clock ?? TimeProvider.System;
            var store = new InventoryStore(context, company, audit,
                new InventoryKilogramScale(settings), effectiveClock);
            return new InventoryManufacturingRealizationParticipant(
                context, store, connection, transaction, companyId, effectiveClock, onStaged);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async Task<InventoryManufacturingInputIssueResultV1> StageInputIssueAsync(
        InventoryManufacturingInputIssueRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        Begin();
        var quantity = InventoryManufacturingRealizationContractV1.Validate(request, metadata, companyId);
        var keyHash = Hash(metadata.IdempotencyKey);
        var payloadHash = RequestHash(request);
        if (await FindCommandAsync(InventoryManufacturingRealizationContractV1.InputIssueCommandName,
                keyHash, cancellationToken) is { } existing)
        {
            EnsureReplay(existing, metadata, payloadHash);
            var replay = JsonSerializer.Deserialize<InventoryManufacturingInputIssueResultV1>(existing.ResultJson, Json)
                ?? throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
            var replayPosition = replay.ParentPosition
                ?? await RecoverInputIssuePositionAsync(request, replay, quantity, cancellationToken);
            EnsurePositionSnapshot(replayPosition);
            return replay with
            {
                ParentPosition = replayPosition,
                IsReplay = true,
                OriginalCorrelationId = existing.CorrelationId,
            };
        }

        var parent = await LoadParentAsync(request.ParentLabelId, request.ExpectedParentLabelVersion,
            requireReleasable: true, cancellationToken);
        var position = await CurrentFreePositionAsync(parent.Label, quantity, cancellationToken);
        var movement = new StockMovement(request.MovementId, companyId, StockMovementKind.Issue,
            request.OccurredAtUtc,
            new StockSourceReference(InventoryManufacturingRealizationContractV1.InputIssueSourceKind,
                request.ManufacturingOrderId, request.IssueId),
            [new StockMovementLine(position, StockDirection.Out, quantity)], CostResult.NotCalculated);
        await RecordMovementAsync(movement, metadata, InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient,
            cancellationToken);

        var result = new InventoryManufacturingInputIssueResultV1(companyId, request.ManufacturingOrderId,
            request.IssueId, request.ManufacturingOrderVersionAfter, parent.Label.Id,
            parent.Row.Version.ToString(CultureInfo.InvariantCulture), movement.Id, quantity.ToString(),
            PositionSnapshot(position), IsReplay: false, OriginalCorrelationId: metadata.CorrelationId);
        await SaveCommandAsync(InventoryManufacturingRealizationContractV1.InputIssueCommandName,
            keyHash, payloadHash, request.IssueId, result, metadata, cancellationToken);
        return result;
    }

    public async Task<InventoryManufacturingInputReturnResultV1> StageInputReturnAsync(
        InventoryManufacturingInputReturnRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        Begin();
        var quantity = InventoryManufacturingRealizationContractV1.Validate(request, metadata, companyId);
        var keyHash = Hash(metadata.IdempotencyKey);
        var payloadHash = RequestHash(request);
        if (await FindCommandAsync(InventoryManufacturingRealizationContractV1.InputReturnCommandName,
                keyHash, cancellationToken) is { } existing)
        {
            EnsureReplay(existing, metadata, payloadHash);
            var replay = JsonSerializer.Deserialize<InventoryManufacturingInputReturnResultV1>(existing.ResultJson, Json)
                ?? throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
            return replay with { IsReplay = true, OriginalCorrelationId = existing.CorrelationId };
        }

        var parent = await LoadParentAsync(request.ParentLabelId, request.ExpectedParentLabelVersion,
            requireReleasable: false, cancellationToken);
        var trace = await InputTraceAsync(request.ManufacturingOrderId, parent.Label,
            InventoryManufacturingRealizationCodesV1.ReturnExceedsIssued, cancellationToken);
        if (trace.IssuedKg <= 0m || trace.ReturnedKg + quantity.Value > trace.IssuedKg)
        {
            throw Refused(409, InventoryManufacturingRealizationCodesV1.ReturnExceedsIssued);
        }

        var movement = new StockMovement(request.MovementId, companyId, StockMovementKind.Return,
            request.OccurredAtUtc,
            new StockSourceReference(InventoryManufacturingRealizationContractV1.InputReturnSourceKind,
                request.ManufacturingOrderId, request.ReturnId),
            [new StockMovementLine(trace.Position, StockDirection.In, quantity)], CostResult.NotCalculated);
        await RecordMovementAsync(movement, metadata, InventoryManufacturingRealizationCodesV1.Unavailable,
            cancellationToken);

        var result = new InventoryManufacturingInputReturnResultV1(companyId, request.ManufacturingOrderId,
            request.ReturnId, request.ManufacturingOrderVersionAfter, parent.Label.Id,
            parent.Row.Version.ToString(CultureInfo.InvariantCulture), movement.Id, quantity.ToString(),
            IsReplay: false, OriginalCorrelationId: metadata.CorrelationId);
        await SaveCommandAsync(InventoryManufacturingRealizationContractV1.InputReturnCommandName,
            keyHash, payloadHash, request.ReturnId, result, metadata, cancellationToken);
        return result;
    }

    public async Task<InventoryManufacturingOutputResultV1> StageOutputAsync(
        InventoryManufacturingOutputRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        Begin();
        var quantity = InventoryManufacturingRealizationContractV1.Validate(request, metadata, companyId);
        var keyHash = Hash(metadata.IdempotencyKey);
        var payloadHash = RequestHash(request);
        if (await FindCommandAsync(InventoryManufacturingRealizationContractV1.OutputCommandName,
                keyHash, cancellationToken) is { } existing)
        {
            EnsureReplay(existing, metadata, payloadHash);
            var replay = JsonSerializer.Deserialize<InventoryManufacturingOutputResultV1>(existing.ResultJson, Json)
                ?? throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
            return replay with { IsReplay = true, OriginalCorrelationId = existing.CorrelationId };
        }

        var parent = await LoadParentAsync(request.ParentLabelId, request.ExpectedParentLabelVersion,
            requireReleasable: false, cancellationToken);
        // Manufacturing itself permits an output only for an issued planned roll. Inventory keeps
        // the same invariant independently from its own physical trace; a declaration cannot mint
        // stock merely because it names a registered label.
        var trace = await InputTraceAsync(request.ManufacturingOrderId, parent.Label,
            InventoryManufacturingRealizationCodesV1.ParentInputNotIssued, cancellationToken);
        if (request.OwnerKind != trace.Position.Owner.Kind || request.OwnerId != trace.Position.Owner.Id
            || request.CustodianKind != trace.Position.Custodian.Kind
            || request.CustodianId != trace.Position.Custodian.Id)
        {
            throw Refused(409, InventoryManufacturingRealizationCodesV1.ParentLabelContextConflict);
        }

        if (await context.Labels.AsNoTracking().AnyAsync(row => row.Id == request.LabelId, cancellationToken))
        {
            throw Refused(409, InventoryManufacturingRealizationCodesV1.OutputLabelAlreadyRegistered);
        }

        MaterialLot lot;
        MaterialLabel label;
        StockMovement movement;
        try
        {
            lot = new MaterialLot(request.LotId, companyId, request.ProductId,
                request.HeatNumber, request.CoilNumber);
            label = new MaterialLabel(request.LabelId, companyId, request.LabelCode, request.ProductId,
                request.LotId, request.RequiredDocuments, request.HeatNumber, request.CoilNumber);
            var position = new StockPosition(companyId,
                new StockOwner(request.OwnerKind, request.OwnerId),
                new StockCustodian(request.CustodianKind, request.CustodianId),
                request.LocationId, label, request.InitialBlock);
            movement = new StockMovement(request.MovementId, companyId, StockMovementKind.Receipt,
                request.OccurredAtUtc,
                new StockSourceReference(InventoryManufacturingRealizationContractV1.OutputSourceKind,
                    request.ManufacturingOrderId, request.OutputId),
                [new StockMovementLine(position, StockDirection.In, quantity)], CostResult.NotCalculated);
        }
        catch (Exception exception) when (exception is InventoryRuleException or ArgumentException)
        {
            throw Refused(400, InventoryManufacturingRealizationCodesV1.InvalidRequest);
        }

        try
        {
            if (await store.SaveLotAsync(lot, [label], metadata, cancellationToken) != InventoryWriteOutcome.Recorded)
            {
                throw Refused(409, InventoryManufacturingRealizationCodesV1.OutputLabelAlreadyRegistered);
            }

            if (await store.RecordMovementAsync(movement, metadata, cancellationToken) != InventoryWriteOutcome.Recorded)
            {
                throw Refused(409, InventoryManufacturingRealizationCodesV1.IdempotencyConflict);
            }
        }
        catch (InventoryManufacturingRealizationException)
        {
            throw;
        }
        catch (InventoryStoreException exception)
        {
            throw StoreRefusal(exception.Code, InventoryManufacturingRealizationCodesV1.Unavailable);
        }
        catch (InventoryRuleException)
        {
            throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        var result = new InventoryManufacturingOutputResultV1(companyId, request.ManufacturingOrderId,
            request.OutputId, request.ManufacturingOrderVersionAfter, parent.Label.Id,
            parent.Row.Version.ToString(CultureInfo.InvariantCulture), request.OutputClass, request.ProductId,
            request.ExpectedProductVersion, lot.Id, label.Id,
            (await context.Labels.AsNoTracking().Where(row => row.Id == label.Id).Select(row => row.Version)
                .SingleAsync(cancellationToken)).ToString(CultureInfo.InvariantCulture),
            movement.Id, quantity.ToString(), IsReplay: false, OriginalCorrelationId: metadata.CorrelationId);
        await SaveCommandAsync(InventoryManufacturingRealizationContractV1.OutputCommandName,
            keyHash, payloadHash, request.OutputId, result, metadata, cancellationToken);
        return result;
    }

    private async Task<ParentLabel> LoadParentAsync(Guid labelId, string expectedVersion,
        bool requireReleasable, CancellationToken cancellationToken)
    {
        var row = await context.Labels.SingleOrDefaultAsync(candidate => candidate.Id == labelId, cancellationToken)
            ?? throw Refused(404, InventoryManufacturingRealizationCodesV1.InvalidReference);
        if (row.Version != long.Parse(expectedVersion, CultureInfo.InvariantCulture))
        {
            throw Refused(409, InventoryManufacturingRealizationCodesV1.LabelVersionConflict);
        }

        MaterialLabel label;
        try
        {
            label = await RestoreLabelAsync(row, cancellationToken);
        }
        catch (Exception exception) when (exception is InventoryRuleException or ArgumentException or JsonException)
        {
            throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        if (requireReleasable && !label.IsReleasableForProductionOrShipment)
        {
            throw Refused(409, InventoryManufacturingRealizationCodesV1.ParentLabelNotReleasable);
        }

        return new ParentLabel(row, label);
    }

    private async Task<StockPosition> CurrentFreePositionAsync(MaterialLabel label, Kilograms quantity,
        CancellationToken cancellationToken)
    {
        var rows = await context.Balances.Where(row => row.LabelId == label.Id).ToListAsync(cancellationToken);
        ValidateStoredPositions(label, rows);
        var free = rows.Where(row => row.Block == (int)BlockState.Free && row.QuantityKg > 0m).ToArray();
        if (free.Length != 1)
        {
            throw free.Length == 0
                ? Refused(409, InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient)
                : Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        var row = free[0];
        if (row.QuantityKg < quantity.Value)
        {
            throw Refused(409, InventoryManufacturingRealizationCodesV1.PhysicalStockInsufficient);
        }

        return Position(row, label);
    }

    private async Task<InputTrace> InputTraceAsync(Guid orderId, MaterialLabel label, string missingIssueCode,
        CancellationToken cancellationToken)
    {
        var issueRows = await (from movement in context.Movements
                join line in context.MovementLines on movement.Id equals line.MovementId
                where movement.Kind == (int)StockMovementKind.Issue
                    && movement.SourceKind == InventoryManufacturingRealizationContractV1.InputIssueSourceKind
                    && movement.SourceId == orderId && movement.SourceLineId != null
                    && movement.SourceGrain == (int)StockSourceGrain.Line && line.LabelId == label.Id
                    && line.Direction == (int)StockDirection.Out
                select line)
            .ToListAsync(cancellationToken);
        if (issueRows.Count == 0)
        {
            throw Refused(409, missingIssueCode);
        }

        var positions = issueRows.Select(row => Position(row, label)).Distinct().ToArray();
        if (positions.Length != 1)
        {
            throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        var returned = await (from movement in context.Movements
                join line in context.MovementLines on movement.Id equals line.MovementId
                where movement.Kind == (int)StockMovementKind.Return
                    && movement.SourceKind == InventoryManufacturingRealizationContractV1.InputReturnSourceKind
                    && movement.SourceId == orderId && movement.SourceLineId != null
                    && movement.SourceGrain == (int)StockSourceGrain.Line && line.LabelId == label.Id
                    && line.Direction == (int)StockDirection.In
                select line.QuantityKg)
            .SumAsync(cancellationToken);
        return new InputTrace(positions[0], issueRows.Sum(row => row.QuantityKg), returned);
    }

    private async Task<InventoryManufacturingPositionSnapshotV1> RecoverInputIssuePositionAsync(
        InventoryManufacturingInputIssueRequestV1 request,
        InventoryManufacturingInputIssueResultV1 replay,
        Kilograms quantity,
        CancellationToken cancellationToken)
    {
        // manufacturing.input.issue.v1 results written before ParentPosition was added still point
        // to the immutable Inventory movement that owns the exact physical context. Recover only
        // from that fact; never infer a Company-owned position or rewrite the accepted result.
        if (replay.CompanyId != companyId
            || replay.CompanyId != request.CompanyId
            || replay.ManufacturingOrderId != request.ManufacturingOrderId
            || replay.IssueId != request.IssueId
            || replay.ManufacturingOrderVersion != request.ManufacturingOrderVersionAfter
            || replay.ParentLabelId != request.ParentLabelId
            || replay.MovementId != request.MovementId
            || !string.Equals(replay.QuantityKg, quantity.ToString(), StringComparison.Ordinal))
        {
            throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        var movement = await context.Movements.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CompanyId == companyId
            && row.Id == request.MovementId
            && row.Kind == (int)StockMovementKind.Issue
            && row.SourceKind == InventoryManufacturingRealizationContractV1.InputIssueSourceKind
            && row.SourceId == request.ManufacturingOrderId
            && row.SourceLineId == request.IssueId
            && row.SourceGrain == (int)StockSourceGrain.Line,
            cancellationToken);
        if (movement is null)
        {
            throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        var lines = await context.MovementLines.AsNoTracking()
            .Where(row => row.CompanyId == companyId && row.MovementId == movement.Id)
            .ToListAsync(cancellationToken);
        if (lines.Count != 1)
        {
            throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        var line = lines[0];
        if (line.Direction != (int)StockDirection.Out
            || line.LabelId != request.ParentLabelId
            || line.ProductId == Guid.Empty
            || line.Block != (int)BlockState.Free
            || line.QuantityKg != quantity.Value)
        {
            throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        var snapshot = new InventoryManufacturingPositionSnapshotV1(
            (StockPartyKind)line.OwnerKind,
            line.OwnerId,
            (StockPartyKind)line.CustodianKind,
            line.CustodianId,
            line.LocationId);
        EnsurePositionSnapshot(snapshot);
        return snapshot;
    }

    private void ValidateStoredPositions(MaterialLabel label, IReadOnlyList<BalanceRow> rows)
    {
        try
        {
            _ = StockLedger.FromStoredBalances(rows.Select(row =>
                new StockBalance(Position(row, label), Kilograms.Of(row.QuantityKg))));
        }
        catch (Exception exception) when (exception is InventoryRuleException or ArgumentException)
        {
            throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }
    }

    private static InventoryManufacturingPositionSnapshotV1 PositionSnapshot(StockPosition position) =>
        new InventoryManufacturingPositionSnapshotV1(
        position.Owner.Kind, position.Owner.Id, position.Custodian.Kind, position.Custodian.Id,
        position.LocationId);

    private static void EnsurePositionSnapshot(InventoryManufacturingPositionSnapshotV1? snapshot)
    {
        try
        {
            if (snapshot is null || snapshot.LocationId == Guid.Empty)
            {
                throw new ArgumentException("Stored parent position is incomplete.");
            }

            _ = new StockOwner(snapshot.OwnerKind, snapshot.OwnerId);
            _ = new StockCustodian(snapshot.CustodianKind, snapshot.CustodianId);
        }
        catch (Exception exception) when (exception is InventoryRuleException or ArgumentException)
        {
            throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }
    }

    private StockPosition Position(BalanceRow row, MaterialLabel label) => new StockPosition(companyId,
        new StockOwner((StockPartyKind)row.OwnerKind, row.OwnerId),
        new StockCustodian((StockPartyKind)row.CustodianKind, row.CustodianId),
        row.LocationId, label, (BlockState)row.Block);

    private StockPosition Position(MovementLineRow row, MaterialLabel label) => new StockPosition(companyId,
        new StockOwner((StockPartyKind)row.OwnerKind, row.OwnerId),
        new StockCustodian((StockPartyKind)row.CustodianKind, row.CustodianId),
        row.LocationId, label, (BlockState)row.Block);

    private async Task<MaterialLabel> RestoreLabelAsync(LabelRow row, CancellationToken cancellationToken)
    {
        var required = JsonSerializer.Deserialize<string[]>(row.RequiredDocumentsJson, Json)
            ?? throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        var documents = await context.LabelDocuments.AsNoTracking().Where(item => item.LabelId == row.Id)
            .OrderBy(item => item.Sequence)
            .Select(item => new LabelDocumentAttachment(item.Kind, item.AttachedAtUtc, item.AttachedBy))
            .ToListAsync(cancellationToken);
        return MaterialLabel.Restore(row.Id, row.CompanyId, row.Code, row.ProductId, row.LotId,
            required, row.HeatNumber, row.CoilNumber, [], documents);
    }

    private async Task RecordMovementAsync(StockMovement movement, InventoryCommandMetadata metadata,
        string negativeStockCode, CancellationToken cancellationToken)
    {
        try
        {
            if (await store.RecordMovementAsync(movement, metadata, cancellationToken) != InventoryWriteOutcome.Recorded)
            {
                throw Refused(409, InventoryManufacturingRealizationCodesV1.IdempotencyConflict);
            }
        }
        catch (InventoryManufacturingRealizationException)
        {
            throw;
        }
        catch (InventoryRuleException exception) when (exception.Code == InventoryRuleCodes.NegativePhysicalStock)
        {
            throw Refused(409, negativeStockCode);
        }
        catch (InventoryStoreException exception)
        {
            throw StoreRefusal(exception.Code, negativeStockCode);
        }
        catch (InventoryRuleException)
        {
            throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }
    }

    private async Task<InventoryCommandRow?> FindCommandAsync(string commandName, string keyHash,
        CancellationToken cancellationToken) => await context.Commands.AsNoTracking().SingleOrDefaultAsync(row =>
        row.CommandName == commandName && row.IdempotencyKeyHash == keyHash, cancellationToken);

    private static void EnsureReplay(InventoryCommandRow existing, InventoryCommandMetadata metadata,
        string payloadHash)
    {
        if (!string.Equals(existing.ActorId, metadata.ActorId, StringComparison.Ordinal)
            || !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            throw Refused(409, InventoryManufacturingRealizationCodesV1.IdempotencyConflict);
        }
    }

    private async Task SaveCommandAsync<T>(string commandName, string keyHash, string payloadHash,
        Guid targetId, T result, InventoryCommandMetadata metadata, CancellationToken cancellationToken)
    {
        context.Commands.Add(new InventoryCommandRow
        {
            CompanyId = companyId,
            CommandName = commandName,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = targetId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = metadata.CorrelationId,
            RecordedAtUtc = clock.GetUtcNow(),
        });
        if (onStaged is not null)
        {
            await onStaged(cancellationToken);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Refused(409, InventoryManufacturingRealizationCodesV1.ConcurrentChange);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw Refused(409, InventoryManufacturingRealizationCodesV1.ConcurrentChange);
        }
        catch (DbUpdateException)
        {
            throw Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable);
        }
    }

    private static InventoryManufacturingRealizationException StoreRefusal(string code,
        string negativeStockCode) => code switch
    {
        InventoryStoreCodes.LabelNotRegistered => Refused(404, InventoryManufacturingRealizationCodesV1.InvalidReference),
        InventoryStoreCodes.LabelConflict or InventoryStoreCodes.LabelCodeTaken
            or InventoryStoreCodes.LotConflict or InventoryStoreCodes.LotProductMismatch
            or InventoryStoreCodes.LotNotFound =>
            Refused(409, InventoryManufacturingRealizationCodesV1.OutputLotOrLabelConflict),
        InventoryStoreCodes.MovementIdConflict or InventoryStoreCodes.DuplicateSource
            or InventoryStoreCodes.SourceGrainMismatch =>
            Refused(409, InventoryManufacturingRealizationCodesV1.IdempotencyConflict),
        InventoryStoreCodes.ConcurrentChange =>
            Refused(409, InventoryManufacturingRealizationCodesV1.ConcurrentChange),
        InventoryScaleCodes.ScaleExceeded =>
            Refused(400, InventoryManufacturingRealizationCodesV1.InvalidQuantity),
        InventoryScaleCodes.ProfileUnusable =>
            Refused(503, InventoryManufacturingRealizationCodesV1.QuantityProfileUnavailable),
        _ when code.StartsWith("settings_", StringComparison.Ordinal) =>
            Refused(503, InventoryManufacturingRealizationCodesV1.QuantityProfileUnavailable),
        _ => Refused(503, InventoryManufacturingRealizationCodesV1.Unavailable),
    };

    private void Begin()
    {
        EnsureActive();
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException("An Inventory participant can execute only one command attempt.");
        }
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (connection.State != ConnectionState.Open || !ReferenceEquals(transaction.Connection, connection)
            || context.Database.CurrentTransaction is not { } enlisted
            || !ReferenceEquals(enlisted.GetDbTransaction(), transaction))
        {
            throw new InvalidOperationException("The owner's Inventory transaction is no longer active.");
        }
    }

    private static string RequestHash<T>(T request) => Hash(JsonSerializer.Serialize(request, Json));

    private static string RequestHash(InventoryManufacturingOutputRequestV1 request) => Hash(
        JsonSerializer.Serialize(new
        {
            request.CompanyId,
            request.ManufacturingOrderId,
            request.OutputId,
            request.ManufacturingOrderVersionAfter,
            request.ParentLabelId,
            request.ExpectedParentLabelVersion,
            request.OutputClass,
            request.ProductId,
            request.ExpectedProductVersion,
            request.LotId,
            request.LabelId,
            request.LabelCode,
            request.HeatNumber,
            request.CoilNumber,
            RequiredDocuments = request.RequiredDocuments.Order(StringComparer.Ordinal),
            request.OwnerKind,
            request.OwnerId,
            request.CustodianKind,
            request.CustodianId,
            request.LocationId,
            request.InitialBlock,
            request.MovementId,
            request.QuantityKg,
            request.OccurredAtUtc,
        }, Json));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToUpperInvariant();

    private static InventoryManufacturingRealizationException Refused(int status, string code) =>
        InventoryManufacturingRealizationException.Refused(status, code);

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await context.DisposeAsync();
    }

    private sealed record ParentLabel(LabelRow Row, MaterialLabel Label);
    private sealed record InputTrace(StockPosition Position, decimal IssuedKg, decimal ReturnedKg);
}
