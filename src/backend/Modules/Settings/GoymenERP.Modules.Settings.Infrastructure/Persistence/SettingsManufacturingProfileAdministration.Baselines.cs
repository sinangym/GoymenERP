using System.Text.Json;
using GoymenERP.Modules.Idempotency.Domain.Executions;
using GoymenERP.Modules.Settings.Application;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;

namespace GoymenERP.Modules.Settings.Infrastructure.Persistence;

internal sealed partial class SettingsManufacturingProfileAdministration
{
    private const string BaselineTransferredEvent = "settings.manufacturing-profile.baseline-transferred.v1";

    Task<ManufacturingProfileCommandResultV1> ISettingsManufacturingParameterProfileAdministrationV1.TransferStartingValuesAsync(
        ManufacturingProfileBaselineTransferRequestV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken) =>
        TransferStartingValuesAsync(ManufacturingProfileBaselineReads.Parameters, ManufacturingParameterProfileActionsV1.Publish,
            request, metadata, cancellationToken);

    Task<ManufacturingProfileCommandResultV1> ISettingsManufacturingToleranceProfileAdministrationV1.TransferStartingValuesAsync(
        ManufacturingProfileBaselineTransferRequestV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken) =>
        TransferStartingValuesAsync(ManufacturingProfileBaselineReads.Tolerances, ManufacturingToleranceProfileActionsV1.Publish,
            request, metadata, cancellationToken);

    private async Task<ManufacturingProfileCommandResultV1> TransferStartingValuesAsync(string kind, string permission,
        ManufacturingProfileBaselineTransferRequestV1 request, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateMetadata(metadata);
        var company = RequireContextCompany();
        var scope = await RequireAsync(permission, company, cancellationToken);
        if (auditWriter is null) throw new SettingsPrerequisiteException(ManufacturingProfileProblemCodesV1.ProviderUnavailable);
        if (request.ExpectedVersion != "0") return Unstored(Answers.Invalid("Aktarım için beklenen sürüm 0 olmalıdır."), metadata);
        if (ReasonRefusal(request.Reason, out var reason) is { } invalid) return Unstored(invalid, metadata);
        var command = $"settings.manufacturing-{kind}.baseline-transfer.v1";
        var keyHash = IdempotencyHash.ComputeKey(metadata.IdempotencyKey);
        var requestHash = IdempotencyHash.ComputePayload(new { request.ExpectedVersion, Reason = reason });
        var existing = await FindExecutionAsync(command, keyHash, cancellationToken);
        await RequireTransferStillAsync(permission, metadata, cancellationToken);
        if (existing is not null) return Replay(existing, requestHash, metadata);

        var transferred = await ReadAsync(() => database.ManufacturingProfileBaselineTransfers.AsNoTracking()
            .AnyAsync(row => row.CompanyId == company && row.Kind == kind, cancellationToken));
        await RequireTransferStillAsync(permission, metadata, cancellationToken);
        if (transferred) return await StoreRefusalAsync(command, Answers.VersionConflict(), keyHash, requestHash, metadata, cancellationToken,
            () => RequireTransferStillAsync(permission, metadata, cancellationToken));

        // Copy only source values that already exist for this company; no default for another company or unknown key.
        string snapshot;
        if (kind == ManufacturingProfileBaselineReads.Parameters)
        {
            var values = ApprovedManufacturingProfileCatalog.ParametersFor(company);
            if (values.Count == 0) return Unstored(Answers.NotFound(), metadata);
            snapshot = JsonSerializer.Serialize(values, ManufacturingProfileBaselineReads.Json);
        }
        else
        {
            var values = ApprovedManufacturingProfileCatalog.ToleranceFor(company);
            if (values.Count == 0) return Unstored(Answers.NotFound(), metadata);
            snapshot = JsonSerializer.Serialize(values, ManufacturingProfileBaselineReads.Json);
        }

        var transfer = new ManufacturingProfileBaselineTransferRow
        {
            CompanyId = company, Kind = kind, TransferId = Guid.NewGuid(), Version = 1,
            SnapshotJson = snapshot, SnapshotHash = ManufacturingProfileBaselineReads.Hash(snapshot),
            ActorId = metadata.ActorId, TransferredAtUtc = timeProvider.GetUtcNow(), Reason = reason,
        };
        await RequireTransferStillAsync(permission, metadata, cancellationToken);
        database.ManufacturingProfileBaselineTransfers.Add(transfer);
        Audit(company, permission, BaselineTransferredEvent, "Settings.ManufacturingProfileBaseline", transfer.TransferId,
            "Transfer", 0, 1, scope, null, transfer, reason, metadata);
        var answer = Answers.Ok(Receipt(transfer.TransferId, 0, 1, ManufacturingProfileStatusesV1.Published));
        return await SaveBaselineOwnedAsync(kind, permission, command, answer, keyHash, requestHash, metadata, cancellationToken);
    }

