using System.Data;
using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Orders;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>
/// Internal predecessor for input-return and output-declaration command surfaces. Manufacturing
/// and Inventory stage one matching fact/effect pair in the Manufacturing-owned SQL transaction.
/// It publishes no route and chooses no output identity, position or ownership policy.
/// </summary>
internal sealed class ManufacturingInventoryReturnOutputHost(
    ManufacturingDbContext manufacturing,
    ManufacturingStore orders,
    ICompanyContext companyContext,
    IErpAuthorityRevalidationService authority,
    ISettingsPrerequisiteProvider settings,
    IManufacturingInventoryOutputAdmission outputAdmission,
    Func<ManufacturingInventoryReturnOutputStage, CancellationToken, Task>? observer = null)
{
    public async Task<ManufacturingInventoryInputReturnResult> ReturnAsync(
        ManufacturingInventoryInputReturnCommand command,
        ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(metadata);
        var companyId = RequireCompany();
        RequireReturn(command, metadata);
        await RequireAuthorityAsync(companyId, cancellationToken);

        await using var transaction = await manufacturing.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var committed = false;
        try
        {
            var manufacturingOutcome = await orders.ReturnUnusedInputAsync(
                command.OrderId, command.ExpectedOrderVersion, command.ReturnId, command.ParentLabelId,
                command.Quantity, command.OccurredAtUtc, metadata, cancellationToken);
            var stored = await RequireStoredOrderAsync(command.OrderId, command.ParentLabelId, cancellationToken);
            var realizationOrderVersion = checked(command.ExpectedOrderVersion + 1);
            RequireRecordedVersion(manufacturingOutcome, stored.Version, realizationOrderVersion);

            var request = new InventoryManufacturingInputReturnRequestV1(
                companyId,
                command.OrderId,
                command.ReturnId,
                realizationOrderVersion.ToString(CultureInfo.InvariantCulture),
                command.ParentLabelId,
                command.ExpectedParentLabelVersion.ToString(CultureInfo.InvariantCulture),
                command.MovementId,
                Quantity(command.Quantity),
                command.OccurredAtUtc);
            var inventoryMetadata = InventoryMetadata(metadata, companyId, command.OrderId, command.ReturnId,
                "manufacturing-input-return");
            var connection = manufacturing.Database.GetDbConnection();
            var databaseTransaction = transaction.GetDbTransaction();
            await using var inventory = await InventoryManufacturingRealizationParticipant.EnlistAsync(
                connection, databaseTransaction, companyId, settings, cancellationToken,
                onStaged: token => OnInventoryStagedAsync(
                    manufacturingOutcome, ManufacturingInventoryReturnOutputStage.ReturnInventoryStaged, token));
            var inventoryResult = await inventory.StageInputReturnAsync(request, inventoryMetadata, cancellationToken);
            RequireOutcomeParity(manufacturingOutcome, inventoryResult.IsReplay);

            if (inventoryResult.CompanyId != companyId
                || inventoryResult.ManufacturingOrderId != command.OrderId
                || inventoryResult.ReturnId != command.ReturnId
                || inventoryResult.ManufacturingOrderVersion != request.ManufacturingOrderVersionAfter
                || inventoryResult.ParentLabelId != command.ParentLabelId
                || inventoryResult.MovementId != command.MovementId
                || inventoryResult.QuantityKg != request.QuantityKg
                || !PositiveVersion(inventoryResult.ParentLabelVersion, out var parentLabelVersion))
            {
                throw Incomplete();
            }

            await BeforeCommitAsync(companyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return new ManufacturingInventoryInputReturnResult(
                command.OrderId, command.ReturnId, realizationOrderVersion, command.ParentLabelId,
                parentLabelVersion, command.MovementId, command.Quantity,
                manufacturingOutcome == ManufacturingWriteOutcome.Replayed);
        }
        catch (Exception exception)
        {
            await RollbackOrThrowAsync(transaction, exception, committed);
            throw;
        }
        finally
        {
            manufacturing.ChangeTracker.Clear();
        }
    }

    public async Task<ManufacturingInventoryOutputResult> DeclareOutputAsync(
        ManufacturingInventoryOutputCommand command,
        ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(metadata);
        var companyId = RequireCompany();
        RequireOutput(command, metadata);
        await RequireAuthorityAsync(companyId, cancellationToken);
        await outputAdmission.ValidateAsync(companyId, command, cancellationToken);

        await using var transaction = await manufacturing.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var committed = false;
        try
        {
            var manufacturingOutcome = await orders.DeclareOutputAsync(
                command.OrderId, command.ExpectedOrderVersion, command.OutputId, command.ParentLabelId,
                command.OutputClass, command.Quantity, command.OccurredAtUtc, metadata, cancellationToken);
            var stored = await RequireStoredOrderAsync(command.OrderId, command.ParentLabelId, cancellationToken);
            var realizationOrderVersion = checked(command.ExpectedOrderVersion + 1);
            RequireRecordedVersion(manufacturingOutcome, stored.Version, realizationOrderVersion);

            var request = new InventoryManufacturingOutputRequestV1(
                companyId,
                command.OrderId,
                command.OutputId,
                realizationOrderVersion.ToString(CultureInfo.InvariantCulture),
                command.ParentLabelId,
                command.ExpectedParentLabelVersion.ToString(CultureInfo.InvariantCulture),
                command.OutputClass.ToString(),
                command.ProductId,
                command.ExpectedProductVersion,
                command.LotId,
                command.LabelId,
                command.LabelCode,
                command.HeatNumber,
                command.CoilNumber,
                command.RequiredDocuments,
                command.OwnerKind,
                command.OwnerId,
                command.CustodianKind,
                command.CustodianId,
                command.LocationId,
                command.InitialBlock,
                command.MovementId,
                Quantity(command.Quantity),
                command.OccurredAtUtc);
            var inventoryMetadata = InventoryMetadata(metadata, companyId, command.OrderId, command.OutputId,
                "manufacturing-output-declaration");
            var connection = manufacturing.Database.GetDbConnection();
            var databaseTransaction = transaction.GetDbTransaction();
            await using var inventory = await InventoryManufacturingRealizationParticipant.EnlistAsync(
                connection, databaseTransaction, companyId, settings, cancellationToken,
                onStaged: token => OnInventoryStagedAsync(
                    manufacturingOutcome, ManufacturingInventoryReturnOutputStage.OutputInventoryStaged, token));
            var inventoryResult = await inventory.StageOutputAsync(request, inventoryMetadata, cancellationToken);
            RequireOutcomeParity(manufacturingOutcome, inventoryResult.IsReplay);

            if (inventoryResult.CompanyId != companyId
                || inventoryResult.ManufacturingOrderId != command.OrderId
                || inventoryResult.OutputId != command.OutputId
                || inventoryResult.ManufacturingOrderVersion != request.ManufacturingOrderVersionAfter
                || inventoryResult.ParentLabelId != command.ParentLabelId
                || inventoryResult.OutputClass != request.OutputClass
                || inventoryResult.ProductId != command.ProductId
                || inventoryResult.ProductVersion != command.ExpectedProductVersion
                || inventoryResult.LotId != command.LotId
                || inventoryResult.LabelId != command.LabelId
                || inventoryResult.MovementId != command.MovementId
                || inventoryResult.QuantityKg != request.QuantityKg
                || !PositiveVersion(inventoryResult.ParentLabelVersion, out var parentLabelVersion)
                || !PositiveVersion(inventoryResult.LabelVersion, out var labelVersion))
            {
                throw Incomplete();
            }

            if (observer is not null)
            {
                await observer(ManufacturingInventoryReturnOutputStage.BeforeCommit, cancellationToken);
            }

            await outputAdmission.ValidateAsync(companyId, command, cancellationToken);
            await RequireAuthorityAsync(companyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return new ManufacturingInventoryOutputResult(
                command.OrderId, command.OutputId, realizationOrderVersion, command.ParentLabelId,
                parentLabelVersion, command.ProductId, command.ExpectedProductVersion, command.LotId,
                command.LabelId, labelVersion, command.MovementId, command.Quantity,
                manufacturingOutcome == ManufacturingWriteOutcome.Replayed);
        }
        catch (Exception exception)
        {
            await RollbackOrThrowAsync(transaction, exception, committed);
            throw;
        }
        finally
        {
            manufacturing.ChangeTracker.Clear();
        }
    }

    private async Task<ProductionOrder> RequireStoredOrderAsync(
        Guid orderId, Guid parentLabelId, CancellationToken cancellationToken)
    {
        var stored = await orders.FindOrderAsync(orderId, cancellationToken) ?? throw Incomplete();
        if (stored.PlannedInputs.All(input => input.ParentRollId != parentLabelId))
        {
            throw Incomplete();
        }

        return stored;
    }

    private async Task OnInventoryStagedAsync(
        ManufacturingWriteOutcome manufacturingOutcome,
        ManufacturingInventoryReturnOutputStage stage,
        CancellationToken cancellationToken)
    {
        if (manufacturingOutcome == ManufacturingWriteOutcome.Replayed)
        {
            throw Incomplete();
        }

        if (observer is not null)
        {
            await observer(stage, cancellationToken);
        }
    }

    private async Task BeforeCommitAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (observer is not null)
        {
            await observer(ManufacturingInventoryReturnOutputStage.BeforeCommit, cancellationToken);
        }

        await RequireAuthorityAsync(companyId, cancellationToken);
    }

    private static void RequireRecordedVersion(
        ManufacturingWriteOutcome outcome, long storedVersion, long realizationVersion)
    {
        if (outcome == ManufacturingWriteOutcome.Recorded && storedVersion != realizationVersion)
        {
            throw Incomplete();
        }
    }

    private static void RequireOutcomeParity(ManufacturingWriteOutcome outcome, bool inventoryReplay)
    {
        if ((outcome == ManufacturingWriteOutcome.Replayed) != inventoryReplay)
        {
            throw Incomplete();
        }
    }

    private static bool PositiveVersion(string value, out long version) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out version) && version > 0;

    private static string Quantity(Kilograms value) =>
        value.Value.ToString("0.###", CultureInfo.InvariantCulture);

    private static InventoryCommandMetadata InventoryMetadata(
        ManufacturingCommandMetadata metadata,
        Guid companyId,
        Guid orderId,
        Guid factId,
        string operation) => new InventoryCommandMetadata(
        metadata.ActorId,
        metadata.PermissionCode,
        metadata.RoleCodes,
        metadata.CorrelationId,
        metadata.CausationId,
        $"{operation}:{companyId:N}:{orderId:N}:{factId:N}",
        metadata.SourceChannel,
        metadata.ClientIpAddress,
        metadata.UserAgent);

    private Guid RequireCompany()
    {
        if (!companyContext.IsResolved || companyContext.CompanyId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Manufacturing company context is not resolved.");
        }

        return companyContext.CompanyId;
    }

    private async Task RequireAuthorityAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var scope = await authority.RevalidatePermissionScopeAsync(
            ErpPermissions.ManufacturingFactsRecord, companyId, cancellationToken);
        if (!scope.IsGranted || !scope.IsCompanyWide || !companyContext.IsResolved
            || companyContext.CompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Manufacturing authority changed before commit.");
        }
    }

    private static void RequireReturn(
        ManufacturingInventoryInputReturnCommand command, ManufacturingCommandMetadata metadata)
    {
        if (command.OrderId == Guid.Empty || command.ReturnId == Guid.Empty || command.ParentLabelId == Guid.Empty
            || command.MovementId == Guid.Empty || command.ExpectedOrderVersion <= 0
            || command.ExpectedParentLabelVersion <= 0 || command.OccurredAtUtc.Offset != TimeSpan.Zero
            || !string.Equals(metadata.PermissionCode, ErpPermissions.ManufacturingFactsRecord,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The Manufacturing input-return command is incomplete.", nameof(command));
        }
    }

    private static void RequireOutput(
        ManufacturingInventoryOutputCommand command, ManufacturingCommandMetadata metadata)
    {
        if (command.OrderId == Guid.Empty || command.OutputId == Guid.Empty || command.ParentLabelId == Guid.Empty
            || command.ProductId == Guid.Empty || command.LotId == Guid.Empty || command.LabelId == Guid.Empty
            || command.LocationId == Guid.Empty || command.OwnerId == Guid.Empty || command.CustodianId == Guid.Empty
            || command.MovementId == Guid.Empty || command.ExpectedOrderVersion <= 0
            || command.ExpectedParentLabelVersion <= 0 || command.RequiredDocuments is null
            || command.OccurredAtUtc.Offset != TimeSpan.Zero || !Enum.IsDefined(command.OutputClass)
            || !Enum.IsDefined(command.OwnerKind) || !Enum.IsDefined(command.CustodianKind)
            || !Enum.IsDefined(command.InitialBlock)
            || !string.Equals(metadata.PermissionCode, ErpPermissions.ManufacturingFactsRecord,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The Manufacturing output command is incomplete.", nameof(command));
        }
    }

    private async Task RollbackOrThrowAsync(
        IDbContextTransaction transaction, Exception attempt, bool committed)
    {
        if (committed)
        {
            return;
        }

        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception rollback) when (rollback is InvalidOperationException or SqlException)
        {
            manufacturing.ChangeTracker.Clear();
            throw new ManufacturingInventoryReturnOutputRecoveryException(attempt, rollback);
        }
    }

    private static ManufacturingInventoryReturnOutputHostException Incomplete() =>
        new ManufacturingInventoryReturnOutputHostException(
            ManufacturingInventoryReturnOutputHostCodes.IncompleteRealization);
}

