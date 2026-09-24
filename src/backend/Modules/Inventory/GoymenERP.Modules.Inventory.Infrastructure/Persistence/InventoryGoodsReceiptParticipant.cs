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
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Lots;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// Inventory's single-use participant in an already-open goods-receipt transaction. It stages and
/// flushes Inventory rows but never commits, rolls back or closes the host's connection/transaction.
/// </summary>
public sealed class InventoryGoodsReceiptParticipant : IInventoryGoodsReceiptParticipantV1
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

    private InventoryGoodsReceiptParticipant(InventoryDbContext context, InventoryStore store,
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

    public static async Task<InventoryGoodsReceiptParticipant> EnlistAsync(DbConnection connection,
        DbTransaction transaction, Guid companyId, ISettingsPrerequisiteProvider settings,
        CancellationToken cancellationToken = default, TimeProvider? clock = null,
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
            return new(context, store, connection, transaction, companyId, effectiveClock, onStaged);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async Task<InventoryGoodsReceiptResultV1> StageAsync(InventoryGoodsReceiptRequestV1 request,
        InventoryCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        EnsureActive();
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            throw new InvalidOperationException("An Inventory participant can execute only one command attempt.");
        }

        var quantity = InventoryGoodsReceiptContractV1.Validate(request, metadata, companyId);
        var keyHash = Hash(metadata.IdempotencyKey);
        var payloadHash = RequestHash(request);
        var existing = await context.Commands.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CommandName == InventoryGoodsReceiptContractV1.CommandName
            && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.ActorId, metadata.ActorId, StringComparison.Ordinal)
                || !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw Refused(409, InventoryGoodsReceiptCodesV1.IdempotencyConflict);
            }

            var replay = JsonSerializer.Deserialize<InventoryGoodsReceiptResultV1>(existing.ResultJson, Json)
                ?? throw Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
            return replay with { IsReplay = true, OriginalCorrelationId = existing.CorrelationId };
        }

        if (await context.Labels.AsNoTracking().AnyAsync(row => row.Id == request.LabelId, cancellationToken))
        {
            throw Refused(409, InventoryGoodsReceiptCodesV1.LabelAlreadyRegistered);
        }

        MaterialLot lot;
        MaterialLabel label;
        StockPosition position;
        StockMovement movement;
        try
        {
            lot = new MaterialLot(request.LotId, companyId, request.ProductId,
                request.HeatNumber, request.CoilNumber);
            label = new MaterialLabel(request.LabelId, companyId, request.LabelCode, request.ProductId,
                request.LotId, request.RequiredDocuments, request.HeatNumber, request.CoilNumber);
            position = new StockPosition(companyId, new StockOwner(request.OwnerKind, request.OwnerId),
                new StockCustodian(request.CustodianKind, request.CustodianId), request.LocationId,
                label, request.InitialBlock);
            movement = new StockMovement(request.MovementId, companyId, StockMovementKind.Receipt,
                request.OccurredAtUtc,
                new StockSourceReference(InventoryGoodsReceiptContractV1.SourceKind,
                    request.AcceptanceId, request.PurchaseOrderLineId),
                [new StockMovementLine(position, StockDirection.In, quantity)], CostResult.NotCalculated);
        }
        catch (Exception exception) when (exception is InventoryRuleException or ArgumentException)
        {
            throw Refused(400, InventoryGoodsReceiptCodesV1.InvalidRequest);
        }

        try
        {
            if (await store.SaveLotAsync(lot, [label], metadata, cancellationToken) != InventoryWriteOutcome.Recorded)
            {
                throw Refused(409, InventoryGoodsReceiptCodesV1.LabelAlreadyRegistered);
            }

            if (await store.RecordMovementAsync(movement, metadata, cancellationToken) != InventoryWriteOutcome.Recorded)
            {
                throw Refused(409, InventoryGoodsReceiptCodesV1.IdempotencyConflict);
            }
        }
        catch (InventoryGoodsReceiptException)
        {
            throw;
        }
        catch (InventoryStoreException exception)
        {
            throw StoreRefusal(exception.Code);
        }
        catch (InventoryRuleException)
        {
            throw Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
        }

        var result = new InventoryGoodsReceiptResultV1(companyId, request.AcceptanceId,
            request.PurchaseOrderId, request.PurchaseOrderLineId, request.ExpectedPurchaseOrderVersion,
            request.ProductId, request.ExpectedProductVersion, request.PurchaseUnitId, lot.Id, label.Id,
            "1", movement.Id, quantity.ToString(), IsReplay: false,
            OriginalCorrelationId: metadata.CorrelationId);
        context.Commands.Add(new InventoryCommandRow
        {
            CompanyId = companyId,
            CommandName = InventoryGoodsReceiptContractV1.CommandName,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = request.AcceptanceId,
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
            throw Refused(409, InventoryGoodsReceiptCodesV1.ConcurrentChange);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw Refused(409, InventoryGoodsReceiptCodesV1.ConcurrentChange);
        }
        catch (DbUpdateException)
        {
            throw Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
        }

        return result;
    }

    private static InventoryGoodsReceiptException StoreRefusal(string code) => code switch
    {
        InventoryStoreCodes.LabelConflict or InventoryStoreCodes.LabelCodeTaken
            or InventoryStoreCodes.LotConflict or InventoryStoreCodes.LotProductMismatch
            or InventoryStoreCodes.LotNotFound => Refused(409, InventoryGoodsReceiptCodesV1.LotOrLabelConflict),
        InventoryStoreCodes.MovementIdConflict or InventoryStoreCodes.DuplicateSource
            or InventoryStoreCodes.SourceGrainMismatch => Refused(409, InventoryGoodsReceiptCodesV1.IdempotencyConflict),
        InventoryStoreCodes.ConcurrentChange => Refused(409, InventoryGoodsReceiptCodesV1.ConcurrentChange),
        InventoryScaleCodes.ScaleExceeded => Refused(400, InventoryGoodsReceiptCodesV1.InvalidQuantity),
        InventoryScaleCodes.ProfileUnusable => Refused(503, InventoryGoodsReceiptCodesV1.QuantityProfileUnavailable),
        _ when code.StartsWith("settings_", StringComparison.Ordinal) =>
            Refused(503, InventoryGoodsReceiptCodesV1.QuantityProfileUnavailable),
        _ => Refused(503, InventoryGoodsReceiptCodesV1.Unavailable),
    };

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

    private static string RequestHash(InventoryGoodsReceiptRequestV1 request) => Hash(
        JsonSerializer.Serialize(new
        {
            request.CompanyId,
            request.AcceptanceId,
            request.PurchaseOrderId,
            request.PurchaseOrderLineId,
            request.ExpectedPurchaseOrderVersion,
            request.ProductId,
            request.ExpectedProductVersion,
            request.PurchaseUnitId,
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

    private static InventoryGoodsReceiptException Refused(int status, string code) =>
        InventoryGoodsReceiptException.Refused(status, code);

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        await context.DisposeAsync();
    }
}
