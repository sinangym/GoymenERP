using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.Stock;
using GoymenERP.Modules.Inventory.Infrastructure.Persistence;
using GoymenERP.Modules.Organization.Application.Contracts;
using GoymenERP.Modules.Purchasing.Application.Contracts;
using GoymenERP.Modules.Purchasing.Infrastructure.Persistence;
using GoymenERP.Modules.Settings.Application.Contracts.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace GoymenERP.Api.PurchasingRuntime;

internal sealed class PurchaseGoodsReceiptHost(
    PurchasingDbContext purchasing,
    IOrganizationLocationCandidatesV1 locationCandidates,
    IOrganizationLocationStatusReaderV1 locationStatus,
    IPurchaseAcceptanceSourceV1 acceptanceSource,
    IInventoryGoodsReceiptSourceV1 inventorySource,
    IServiceProvider services,
    TimeProvider clock) : IPurchaseGoodsReceiptTransactionV1, IPurchaseGoodsReceiptReadsV1
{
    public async Task<T> ExecuteAsync<T>(Guid companyId,
        Func<IPurchaseGoodsReceiptParticipantsV1, CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty) throw new InvalidOperationException("Goods receipt requires a company.");
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(beforeCommit);
        try
        {
            await using var transaction = await purchasing.Database.BeginTransactionAsync(cancellationToken);
            await using var purchase = await PurchaseAcceptanceParticipant.EnlistAsync(
                purchasing.Database.GetDbConnection(), transaction.GetDbTransaction(), companyId, cancellationToken);
            await using var inventory = await InventoryGoodsReceiptParticipant.EnlistAsync(
                purchasing.Database.GetDbConnection(), transaction.GetDbTransaction(), companyId,
                services.GetRequiredService<ISettingsPrerequisiteProvider>(), cancellationToken, clock);
            var participants = new Participants(companyId, purchase, inventory, locationStatus);
            try
            {
                var result = await operation(participants, cancellationToken);
                await beforeCommit(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await RollbackQuietlyAsync(transaction);
                purchasing.ChangeTracker.Clear();
                throw;
            }
        }
        catch (InventoryGoodsReceiptException exception)
        {
            throw new PurchasingServiceException(exception.Status, exception.Code);
        }
    }

    public async Task<PurchaseGoodsReceiptOptionsV1> GetOptionsAsync(PurchaseReceiptSourceV1 source,
        CancellationToken cancellationToken)
    {
        var answer = await locationCandidates.SearchAsync(source.CompanyId,
            new OrganizationLocationCandidateQueryV1(null, 100), cancellationToken);
        return new PurchaseGoodsReceiptOptionsV1(source, answer.Items.Select(item =>
            new PurchaseReceiptLocationV1(item.LocationId, item.LocationCode, item.LocationName, item.SiteName)).ToArray(),
            answer.SearchWindowFull);
    }

    public async Task<PurchaseGoodsReceiptViewV1?> GetReceiptAsync(Guid orderId, Guid acceptanceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var purchase = await acceptanceSource.ReadAsync(orderId, acceptanceId, cancellationToken);
            if (purchase is null) return null;
            var stock = await inventorySource.ReadAsync(acceptanceId, cancellationToken);
            if (stock is null) return null;
            var request = stock.OriginalRequest;
            var result = stock.OriginalResult;
            if (purchase.CompanyId != request.CompanyId || purchase.AcceptanceId != request.AcceptanceId
                || purchase.PurchaseOrderId != request.PurchaseOrderId || purchase.PurchaseOrderLineId != request.PurchaseOrderLineId
                || purchase.PurchaseOrderVersionAfter != request.PurchaseOrderVersionAfter
                || result.PurchaseOrderVersion != request.PurchaseOrderVersionAfter)
                throw InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
            return new PurchaseGoodsReceiptViewV1(request.CompanyId, request.AcceptanceId, request.PurchaseOrderId,
                request.PurchaseOrderLineId, request.PurchaseOrderVersionAfter, request.ProductId,
                request.ExpectedProductVersion, request.PurchaseUnitId, purchase.AcceptedQuantity,
                purchase.RemainingQuantity, request.LotId, request.LabelId, request.LabelCode, result.LabelVersion,
                request.MovementId, request.LocationId, request.QuantityKg, request.InitialBlock.ToString(), request.OccurredAtUtc);
        }
        catch (InventoryGoodsReceiptException exception)
        {
            throw new PurchasingServiceException(exception.Status, exception.Code);
        }
    }

    private static async Task RollbackQuietlyAsync(IDbContextTransaction transaction)
    {
        try { await transaction.RollbackAsync(CancellationToken.None); }
        catch { /* Preserve the original operation failure. */ }
    }

    private sealed class Participants(Guid companyId, IPurchaseAcceptanceParticipantV1 purchase,
        IInventoryGoodsReceiptParticipantV1 inventory, IOrganizationLocationStatusReaderV1 locations)
        : IPurchaseGoodsReceiptParticipantsV1
    {
        public async Task<PurchaseReceiptStockRequestV1?> ReadOriginalAsync(Guid acceptanceId,
            PurchasingWriteEnvelope envelope, CancellationToken cancellationToken)
        {
            var source = await inventory.ReadReplayAsync(acceptanceId, Metadata(envelope), cancellationToken);
            if (source is null) return null;
            var request = source.OriginalRequest;
            return new PurchaseReceiptStockRequestV1(request.CompanyId, request.AcceptanceId, request.PurchaseOrderId,
                request.PurchaseOrderLineId, request.PurchaseOrderVersionAfter, request.ProductId,
                request.ExpectedProductVersion, request.PurchaseUnitId, request.LotId, request.LabelId,
                request.LabelCode, request.HeatNumber, request.CoilNumber, request.RequiredDocuments,
                request.OwnerKind.ToString(), request.OwnerId, request.CustodianKind.ToString(), request.CustodianId,
                request.LocationId, request.MovementId, request.QuantityKg, request.OccurredAtUtc)
                { InitialBlock = request.InitialBlock.ToString() };
        }

        public async Task<PurchaseReceiptFactsV1> ResolveFirstAsync(PurchaseReceiptSourceV1 source, Guid locationId,
            CancellationToken cancellationToken)
        {
            var status = await locations.ReadAsync(source.CompanyId, locationId, cancellationToken);
            if (status is null || !status.IsActive || status.IsExternal)
                throw InventoryGoodsReceiptException.Refused(404, InventoryGoodsReceiptCodesV1.InvalidReference);
            return new(source.CompanyId, source.ProductId, source.ProductVersion, locationId,
                StockPartyKind.Company.ToString(), companyId, StockPartyKind.Company.ToString(), companyId);
        }

        public Task<PurchaseAcceptanceStageResultV1> StagePurchaseAsync(PurchaseAcceptanceStageRequestV1 request,
            PurchasingWriteEnvelope envelope, CancellationToken cancellationToken) =>
            purchase.StageAsync(request, envelope, cancellationToken);

        public async Task<PurchaseReceiptStockResultV1> StageInventoryAsync(PurchaseReceiptStockRequestV1 request,
            PurchasingWriteEnvelope envelope, CancellationToken cancellationToken)
        {
            if (!Enum.TryParse<StockPartyKind>(request.OwnerKind, false, out var owner)
                || !Enum.TryParse<StockPartyKind>(request.CustodianKind, false, out var custodian)
                || !Enum.TryParse<BlockState>(request.InitialBlock, false, out var block))
                throw InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
            var result = await inventory.StageAsync(new InventoryGoodsReceiptRequestV1(request.CompanyId,
                request.AcceptanceId, request.PurchaseOrderId, request.PurchaseOrderLineId,
                request.PurchaseOrderVersionAfter, request.ProductId, request.ExpectedProductVersion,
                request.PurchaseUnitId, request.LotId, request.LabelId, request.LabelCode, request.HeatNumber,
                request.CoilNumber, request.RequiredDocuments, owner, request.OwnerId, custodian,
                request.CustodianId, request.LocationId, block, request.MovementId, request.QuantityKg,
                request.OccurredAtUtc), Metadata(envelope), cancellationToken);
            return new(result.CompanyId, result.AcceptanceId, result.PurchaseOrderId, result.PurchaseOrderLineId,
                result.PurchaseOrderVersion, result.ProductId, result.ProductVersion, result.PurchaseUnitId,
                result.LotId, result.LabelId, result.LabelVersion, result.MovementId, result.QuantityKg,
                result.IsReplay, result.OriginalCorrelationId);
        }

        private static InventoryCommandMetadata Metadata(PurchasingWriteEnvelope envelope) => new(
            envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes, envelope.CorrelationId,
            envelope.CausationId, envelope.IdempotencyKey, envelope.SourceChannel,
            envelope.ClientIpAddress, envelope.UserAgent);
    }
}
