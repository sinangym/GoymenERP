namespace GoymenERP.Modules.Sales.Application.Contracts;

/// <summary>
/// SAT-D2 / API-12: the discount limit groups of the current actor in the current company — the opaque identities the
/// Settings owner's <c>ISettingsSalesPolicyProviderV1.GetSalesDiscountLimitAsync</c> is asked about. Mapping a user's roles to
/// limit groups is the Identity owner's work and a human grant gate (Settings README, "Consumer obligations"); Sales never
/// derives a group from a role name. The company and the actor are the server's, never a request input.
/// <para>
/// Until an owner registers this port, Sales reads no group: no limit is defined for anyone, so every positive discount
/// goes to approval (SAT-D2, fail-closed) — never "unlimited". An empty answer means the same.
/// </para>
/// </summary>
public interface ISalesDiscountLimitGroupSourceV1
{
    Task<IReadOnlyList<Guid>> GetCurrentActorLimitGroupsAsync(CancellationToken cancellationToken = default);
}
