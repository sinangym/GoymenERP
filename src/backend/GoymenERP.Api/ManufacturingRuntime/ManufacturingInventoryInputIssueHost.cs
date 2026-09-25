using System.Data;
using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.ManufacturingRuntime;

/// <summary>
/// Internal predecessor for the Manufacturing input-issue command surface. It owns the one SQL
/// transaction in which Manufacturing records the fact and Inventory records the physical issue.
/// It deliberately exposes no endpoint and chooses no return/output physical facts.
/// </summary>
internal sealed class ManufacturingInventoryInputIssueHost(
    ManufacturingDbContext manufacturing,
    ManufacturingStore orders,
    ICompanyContext companyContext,
    IErpAuthorityRevalidationService authority,
    ISettingsPrerequisiteProvider settings,
    Func<ManufacturingInventoryIssueStage, CancellationToken, Task>? observer = null)
    : IManufacturingInventoryInputIssuePortV1
{
    async Task<ManufacturingInventoryInputIssuePortResultV1> IManufacturingInventoryInputIssuePortV1.IssueAsync(
        ManufacturingInventoryInputIssuePortCommandV1 command,
        ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await IssueAsync(new ManufacturingInventoryInputIssueCommand(
                command.OrderId,
                command.ExpectedOrderVersion,
                command.IssueId,
                command.ParentLabelId,
                command.MovementId,
                command.Quantity,
                command.OccurredAtUtc), metadata, cancellationToken);
            return new ManufacturingInventoryInputIssuePortResultV1(
                result.OrderVersion, result.ParentLabelVersion, result.ParentPosition, result.IsReplay);
        }
        catch (InventoryManufacturingRealizationException exception)
        {
            throw PortRefusal(exception.Status, exception.Code);
        }
        catch (ManufacturingInventoryInputIssueHostException exception)
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Conflict, exception.Code);
        }
        catch (ManufacturingInventoryInputIssueRecoveryException)
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Unavailable,
                InventoryManufacturingRealizationCodesV1.Unavailable);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Denied, ManufacturingApiCodes.AuthorityDenied);
        }
    }

    public async Task<ManufacturingInventoryInputIssueResult> IssueAsync(
        ManufacturingInventoryInputIssueCommand command,
        ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(metadata);
        var companyId = RequireCompany();
        RequireCommand(command, metadata);
        await RequireAuthorityAsync(companyId, cancellationToken);

        await using var transaction = await manufacturing.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var committed = false;
        try
        {
            var manufacturingOutcome = await orders.IssueInputAsync(command.OrderId, command.ExpectedOrderVersion,
                command.IssueId, command.ParentLabelId, command.Quantity, command.OccurredAtUtc, metadata,
                cancellationToken);
            var stored = await orders.FindOrderAsync(command.OrderId, cancellationToken)
                ?? throw new ManufacturingInventoryInputIssueHostException(
                    ManufacturingInventoryInputIssueHostCodes.IncompleteRealization);
            var input = stored.PlannedInputs.SingleOrDefault(candidate => candidate.ParentRollId == command.ParentLabelId);
            if (input?.ExpectedLabelVersion is not { } expectedLabelVersion)
            {
                throw new ManufacturingInventoryInputIssueHostException(
                    ManufacturingInventoryInputIssueHostCodes.MissingParentLabelVersion);
            }

            var realizationOrderVersion = checked(command.ExpectedOrderVersion + 1);
            if (manufacturingOutcome == ManufacturingWriteOutcome.Recorded
                && stored.Version != realizationOrderVersion)
            {
                throw new ManufacturingInventoryInputIssueHostException(
                    ManufacturingInventoryInputIssueHostCodes.IncompleteRealization);
            }

            var inventoryMetadata = InventoryMetadata(metadata, companyId, command);
            var request = new InventoryManufacturingInputIssueRequestV1(
                companyId,
                command.OrderId,
                command.IssueId,
                realizationOrderVersion.ToString(CultureInfo.InvariantCulture),
                command.ParentLabelId,
                expectedLabelVersion.ToString(CultureInfo.InvariantCulture),
                command.MovementId,
                command.Quantity.Value.ToString("0.###", CultureInfo.InvariantCulture),
                command.OccurredAtUtc);
            var connection = manufacturing.Database.GetDbConnection();
            var databaseTransaction = transaction.GetDbTransaction();
            await using var inventory = await InventoryManufacturingRealizationParticipant.EnlistAsync(
                connection, databaseTransaction, companyId, settings, cancellationToken,
                onStaged: async token =>
                {
                    // A pre-existing Manufacturing fact is complete only when Inventory can replay
                    // the already-recorded participant command. If it tries to stage a first effect,
                    // this is a legacy/incomplete pair and the owner transaction must roll it back.
                    if (manufacturingOutcome == ManufacturingWriteOutcome.Replayed)
                    {
                        throw new ManufacturingInventoryInputIssueHostException(
                            ManufacturingInventoryInputIssueHostCodes.IncompleteRealization);
                    }

                    if (observer is not null)
                    {
                        await observer(ManufacturingInventoryIssueStage.InventoryStaged, token);
                    }
                });
            var inventoryResult = await inventory.StageInputIssueAsync(request, inventoryMetadata, cancellationToken);
            if ((manufacturingOutcome == ManufacturingWriteOutcome.Replayed) != inventoryResult.IsReplay)
            {
                // Both module effects must have been born in the same owner transaction. Neither
                // a legacy Manufacturing fact nor an orphan Inventory movement is repaired here.
                throw new ManufacturingInventoryInputIssueHostException(
                    ManufacturingInventoryInputIssueHostCodes.IncompleteRealization);
            }

            var parentPosition = inventoryResult.ParentPosition;
            if (inventoryResult.CompanyId != companyId || inventoryResult.ManufacturingOrderId != command.OrderId
                || inventoryResult.IssueId != command.IssueId || inventoryResult.ParentLabelId != command.ParentLabelId
                || inventoryResult.MovementId != command.MovementId
                || !long.TryParse(inventoryResult.ParentLabelVersion, NumberStyles.None,
                    CultureInfo.InvariantCulture, out var parentLabelVersion)
                || parentLabelVersion <= 0
                || parentPosition is null
                || !Enum.IsDefined(parentPosition.OwnerKind) || parentPosition.OwnerId == Guid.Empty
                || !Enum.IsDefined(parentPosition.CustodianKind) || parentPosition.CustodianId == Guid.Empty
                || parentPosition.LocationId == Guid.Empty)
            {
                throw new ManufacturingInventoryInputIssueHostException(
                    ManufacturingInventoryInputIssueHostCodes.IncompleteRealization);
            }

            var manufacturingPosition = new ManufacturingInventoryPositionSnapshotV1(
                parentPosition.OwnerKind.ToString(),
                parentPosition.OwnerId,
                parentPosition.CustodianKind.ToString(),
                parentPosition.CustodianId,
                parentPosition.LocationId);

            if (observer is not null)
            {
                await observer(ManufacturingInventoryIssueStage.BeforeCommit, cancellationToken);
            }

            await RequireAuthorityAsync(companyId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return new ManufacturingInventoryInputIssueResult(
                command.OrderId,
                command.IssueId,
                realizationOrderVersion,
                command.ParentLabelId,
                parentLabelVersion,
                manufacturingPosition,
                command.MovementId,
                command.Quantity,
                manufacturingOutcome == ManufacturingWriteOutcome.Replayed);
        }
        catch (Exception exception)
        {
            if (!committed)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (Exception rollback) when (rollback is InvalidOperationException or SqlException)
                {
                    manufacturing.ChangeTracker.Clear();
                    throw new ManufacturingInventoryInputIssueRecoveryException(exception, rollback);
                }
            }

            manufacturing.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            if (committed)
            {
                manufacturing.ChangeTracker.Clear();
            }
        }
    }

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

    private static void RequireCommand(ManufacturingInventoryInputIssueCommand command,
        ManufacturingCommandMetadata metadata)
    {
        if (command.OrderId == Guid.Empty || command.IssueId == Guid.Empty || command.ParentLabelId == Guid.Empty
            || command.MovementId == Guid.Empty || command.ExpectedOrderVersion <= 0
            || command.OccurredAtUtc.Offset != TimeSpan.Zero
            || !string.Equals(metadata.PermissionCode, ErpPermissions.ManufacturingFactsRecord,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The Manufacturing input-issue command is incomplete.", nameof(command));
        }
    }

    private static InventoryCommandMetadata InventoryMetadata(ManufacturingCommandMetadata metadata,
        Guid companyId, ManufacturingInventoryInputIssueCommand command) => new(
        metadata.ActorId,
        metadata.PermissionCode,
        metadata.RoleCodes,
        metadata.CorrelationId,
        metadata.CausationId,
        // The participant command follows the physical Manufacturing fact, rather than one HTTP
        // attempt. A different transport key for the same fact therefore replays one movement.
        $"manufacturing-input-issue:{companyId:N}:{command.OrderId:N}:{command.IssueId:N}",
        metadata.SourceChannel,
        metadata.ClientIpAddress,
        metadata.UserAgent);

    private static ManufacturingInventoryRealizationPortException PortRefusal(int status, string code) =>
        new(status switch
        {
            StatusCodes.Status400BadRequest => ManufacturingApiStatus.Invalid,
            StatusCodes.Status404NotFound => ManufacturingApiStatus.NotFound,
            StatusCodes.Status409Conflict => ManufacturingApiStatus.Conflict,
            _ => ManufacturingApiStatus.Unavailable,
        }, code);
}

