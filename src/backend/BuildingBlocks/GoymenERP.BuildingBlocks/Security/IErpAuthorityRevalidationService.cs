namespace GoymenERP.BuildingBlocks.Security;

/// <summary>
/// Measures an actor's authority for one permission as it stands NOW, for a caller that must not
/// release something it already read under an authority that has since been taken away (Business
/// #66 5720113915; the measurement that asked for it is #265 comment 5719822744).
/// <para>
/// A separate port on purpose. <see cref="IErpAuthorizationService"/> answers from the request's
/// authorization view: it caches per (actor, company, permission) so that one request reads a
/// stable image, and other modules' test doubles implement it, so it does not change.
/// </para>
/// <para>
/// This is NOT a way around that cache. A revalidation re-reads the authority rows and then
/// REPLACES the cached answer for that (actor, company, permission), so the request's view moves
/// forward with it: a grant this call finds revoked can no longer be resurrected by a later
/// <see cref="IErpAuthorizationService.HasPermissionAsync"/> in the same request. There is one
/// answer per request, and this is how it is refreshed.
/// </para>
/// <para>
/// The refresh works in both directions, because it reports what the database says at that
/// moment: a grant revoked during the request becomes visible to the rest of it, and so does a
/// grant ADDED during the request. Neither direction opens anything the stored authority does not
/// already say, and only a caller that asks for a fresh measurement moves its own view.
/// </para>
/// <para>
/// <b>Who uses it:</b> an owner-owned read that fences results it already holds — it asks once
/// before the first read and again at each fence before releasing what a read produced. A
/// capability or menu snapshot does not use it: that is a per-request image by contract, and it
/// authorizes nothing.
/// </para>
/// <para>
/// <b>Cost:</b> one database round-trip per call, the same query
/// <see cref="IErpAuthorizationService.GetPermissionScopeAsync"/> makes on a cache miss. It is
/// never served from the cache, so a caller that fences four reads pays five round-trips for one
/// use case. That is the price of the guarantee; a caller that does not need it keeps asking
/// <see cref="IErpAuthorizationService"/>.
/// </para>
/// <para>
/// <b>What it does not do:</b> it grants nothing, registers no permission name, and decides no
/// scope semantics. An unknown name, an empty company or an unidentified actor is
/// <see cref="PermissionScope.Denied"/> without a read, exactly as on the cached path. The
/// per-actor snapshot rows behind
/// <see cref="IErpAuthorizationService.GetAuthorizationSnapshotAsync"/> are left alone.
/// </para>
/// </summary>
public interface IErpAuthorityRevalidationService
{
    Task<PermissionScope> RevalidatePermissionScopeAsync(
        string permission,
        Guid companyId,
        CancellationToken cancellationToken = default);
}
