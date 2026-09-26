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
using GoymenERP.Modules.Inventory.Domain.Allocations;
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
/// Inventory's single-use participant in the central host's already-open definite-shipment
/// transaction. It flushes Inventory rows but never commits, rolls back or closes borrowed handles.
/// </summary>
public sealed class InventoryDefiniteShipmentParticipant : IInventoryDefiniteShipmentParticipantV1
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly InventoryDbContext context;
    private readonly InventoryStore store;
    private readonly DbConnection connection;
    private readonly DbTransaction transaction;
    private readonly Guid companyId;
    private readonly TimeProvider clock;
    private readonly Func<CancellationToken, Task>? onStaged;
    private int started;
    private bool disposed;

    private InventoryDefiniteShipmentParticipant(InventoryDbContext context, InventoryStore store,
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

    public static async Task<InventoryDefiniteShipmentParticipant> EnlistAsync(
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
            var store = new InventoryStore(context, company, audit, new InventoryKilogramScale(settings), effectiveClock);
            return new(context, store, connection, transaction, companyId, effectiveClock, onStaged);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async Task<InventoryDefiniteShipmentResultV1> StageAsync(
        InventoryDefiniteShipmentRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        EnsureActive();
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException("An Inventory participant can execute only one command attempt.");
        }

        var quantity = InventoryDefiniteShipmentContractV1.Validate(request, metadata, companyId);
        var keyHash = Hash(metadata.IdempotencyKey);
        var payloadHash = RequestHash(request);
        var existing = await context.Commands.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CommandName == InventoryDefiniteShipmentOutboxV1.SourceOperation
            && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.ActorId, metadata.ActorId, StringComparison.Ordinal)
                || !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw Refused(409, InventoryDefiniteShipmentCodesV1.IdempotencyConflict);
            }

            var replay = JsonSerializer.Deserialize<InventoryDefiniteShipmentResultV1>(existing.ResultJson, Json)
                ?? throw Refused(503, InventoryDefiniteShipmentCodesV1.Unavailable);
            return replay with { IsReplay = true, OriginalCorrelationId = existing.CorrelationId };
        }

        var allocationRow = await context.Allocations.SingleOrDefaultAsync(row => row.Id == request.AllocationId,
            cancellationToken) ?? throw Refused(404, InventoryDefiniteShipmentCodesV1.InvalidReference);
        var labelRow = await context.Labels.SingleOrDefaultAsync(row => row.Id == request.LabelId, cancellationToken)
            ?? throw Refused(404, InventoryDefiniteShipmentCodesV1.InvalidReference);
        var expectedAllocationVersion = long.Parse(request.ExpectedAllocationVersion, CultureInfo.InvariantCulture);
        var expectedLabelVersion = long.Parse(request.ExpectedLabelVersion, CultureInfo.InvariantCulture);
        if (labelRow.Version != expectedLabelVersion)
        {
            throw Refused(409, InventoryDefiniteShipmentCodesV1.LabelVersionConflict);
        }

        MaterialLabel label;
        StockPosition position;
        StockAllocation allocation;
        try
        {
            label = await RestoreLabelAsync(labelRow, cancellationToken);
            position = new StockPosition(companyId,
                new StockOwner((StockPartyKind)allocationRow.OwnerKind, allocationRow.OwnerId),
                new StockCustodian((StockPartyKind)allocationRow.CustodianKind, allocationRow.CustodianId),
                allocationRow.LocationId, label, (BlockState)allocationRow.Block);
            allocation = StockAllocation.Restore(allocationRow.Id, position,
                Kilograms.Positive(allocationRow.AllocatedKg),
                Kilograms.Of(allocationRow.RemainingKg), allocationRow.Version,
                (StockAllocationStatus)allocationRow.Status);
        }
        catch (Exception exception) when (exception is InventoryRuleException or ArgumentException)
        {
            throw Refused(503, InventoryDefiniteShipmentCodesV1.Unavailable);
        }

        // An allocation stored before the Sales ownership gate (or any legacy row) may still name a position this
        // company does not own and hold; a definite shipment never consumes one (#479 review P2-2).
        // TODO(#66/5830378162): replace with the accepted customer-owned/fason rule once decided.
        if (!InventorySalesOwnershipGuardV1.IsCompanyOwnedAndHeld(position.Owner.Kind, position.Owner.Id,
                position.Custodian.Kind, position.Custodian.Id, companyId))
        {
            throw Refused(409, InventoryDefiniteShipmentCodesV1.OwnerNotCompany);
        }

        if (!label.IsReleasableForProductionOrShipment)
        {
            throw Refused(409, InventoryDefiniteShipmentCodesV1.LabelNotReleasable);
        }
        try
        {
            allocation.Consume(expectedAllocationVersion, request.LabelId, quantity);
        }
        catch (StockAllocationException exception)
        {
            throw exception.Failure switch
            {
                StockAllocationFailure.VersionConflict => Refused(409, InventoryDefiniteShipmentCodesV1.AllocationVersionConflict),
                StockAllocationFailure.StateConflict => Refused(409, InventoryDefiniteShipmentCodesV1.AllocationState),
                StockAllocationFailure.LabelMismatch => Refused(409, InventoryDefiniteShipmentCodesV1.AllocationLabelMismatch),
                StockAllocationFailure.Insufficient => Refused(409, InventoryDefiniteShipmentCodesV1.AllocationInsufficient),
                _ => Refused(503, InventoryDefiniteShipmentCodesV1.Unavailable),
            };
        }

        var balance = await context.Balances.SingleOrDefaultAsync(row =>
            row.LabelId == position.LabelId && row.OwnerKind == (int)position.Owner.Kind
            && row.OwnerId == position.Owner.Id && row.CustodianKind == (int)position.Custodian.Kind
            && row.CustodianId == position.Custodian.Id && row.LocationId == position.LocationId
            && row.ProductId == position.ProductId && row.Block == (int)BlockState.Free, cancellationToken);
        if (balance is null || balance.QuantityKg < quantity.Value)
        {
            throw Refused(409, InventoryDefiniteShipmentCodesV1.PhysicalStockInsufficient);
        }

        var movement = new StockMovement(request.MovementId, companyId, StockMovementKind.Issue,
            request.OccurredAtUtc,
            new StockSourceReference("logistics.definite-shipment", request.ShipmentId,
                request.LoadingInstructionLineId),
            [new StockMovementLine(position, StockDirection.Out, quantity)], CostResult.NotCalculated);
        InventoryWriteOutcome movementOutcome;
        try
        {
            movementOutcome = await store.StageMovementAsync(movement, metadata, recordAudit: false, cancellationToken);
        }
        catch (InventoryRuleException exception) when (exception.Code == InventoryRuleCodes.NegativePhysicalStock)
        {
            throw Refused(409, InventoryDefiniteShipmentCodesV1.PhysicalStockInsufficient);
        }
        catch (InventoryStoreException exception) when (exception.Code is
            InventoryStoreCodes.MovementIdConflict or InventoryStoreCodes.DuplicateSource)
        {
            throw Refused(409, InventoryDefiniteShipmentCodesV1.IdempotencyConflict);
        }
        catch (InventoryStoreException exception) when (exception.Code == InventoryStoreCodes.ConcurrentChange)
        {
            throw Refused(409, InventoryDefiniteShipmentCodesV1.ConcurrentChange);
        }
        catch (Exception exception) when (exception is InventoryStoreException or InventoryRuleException)
        {
            throw Refused(503, InventoryDefiniteShipmentCodesV1.Unavailable);
        }

        if (movementOutcome != InventoryWriteOutcome.Recorded)
        {
            throw Refused(409, InventoryDefiniteShipmentCodesV1.IdempotencyConflict);
        }

        var versionBefore = allocationRow.Version;
        var remainingBefore = allocationRow.RemainingKg;
        allocationRow.RemainingKg = allocation.Remaining.Value;
        allocationRow.Status = (int)allocation.Status;
        allocationRow.Version = allocation.Version;
        allocationRow.UpdatedAtUtc = clock.GetUtcNow();

        var outboxMessageId = Guid.NewGuid();
        var result = new InventoryDefiniteShipmentResultV1(companyId, request.ShipmentId,
            allocation.Id, allocation.Version.ToString(CultureInfo.InvariantCulture), allocation.Remaining.ToString(),
            label.Id, labelRow.Version.ToString(CultureInfo.InvariantCulture), request.MovementId, outboxMessageId,
            IsReplay: false, OriginalCorrelationId: metadata.CorrelationId);
        var payload = new InventoryDefiniteShipmentRecordedV1(companyId, request.ShipmentId,
            request.LoadingInstructionId, request.LoadingInstructionLineId, allocation.Id,
            result.AllocationVersion, label.Id, result.LabelVersion, request.MovementId, quantity.ToString(),
            request.OccurredAtUtc, metadata.CorrelationId, metadata.CausationId);
        context.OutboxMessages.Add(new InventoryOutboxRow
        {
            Id = outboxMessageId,
            CompanyId = companyId,
            EventType = InventoryDefiniteShipmentOutboxV1.EventType,
            SchemaVersion = InventoryDefiniteShipmentOutboxV1.SchemaVersion,
            SourceOperation = InventoryDefiniteShipmentOutboxV1.SourceOperation,
            AggregateType = InventoryDefiniteShipmentOutboxV1.AggregateType,
            AggregateId = allocation.Id,
            AggregateVersion = allocation.Version,
            Sequence = allocation.Version,
            PayloadJson = JsonSerializer.Serialize(payload, Json),
            OccurredAtUtc = request.OccurredAtUtc,
            CorrelationId = metadata.CorrelationId,
            CausationId = metadata.CausationId,
            RecordedAtUtc = clock.GetUtcNow(),
        });
        context.Commands.Add(new InventoryCommandRow
        {
            CompanyId = companyId,
            CommandName = InventoryDefiniteShipmentOutboxV1.SourceOperation,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = request.ShipmentId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = metadata.CorrelationId,
            RecordedAtUtc = clock.GetUtcNow(),
        });
        store.AuditDefiniteShipment(metadata, companyId, allocation.Id, versionBefore, allocation.Version,
            JsonSerializer.Serialize(new { version = versionBefore, remainingKg = remainingBefore }, Json),
            JsonSerializer.Serialize(new
            {
                version = allocation.Version,
                remainingKg = allocation.Remaining.Value,
                request.ShipmentId,
                request.MovementId,
                outboxMessageId,
            }, Json));

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
            throw Refused(409, InventoryDefiniteShipmentCodesV1.ConcurrentChange);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw Refused(409, InventoryDefiniteShipmentCodesV1.ConcurrentChange);
        }
        catch (DbUpdateException)
        {
            throw Refused(503, InventoryDefiniteShipmentCodesV1.Unavailable);
        }

        return result;
    }

    private async Task<MaterialLabel> RestoreLabelAsync(LabelRow row, CancellationToken cancellationToken)
    {
        var required = JsonSerializer.Deserialize<string[]>(row.RequiredDocumentsJson, Json)
            ?? throw Refused(503, InventoryDefiniteShipmentCodesV1.Unavailable);
        var documents = await context.LabelDocuments.AsNoTracking().Where(item => item.LabelId == row.Id)
            .OrderBy(item => item.Sequence).Select(item =>
                new LabelDocumentAttachment(item.Kind, item.AttachedAtUtc, item.AttachedBy))
            .ToListAsync(cancellationToken);
        return MaterialLabel.Restore(row.Id, row.CompanyId, row.Code, row.ProductId, row.LotId,
            required, row.HeatNumber, row.CoilNumber, [], documents);
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

    private static string RequestHash(InventoryDefiniteShipmentRequestV1 request) => Hash(
        JsonSerializer.Serialize(new
        {
            request.CompanyId,
            request.ShipmentId,
            request.LoadingInstructionId,
            request.LoadingInstructionLineId,
            request.AllocationId,
            request.ExpectedAllocationVersion,
            request.LabelId,
            request.ExpectedLabelVersion,
            request.MovementId,
            request.QuantityKg,
            request.OccurredAtUtc,
        }, Json));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToUpperInvariant();

    private static InventoryDefiniteShipmentException Refused(int status, string code) =>
        InventoryDefiniteShipmentException.Refused(status, code);

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await context.DisposeAsync();
    }
}
