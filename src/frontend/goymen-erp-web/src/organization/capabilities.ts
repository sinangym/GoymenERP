/**
 * Capability ids the Ayarlar/Organizasyon screens need — a STATEMENT OF INTENT, not a grant.
 *
 * READ THIS BEFORE USING ANY VALUE HERE.
 *
 * The header here used to say the server published no `organization.*` capability at all,
 * which was true at `main@3e7dd98` and has not been true since #246. It now publishes all
 * of the ids below (`OrganizationShellCapabilityProvider`). The correction matters more
 * than the fact: a stale "the server does not do this yet" reads as a live constraint to
 * the next person, who then builds around a staging problem that was solved months ago.
 *
 * What has NOT changed is what listing an id here means. It is this lane saying it has an
 * implementation for that id — nothing more. Whether the id is GRANTED is the server's
 * answer, per caller, and:
 *
 *   - `hasCapability` answers false for an id the server did not grant THIS caller,
 *   - `CapabilityRoute` then closes that route onto the same neutral not-found block any
 *     unknown path gets, and
 *   - the nav entry is not drawn.
 *
 * That is the INTENDED state, not a gap. Listing an id grants nothing; the server
 * decides availability and re-authorizes every request regardless (ERP-000K §10.2).
 * The frontend must never mint a capability to open its own screen.
 *
 * WHY THESE, AND WHY NOT FEWER. Each maps onto a permission that already exists
 * and is already enforced in merged code (`ErpPermissions`), so the split mirrors a
 * real authority boundary rather than a UI convenience:
 *
 *   organization.sites.list        <- organization.locations.read
 *   organization.locations.detail  <- organization.locations.read
 *   organization.locations.status  <- organization.locations.status.update
 *   organization.sites.read        <- organization.sites.read
 *   organization.work-centers.list <- organization.work-centers.read
 *   organization.work-centers.detail <- organization.work-centers.read (ORG-04, the row's own read)
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
   *
   * Permissions: `organization.locations.read` + `organization.locations.status.update`.
   */
  locationStatus: 'organization.locations.status',
  /**
   * The OPTIONAL facility-label control on ORG-01. Also not a route.
   *
   * Permissions: `organization.locations.read` + `organization.sites.read`.
   *
   * This is what makes the facility names an optional enrichment rather than a
   * precondition: the ORG-01 ROUTE stands on `organization.locations.read` alone, so a
   * user without sites access still gets the list and the card they are entitled to.
   *
   * THE RULE THIS ID CARRIES: when it is absent, the `/sites` request is NOT ISSUED AT
   * ALL. Firing it anyway and then quietly discarding the 403 would be the client
   * deciding which refusals count — the capability is the server's own statement that
   * this read is not available here, so there is nothing to ask. When the capability IS
   * present and a real 403 still comes back, that is a genuine refusal and goes through
   * the ordinary capture/report path like any other.
   */
  sitesRead: 'organization.sites.read',
  /**
   * ORG-03 — the company's work centres.
   * Route `/ayarlar/organizasyon/is-merkezleri`.
   *
   * A SEPARATE grant from ORG-01, and deliberately not a subview of it: the server
   * authorizes `/organization/work-centers` with `organization.work-centers.read` and
   * `/organization/locations` with `organization.locations.read`, so a user can hold
   * either without the other. Folding this screen behind the location list would hide
   * it from a work-centre reader who is entitled to it.
   *
   * The reverse case is carried in the ROW, not in this table: a work-centre reader
   * without location access still sees every row, and `canOpenLocationDetail` (#377)
   * says per row whether its location card would open. The screen never derives that.
   */
  workCentersList: 'organization.work-centers.list',
  /**
   * ORG-04 — one work centre's read-only card (Business #66 5702892195).
   * Route `/ayarlar/organizasyon/is-merkezleri/:workCenterId`.
   *
   * The same grant as ORG-03 and nothing more: the card is the list row's own record, read
   * by the same scope predicate. The location link inside it follows the record's own
   * `canOpenLocationDetail`, exactly as the row does; it is never derived here.
   */
  workCenterDetail: 'organization.work-centers.detail',
} as const

export type OrganizationCapabilityId =
  (typeof ORGANIZATION_CAPABILITY)[keyof typeof ORGANIZATION_CAPABILITY]

/** Route path per capability, as proposed to the shell owner. */
export const ORGANIZATION_ROUTE: Record<string, string> = {
  [ORGANIZATION_CAPABILITY.settingsList]: '/ayarlar/organizasyon',
  [ORGANIZATION_CAPABILITY.locationDetail]: '/ayarlar/organizasyon/lokasyon/:locationId',
  [ORGANIZATION_CAPABILITY.workCentersList]: '/ayarlar/organizasyon/is-merkezleri',
  [ORGANIZATION_CAPABILITY.workCenterDetail]: '/ayarlar/organizasyon/is-merkezleri/:workCenterId',
}

/**
 * Nav label. The two LISTS get one; the card does not — it is reached from a row.
 *
 * ORG-03 gets its own sidebar entry rather than a link inside ORG-01 because of the
 * permission split above: a user may hold `organization.work-centers.read` without
 * `organization.locations.read`, and for that user ORG-01 does not open at all. An entry
 * point that only exists inside a screen they cannot reach is no entry point (K5).
 */
export const ORGANIZATION_NAV_LABEL: Record<string, string> = {
  [ORGANIZATION_CAPABILITY.settingsList]: 'Organizasyon',
  [ORGANIZATION_CAPABILITY.locationDetail]: '',
  [ORGANIZATION_CAPABILITY.workCentersList]: 'İş Merkezleri',
  [ORGANIZATION_CAPABILITY.workCenterDetail]: '',
}

/** Concrete path to one location's card. */
export function locationDetailPath(locationId: string): string {
  return `/ayarlar/organizasyon/lokasyon/${encodeURIComponent(locationId)}`
}

/** Concrete path to one work centre's card (ORG-04). */
export function workCenterDetailPath(workCenterId: string): string {
  return `/ayarlar/organizasyon/is-merkezleri/${encodeURIComponent(workCenterId)}`
}

/** Existing unpaged work-center route; this query only narrows its authorized result in the UI. */
export function workCenterListPath(locationId?: string): string {
  const path = ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.workCentersList]
  return locationId === undefined ? path : `${path}?locationId=${encodeURIComponent(locationId)}`
}