/// <summary>
/// Required host-owned validation of explicit output product, label and physical-position facts.
/// The runtime deliberately has no default; future composition must bind accepted owner policies.
/// </summary>
internal interface IManufacturingInventoryOutputAdmission
{
    Task ValidateAsync(Guid companyId, ManufacturingInventoryOutputCommand command,
        CancellationToken cancellationToken);
}

internal sealed record ManufacturingInventoryInputReturnCommand(
    Guid OrderId,
    long ExpectedOrderVersion,
    Guid ReturnId,
    Guid ParentLabelId,
    long ExpectedParentLabelVersion,
    Guid MovementId,
    Kilograms Quantity,
    DateTimeOffset OccurredAtUtc);

internal sealed record ManufacturingInventoryOutputCommand(
    Guid OrderId,
    long ExpectedOrderVersion,
    Guid OutputId,
    Guid ParentLabelId,
    long ExpectedParentLabelVersion,
    OutputClass OutputClass,
    Guid ProductId,
    string ExpectedProductVersion,
    Guid LotId,
    Guid LabelId,
    string LabelCode,
    string? HeatNumber,
    string? CoilNumber,
    IReadOnlyList<string> RequiredDocuments,
    StockPartyKind OwnerKind,
    Guid OwnerId,
    StockPartyKind CustodianKind,
    Guid CustodianId,
    Guid LocationId,
    BlockState InitialBlock,
    Guid MovementId,
    Kilograms Quantity,
    DateTimeOffset OccurredAtUtc);