    private async Task<ManufacturingProfileCommandResultV1> SaveBaselineOwnedAsync(string kind, string permission, string command,
        ManufacturingProfileCommandAnswerV1 answer, string keyHash, string requestHash, SettingsCommandMetadataV1 metadata,
        CancellationToken token)
    {
        if (auditWriter is null) throw new SettingsPrerequisiteException(ManufacturingProfileProblemCodesV1.ProviderUnavailable);
        try
        {
            AddExecution(command, answer, keyHash, requestHash, metadata);
            var conflict = false;
            await using (var transaction = await database.Database.BeginTransactionAsync(token))
            {
                await RequireTransferStillAsync(permission, metadata, token);
                // The Audit owner consumes its collector before the legacy Settings interceptor sees it.
                // A failed Settings write or late authority revocation rolls back both module owners.
                await auditWriter.WritePendingAsync(transaction.GetDbTransaction(), token);
                await RequireTransferStillAsync(permission, metadata, token);
                try { await database.SaveChangesAsync(token); }
                catch (DbUpdateException exception) when (exception.GetBaseException() is SqlException { Number: 2601 or 2627 })
                { conflict = true; }
                if (conflict) await transaction.RollbackAsync(CancellationToken.None);
                else
                {
                    await RequireTransferStillAsync(permission, metadata, token);
                    await transaction.CommitAsync(token);
                }
            }
            if (!conflict) return new(answer, false, metadata.CorrelationId);
            database.ChangeTracker.Clear();
            var winner = await FindExecutionAsync(command, keyHash, token);
            await RequireTransferStillAsync(permission, metadata, token);
            if (winner is not null) return Replay(winner, requestHash, metadata);
            var transferred = await database.ManufacturingProfileBaselineTransfers.AsNoTracking()
                .AnyAsync(row => row.CompanyId == metadata.CompanyId && row.Kind == kind, token);
            await RequireTransferStillAsync(permission, metadata, token);
            if (!transferred) throw new SettingsPrerequisiteException(ManufacturingProfileProblemCodesV1.ProviderUnavailable);
            return await StoreRefusalAsync(command, Answers.VersionConflict(), keyHash, requestHash, metadata, token,
                () => RequireTransferStillAsync(permission, metadata, token));
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not UnauthorizedAccessException and not SettingsPrerequisiteException)
        {
            throw new SettingsPrerequisiteException(ManufacturingProfileProblemCodesV1.ProviderUnavailable);
        }
        finally { auditWriter.DiscardPending(); database.ChangeTracker.Clear(); }
    }

    private async Task RequireTransferStillAsync(string permission, SettingsCommandMetadataV1 metadata, CancellationToken cancellationToken)
    {
        RequireTransferContext(metadata);
        await RequireStillAsync(permission, metadata.CompanyId, cancellationToken);
        RequireTransferContext(metadata);
    }

    private void RequireTransferContext(SettingsCommandMetadataV1 metadata)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId != metadata.ActorId || !companyContext.IsResolved
            || companyContext.CompanyId != metadata.CompanyId || !currentUser.CanAccessCompany(metadata.CompanyId))
            throw new UnauthorizedAccessException();
    }
}
