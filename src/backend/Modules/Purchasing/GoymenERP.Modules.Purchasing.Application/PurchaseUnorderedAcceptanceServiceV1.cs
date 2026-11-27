using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Observability;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Modules.Purchasing.Application;

/// <summary>Records an actual unordered acceptance occurrence only. There is no Inventory, Finance, matching,
/// commercial price, or approval engine dependency. A missing actual prerequisite owner fails closed.</summary>
public sealed class PurchaseUnorderedAcceptanceServiceV1(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, ICorrelationContext correlation, IPurchaseUnorderedAcceptanceStoreV1 store,
    TimeProvider time, IPurchaseUnorderedAcceptancePrerequisitesV1? prerequisites = null) : IPurchaseUnorderedAcceptanceServiceV1
{
    public async Task<PurchaseUnorderedAcceptanceWriteV1> RecordAsync(RecordPurchaseUnorderedAcceptanceV1 request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        var scope = await RequireAsync(cancellationToken);
        var frozen = PurchaseUnorderedAcceptanceOwnerContractV1.Freeze(request);
        if (!PurchaseUnorderedAcceptanceOwnerContractV1.Valid(frozen)) throw Failure(400);
        var envelope = new PurchasingWriteEnvelope(scope.Actor, PurchasingPermissionManifestV1.GoodsReceiptsRecord,
            scope.Roles, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey, correlation.SourceChannel,
            correlation.ClientIpAddress, correlation.UserAgent);
        var hash = PurchasingRequestHash.Compute(PurchaseUnorderedAcceptanceOwnerContractV1.RecordCommand, frozen.AcceptanceId, frozen);
        PurchaseUnorderedAcceptanceWriteV1 result;
        try
        {
            result = await store.RecordAsync(scope.Company, frozen, envelope, hash, async token =>
            {
                if (prerequisites is null) throw Failure(503);
                try { await prerequisites.ValidateAsync(scope.Company, frozen, Fence, token); }
                catch (OperationCanceledException) { throw; }
                catch { await Fence(token); throw; }
                await Fence(token);
                return new(new(scope.Company, frozen.AcceptanceId, frozen.AcceptanceLineId, 1, frozen.SupplierPartyId,
                    frozen.CompanyAccountId, frozen.Subject, frozen.UnitId, frozen.AcceptedQuantity, frozen.AcceptedQuantity,
                    frozen.OccurredAtUtc, frozen.Evidence, frozen.ControlEvidence), frozen.ExpectedPartyVersion,
                    frozen.ExpectedAccountVersion, frozen.UnitProfile, scope.Actor, time.GetUtcNow());
            }, Fence, cancellationToken);
        }
        catch (PurchasingStoreException error) when (error.Code == PurchasingStoreCodes.SameKeyPayloadMismatch)
        { await Fence(cancellationToken); throw new PurchasingServiceException(409, PurchasingProblemCodesV1.SameKeyPayloadMismatch); }
        await Fence(cancellationToken);
        if (result is null || !PurchaseUnorderedAcceptanceOwnerContractV1.Valid(result.Record)
            || result.Record.Source.CompanyId != scope.Company || result.Record.RecordedBy != scope.Actor
            || PurchasingRequestHash.Compute(PurchaseUnorderedAcceptanceOwnerContractV1.RecordCommand, frozen.AcceptanceId,
                PurchaseUnorderedAcceptanceOwnerContractV1.Request(result.Record)) != hash
            || string.IsNullOrWhiteSpace(result.OriginalCorrelationId)
            || !result.IsReplay && result.OriginalCorrelationId != metadata.CorrelationId) throw Failure(503);
        return result;
        async Task Fence(CancellationToken token)
        {
            var current = await RequireAsync(token);
            if (current.Company != scope.Company || current.Actor != scope.Actor) throw Failure(403);
            token.ThrowIfCancellationRequested();
        }
    }

    private async Task<(Guid Company, string Actor, IReadOnlyCollection<string> Roles)> RequireAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw Failure(401);
        var id = company.CompanyId; var actor = user.UserId;
        if (!Same(id, actor)) throw Failure(403);
        var measured = await authority.RevalidatePermissionScopeAsync(PurchasingPermissionManifestV1.GoodsReceiptsRecord, id, token);
        token.ThrowIfCancellationRequested();
        if (!measured.IsGranted || !measured.IsCompanyWide || !Same(id, actor)) throw Failure(403);
        return (id, actor!, measured.RoleCodes.Order(StringComparer.Ordinal).ToArray());
    }
    private bool Same(Guid id, string? actor) => id != Guid.Empty && company.IsResolved && company.CompanyId == id
        && user.IsAuthenticated && user.UserId == actor && user.CanAccessCompany(id);
    private static PurchasingServiceException Failure(int status) => new(status,
        status == 401 ? "unauthenticated" : status == 403 ? "forbidden" : status == 400 ? "validation_failed" : "service_unavailable");
}
