using GoymenERP.BuildingBlocks.Multitenancy;
using GoymenERP.BuildingBlocks.Security;
using GoymenERP.Modules.Purchasing.Application.Contracts;

namespace GoymenERP.Api.PurchasingRuntime;

/// <summary>Exact Purchasing occurrence facts under the same company-wide OrdersRead permission as native
/// goods-receipt detail. No general Documents/Inventory read, physical receipt certification or commercial effect.</summary>
public sealed class PurchaseUnorderedAcceptanceReadHostV1(ICompanyContext company, ICurrentUser user,
    IErpAuthorityRevalidationService authority, IPurchaseUnorderedAcceptanceSourceV1? source = null)
{
    public async Task<PurchaseUnorderedAcceptanceSourceV1> ReadAsync(Guid acceptanceId, Guid acceptanceLineId,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || string.IsNullOrWhiteSpace(user.UserId)) throw Failure(401);
        var companyId = company.IsResolved ? company.CompanyId : Guid.Empty; var actor = user.UserId;
        await Fence();
        if (acceptanceId == Guid.Empty || acceptanceLineId == Guid.Empty) throw Failure(404);
        if (source is null) throw Failure(503);
        PurchaseUnorderedAcceptanceSourceAnswerV1 answer;
        try { answer = await source.ReadAsync(companyId, acceptanceId, acceptanceLineId, token); }
        catch (OperationCanceledException) { throw; }
        catch { await Fence(); throw Failure(503); }
        // Detach the sole collection before the last external authority callback; release exactly the observed owner facts.
        var observed = answer?.Source;
        var frozen = observed?.ControlEvidence is { } controls
            ? observed with { ControlEvidence = Array.AsReadOnly(controls.ToArray()) } : null;
        await Fence();
        if (answer is null) throw Failure(503);
        if (answer.Outcome == PurchaseUnorderedAcceptanceSourceOutcomeV1.NotFound
            || answer.Outcome == PurchaseUnorderedAcceptanceSourceOutcomeV1.Found && observed is { } foreign && foreign.CompanyId != companyId)
            throw Failure(404);
        if (answer.Outcome != PurchaseUnorderedAcceptanceSourceOutcomeV1.Found || frozen is null
            || frozen.AcceptanceId != acceptanceId || frozen.AcceptanceLineId != acceptanceLineId
            || frozen.Version < 1 || frozen.SupplierPartyId == Guid.Empty || frozen.CompanyAccountId == Guid.Empty
            || frozen.Subject is null || frozen.Evidence is null || frozen.UnitId == Guid.Empty)
            throw Failure(503);
        return frozen;

        async Task Fence()
        {
            token.ThrowIfCancellationRequested(); Same();
            PermissionScope grant;
            try { grant = await authority.RevalidatePermissionScopeAsync(PurchasingPermissionManifestV1.OrdersRead, companyId, token); }
            catch (OperationCanceledException) { throw; }
            catch { token.ThrowIfCancellationRequested(); Same(); throw Failure(503); }
            token.ThrowIfCancellationRequested(); Same();
            if (grant is not { IsGranted: true, IsCompanyWide: true }) throw Failure(403);
        }
        void Same()
        {
            if (companyId == Guid.Empty || !company.IsResolved || company.CompanyId != companyId || !user.IsAuthenticated
                || user.UserId != actor || !user.CanAccessCompany(companyId)) throw Failure(403);
        }
    }
    private static PurchasingServiceException Failure(int status) => new(status,
        status == 401 ? "unauthenticated" : status == 403 ? "forbidden" : status == 404 ? "not_found" : "service_unavailable");
}
