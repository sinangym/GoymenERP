/**
 * Capability ids the Ayarlar/Organizasyon screens need — PROPOSED SOURCE, not a grant.
 *
 * READ THIS BEFORE USING ANY VALUE HERE.
 *
 * The server publishes NO `organization.*` shell capability today. At `main@3e7dd98`
 * the only implementation of `IShellCapabilityProvider` is
 * `CounterpartyShellCapabilityProvider`; there is no Organization counterpart. So the
 * three ids below are a PROPOSAL handed to the owners who can actually publish them
 * (see ./SHARED_MOUNT_HANDOFF.md), and until the backend registry declares them:
 *
 *   - `hasCapability` answers false for all three,
 *   - `CapabilityRoute` closes these routes onto the same neutral not-found block any
 *     unknown path gets, and
 *   - the nav shows nothing.
 *
 * That is the INTENDED state, not a gap. Listing an id grants nothing; the server
 * decides availability and re-authorizes every request regardless (ERP-000K §10.2).
 * The frontend must never mint a capability to open its own screen.
 *
 * WHY THESE THREE, AND WHY NOT FEWER. Each maps onto a permission that already exists
 * and is already enforced in merged code (`ErpPermissions`), so the split mirrors a
 * real authority boundary rather than a UI convenience:
 *
 *   organization.sites.list       <- organization.sites.read + organization.locations.read
 *   organization.locations.detail <- organization.locations.read
 *   organization.locations.status <- organization.locations.status.update
 *
 * Reading a location and being allowed to deactivate it are DIFFERENT authorities on
 * the server — the endpoints carry different `RequireAuthorization` attributes — so
 * fusing the last two into one id would hand out the write wherever the read is
 * granted. The same reasoning made the Cari detail a separate grant from the list
 * (#182/#172).
 *
 * Note the read scopes are not identical either: `/sites` is scoped by
 * `organization.sites.read` and `/locations` by `organization.locations.read`, so a
 * user can legitimately hold one and not the other. The list screen is built to
 * survive that (see OrganizationSettingsPage).
 */

export const ORGANIZATION_CAPABILITY = {
  /**
   * ORG-01 — Ayarlar > Organizasyon: facilities + their locations.
   * Route `/ayarlar/organizasyon`.
   */
  settingsList: 'organization.sites.list',
  /**
   * ORG-02 — one location's card.
   * Route `/ayarlar/organizasyon/lokasyon/:locationId`. A SEPARATE grant from the
   * list, for the reason #182 made the Cari detail separate.
   */
  locationDetail: 'organization.locations.detail',
  /**
   * The reasoned activate/deactivate control INSIDE ORG-02. Not a route — a control.
   * Gated with `hasCapability`, so a read-only holder sees the card without a write
   * surface that would only fail at the server.
   */
  locationStatus: 'organization.locations.status',
} as const

export type OrganizationCapabilityId =
  (typeof ORGANIZATION_CAPABILITY)[keyof typeof ORGANIZATION_CAPABILITY]

/** Route path per capability, as proposed to the shell owner. */
export const ORGANIZATION_ROUTE: Record<string, string> = {
  [ORGANIZATION_CAPABILITY.settingsList]: '/ayarlar/organizasyon',
  [ORGANIZATION_CAPABILITY.locationDetail]: '/ayarlar/organizasyon/lokasyon/:locationId',
}

/** Nav label. Only the list gets one — the card is reached from a row, never the sidebar. */
export const ORGANIZATION_NAV_LABEL: Record<string, string> = {
  [ORGANIZATION_CAPABILITY.settingsList]: 'Organizasyon',
  [ORGANIZATION_CAPABILITY.locationDetail]: '',
}

/** Concrete path to one location's card. */
export function locationDetailPath(locationId: string): string {
  return `/ayarlar/organizasyon/lokasyon/${encodeURIComponent(locationId)}`
}
