using System.Data;
using System.Globalization;
using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Manufacturing.Application;
using GoymenERP.Modules.Manufacturing.Domain.Common;
using GoymenERP.Modules.Manufacturing.Domain.Lineage;
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
/// URT-S21 (URT-D62): the output label's own code is given in the same transaction, after the output fact and before
/// Inventory opens the label — the entered code, otherwise the next number of the company's Ayarlar output label format
/// (<paramref name="labelCodePolicy"/>; unregistered means "not entered" and an output then needs an entered code).
/// <paramref name="labelCodeRegister"/> is Inventory's optional read of codes already used by stock labels.
/// </summary>
internal sealed class ManufacturingInventoryReturnOutputHost(
    ManufacturingDbContext manufacturing,
    ManufacturingStore orders,
    ICompanyContext companyContext,
    IErpAuthorityRevalidationService authority,
    ISettingsPrerequisiteProvider settings,
    IManufacturingInventoryOutputAdmission outputAdmission,
    Func<ManufacturingInventoryReturnOutputStage, CancellationToken, Task>? observer = null,
    IManufacturingOutputLabelCodePolicyReadPortV1? labelCodePolicy = null,
    IManufacturingLabelCodeRegisterReadPortV1? labelCodeRegister = null,
    IInventoryLabelDispositionReadPortV1? labelDispositions = null)
    : IManufacturingInventoryReturnOutputPortV1, IManufacturingInventoryOutputCorrectionPortV1
{
    async Task<ManufacturingInventoryInputReturnPortResultV1> IManufacturingInventoryReturnOutputPortV1.ReturnAsync(
        ManufacturingInventoryInputReturnPortCommandV1 command,
        ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        // Never discard the explicit issue trace while the Inventory owner contract is being integrated.
        // The integration adapter replaces this fence with the exact owner request member, before any fact is staged.
        if (command.SourceIssueMovementId is not null)
            throw new ManufacturingInventoryRealizationPortException(ManufacturingApiStatus.Unavailable,
                InventoryManufacturingRealizationCodesV1.Unavailable);
        try
        {
            var result = await ReturnAsync(new ManufacturingInventoryInputReturnCommand(
                command.OrderId,
                command.ExpectedOrderVersion,
                command.ReturnId,
                command.ParentLabelId,
                command.ExpectedParentLabelVersion,
                command.MovementId,
                command.Quantity,
                command.OccurredAtUtc), metadata, cancellationToken);
            return new ManufacturingInventoryInputReturnPortResultV1(
                result.OrderVersion, result.ParentLabelVersion, result.IsReplay);
        }
        catch (InventoryManufacturingRealizationException exception)
        {
            throw PortRefusal(exception.Status, exception.Code);
        }
        catch (ManufacturingInventoryReturnOutputHostException exception)
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Conflict, exception.Code);
        }
        catch (ManufacturingInventoryReturnOutputRecoveryException)
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

    async Task<ManufacturingInventoryOutputPortResultV1> IManufacturingInventoryReturnOutputPortV1.DeclareOutputAsync(
        ManufacturingInventoryOutputPortCommandV1 command,
        ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<StockPartyKind>(command.OwnerKind, ignoreCase: false, out var ownerKind)
            || !Enum.IsDefined(ownerKind))
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, "ownerKind");
        }

        if (!Enum.TryParse<StockPartyKind>(command.CustodianKind, ignoreCase: false, out var custodianKind)
            || !Enum.IsDefined(custodianKind))
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, "custodianKind");
        }

        if (!Enum.TryParse<BlockState>(command.InitialBlock, ignoreCase: false, out var initialBlock)
            || !Enum.IsDefined(initialBlock))
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid, "initialBlock");
        }

        // URT-S16 (URT-D43, BS-14): a class with no stock label, or a remnant classification the fixed mapping does not
        // allow, is refused before any read or write.
        if (!ManufacturingOutputClassMappingV1.TryMap(command.OutputClass, command.RemnantClassification, out _))
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid,
                command.RemnantClassification is null ? "outputClass" : "remnantClassification");
        }

        try
        {
            var result = await DeclareOutputAsync(new ManufacturingInventoryOutputCommand(
                command.OrderId,
                command.ExpectedOrderVersion,
                command.OutputId,
                command.ParentLabelId,
                command.ExpectedParentLabelVersion,
                command.OutputClass,
                command.ProductId,
                command.ExpectedProductVersion,
                command.LotId,
                command.LabelId,
                command.LabelCode,
                command.HeatNumber,
                command.CoilNumber,
                command.RequiredDocuments,
                ownerKind,
                command.OwnerId,
                custodianKind,
                command.CustodianId,
                command.LocationId,
                initialBlock,
                command.MovementId,
                command.Quantity,
                command.OccurredAtUtc,
                command.RemnantClassification, command.OutputLine), metadata, cancellationToken);
            return new ManufacturingInventoryOutputPortResultV1(
                result.OrderVersion, result.ParentLabelVersion, result.LabelVersion, result.IsReplay);
        }
        catch (InventoryManufacturingRealizationException exception)
            when (exception.Code is InventoryManufacturingRealizationCodesV2.OutputClassNotStockable
                or InventoryManufacturingRealizationCodesV2.RemnantClassificationInvalid)
        {
            // URT-S16: the Manufacturing mapping refuses these first; should Inventory still refuse the class, the
            // command keeps the declared Manufacturing 400 contract and names the field.
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Invalid, ManufacturingApiCodes.RequestInvalid,
                exception.Code == InventoryManufacturingRealizationCodesV2.OutputClassNotStockable
                    ? "outputClass"
                    : "remnantClassification");
        }
        catch (InventoryManufacturingRealizationException exception)
        {
            throw PortRefusal(exception.Status, exception.Code);
        }
        catch (ManufacturingStoreException exception)
            when (OutputLabelCodeRefusal(exception) is { } refusal)
        {
            // URT-S21 (URT-D62): the output label code refusals keep their own code and name the field.
            throw refusal;
        }
        catch (ManufacturingInventoryReturnOutputHostException exception)
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Conflict, exception.Code);
        }
        catch (ManufacturingInventoryReturnOutputRecoveryException)
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

    async Task<ManufacturingInventoryOutputCorrectionPortResultV1>
        IManufacturingInventoryOutputCorrectionPortV1.CorrectOutputAsync(
            ManufacturingInventoryOutputCorrectionPortCommandV1 command,
            ManufacturingCommandMetadata metadata,
            CancellationToken cancellationToken)
    {
        try
        {
            var result = await CorrectOutputAsync(command, metadata, cancellationToken);
            return new ManufacturingInventoryOutputCorrectionPortResultV1(
                result.OrderVersion, result.LabelVersion, result.IsReplay);
        }
        catch (InventoryManufacturingRealizationException exception)
        {
            throw OutputCorrectionRefusal(exception.Status, exception.Code);
        }
        catch (ManufacturingInventoryReturnOutputHostException exception)
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Conflict, exception.Code);
        }
        catch (ManufacturingInventoryReturnOutputRecoveryException)
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

    /// <summary>
    /// F-009 D3 (API-25, URT-S2): records the output reversal and Inventory's <c>manufacturing.output-reversal</c>
    /// movement in one Manufacturing-owned transaction. The label disposition read first only proves that the label is
    /// this output's own (an unknown label, or another order's or output's label, is "not found"); it is never the
    /// decision source. Inventory decides under the label version fence, inside the transaction, whether the mass can
    /// still leave the output position, and its refusal rolls the Manufacturing fact back.
    /// </summary>
    public async Task<ManufacturingInventoryOutputCorrectionResult> CorrectOutputAsync(
        ManufacturingInventoryOutputCorrectionPortCommandV1 command,
        ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(metadata);
        var companyId = RequireCompany();
        RequireCorrection(command, metadata);
        await RequireAuthorityAsync(companyId, ErpPermissions.ManufacturingFactsCorrect, cancellationToken);
        await RequireOwnOutputLabelAsync(companyId, command, cancellationToken);

        await using var transaction = await manufacturing.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var committed = false;
        try
        {
            var manufacturingOutcome = await orders.ReverseOutputAsync(
                command.OrderId, command.ExpectedOrderVersion, command.ReversalId, command.OutputId,
                command.Quantity, command.Reason, command.OccurredAtUtc, metadata, cancellationToken);
            var stored = await orders.FindOrderAsync(command.OrderId, cancellationToken) ?? throw Incomplete();
            if (stored.OutputReversals.All(reversal =>
                    reversal.ReversalId != command.ReversalId || reversal.OutputId != command.OutputId))
            {
                throw Incomplete();
            }

            var realizationOrderVersion = checked(command.ExpectedOrderVersion + 1);
            RequireRecordedVersion(manufacturingOutcome, stored.Version, realizationOrderVersion);

            var request = new InventoryManufacturingOutputReversalRequestV1(
                companyId,
                command.OrderId,
                command.OutputId,
                command.ReversalId,
                realizationOrderVersion.ToString(CultureInfo.InvariantCulture),
                command.LabelId,
                command.ExpectedLabelVersion.ToString(CultureInfo.InvariantCulture),
                command.MovementId,
                Quantity(command.Quantity),
                command.OccurredAtUtc);
            var inventoryMetadata = InventoryMetadata(metadata, companyId, command.OrderId, command.ReversalId,
                "manufacturing-output-correction");
            var connection = manufacturing.Database.GetDbConnection();
            var databaseTransaction = transaction.GetDbTransaction();
            await using var inventory = await InventoryManufacturingRealizationParticipant.EnlistAsync(
                connection, databaseTransaction, companyId, settings, cancellationToken,
                onStaged: token => OnInventoryStagedAsync(
                    manufacturingOutcome, ManufacturingInventoryReturnOutputStage.OutputCorrectionInventoryStaged,
                    token));
            var inventoryResult = await inventory.StageOutputReversalAsync(request, inventoryMetadata, cancellationToken);
            RequireOutcomeParity(manufacturingOutcome, inventoryResult.IsReplay);

            if (inventoryResult.CompanyId != companyId
                || inventoryResult.ManufacturingOrderId != command.OrderId
                || inventoryResult.OutputId != command.OutputId
                || inventoryResult.ReversalId != command.ReversalId
                || inventoryResult.ManufacturingOrderVersion != request.ManufacturingOrderVersionAfter
                || inventoryResult.LabelId != command.LabelId
                || inventoryResult.MovementId != command.MovementId
                || inventoryResult.ReversedReceiptMovementId == Guid.Empty
                || inventoryResult.QuantityKg != request.QuantityKg
                || !PositiveVersion(inventoryResult.LabelVersion, out var labelVersion))
            {
                throw Incomplete();
            }

            await BeforeCommitAsync(companyId, ErpPermissions.ManufacturingFactsCorrect, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            committed = true;
            return new ManufacturingInventoryOutputCorrectionResult(
                command.OrderId, command.OutputId, command.ReversalId, realizationOrderVersion, command.LabelId,
                labelVersion, command.MovementId, inventoryResult.ReversedReceiptMovementId, command.Quantity,
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

    /// <summary>
    /// A pre-check only (TOCTOU-safe because Inventory re-checks under the label version fence): the label must be
    /// a manufacturing output label of exactly this order and output. Without the read port nothing is corrected.
    /// </summary>
    private async Task RequireOwnOutputLabelAsync(Guid companyId,
        ManufacturingInventoryOutputCorrectionPortCommandV1 command, CancellationToken cancellationToken)
    {
        if (labelDispositions is null)
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Unavailable, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        var read = await labelDispositions.GetAsync(companyId, command.LabelId, cancellationToken);
        switch (read.Status)
        {
            case InventoryLabelDispositionStatusV1.Value
                when read.Disposition is { Output: { } origin } disposition
                    && disposition.CompanyId == companyId
                    && disposition.LabelId == command.LabelId
                    && origin.ManufacturingOrderId == command.OrderId
                    && origin.OutputId == command.OutputId:
                return;
            case InventoryLabelDispositionStatusV1.Unreadable:
                throw new ManufacturingInventoryRealizationPortException(
                    ManufacturingApiStatus.Conflict, ManufacturingOutputCorrectionCodesV1.LabelUnreadable);
            default:
                throw new ManufacturingInventoryRealizationPortException(
                    ManufacturingApiStatus.NotFound, InventoryManufacturingRealizationCodesV1.InvalidReference);
        }
    }

    private static void RequireCorrection(
        ManufacturingInventoryOutputCorrectionPortCommandV1 command, ManufacturingCommandMetadata metadata)
    {
        if (command.OrderId == Guid.Empty || command.OutputId == Guid.Empty || command.ReversalId == Guid.Empty
            || command.LabelId == Guid.Empty || command.MovementId == Guid.Empty
            || command.ReversalId == command.MovementId || command.ReversalId == command.OutputId
            || command.ExpectedOrderVersion <= 0 || command.ExpectedLabelVersion <= 0
            || command.Reason is null || command.OccurredAtUtc.Offset != TimeSpan.Zero
            || !string.Equals(metadata.PermissionCode, ErpPermissions.ManufacturingFactsCorrect,
                StringComparison.Ordinal))
        {
            throw new ArgumentException("The Manufacturing output correction command is incomplete.", nameof(command));
        }
    }

    /// <summary>
    /// Inventory's closed reversal refusal set: each reversal-only code becomes the Manufacturing correction code that
    /// names the same reason; a V1 realization code keeps the shared mapping; any code outside the set fails closed as
    /// unavailable rather than being passed on as a reason Inventory never declared.
    /// </summary>
    private static ManufacturingInventoryRealizationPortException OutputCorrectionRefusal(int status, string code)
    {
        if (!InventoryManufacturingOutputReversalCodesV1.IsDefined(status, code))
        {
            return new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Unavailable, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        var mapped = code switch
        {
            InventoryManufacturingOutputReversalCodesV1.ExceedsProduced => ManufacturingOutputCorrectionCodesV1.ExceedsProduced,
            InventoryManufacturingOutputReversalCodesV1.Shipped => ManufacturingOutputCorrectionCodesV1.Shipped,
            InventoryManufacturingOutputReversalCodesV1.IssuedToManufacturing =>
                ManufacturingOutputCorrectionCodesV1.IssuedToManufacturing,
            InventoryManufacturingOutputReversalCodesV1.Transferred => ManufacturingOutputCorrectionCodesV1.Transferred,
            InventoryManufacturingOutputReversalCodesV1.Allocated => ManufacturingOutputCorrectionCodesV1.Allocated,
            InventoryManufacturingOutputReversalCodesV1.Blocked => ManufacturingOutputCorrectionCodesV1.Blocked,
            InventoryManufacturingOutputReversalCodesV1.NotFree => ManufacturingOutputCorrectionCodesV1.NotFree,
            _ => null,
        };
        return mapped is not null && status == StatusCodes.Status409Conflict
            ? new ManufacturingInventoryRealizationPortException(ManufacturingApiStatus.Conflict, mapped)
            : PortRefusal(status, code);
    }

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
        var expectedLabel = RequireOutput(command, metadata);
        await RequireAuthorityAsync(companyId, cancellationToken);
        await outputAdmission.ValidateAsync(companyId, command, cancellationToken);
        var labelCodeFormat = await ReadOutputLabelCodeFormatAsync(companyId, cancellationToken);

        await using var transaction = await manufacturing.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var committed = false;
        try
        {
            var manufacturingOutcome = await orders.DeclareOutputAsync(
                command.OrderId, command.ExpectedOrderVersion, command.OutputId, command.ParentLabelId,
                command.OutputClass, command.Quantity, command.OccurredAtUtc, command.OutputLine, metadata, cancellationToken);
            var stored = await RequireStoredOrderAsync(command.OrderId, command.ParentLabelId, cancellationToken);
            var realizationOrderVersion = checked(command.ExpectedOrderVersion + 1);
            RequireRecordedVersion(manufacturingOutcome, stored.Version, realizationOrderVersion);

            // URT-S21 (URT-D62): the output label takes its own code here — never an input label's number; the input
            // labels stay the lineage edges recorded below.
            var labelCode = await AssignOutputLabelCodeAsync(
                companyId, command, labelCodeFormat, manufacturingOutcome, metadata, cancellationToken);

            // URT-S16 (URT-D43, BS-5, BS-14): the V2 output declaration opens the label with the classification of the
            // fixed Manufacturing mapping and, for a usable remnant, the operator's classification (sent explicitly,
            // the default resolved here) and the UsableRemnant production status.
            var request = new InventoryManufacturingOutputRequestV2(
                companyId,
                command.OrderId,
                command.OutputId,
                realizationOrderVersion.ToString(CultureInfo.InvariantCulture),
                command.ParentLabelId,
                command.ExpectedParentLabelVersion.ToString(CultureInfo.InvariantCulture),
                expectedLabel.InventoryOutputClass,
                command.ProductId,
                command.ExpectedProductVersion,
                command.LotId,
                command.LabelId,
                labelCode.LabelCode,
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
                command.OccurredAtUtc,
                expectedLabel.ProductionStatus is null
                    ? null
                    : (InventoryLabelClassificationV1?)expectedLabel.Classification);
            // Inventory command identities are capped at 128 characters. The explicit company/order/output
            // grain already makes this participant key unique; keep the operation token short enough that the
            // complete deterministic identity remains inside that owner contract.
            var inventoryMetadata = InventoryMetadata(metadata, companyId, command.OrderId, command.OutputId,
                "mfg-output");
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
                || inventoryResult.Classification != expectedLabel.Classification
                || inventoryResult.ProductionStatus != expectedLabel.ProductionStatus
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

            // URT-S20 (URT-D62, URT-D63): the output label's lineage edges — every input label Inventory staged for the
            // output's genealogy, with its consumed kg — are recorded in Manufacturing in this same transaction, from
            // Inventory's port answer (Manufacturing never writes Inventory's label tables). A replayed output replays
            // its stored edges; a different answer for the same output refuses and rolls back both owners.
            await RecordOutputLineageAsync(command, inventoryResult, manufacturingOutcome, metadata, cancellationToken);

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
                manufacturingOutcome == ManufacturingWriteOutcome.Replayed, labelCode.LabelCode);
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

    private async Task<string?> ReadOutputLabelCodeFormatAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (labelCodePolicy is null)
        {
            return ManufacturingOutputLabelCodePolicyV1.NotSet.GeneratedLabelCodeFormat;
        }

        ManufacturingOutputLabelCodePolicyV1? policy;
        try
        {
            policy = await labelCodePolicy.ReadAsync(companyId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Unavailable, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        // An unreadable Ayarlar answer fails closed; it never falls back to a format of Manufacturing's own.
        if (policy is null)
        {
            throw new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Unavailable, InventoryManufacturingRealizationCodesV1.Unavailable);
        }

        return policy.GeneratedLabelCodeFormat;
    }

    private async Task<ManufacturingOutputLabelCodeV1> AssignOutputLabelCodeAsync(
        Guid companyId,
        ManufacturingInventoryOutputCommand command,
        string? labelCodeFormat,
        ManufacturingWriteOutcome manufacturingOutcome,
        ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        Func<IReadOnlyCollection<string>, CancellationToken, Task<IReadOnlySet<string>>>? registered =
            labelCodeRegister is null
                ? null
                : (codes, token) => labelCodeRegister.RegisteredAsync(companyId, codes, token);
        ManufacturingOutputLabelCodeV1 assigned;
        try
        {
            assigned = await orders.AssignOutputLabelCodeAsync(
                command.OrderId, command.OutputId, command.LabelId, command.LabelCode, labelCodeFormat, registered,
                manufacturingOutcome == ManufacturingWriteOutcome.Replayed, metadata, cancellationToken);
        }
        catch (ManufacturingStoreException exception)
            when (exception.Code == ManufacturingStoreCodes.LineageInvalid)
        {
            // The output fact this transaction just recorded or replayed is not readable, or the register answered
            // nothing: an incomplete realization, both owners roll back.
            throw Incomplete();
        }

        if (assigned.OutputId != command.OutputId || assigned.LabelId != command.LabelId
            || !ProductionOutputLabelCodes.IsValidCode(assigned.LabelCode))
        {
            throw Incomplete();
        }

        return assigned;
    }

    private static ManufacturingInventoryRealizationPortException? OutputLabelCodeRefusal(
        ManufacturingStoreException exception) =>
        exception.Code switch
        {
            ManufacturingStoreCodes.OutputLabelCodeRequired => new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Unprocessable, exception.Code, "labelCode"),
            ManufacturingStoreCodes.OutputLabelCodeFormatInvalid => new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Unprocessable, exception.Code),
            ManufacturingStoreCodes.OutputLabelCodeConflict => new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Conflict, exception.Code, "labelCode"),
            ManufacturingStoreCodes.OutputLabelCodeEntryNotAllowed => new ManufacturingInventoryRealizationPortException(
                ManufacturingApiStatus.Unprocessable, exception.Code, "labelCode"),
            _ => null,
        };

    private async Task RecordOutputLineageAsync(
        ManufacturingInventoryOutputCommand command,
        InventoryManufacturingOutputResultV2 inventoryResult,
        ManufacturingWriteOutcome manufacturingOutcome,
        ManufacturingCommandMetadata metadata,
        CancellationToken cancellationToken)
    {
        var inputs = LineageInputs(inventoryResult.GenealogyInputs, command.LabelId);
        try
        {
            await orders.RecordOutputLineageAsync(
                command.OrderId, command.OutputId, command.LabelId, inventoryResult.GenealogyTransformationId, inputs,
                manufacturingOutcome == ManufacturingWriteOutcome.Replayed, metadata, cancellationToken);
        }
        catch (ManufacturingStoreException exception)
            when (exception.Code == ManufacturingStoreCodes.LineageInvalid)
        {
            // Inventory's genealogy answer the share calculator cannot read is an incomplete realization, not a
            // caller error: both owners roll back.
            throw Incomplete();
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

    private Task BeforeCommitAsync(Guid companyId, CancellationToken cancellationToken) =>
        BeforeCommitAsync(companyId, ErpPermissions.ManufacturingFactsRecord, cancellationToken);

    private async Task BeforeCommitAsync(Guid companyId, string permission, CancellationToken cancellationToken)
    {
        if (observer is not null)
        {
            await observer(ManufacturingInventoryReturnOutputStage.BeforeCommit, cancellationToken);
        }

        await RequireAuthorityAsync(companyId, permission, cancellationToken);
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

    /// <summary>
    /// URT-S20: Inventory's genealogy inputs as the share calculator's inputs. A missing list or input, an empty or
    /// self-referencing label id, or a kg Manufacturing cannot read exactly (more than three decimals, negative, not a
    /// number) is an incomplete realization: the transaction rolls back, nothing is cut or guessed.
    /// </summary>
    private static IReadOnlyList<ProductionLineageShareInput> LineageInputs(
        IReadOnlyList<InventoryManufacturingGenealogyInputV2>? genealogyInputs, Guid outputLabelId)
    {
        if (genealogyInputs is null || genealogyInputs.Count == 0)
        {
            throw Incomplete();
        }

        var inputs = new List<ProductionLineageShareInput>(genealogyInputs.Count);
        foreach (var input in genealogyInputs)
        {
            if (input is null || input.LabelId == Guid.Empty || input.LabelId == outputLabelId
                || !decimal.TryParse(input.QuantityKg, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                    out var quantity))
            {
                throw Incomplete();
            }

            try
            {
                inputs.Add(new ProductionLineageShareInput(input.LabelId, new Kilograms(quantity)));
            }
            catch (ManufacturingRuleException)
            {
                throw Incomplete();
            }
        }

        return inputs;
    }

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

    private Task RequireAuthorityAsync(Guid companyId, CancellationToken cancellationToken) =>
        RequireAuthorityAsync(companyId, ErpPermissions.ManufacturingFactsRecord, cancellationToken);

    private async Task RequireAuthorityAsync(Guid companyId, string permission, CancellationToken cancellationToken)
    {
        var scope = await authority.RevalidatePermissionScopeAsync(permission, companyId, cancellationToken);
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

    private static ManufacturingOutputLabelV1 RequireOutput(
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

        // URT-S16 (URT-D43): fail-closed when the class has no stock label or the remnant classification is not one
        // the fixed mapping allows.
        if (!ManufacturingOutputClassMappingV1.TryMap(command.OutputClass, command.RemnantClassification,
                out var label))
        {
            throw new ArgumentException("The Manufacturing output class has no stock label.", nameof(command));
        }

        return label;
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

    private static ManufacturingInventoryRealizationPortException PortRefusal(int status, string code) =>
        new(status switch
        {
            StatusCodes.Status400BadRequest => ManufacturingApiStatus.Invalid,
            StatusCodes.Status404NotFound => ManufacturingApiStatus.NotFound,
            StatusCodes.Status409Conflict => ManufacturingApiStatus.Conflict,
            _ => ManufacturingApiStatus.Unavailable,
        }, code);
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
    string? LabelCode,
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
    DateTimeOffset OccurredAtUtc,
    InventoryLabelClassificationV1? RemnantClassification = null,
    PlanOutputLineReference? OutputLine = null);

internal sealed record ManufacturingInventoryInputReturnResult(
    Guid OrderId,
    Guid ReturnId,
    long OrderVersion,
    Guid ParentLabelId,
    long ParentLabelVersion,
    Guid MovementId,
    Kilograms Quantity,
    bool IsReplay);

internal sealed record ManufacturingInventoryOutputCorrectionResult(
    Guid OrderId,
    Guid OutputId,
    Guid ReversalId,
    long OrderVersion,
    Guid LabelId,
    long LabelVersion,
    Guid MovementId,
    Guid ReversedReceiptMovementId,
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
    bool IsReplay,
    string LabelCode);

internal enum ManufacturingInventoryReturnOutputStage
{
    ReturnInventoryStaged = 1,
    OutputInventoryStaged = 2,
    BeforeCommit = 3,
    OutputCorrectionInventoryStaged = 4,
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