internal sealed record ManufacturingInventoryInputReturnResult(
    Guid OrderId,
    Guid ReturnId,
    long OrderVersion,
    Guid ParentLabelId,
    long ParentLabelVersion,
    Guid MovementId,
    Kilograms Quantity,
    bool IsReplay);

internal sealed record ManufacturingInventoryOutputResult(
    Guid OrderId,
    Guid OutputId,
    long OrderVersion,
    Guid ParentLabelId,
    long ParentLabelVersion,
    Guid ProductId,
    string ProductVersion,
    Guid LotId,
    Guid LabelId,
    long LabelVersion,
    Guid MovementId,
    Kilograms Quantity,
    bool IsReplay);

internal enum ManufacturingInventoryReturnOutputStage
{
    ReturnInventoryStaged = 1,
    OutputInventoryStaged = 2,
    BeforeCommit = 3,
}

internal static class ManufacturingInventoryReturnOutputHostCodes
{
    public const string IncompleteRealization = "manufacturing_inventory_realization_incomplete";
}

internal sealed class ManufacturingInventoryReturnOutputHostException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

internal sealed class ManufacturingInventoryReturnOutputRecoveryException(Exception attempt, Exception rollback)
    : Exception("Manufacturing return/output transaction completion could not be confirmed.",
        new AggregateException(attempt, rollback));
