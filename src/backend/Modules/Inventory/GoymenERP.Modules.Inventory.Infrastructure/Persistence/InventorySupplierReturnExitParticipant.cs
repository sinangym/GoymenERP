using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.Modules.Audit.Application.Contracts;
using GoymenERP.Modules.Audit.Infrastructure.Persistence;
using GoymenERP.Modules.Audit.Infrastructure.Services;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain;
using GoymenERP.Modules.Inventory.Domain.Allocations;
using GoymenERP.Modules.Inventory.Domain.Costing;
using GoymenERP.Modules.Inventory.Domain.Labels;
using GoymenERP.Modules.Inventory.Domain.Movements;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// B-19 / SAT-D306: Inventory's single-use participant in the purchasing host's already-open supplier-return
/// transaction. It writes one exit movement of the original receipt's label from its free or blocked kilograms, never
/// more than that receipt brought in minus the earlier supplier returns of it (SAT-D306 (a)), and never below the
/// label's active allocations: KLN-D93 refuses that with its own code and never releases an allocation. It flushes
/// Inventory rows but never commits, rolls back or closes borrowed handles. The original receipt cost is carried in the
/// result, outbox and audit; the average-cost ledger is only read here, never written: SAT-D371 refuses a return whose
/// original cost would leave a cost difference against a readable position (SAT-D306 (c)) until the Finance
/// accounting-voucher spec names its account.
/// </summary>
public sealed class InventorySupplierReturnExitParticipant : IInventorySupplierReturnExitParticipantV1
{
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

    private InventorySupplierReturnExitParticipant(
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

    public static async Task<InventorySupplierReturnExitParticipant> EnlistAsync(
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
            return new InventorySupplierReturnExitParticipant(
                context, store, audit, connection, transaction, companyId, effectiveClock, onStaged);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async Task<InventorySupplierReturnExitResultV1> StageAsync(
        InventorySupplierReturnExitRequestV1 request,
        InventoryCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        Begin();
        var input = InventorySupplierReturnExitContractV1.Validate(request, metadata, companyId);
        var keyHash = Hash(metadata.IdempotencyKey);
        var payloadHash = RequestHash(request);
        var existing = await context.Commands.AsNoTracking().SingleOrDefaultAsync(row =>
            row.CommandName == InventorySupplierReturnExitContractV1.CommandName
            && row.IdempotencyKeyHash == keyHash, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.ActorId, metadata.ActorId, StringComparison.Ordinal)
                || !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
            {
                throw Refused(409, InventorySupplierReturnExitCodesV1.IdempotencyConflict);
            }

            var replay = JsonSerializer.Deserialize<InventorySupplierReturnExitResultV1>(existing.ResultJson, Json)
                ?? throw Refused(503, InventorySupplierReturnExitCodesV1.Unavailable);
            return replay with { IsReplay = true, OriginalCorrelationId = existing.CorrelationId };
        }

        var receipt = await OriginalReceiptTraceAsync(request.GoodsAcceptanceId, cancellationToken);
        var returned = await ReturnedQuantityAsync(receipt, cancellationToken);
        if (returned + input.Quantity.Value > receipt.QuantityKg)
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.ExitExceedsReceipt);
        }

