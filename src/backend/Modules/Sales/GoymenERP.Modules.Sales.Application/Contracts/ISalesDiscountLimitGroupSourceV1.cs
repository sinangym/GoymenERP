namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// SAT-D2 / API-12: the discount limit groups of the current actor in the current company — the opaque identities the
/// Settings owner's <c>ISettingsSalesPolicyProviderV1.GetSalesDiscountLimitAsync</c> is asked about. SAT-D63/D66
/// (<c>docs/decisions/SINAN_KARARLARI_2026-10-27_SATIS_EK10.md</c>, <c>…_EK11.md</c>): limit groups are separate from roles
/// and PERSONS are bound to them, per company; the binding is the Settings owner's record (an authorized person's data entry,
/// never a role mapping and never seeded), and the host composes this port from the Settings owner's read. Sales never derives
/// a group from a role name. The company and the actor are the server's, never a request input.
/// <para>
/// Until a host registers this port, Sales reads no group: no limit is defined for anyone, so every positive discount goes to
/// approval (SAT-D2, fail-closed) — never "unlimited". An empty answer (no binding, or only passive groups) means the same.
/// An owner that cannot answer throws <see cref="SalesDiscountLimitGroupSourceException"/>: denied → 403, otherwise 503
/// (<c>sales_discount_limit_unavailable</c>) — never an empty answer.
/// </para>
/// </summary>
public interface ISalesDiscountLimitGroupSourceV1
{
    Task<IReadOnlyList<Guid>> GetCurrentActorLimitGroupsAsync(CancellationToken cancellationToken = default);
}

/// <summary>The limit group owner could not answer: <see cref="IsDenied"/> (403) or unavailable (503).</summary>
public sealed class SalesDiscountLimitGroupSourceException(bool isDenied)
    : Exception(isDenied ? "sales_discount_limit_groups_denied" : "sales_discount_limit_groups_unavailable")
{
    public bool IsDenied { get; } = isDenied;
}
