using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Quantities;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// Inventory's single-use participant in Logistics' already-open customer-return approval
/// transaction. It flushes Inventory rows but never commits, rolls back or closes borrowed handles.
/// </summary>
public sealed class InventoryCustomerReturnParticipant : IInventoryCustomerReturnParticipantV1
{
    private const string DefiniteShipmentSourceKind = "logistics.definite-shipment";
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private readonly InventoryDbContext context;
    private readonly InventoryStore store;
    private readonly AuditEventCollector audit;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly Guid companyId;
    private readonly TimeProvider clock;
    private readonly Func<CancellationToken, Task>? onStaged;
    private int started;
    private bool disposed;

    private InventoryCustomerReturnParticipant(
        InventoryDbContext context,
        InventoryStore store,
        AuditEventCollector audit,
        DbConnection connection,
        DbTransaction transaction,
        Guid companyId,
        TimeProvider clock,
        Func<CancellationToken, Task>? onStaged)
    {
        this.context = context;
        this.store = store;
        this.audit = audit;
        this.connection = connection;
        this.transaction = transaction;
        this.companyId = companyId;
        this.clock = clock;
        this.onStaged = onStaged;
    }

    public static async Task<InventoryCustomerReturnParticipant> EnlistAsync(
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
            var store = new InventoryStore(
                context,
                company,
                audit,
                new InventoryKilogramScale(settings),
                effectiveClock);
            return new InventoryCustomerReturnParticipant(
                context, store, audit, connection, transaction, companyId, effectiveClock, onStaged);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async Task<InventoryCustomerReturnResultV1> StageAsync(
        InventoryCustomerReturnRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        Begin();
        var quantity = InventoryCustomerReturnContractV1.Validate(request, metadata, companyId);
        var keyHash = Hash(metadata.IdempotencyKey);
        var payloadHash = RequestHash(request);
        var existing = await context.Commands.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CommandName == InventoryCustomerReturnContractV1.CommandName
            && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.ActorId, metadata.ActorId, StringComparison.Ordinal)
                || !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw Refused(409, InventoryCustomerReturnCodesV1.IdempotencyConflict);
            }