        var source = await SourcePositionAsync(receipt, request.SourceBlock, cancellationToken);
        if (source.QuantityKg < input.Quantity.Value)
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.StockNotAvailable);
        }

        await RequireExitLeavesAllocationsAsync(receipt.LabelRow.Id, input.Quantity.Value, cancellationToken);
        await RequireNoUnpostedCostDifferenceAsync(receipt, source, input, cancellationToken);

        var versionBefore = receipt.LabelRow.Version;
        var movement = new StockMovement(
            request.InventoryMovementId,
            companyId,
            StockMovementKind.SupplierReturnExit,
            request.OccurredAtUtc,
            new StockSourceReference(
                InventorySupplierReturnExitContractV1.SourceKind,
                receipt.MovementId,
                request.SupplierReturnLineId),
            [new StockMovementLine(source.Position, StockDirection.Out, input.Quantity)],
            CostResult.NotCalculated);
        try
        {
            var outcome = await store.StageMovementAsync(
                movement, metadata, recordAudit: false, cancellationToken);
            if (outcome != InventoryWriteOutcome.Recorded)
            {
                throw Refused(409, InventorySupplierReturnExitCodesV1.IdempotencyConflict);
            }
        }
        catch (InventorySupplierReturnExitException)
        {
            throw;
        }
        catch (InventoryStoreException exception)
        {
            throw StoreRefusal(exception.Code);
        }
        catch (InventoryRuleException exception) when (exception.Code == InventoryRuleCodes.NegativePhysicalStock)
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.StockNotAvailable);
        }
        catch (InventoryRuleException)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.Unavailable);
        }

        var outboxMessageId = Guid.NewGuid();
        var labelVersion = receipt.LabelRow.Version.ToString(CultureInfo.InvariantCulture);
        var cost = input.OriginalUnitCostTryPerKg?.ToString("0.####", CultureInfo.InvariantCulture);
        var position = source.Position;
        var result = new InventorySupplierReturnExitResultV1(
            companyId,
            request.SupplierReturnId,
            request.SupplierReturnLineId,
            request.GoodsAcceptanceId,
            movement.Id,
            receipt.MovementId,
            position.LabelId,
            labelVersion,
            request.SourceBlock,
            input.Quantity.ToString(),
            cost,
            outboxMessageId,
            IsReplay: false,
            OriginalCorrelationId: metadata.CorrelationId);
        var payload = new InventorySupplierReturnExitRecordedV1(
            companyId,
            request.SupplierReturnId,
            request.SupplierReturnLineId,
            request.GoodsAcceptanceId,
            receipt.MovementId,
            movement.Id,
            position.LabelId,
            labelVersion,
            position.ProductId,
            position.Owner.Kind,
            position.Owner.Id,
            position.Custodian.Kind,
            position.Custodian.Id,
            position.LocationId,
            request.SourceBlock,
            input.Quantity.ToString(),
            cost,
            request.OccurredAtUtc,
            metadata.CorrelationId,
            metadata.CausationId);
        context.OutboxMessages.Add(new InventoryOutboxRow
        {
            Id = outboxMessageId,
            CompanyId = companyId,
            EventType = InventorySupplierReturnExitOutboxV1.EventType,
            SchemaVersion = InventorySupplierReturnExitOutboxV1.SchemaVersion,
            SourceOperation = InventorySupplierReturnExitOutboxV1.SourceOperation,
            AggregateType = InventorySupplierReturnExitOutboxV1.AggregateType,
            AggregateId = position.LabelId,
            AggregateVersion = receipt.LabelRow.Version,
            Sequence = receipt.LabelRow.Version,
            PayloadJson = JsonSerializer.Serialize(payload, Json),
            OccurredAtUtc = request.OccurredAtUtc,
            CorrelationId = metadata.CorrelationId,
            CausationId = metadata.CausationId,
            RecordedAtUtc = clock.GetUtcNow(),
        });
        context.Commands.Add(new InventoryCommandRow
        {
            CompanyId = companyId,
            CommandName = InventorySupplierReturnExitContractV1.CommandName,
            IdempotencyKeyHash = keyHash,
            ActorId = metadata.ActorId,
            PayloadHash = payloadHash,
            TargetId = request.SupplierReturnLineId,
            ResultJson = JsonSerializer.Serialize(result, Json),
            CorrelationId = metadata.CorrelationId,
            RecordedAtUtc = clock.GetUtcNow(),
        });
        audit.Enqueue(new AuditEventDraft(
            companyId,
            InventorySupplierReturnExitOutboxV1.EventType,
            InventorySupplierReturnExitOutboxV1.AggregateType,
            position.LabelId.ToString("D"),
            "ReturnToSupplier",
            versionBefore,
            receipt.LabelRow.Version,
            AuditActorTypes.User,
            metadata.ActorId,
            metadata.PermissionCode,
            metadata.RoleCodes,
            JsonSerializer.Serialize(new
            {
                labelVersion = versionBefore,
                receivedKg = receipt.QuantityKg,
                returnedKg = returned,
                sourceBlock = request.SourceBlock,
                positionKg = source.QuantityKg,
            }, Json),
            JsonSerializer.Serialize(new
            {
                labelVersion = receipt.LabelRow.Version,
                returnedKg = returned + input.Quantity.Value,
                positionKg = source.QuantityKg - input.Quantity.Value,
                originalUnitCostTryPerKg = cost,
                request.SupplierReturnId,
                request.SupplierReturnLineId,
                request.GoodsAcceptanceId,
                originalReceiptMovementId = receipt.MovementId,
                inventoryMovementId = movement.Id,
                outboxMessageId,
            }, Json),
            "Tedarikçi iadesinin stoktan çıkışı",
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
            throw Refused(409, InventorySupplierReturnExitCodesV1.ConcurrentChange);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.ConcurrentChange);
        }
        catch (DbUpdateException)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.Unavailable);
        }

        return result;
    }

    /// <summary>The one whole-acceptance receipt the goods-receipt participant wrote for this acceptance.</summary>
    private async Task<ReceiptTrace> OriginalReceiptTraceAsync(
        Guid goodsAcceptanceId,
        CancellationToken cancellationToken)
    {
        var movements = await context.Movements.AsNoTracking().Where(row =>
            row.Kind == (int)StockMovementKind.Receipt
            && row.SourceKind == InventoryGoodsReceiptContractV1.SourceKind
            && row.SourceId == goodsAcceptanceId).ToListAsync(cancellationToken);
        if (movements.Count == 0)
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.ReceiptNotRecorded);
        }

        if (movements.Count != 1 || movements[0].SourceLineId is not null
            || movements[0].SourceGrain != (int)StockSourceGrain.Whole)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        }

        var movement = movements[0];
        var lines = await context.MovementLines.AsNoTracking()
            .Where(row => row.MovementId == movement.Id)
            .ToListAsync(cancellationToken);
        if (lines.Count != 1)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        }

        var line = lines[0];
        if (line.Direction != (int)StockDirection.In
            || line.QuantityKg <= 0m
            || line.LabelId == Guid.Empty || line.ProductId == Guid.Empty
            || line.OwnerId == Guid.Empty
            || !Enum.IsDefined((StockPartyKind)line.OwnerKind))
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        }

        var labelRow = await context.Labels.SingleOrDefaultAsync(row => row.Id == line.LabelId, cancellationToken)
            ?? throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        if (labelRow.ProductId != line.ProductId)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        }

        MaterialLabel label;
        try
        {
            label = await RestoreLabelAsync(labelRow, cancellationToken);
        }
        catch (Exception exception) when (exception is InventoryRuleException or ArgumentException or JsonException)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        }

        return new ReceiptTrace(movement.Id, line.QuantityKg, (StockPartyKind)line.OwnerKind, line.OwnerId,
            line.ProductId, label, labelRow);
    }

    /// <summary>
    /// Where the received label stands now in the requested block. The label may have moved since the receipt, but its
    /// ownership may not have changed: a supplier return leaves only the stock the receipt brought in.
    /// </summary>
    private async Task<SourcePosition> SourcePositionAsync(
        ReceiptTrace receipt,
        BlockState block,
        CancellationToken cancellationToken)
    {
        var rows = await context.Balances.AsNoTracking()
            .Where(row => row.LabelId == receipt.LabelRow.Id && row.Block == (int)block && row.QuantityKg > 0m)
            .ToListAsync(cancellationToken);
        if (rows.Count != 1)
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.StockNotAvailable);
        }

        var row = rows[0];
        if (row.OwnerKind != (int)receipt.OwnerKind || row.OwnerId != receipt.OwnerId
            || row.ProductId != receipt.ProductId)
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.StockNotAvailable);
        }

        if (!Enum.IsDefined((StockPartyKind)row.CustodianKind) || row.CustodianId == Guid.Empty
            || row.LocationId == Guid.Empty)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        }

        StockPosition position;
        try
        {
            position = new StockPosition(
                companyId,
                new StockOwner(receipt.OwnerKind, receipt.OwnerId),
                new StockCustodian((StockPartyKind)row.CustodianKind, row.CustodianId),
                row.LocationId,
                receipt.Label,
                block);
        }
        catch (Exception exception) when (exception is InventoryRuleException or ArgumentException)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        }

        return new SourcePosition(position, row.QuantityKg);
    }

    /// <summary>SAT-D306 (a): what earlier supplier returns of this receipt already took out.</summary>
    private async Task<decimal> ReturnedQuantityAsync(ReceiptTrace receipt, CancellationToken cancellationToken)
    {
        var movements = await context.Movements.AsNoTracking().Where(row =>
            row.Kind == (int)StockMovementKind.SupplierReturnExit
            && row.SourceKind == InventorySupplierReturnExitContractV1.SourceKind
            && row.SourceId == receipt.MovementId).ToListAsync(cancellationToken);
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
                || movementLines.Length != 1 || !Matches(movementLines[0], receipt))
            {
                throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
            }
        }

        return lines.Sum(row => row.QuantityKg);
    }

    /// <summary>
    /// KLN-D93: a reservation is never silently broken. What stays on the label must cover its active allocations;
    /// otherwise the return is refused with its own code. The return never releases an allocation: the allocation is
    /// released first by its own command, then the return is retried.
    /// </summary>
    private async Task RequireExitLeavesAllocationsAsync(Guid labelId, decimal exitKg,
        CancellationToken cancellationToken)
    {
        var onLabel = await context.Balances.AsNoTracking()
            .Where(row => row.LabelId == labelId)
            .SumAsync(row => (decimal?)row.QuantityKg, cancellationToken) ?? 0m;
        var reserved = await context.Allocations.AsNoTracking()
            .Where(row => row.LabelId == labelId && row.Status == (int)StockAllocationStatus.Active)
            .SumAsync(row => (decimal?)row.RemainingKg, cancellationToken) ?? 0m;
        if (reserved > onLabel - exitKg)
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.StockAllocated);
        }
    }

    /// <summary>
    /// SAT-D371 (completes SAT-D306 (c)): the account of a purchase-return cost difference is named by the Finance
    /// accounting-voucher spec, so until then a return that would leave one is refused; a return without a difference
    /// proceeds under the existing rules. A difference is measurable only for the company's own goods (counterparty-owned
    /// goods never had this company's purchase cost), with a known original cost, against a readable ledger position at
    /// the exit's location whose quantity is the costed stock there (the ledger writer's own reading). An unknown cost,
    /// an untracked or unreadable position, or a ledger that disagrees with the stock has no figure to compare: the
    /// return is not refused for it here, and nothing is written to the ledger. The rule itself is the cost core's.
    /// </summary>
    private async Task RequireNoUnpostedCostDifferenceAsync(ReceiptTrace receipt, SourcePosition source,
        InventorySupplierReturnExitInput input, CancellationToken cancellationToken)
    {
        if (input.OriginalUnitCostTryPerKg is not { } returnCost
            || receipt.OwnerKind != StockPartyKind.Company || receipt.OwnerId != companyId)
        {
            return;
        }

        var locationId = source.Position.LocationId;
        var productId = receipt.ProductId;
        var row = await context.AverageCostLedger.AsNoTracking()
            .Where(item => item.CompanyId == companyId && item.LocationId == locationId && item.ProductId == productId)
            .SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return;
        }

        AverageCostPosition position;
        try
        {
            position = InventoryAverageCostLedgerRows.PositionOf(row);
        }
        catch (InvalidOperationException)
        {
            throw Refused(503, InventorySupplierReturnExitCodesV1.Unavailable);
        }

        if (position.Status != AverageCostStatus.Known)
        {
            return;
        }

        var stockBeforeKg = await InventoryAverageCostLedgerRows.CostedStock(context, companyId, productId)
            .Where(balance => balance.LocationId == locationId)
            .SumAsync(balance => (decimal?)balance.QuantityKg, cancellationToken) ?? 0m;
        if (stockBeforeKg != position.QuantityKg)
        {
            return;
        }

        if (AverageCostCalculator.PurchaseReturnLeavesCostDifference(position, input.Quantity.Value, returnCost))
        {
            throw Refused(409, InventorySupplierReturnExitCodesV1.CostDifferenceUnposted);
        }
    }

    private static bool Matches(MovementLineRow row, ReceiptTrace receipt) =>
        row.Direction == (int)StockDirection.Out
        && row.OwnerKind == (int)receipt.OwnerKind && row.OwnerId == receipt.OwnerId
        && row.LabelId == receipt.LabelRow.Id && row.ProductId == receipt.ProductId
        && (row.Block == (int)BlockState.Free || row.Block == (int)BlockState.Blocked)
        && row.QuantityKg > 0m;

    private async Task<MaterialLabel> RestoreLabelAsync(LabelRow row, CancellationToken cancellationToken)
    {
        var required = JsonSerializer.Deserialize<string[]>(row.RequiredDocumentsJson, Json)
            ?? throw Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid);
        var documents = await context.LabelDocuments.AsNoTracking()
            .Where(item => item.LabelId == row.Id)
            .OrderBy(item => item.Sequence)
            .Select(item => new LabelDocumentAttachment(item.Kind, item.AttachedAtUtc, item.AttachedBy))
            .ToListAsync(cancellationToken);
        return MaterialLabel.Restore(row.Id, row.CompanyId, row.Code, row.ProductId, row.LotId,
            row.ProductVersion, required, row.HeatNumber, row.CoilNumber, [], documents);
    }

    private static InventorySupplierReturnExitException StoreRefusal(string code) => code switch
    {
        InventoryStoreCodes.LabelNotRegistered or InventoryStoreCodes.LabelConflict =>
            Refused(503, InventorySupplierReturnExitCodesV1.ReceiptTraceInvalid),
        InventoryStoreCodes.MovementIdConflict or InventoryStoreCodes.DuplicateSource
            or InventoryStoreCodes.SourceGrainMismatch =>
            Refused(409, InventorySupplierReturnExitCodesV1.IdempotencyConflict),
        InventoryStoreCodes.ConcurrentChange =>
            Refused(409, InventorySupplierReturnExitCodesV1.ConcurrentChange),
        InventoryScaleCodes.ScaleExceeded =>
            Refused(400, InventorySupplierReturnExitCodesV1.InvalidQuantity),
        InventoryScaleCodes.ProfileUnusable =>
            Refused(503, InventorySupplierReturnExitCodesV1.QuantityProfileUnavailable),
        _ when code.StartsWith("settings_", StringComparison.Ordinal) =>
            Refused(503, InventorySupplierReturnExitCodesV1.QuantityProfileUnavailable),
        _ => Refused(503, InventorySupplierReturnExitCodesV1.Unavailable),
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

    private static string RequestHash(InventorySupplierReturnExitRequestV1 request) =>
        Hash(JsonSerializer.Serialize(request, Json));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static InventorySupplierReturnExitException Refused(int status, string code) =>
        InventorySupplierReturnExitException.Refused(status, code);

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        await context.DisposeAsync();
    }

    private sealed record ReceiptTrace(
        Guid MovementId,
        decimal QuantityKg,
        StockPartyKind OwnerKind,
        Guid OwnerId,
        Guid ProductId,
        MaterialLabel Label,
        LabelRow LabelRow);

    private sealed record SourcePosition(StockPosition Position, decimal QuantityKg);
}
