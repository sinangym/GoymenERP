/**
 * Capability ids the Ayarlar/Organizasyon screens need — the SERVER's, mirrored here.
 *
 * READ THIS BEFORE USING ANY VALUE HERE.
 *
 * This header used to say the ids were a PROPOSAL and that the server published no
 * `organization.*` capability. Both were true at `main@3e7dd98` and BOTH ARE NOW STALE:
 * `OrganizationShellCapabilityProvider` is merged (#246) and publishes the first four,
 * and `organization.work-centers.list` is declared alongside them by this same slice.
 *
 * What has NOT changed is what listing an id here means: nothing. The server decides
 * availability and re-authorizes every request regardless (ERP-000K §10.2), so until a
 * grant is in the snapshot `hasCapability` answers false, `CapabilityRoute` closes the
 * route onto the same neutral not-found block any unknown path gets, and the nav shows
 * nothing. The frontend must never mint a capability to open its own screen.
 *
 * WHY THESE FIVE, AND WHY NOT FEWER. Each maps onto the permission the backend registry
 * actually publishes it on, so the split mirrors a real authority boundary rather than a
 * UI convenience:
 *
 *   organization.sites.list        <- organization.locations.read          (route, ORG-01)
 *   organization.locations.detail  <- organization.locations.read          (route, ORG-02)
 *   organization.work-centers.list <- organization.work-centers.read       (route, ORG-03)
 *   organization.sites.read        <- locations.read + sites.read          (control)
 *   organization.locations.status  <- locations.read + status.update       (control)
 *
 * Reading a location and being allowed to deactivate it are DIFFERENT authorities on
 * the server — the endpoints carry different `RequireAuthorization` attributes — so
 * fusing those into one id would hand out the write wherever the read is granted. The
 * same reasoning made the Cari detail a separate grant from the list (#182/#172), and
 * the same reasoning keeps ORG-03 off the locations permission: three reads, three
 * permissions, and no one of them is a back door into another.
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
   * ORG-03 — the authorized work-center list. Route `/ayarlar/organizasyon/is-merkezleri`.
   *
   * A SEPARATE ROUTE, not a section of ORG-01, because it stands on a DIFFERENT
   * authority: the endpoint behind it authorizes `organization.work-centers.read`,
   * while the ORG-01 route stands on `organization.locations.read` alone. Folding the
   * work centers into ORG-01 would close a list this user is entitled to whenever the
   * locations read is missing — the exact failure the sites mapping removed.
   *
   * Backend publishes it on `organization.work-centers.read` AND NOTHING ELSE, so the
   * two directions both hold: locations without work centers does not open it, and work
   * centers without locations does not close it.
   */
  workCenterList: 'organization.work-centers.list',
} as const

export type OrganizationCapabilityId =
  (typeof ORGANIZATION_CAPABILITY)[keyof typeof ORGANIZATION_CAPABILITY]

/** Route path per capability, as MIRRORED TODAY in `shell/capabilities.ts`. */
export const ORGANIZATION_ROUTE: Record<string, string> = {
  [ORGANIZATION_CAPABILITY.settingsList]: '/ayarlar/organizasyon',
  [ORGANIZATION_CAPABILITY.locationDetail]: '/ayarlar/organizasyon/lokasyon/:locationId',
}

/**
 * Routes this lane mounts that the SHELL REGISTRY has not mirrored yet.
 *
 * Why a second table instead of a third entry above: `shell/organizationMountDrift.test.ts`
 * — owned by the shared-integration owner, not by this lane — asserts SET EQUALITY
 * between `Object.keys(ORGANIZATION_ROUTE)` and the `organization.*` ids in
 * `shell/capabilities.ts`. That test is right, and it is the mechanism that keeps the
 * two files from drifting; it also means a lane-only addition to the table above would
 * turn a shared test red for a reason the shell owner has not had a chance to act on.
 *
 * So the route is declared here, mounted by `organizationRoutes.tsx` (which the shell
 * already spreads), and gated by `CapabilityRoute` exactly like the other two — the
 * screen is reachable by URL the moment Backend publishes the id. What is still missing
 * without the shell delta is only the SIDEBAR ENTRY, which is the shell owner's to add.
 *
 * `WORKCENTER_SHELL_MOUNT_HANDOFF.md` carries that delta. Applying it means adding the
 * three shell lines AND moving this entry into `ORGANIZATION_ROUTE` above, in the SAME
 * commit — the drift test is what forces the two halves to land together, which is the
 * property worth keeping rather than working around.
 */
export const ORGANIZATION_ROUTE_AWAITING_SHELL: Record<string, string> = {
  [ORGANIZATION_CAPABILITY.workCenterList]: '/ayarlar/organizasyon/is-merkezleri',
}

/** Every route this lane mounts, whichever table currently holds it. */
export const ORGANIZATION_MOUNTED_ROUTE: Record<string, string> = {
  ...ORGANIZATION_ROUTE,
  ...ORGANIZATION_ROUTE_AWAITING_SHELL,
}

/** Nav label. Only the list gets one — the card is reached from a row, never the sidebar. */
export const ORGANIZATION_NAV_LABEL: Record<string, string> = {
  [ORGANIZATION_CAPABILITY.settingsList]: 'Organizasyon',
  [ORGANIZATION_CAPABILITY.locationDetail]: '',
}

/**
 * ORG-03 DOES want a sidebar entry, and that is not a style choice.
 *
 * ORG-01 cannot host the link: its route stands on `organization.locations.read`, and
 * the whole point of this slice is the user who holds `work-centers.read` WITHOUT it.
 * For that user a link that only exists inside ORG-01 is no link at all, so the work
 * centers are reached from the sidebar and the location card is reached from a row.
 */
export const ORGANIZATION_NAV_LABEL_AWAITING_SHELL: Record<string, string> = {
  [ORGANIZATION_CAPABILITY.workCenterList]: 'İş Merkezleri',
}

/** Concrete path to one location's card. */
export function locationDetailPath(locationId: string): string {
  return `/ayarlar/organizasyon/lokasyon/${encodeURIComponent(locationId)}`
}

/** Concrete path to the work-center list. Read from the table, never retyped. */
export function workCenterListPath(): string {
  return ORGANIZATION_ROUTE_AWAITING_SHELL[ORGANIZATION_CAPABILITY.workCenterList]
}