            var replay = JsonSerializer.Deserialize<InventoryCustomerReturnResultV1>(existing.ResultJson, Json)
                ?? throw Refused(503, InventoryCustomerReturnCodesV1.Unavailable);
            return replay with { IsReplay = true, OriginalCorrelationId = existing.CorrelationId };
        }

        var trace = await OriginalShipmentTraceAsync(request.SourceShipmentId,
            request.LoadingInstructionLineId, cancellationToken);
        var returned = await ReturnedQuantityAsync(trace, cancellationToken);
        if (returned + quantity.Value > trace.QuantityKg)
        {
            throw Refused(409, InventoryCustomerReturnCodesV1.ReturnExceedsShipment);
        }

        var versionBefore = trace.LabelRow.Version;
        var movement = new StockMovement(
            request.InventoryMovementId,
            companyId,
            StockMovementKind.Return,
            request.OccurredAtUtc,
            new StockSourceReference(
                InventoryCustomerReturnContractV1.SourceKind,
                trace.MovementId,
                request.CustomerReturnId),
            [new StockMovementLine(trace.Position, StockDirection.In, quantity)],
            CostResult.NotCalculated);
        try
        {
            var outcome = await store.StageMovementAsync(
                movement, metadata, recordAudit: false, cancellationToken);
            if (outcome != InventoryWriteOutcome.Recorded)
            {
                throw Refused(409, InventoryCustomerReturnCodesV1.IdempotencyConflict);
            }
        }
        catch (InventoryCustomerReturnException)
        {
            throw;
        }
        catch (InventoryStoreException exception)
        {
            throw StoreRefusal(exception.Code);
        }
        catch (InventoryRuleException)
        {
            throw Refused(503, InventoryCustomerReturnCodesV1.Unavailable);
        }

        var outboxMessageId = Guid.NewGuid();
        var labelVersion = trace.LabelRow.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var result = new InventoryCustomerReturnResultV1(
            companyId,
            request.CustomerReturnId,
            request.SourceShipmentId,
            movement.Id,
            trace.MovementId,
            trace.Position.LabelId,
            labelVersion,
            quantity.ToString(),
            outboxMessageId,
            IsReplay: false,
            OriginalCorrelationId: metadata.CorrelationId);
        var payload = new InventoryCustomerReturnRecordedV1(
            companyId,
            request.CustomerReturnId,
            request.SourceShipmentId,
            request.LoadingInstructionLineId,
            trace.MovementId,
            movement.Id,
            trace.Position.LabelId,
            labelVersion,
            trace.Position.ProductId,
            trace.Position.Owner.Kind,
            trace.Position.Owner.Id,
            trace.Position.Custodian.Kind,
            trace.Position.Custodian.Id,
            trace.Position.LocationId,
            quantity.ToString(),
            request.OccurredAtUtc,
            metadata.CorrelationId,
            metadata.CausationId);
        context.OutboxMessages.Add(new InventoryOutboxRow
        {
            Id = outboxMessageId,
            CompanyId = companyId,
            EventType = InventoryCustomerReturnOutboxV1.EventType,
            SchemaVersion = InventoryCustomerReturnOutboxV1.SchemaVersion,
            SourceOperation = InventoryCustomerReturnOutboxV1.SourceOperation,
            AggregateType = InventoryCustomerReturnOutboxV1.AggregateType,
            AggregateId = trace.Position.LabelId,
            AggregateVersion = trace.LabelRow.Version,
            Sequence = trace.LabelRow.Version,
            PayloadJson = JsonSerializer.Serialize(payload, Json),
            OccurredAtUtc = request.OccurredAtUtc,
            CorrelationId = metadata.CorrelationId,
            CausationId = metadata.CausationId,
            RecordedAtUtc = clock.GetUtcNow(),
        });
        context.Commands.Add(new InventoryCommandRow
        {
            CompanyId = companyId,
            CommandName = InventoryCustomerReturnContractV1.CommandName,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = request.CustomerReturnId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = metadata.CorrelationId,
            RecordedAtUtc = clock.GetUtcNow(),
        });
        audit.Enqueue(new AuditEventDraft(
            companyId,
            InventoryCustomerReturnOutboxV1.EventType,
            InventoryCustomerReturnOutboxV1.AggregateType,
            trace.Position.LabelId.ToString("D"),
            "ReturnFromCustomer",
            versionBefore,
            trace.LabelRow.Version,
            AuditActorTypes.User,
            metadata.ActorId,
            metadata.PermissionCode,
            metadata.RoleCodes,
            JsonSerializer.Serialize(new
            {
                labelVersion = versionBefore,
                shippedKg = trace.QuantityKg,
                returnedKg = returned,
            }, Json),
            JsonSerializer.Serialize(new
            {
                labelVersion = trace.LabelRow.Version,
                returnedKg = returned + quantity.Value,
                request.SourceShipmentId,
                request.CustomerReturnId,
                originalShipmentMovementId = trace.MovementId,
                inventoryMovementId = movement.Id,
                outboxMessageId,
            }, Json),
            "Onaylı müşteri iadesinin özgün etikete stok dönüşü",
            ApprovalContextJson: null,
            AuditAiInvolvement.None,
            AiContextJson: null,
            metadata.SourceChannel,
            metadata.ClientIpAddress,
            metadata.UserAgent,
            metadata.CorrelationId,
            metadata.CausationId,
            metadata.IdempotencyKey));

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
            throw Refused(409, InventoryCustomerReturnCodesV1.ConcurrentChange);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw Refused(409, InventoryCustomerReturnCodesV1.ConcurrentChange);
        }
        catch (DbUpdateException)
        {
            throw Refused(503, InventoryCustomerReturnCodesV1.Unavailable);
        }

        return result;
    }

    private async Task<ShipmentTrace> OriginalShipmentTraceAsync(
        Guid shipmentId,
        Guid loadingInstructionLineId,
        CancellationToken cancellationToken)
    {
        var movements = await context.Movements.AsNoTracking().Where(row =>
            row.Kind == (int)StockMovementKind.Issue
            && row.SourceKind == DefiniteShipmentSourceKind
            && row.SourceId == shipmentId
            && row.SourceLineId == loadingInstructionLineId
            && row.SourceGrain == (int)StockSourceGrain.Line).ToListAsync(cancellationToken);
        if (movements.Count == 0)
        {
            throw Refused(409, InventoryCustomerReturnCodesV1.ShipmentNotRecorded);
        }

        if (movements.Count != 1)
        {
            throw Refused(503, InventoryCustomerReturnCodesV1.ShipmentTraceInvalid);
        }

        var movement = movements[0];
        var lines = await context.MovementLines.AsNoTracking()
            .Where(row => row.MovementId == movement.Id)
            .ToListAsync(cancellationToken);
        if (lines.Count != 1)
        {
            throw Refused(503, InventoryCustomerReturnCodesV1.ShipmentTraceInvalid);
        }

        var line = lines[0];
        if (line.Direction != (int)StockDirection.Out
            || line.Block != (int)BlockState.Free
            || line.QuantityKg <= 0m
            || line.LabelId == Guid.Empty || line.ProductId == Guid.Empty
            || line.OwnerId == Guid.Empty || line.CustodianId == Guid.Empty || line.LocationId == Guid.Empty
            || !Enum.IsDefined((StockPartyKind)line.OwnerKind)
            || !Enum.IsDefined((StockPartyKind)line.CustodianKind))
        {
            throw Refused(503, InventoryCustomerReturnCodesV1.ShipmentTraceInvalid);
        }

        var labelRow = await context.Labels.SingleOrDefaultAsync(row => row.Id == line.LabelId, cancellationToken)
            ?? throw Refused(503, InventoryCustomerReturnCodesV1.ShipmentTraceInvalid);
        if (labelRow.ProductId != line.ProductId)
        {
            throw Refused(503, InventoryCustomerReturnCodesV1.ShipmentTraceInvalid);
        }

        MaterialLabel label;
        try
        {
            label = await RestoreLabelAsync(labelRow, cancellationToken);
        }
        catch (Exception exception) when (exception is InventoryRuleException or ArgumentException or JsonException)
        {
            throw Refused(503, InventoryCustomerReturnCodesV1.ShipmentTraceInvalid);
        }

        var position = new StockPosition(
            companyId,
            new StockOwner((StockPartyKind)line.OwnerKind, line.OwnerId),
            new StockCustodian((StockPartyKind)line.CustodianKind, line.CustodianId),
            line.LocationId,
            label,
            BlockState.Free);
        return new ShipmentTrace(movement.Id, line.QuantityKg, position, labelRow);
    }

    private async Task<decimal> ReturnedQuantityAsync(ShipmentTrace trace, CancellationToken cancellationToken)
    {
        var movements = await context.Movements.AsNoTracking().Where(row =>
            row.Kind == (int)StockMovementKind.Return
            && row.SourceKind == InventoryCustomerReturnContractV1.SourceKind
            && row.SourceId == trace.MovementId).ToListAsync(cancellationToken);
        if (movements.Count == 0)
        {
            return 0m;
        }

        var movementIds = movements.Select(row => row.Id).ToArray();
        var lines = await context.MovementLines.AsNoTracking()
            .Where(row => movementIds.Contains(row.MovementId))
            .ToListAsync(cancellationToken);
        foreach (var movement in movements)
        {
            var movementLines = lines.Where(row => row.MovementId == movement.Id).ToArray();
            if (movement.SourceLineId is null || movement.SourceGrain != (int)StockSourceGrain.Line
                || movementLines.Length != 1 || !Matches(movementLines[0], trace.Position))
            {
                throw Refused(503, InventoryCustomerReturnCodesV1.ShipmentTraceInvalid);
            }
        }

        return lines.Sum(row => row.QuantityKg);
    }

    private static bool Matches(MovementLineRow row, StockPosition position) =>
        row.Direction == (int)StockDirection.In
        && row.OwnerKind == (int)position.Owner.Kind && row.OwnerId == position.Owner.Id
        && row.CustodianKind == (int)position.Custodian.Kind && row.CustodianId == position.Custodian.Id
        && row.LocationId == position.LocationId && row.LabelId == position.LabelId
        && row.ProductId == position.ProductId && row.Block == (int)BlockState.Free
        && row.QuantityKg > 0m;

    private async Task<MaterialLabel> RestoreLabelAsync(LabelRow row, CancellationToken cancellationToken)
    {
        var required = JsonSerializer.Deserialize<string[]>(row.RequiredDocumentsJson, Json)
            ?? throw Refused(503, InventoryCustomerReturnCodesV1.ShipmentTraceInvalid);
        var documents = await context.LabelDocuments.AsNoTracking()
            .Where(item => item.LabelId == row.Id)
            .OrderBy(item => item.Sequence)
            .Select(item => new LabelDocumentAttachment(item.Kind, item.AttachedAtUtc, item.AttachedBy))
            .ToListAsync(cancellationToken);
        return MaterialLabel.Restore(row.Id, row.CompanyId, row.Code, row.ProductId, row.LotId,
            row.ProductVersion, required, row.HeatNumber, row.CoilNumber, [], documents);
    }

    private static InventoryCustomerReturnException StoreRefusal(string code) => code switch
    {
        InventoryStoreCodes.LabelNotRegistered =>
            Refused(503, InventoryCustomerReturnCodesV1.ShipmentTraceInvalid),
        InventoryStoreCodes.MovementIdConflict or InventoryStoreCodes.DuplicateSource
            or InventoryStoreCodes.SourceGrainMismatch =>
            Refused(409, InventoryCustomerReturnCodesV1.IdempotencyConflict),
        InventoryStoreCodes.ConcurrentChange =>
            Refused(409, InventoryCustomerReturnCodesV1.ConcurrentChange),
        InventoryScaleCodes.ScaleExceeded =>
            Refused(400, InventoryCustomerReturnCodesV1.InvalidQuantity),
        InventoryScaleCodes.ProfileUnusable =>
            Refused(503, InventoryCustomerReturnCodesV1.QuantityProfileUnavailable),
        _ when code.StartsWith("settings_", StringComparison.Ordinal) =>
            Refused(503, InventoryCustomerReturnCodesV1.QuantityProfileUnavailable),
        _ => Refused(503, InventoryCustomerReturnCodesV1.Unavailable),
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

    private static string RequestHash(InventoryCustomerReturnRequestV1 request) =>
        Hash(JsonSerializer.Serialize(request, Json));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static InventoryCustomerReturnException Refused(int status, string code) =>
        InventoryCustomerReturnException.Refused(status, code);

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await context.DisposeAsync();
    }

    private sealed record ShipmentTrace(
        Guid MovementId,
        decimal QuantityKg,
        StockPosition Position,
        LabelRow LabelRow);
}
