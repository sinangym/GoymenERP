using System.Data.Common;
using System.Globalization;
using GoymenERP.BuildingBlocks.Concurrency;
using GoymenERP.Modules.Inventory.Application;
using GoymenERP.Modules.Inventory.Domain.FutureSupply;
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
    public async Task<T> ExecuteAsync<T>(Guid companyId, PurchaseGoodsReceiptTargetV1 target, Guid acceptanceId,
        PurchasingWriteEnvelope envelope,
        Func<IPurchaseGoodsReceiptParticipantsV1, CancellationToken, Task<T>> operation,
        Func<CancellationToken, Task> beforeCommit,
        CancellationToken cancellationToken = default)
    {
        PurchaseGoodsReceiptPrelock.ValidateIntent(companyId, target);
        if (acceptanceId == Guid.Empty) throw new PurchasingServiceException(400, PurchaseAcceptanceParticipantCodes.InvalidRequest);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(beforeCommit);
        try
        {
            for (var attempt = 0; attempt < PurchaseGoodsReceiptPrelock.MaximumPreparationAttempts; attempt++)
            {
                await using var transaction = await purchasing.Database.BeginTransactionAsync(cancellationToken);
                await using var purchase = await PurchaseAcceptanceParticipant.EnlistAsync(
                    purchasing.Database.GetDbConnection(), transaction.GetDbTransaction(), companyId, cancellationToken);
                await using var cost = await PurchaseReceiptCostScope.EnlistAsync(purchasing.Database.GetDbConnection(),
                    transaction.GetDbTransaction(), companyId, services, cancellationToken);
                await using var inventory = await InventoryGoodsReceiptParticipant.EnlistAsync(
                    purchasing.Database.GetDbConnection(), transaction.GetDbTransaction(), companyId,
                    services.GetRequiredService<ISettingsPrerequisiteProvider>(), cancellationToken, clock, costSource: cost.Source);
                var participants = new Participants(companyId, purchase, inventory, locationStatus,
                    services.GetRequiredService<IInventoryGoodsReceiptFutureSupplyPortV1>(),
                    services.GetRequiredService<IInventoryFutureAllocationReleasePortV1>(),
                    purchasing.Database.GetDbConnection(), transaction.GetDbTransaction());
                try
                {
                    // Owner replay comes FIRST: never require today's mutable order state or newly created allocation locks to
                    // return yesterday's immutable receipt. The existing callback still checks the complete physical payload and
                    // Purchasing actor/key/request hash; this lookup cannot turn a changed request into a successful replay.
                    var original = await participants.ReadOriginalAsync(acceptanceId, envelope, cancellationToken);
                    if (original is not null)
                    {
                        if (original.CompanyId != companyId || original.AcceptanceId != acceptanceId
                            || original.PurchaseOrderId != target.PurchaseOrderId || original.PurchaseOrderLineId != target.PurchaseOrderLineId)
                            throw InventoryGoodsReceiptException.Refused(409, InventoryGoodsReceiptCodesV1.IdempotencyConflict);
                    }
                    else
                    {
                        await using var readSet = await services.GetRequiredService<IInventoryFutureAllocationReleasePortV1>()
                            .EnlistAsync(purchasing.Database.GetDbConnection(), transaction.GetDbTransaction(), cancellationToken);
                        var ready = await PurchaseGoodsReceiptPrelock.PrepareAsync(companyId, target,
                            token => purchase.ResolveReceiptTargetAsync(target.PurchaseOrderId, target.PurchaseOrderLineId, token),
                            readSet,
                            services.GetRequiredService<ISalesOrderLineSerializationLockBinderV1>()
                                .Bind(purchasing.Database.GetDbConnection(), transaction.GetDbTransaction()),
                            services.GetRequiredService<IPurchaseLineSerializationLockBinderV1>()
                                .Bind(purchasing.Database.GetDbConnection(), transaction.GetDbTransaction()), cancellationToken);
                        if (!ready)
                        {
                            await RollbackQuietlyAsync(transaction);
                            purchasing.ChangeTracker.Clear();
                            continue; // No business callback, participant stage, key, audit or outbox has run.
                        }
                    }
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
            throw PurchaseGoodsReceiptPrelock.RetryExhausted();
        }
        catch (InventoryGoodsReceiptException exception)
        {
            throw new PurchasingServiceException(exception.Status, exception.Code);
        }
        catch (SalesOrderLineBusyException exception) { throw new PurchasingServiceException(409, exception.Code); }
        catch (PurchaseLineBusyException exception) { throw new PurchasingServiceException(409, exception.Code); }
        catch (InventoryAllocationException exception)
        {
            throw new PurchasingServiceException(exception.Status == 409 ? 409 : 503,
                exception.Status == 409 ? PurchaseAcceptanceParticipantCodes.ConcurrentChange : PurchaseGoodsReceiptCodesV1.Unavailable);
        }
    }

    public async Task<PurchaseGoodsReceiptOptionsV1> GetOptionsAsync(PurchaseReceiptSourceV1 source,
        CancellationToken cancellationToken)
    {
        var answer = await locationCandidates.SearchAsync(source.CompanyId,
            new OrganizationLocationCandidateQueryV1(null, 100), cancellationToken);
        if (source.PurchaseOrderGeneralLineId == Guid.Empty) throw PurchaseGoodsReceiptPrelock.Unavailable();
        var allocated = await services.GetRequiredService<IInventoryFutureAllocationReadPortV1>()
            .GetAllocatedOnPurchaseLineAsync(source.PurchaseOrderGeneralLineId, cancellationToken);
        return new PurchaseGoodsReceiptOptionsV1(source, answer.Items.Select(item =>
            new PurchaseReceiptLocationV1(item.LocationId, item.LocationCode, item.LocationName, item.SiteName)).ToArray(),
            answer.SearchWindowFull) { FutureSupply = FutureSupplySummary(source, allocated) };
    }

    internal static PurchaseReceiptFutureSupplySummaryV1 FutureSupplySummary(PurchaseReceiptSourceV1 source,
        InventoryFutureAllocationSumV1? answer)
    {
        if (source.CompanyId == Guid.Empty || source.PurchaseOrderId == Guid.Empty || source.PurchaseOrderGeneralLineId == Guid.Empty
            || source.UnitId == Guid.Empty) throw PurchaseGoodsReceiptPrelock.Unavailable();
        var quantity = answer switch
        {
            { Outcome: InventoryFutureAllocationReadOutcomeV1.None, QuantityText: null, UnitId: null } => "0",
            { Outcome: InventoryFutureAllocationReadOutcomeV1.Present, QuantityText: { } text, UnitId: { } unit }
                when unit == source.UnitId && InventoryFutureAllocationTextV1.TryPositiveQuantity(text, out var parsed)
                    && parsed.ToString() == text => text,
            _ => throw PurchaseGoodsReceiptPrelock.Unavailable(),
        };
        return new(source.CompanyId, source.PurchaseOrderId, source.PurchaseOrderGeneralLineId, source.UnitId, quantity);
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

    /// <summary>
    /// The participants of one receipt transaction. F-017 STOK §3.5 (F017-c, ALS-D84 order): after the purchase acceptance and the
    /// Inventory label are staged, the same transaction converts the received general line's future supply allocations; the
    /// protected receipt contracts do not change and a technical conversion failure rolls the whole receipt back. The bulk
    /// receipt host enlists the same participants once per label.
    /// </summary>
    internal sealed class Participants(Guid companyId, IPurchaseAcceptanceParticipantV1 purchase,
        IInventoryGoodsReceiptParticipantV1 inventory, IOrganizationLocationStatusReaderV1 locations,
        IInventoryGoodsReceiptFutureSupplyPortV1 futureSupply, IInventoryFutureAllocationReleasePortV1 futureReleases,
        DbConnection connection, DbTransaction transaction)
        : IPurchaseGoodsReceiptParticipantsV1
    {
        private PurchaseAcceptanceStageResultV1? staged;

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

        public async Task<PurchaseAcceptanceStageResultV1> StagePurchaseAsync(PurchaseAcceptanceStageRequestV1 request,
            PurchasingWriteEnvelope envelope, CancellationToken cancellationToken)
        {
            var result = await purchase.StageAsync(request, envelope, cancellationToken);
            staged = result;
            return result;
        }

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
            var conversion = await ConvertFutureSupplyAsync(result, envelope, cancellationToken);
            var releases = LinkFailedReleases(conversion, result.AcceptanceId);
            if (releases.Count > 0 && !conversion.IsReplay)
            {
                releases = [.. releases, .. await ReleaseRemainingActiveAsync(releases[0].FailureCode, envelope, cancellationToken)];
            }

            return new(result.CompanyId, result.AcceptanceId, result.PurchaseOrderId, result.PurchaseOrderLineId,
                result.PurchaseOrderVersion, result.ProductId, result.ProductVersion, result.PurchaseUnitId,
                result.LotId, result.LabelId, result.LabelVersion, result.MovementId, result.QuantityKg,
                result.IsReplay, result.OriginalCorrelationId)
            {
                FutureSupplyReleases = releases,
            };
        }

        /// <summary>
        /// KLN-D94 (SINAN-CEVAP 34): a link failure released the allocation's WHOLE remainder in Inventory (link row, audit,
        /// released outbox for Sales). The receipt stands; the conversions that ended <c>LinkFailed</c> go back to the receiving
        /// user as warnings. A conversion answer for another acceptance, or a released row without its failure code, fails closed
        /// (503, the receipt rolls back) instead of hiding a release.
        /// </summary>
        internal static IReadOnlyList<PurchaseReceiptFutureSupplyReleaseV1> LinkFailedReleases(
            InventoryGoodsReceiptFutureSupplyResultV1? conversion, Guid acceptanceId)
        {
            if (conversion is null || conversion.AcceptanceId != acceptanceId || conversion.Conversions is null)
                throw InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
            var releases = new List<PurchaseReceiptFutureSupplyReleaseV1>();
            foreach (var item in conversion.Conversions)
            {
                if (item is null)
                    throw InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
                if (item.Outcome == InventoryFutureSupplyConversionOutcomeV1.Converted.ToString()) continue;
                if (item.Outcome != InventoryFutureSupplyConversionOutcomeV1.LinkFailed.ToString()
                    || string.IsNullOrWhiteSpace(item.FailureReason))
                    throw InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
                releases.Add(new PurchaseReceiptFutureSupplyReleaseV1(item.FutureAllocationId, item.Quantity, item.FailureReason));
            }
            return releases;
        }

        /// <summary>
        /// F017-c: the accepted quantity (purchase unit) and the label's measured kilograms go to Inventory's conversion port on the
        /// receipt's own connection and transaction. The link rows, audit and outbox stay in Inventory; the host returns the answer
        /// so KLN-D94 releases reach the receipt response. Inventory refusals map to the receipt's own stable codes.
        /// </summary>
        private async Task<InventoryGoodsReceiptFutureSupplyResultV1> ConvertFutureSupplyAsync(InventoryGoodsReceiptResultV1 result,
            PurchasingWriteEnvelope envelope, CancellationToken cancellationToken)
        {
            var acceptance = staged;
            if (acceptance is null || acceptance.AcceptanceId != result.AcceptanceId
                || acceptance.PurchaseOrderId != result.PurchaseOrderId || acceptance.CompanyId != result.CompanyId)
                throw InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
            try
            {
                await using var session = await futureSupply.EnlistAsync(connection, transaction, cancellationToken);
                return await session.ConvertAsync(new InventoryGoodsReceiptFutureSupplyRequestV1(result.CompanyId,
                    result.AcceptanceId, result.PurchaseOrderId,
                    acceptance.PurchaseOrderGeneralLineId ?? acceptance.PurchaseOrderLineId, result.PurchaseUnitId,
                    result.ProductId, result.ProductVersion, result.LabelId, acceptance.AcceptedQuantity, result.QuantityKg,
                    result.IsReplay), Metadata(envelope), cancellationToken);
            }
            catch (InventoryAllocationException exception)
            {
                throw exception.Status == 409
                    ? InventoryGoodsReceiptException.Refused(409,
                        exception.Code == InventoryFutureAllocationCodesV1.IdempotencyConflict
                            ? InventoryGoodsReceiptCodesV1.IdempotencyConflict
                            : InventoryGoodsReceiptCodesV1.ConcurrentChange)
                    : InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
            }
        }

        /// <summary>
        /// KLN-D110 (2): once a link failure released an allocation, EVERY allocation still Active on the received general line is
        /// released whole too (only the allocations this receipt's quantity reached were converted or failed by Inventory). The
        /// owner's release port runs on the receipt's own connection and transaction, so Inventory writes its own allocation,
        /// ledger, audit and <c>inventory.future-allocation.released.v1</c> outbox (reason <c>acceptance-link-failed</c>, the
        /// failure's Turkish text, origin purchasing) and each Sales line responsible is warned by Sales from that event. A
        /// replayed conversion is not swept again (it would release allocations created after the original receipt).
        /// </summary>
        private async Task<IReadOnlyList<PurchaseReceiptFutureSupplyReleaseV1>> ReleaseRemainingActiveAsync(string failureCode,
            PurchasingWriteEnvelope envelope, CancellationToken cancellationToken)
        {
            var acceptance = staged
                ?? throw InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
            try
            {
                await using var session = await futureReleases.EnlistAsync(connection, transaction, cancellationToken);
                return await ReleaseRemainingActiveAsync(session, acceptance.PurchaseOrderGeneralLineId ?? acceptance.PurchaseOrderLineId,
                    failureCode, Metadata(envelope), cancellationToken);
            }
            catch (InventoryAllocationException exception)
            {
                throw exception.Status == 409
                    ? InventoryGoodsReceiptException.Refused(409, InventoryGoodsReceiptCodesV1.ConcurrentChange)
                    : InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
            }
        }

        /// <summary>
        /// The KLN-D110 sweep over one release session: each Active allocation of <paramref name="generalLineId"/> is released whole
        /// with the version just read and becomes one more receipt warning with the triggering failure code. An answer for another
        /// line, a non-positive remainder, an unreadable version or a release that does not end Released fails closed (503).
        /// </summary>
        internal static async Task<IReadOnlyList<PurchaseReceiptFutureSupplyReleaseV1>> ReleaseRemainingActiveAsync(
            IInventoryFutureAllocationReleaseSessionV1 session, Guid generalLineId, string failureCode,
            InventoryCommandMetadata metadata, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (generalLineId == Guid.Empty || !PurchaseReceiptFutureSupplyReleaseCodesV1.All.Contains(failureCode))
                throw InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
            var reasonText = InventoryFutureSupplyLinkFailuresV1.ReleaseText(failureCode);
            var active = await session.ListActiveByPurchaseLineAsync(generalLineId, cancellationToken)
                ?? throw InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
            var releases = new List<PurchaseReceiptFutureSupplyReleaseV1>();
            foreach (var view in active)
            {
                if (view is null || view.AllocationId == Guid.Empty || view.PurchaseOrderLineId != generalLineId
                    || view.Status != FutureSupplyAllocationStatus.Active.ToString()
                    || !long.TryParse(view.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version <= 0
                    || !decimal.TryParse(view.RemainingQuantity, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var remaining)
                    || remaining <= 0m || releases.Any(current => current.FutureAllocationId == view.AllocationId))
                    throw InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
                var after = await session.ReleaseAsync(new InventoryFutureAllocationReleaseRequestV1(view.AllocationId, version, null,
                    InventoryFutureAllocationReleaseReasonsV1.AcceptanceLinkFailed, reasonText, InventoryFutureAllocationOriginsV1.Purchasing),
                    metadata, cancellationToken);
                if (after is null || after.AllocationId != view.AllocationId
                    || after.Status != FutureSupplyAllocationStatus.Released.ToString())
                    throw InventoryGoodsReceiptException.Refused(503, InventoryGoodsReceiptCodesV1.Unavailable);
                releases.Add(new PurchaseReceiptFutureSupplyReleaseV1(view.AllocationId, view.RemainingQuantity, failureCode));
            }

            return releases;
        }

        private static InventoryCommandMetadata Metadata(PurchasingWriteEnvelope envelope) => new(
            envelope.ActorId, envelope.PermissionCode, envelope.RoleCodes, envelope.CorrelationId,
            envelope.CausationId, envelope.IdempotencyKey, envelope.SourceChannel,
            envelope.ClientIpAddress, envelope.UserAgent);
    }
}
