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
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default) =>
        await RecordCoreAsync(request, null, metadata, cancellationToken);

    public async Task<PurchaseUnorderedAcceptanceWriteV1> RecordOccurrenceAsync(RecordPurchaseUnorderedAcceptanceV2 request,
        PurchasingCommandMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Occurrence is null) { await RequireAsync(cancellationToken); throw Failure(400); }
        return await RecordCoreAsync(request.Occurrence, new(request.BusinessDate, request.Reason), metadata, cancellationToken);
    }

    private async Task<PurchaseUnorderedAcceptanceWriteV1> RecordCoreAsync(RecordPurchaseUnorderedAcceptanceV1 request,
        PurchaseUnorderedAcceptanceDeclarationV2? declaration, PurchasingCommandMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(metadata);
        var frozen = PurchaseUnorderedAcceptanceOwnerContractV1.Freeze(request);
        var scope = await RequireAsync(cancellationToken);
        if (!PurchaseUnorderedAcceptanceOwnerContractV1.Valid(frozen)) throw Failure(400);
        var envelope = new PurchasingWriteEnvelope(scope.Actor, PurchasingPermissionManifestV1.GoodsReceiptsRecord,
            scope.Roles, metadata.CorrelationId, metadata.CausationId, metadata.IdempotencyKey, correlation.SourceChannel,
            correlation.ClientIpAddress, correlation.UserAgent);
        var occurrence = declaration is null ? null : new RecordPurchaseUnorderedAcceptanceV2(frozen, declaration.BusinessDate, declaration.Reason);
        if (occurrence is not null && !PurchaseUnorderedAcceptanceOwnerContractV1.Valid(occurrence)) throw Failure(400);
        var hash = occurrence is null ? PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(frozen)
            : PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(occurrence);
        PurchaseUnorderedAcceptanceWriteV1 result;
        try
        {
            async Task<PurchaseUnorderedAcceptanceRecordV1> Prepare(CancellationToken token)
            {
                if (prerequisites is null) throw Failure(503);
                try
                {
                    if (occurrence is null) await prerequisites.ValidateAsync(scope.Company, frozen, Fence, token);
                    else await prerequisites.ValidateOccurrenceAsync(scope.Company, occurrence, Fence, token);
                }
                catch (OperationCanceledException) { throw; }
                catch { await Fence(token); throw; }
                await Fence(token);
                return new(new(scope.Company, frozen.AcceptanceId, frozen.AcceptanceLineId, 1, frozen.SupplierPartyId,
                    frozen.CompanyAccountId, frozen.Subject, frozen.UnitId, frozen.AcceptedQuantity, frozen.AcceptedQuantity,
                    frozen.OccurredAtUtc, frozen.Evidence, frozen.ControlEvidence) { Declaration = declaration }, frozen.ExpectedPartyVersion,
                    frozen.ExpectedAccountVersion, frozen.UnitProfile, scope.Actor, time.GetUtcNow());
            }
            result = occurrence is null
                ? await store.RecordAsync(scope.Company, frozen, envelope, hash, Prepare, Fence, cancellationToken)
                : await store.RecordOccurrenceAsync(scope.Company, occurrence, envelope, hash, Prepare, Fence, cancellationToken);
        }
        catch (PurchasingStoreException error) when (error.Code == PurchasingStoreCodes.SameKeyPayloadMismatch)
        { await Fence(cancellationToken); throw new PurchasingServiceException(409, PurchasingProblemCodesV1.SameKeyPayloadMismatch); }
        await Fence(cancellationToken);
        if (result is null || !PurchaseUnorderedAcceptanceOwnerContractV1.Valid(result.Record)
            || result.Record.Source.CompanyId != scope.Company || result.Record.RecordedBy != scope.Actor
            || PurchaseUnorderedAcceptanceOwnerContractV1.RequestHash(result.Record) != hash
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
        if (!company.IsResolved) throw Failure(403);
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
