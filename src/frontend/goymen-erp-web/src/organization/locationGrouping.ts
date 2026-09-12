/**
 * Groups a page of locations under their facility, for ORG-01.
 *
 * This is PRESENTATION of rows already in hand, not a filter. The endpoint has no
 * `siteId` parameter, so a group here means "the rows on THIS page that belong to
 * this site" and never "this site's locations" — the screen states the page boundary
 * separately so the two cannot be confused.
 *
 * Lives in its own module rather than beside the component so it can be tested
 * directly (and so the component file only exports components).
 */

import type { LocationSummary, SiteSummary } from './contract'
import { siteTypeLabel, UNKNOWN_SITE_HEADING } from './organizationText'

export interface LocationGroup {
  readonly siteId: string
  readonly heading: string
  /** Null for the unknown-site group, which has no type to show. */
  readonly subheading: string | null
  readonly rows: readonly LocationSummary[]
}

export function groupLocations(
  locations: readonly LocationSummary[],
  sites: readonly SiteSummary[],
): readonly LocationGroup[] {
  const byId = new Map(sites.map((site) => [site.id, site]))
  const groups = new Map<string, LocationSummary[]>()
  for (const location of locations) {
    const bucket = groups.get(location.siteId)
    if (bucket === undefined) groups.set(location.siteId, [location])
    else bucket.push(location)
  }
  /**
   * Insertion order is preserved by Map, so the groups follow the server's `Code`
   * ordering of the rows rather than an ordering we invent. Within a group the rows
   * keep that order too.
   *
   * Rows whose site is absent from `/sites` get their OWN group rather than being
   * dropped or filed under a guessed facility. That combination is legitimate, not
   * corruption: `/sites` is scoped by `organization.sites.read` and `/locations` by
   * `organization.locations.read`, so a user can hold location access to a facility
   * they may not read. Dropping the row would hide a location the user IS entitled
   * to see.
   */
  return [...groups.entries()].map(([siteId, rows]) => {
    const site = byId.get(siteId)
    return {
      siteId,
      heading: site === undefined ? UNKNOWN_SITE_HEADING : `${site.code} — ${site.name}`,
      subheading: site === undefined ? null : siteTypeLabel(site.type),
      rows,
    }
  })
}