internal sealed record ManufacturingInventoryInputIssueCommand(
    Guid OrderId,
    long ExpectedOrderVersion,
    Guid IssueId,
    Guid ParentLabelId,
    Guid MovementId,
    Kilograms Quantity,
    DateTimeOffset OccurredAtUtc);

internal sealed record ManufacturingInventoryInputIssueResult(
    Guid OrderId,
    Guid IssueId,
    long OrderVersion,
    Guid ParentLabelId,
    long ParentLabelVersion,
    ManufacturingInventoryPositionSnapshotV1 ParentPosition,
    Guid MovementId,
    Kilograms Quantity,
    bool IsReplay);

internal enum ManufacturingInventoryIssueStage
{
    InventoryStaged = 1,
    BeforeCommit = 2,
}

internal static class ManufacturingInventoryInputIssueHostCodes
{
    public const string MissingParentLabelVersion = "manufacturing_inventory_parent_label_version_missing";
    public const string IncompleteRealization = "manufacturing_inventory_realization_incomplete";
}

internal sealed class ManufacturingInventoryInputIssueHostException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

internal sealed class ManufacturingInventoryInputIssueRecoveryException(Exception attempt, Exception rollback)
    : Exception("Manufacturing input-issue transaction completion could not be confirmed.",
        new AggregateException(attempt, rollback));
