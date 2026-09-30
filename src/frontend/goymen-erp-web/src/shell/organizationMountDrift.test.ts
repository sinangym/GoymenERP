/**
 * The shell mounts exactly the Organization capability ids that lane declares as ROUTES.
 *
 * The two lists exist in two files because the lanes have a single-writer boundary:
 * `src/organization/capabilities.ts` belongs to `CLAUDE-FRONTEND-ORGANIZASYON`, and
 * `src/shell/capabilities.ts` belongs to the shared-integration owner. If one side is
 * renamed without the other, the shell quietly stops mounting a feature the server has
 * granted — a silent loss, and a hard one to diagnose from the symptom.
 *
 * WHAT THIS IS NOT: backend parity. There is no `organization.*` constant in any
 * published shell-capability provider yet — Backend #246 is completing the normal
 * provider on its own branch, and until it merges `hasCapability` answers false for
 * these and `CapabilityRoute` closes both routes onto the neutral not-found block. That
 * is the intended state, and this file must not be counted as evidence of the server
 * side. The real parity test belongs to Backend.
 */

import { describe, expect, it } from 'vitest'
import { matchPath } from 'react-router'
import { CAPABILITY, CAPABILITY_ROUTE, CAPABILITY_NAV_LABEL } from './capabilities'
import {
  ORGANIZATION_CAPABILITY,
  ORGANIZATION_ROUTE,
  ORGANIZATION_NAV_LABEL,
  locationDetailPath,
  workCenterDetailPath,
} from '../organization/capabilities'

describe('shell ↔ Organization lane capability ids', () => {
  it('declares the same four route ids the lane does', () => {
    expect(CAPABILITY.organizationSitesList).toBe(ORGANIZATION_CAPABILITY.settingsList)
    expect(CAPABILITY.organizationLocationsDetail).toBe(ORGANIZATION_CAPABILITY.locationDetail)
    expect(CAPABILITY.organizationWorkCentersList).toBe(ORGANIZATION_CAPABILITY.workCentersList)
    expect(CAPABILITY.organizationWorkCentersDetail).toBe(ORGANIZATION_CAPABILITY.workCenterDetail)
  })

  it('mounts every ROUTE the lane declares, and nothing that is only a control', () => {
    /**
     * Set equality against the lane's ROUTE table — deliberately NOT against all of
     * `ORGANIZATION_CAPABILITY`, which is the difference from the Ürün drift test.
     *
     * That lane declares four ids and Backend #246 publishes four, but only two are
     * routes. `organization.locations.status` (the reasoned activate/deactivate) and
     * `organization.sites.read` (the optional facility label) are CONTROLS inside a
     * screen, gated with `hasCapability`, not mounted. A control id in a route registry
     * would be a route pointing at nothing, so their absence here is the assertion, not
     * an omission — and it is asserted by SYMBOL, so a rename on their side that turned
     * a control into a route would surface here rather than pass unnoticed.
     */
    const laneRouteIds = new Set(Object.keys(ORGANIZATION_ROUTE))
    const shellOrganizationIds = new Set(
      (Object.values(CAPABILITY) as string[]).filter((id) => id.startsWith('organization.')),
    )
    expect([...shellOrganizationIds].sort()).toEqual([...laneRouteIds].sort())
    expect(shellOrganizationIds.has(ORGANIZATION_CAPABILITY.locationStatus)).toBe(false)
    expect(shellOrganizationIds.has(ORGANIZATION_CAPABILITY.sitesRead)).toBe(false)
    // ...and the lane keeps them out of its own route and nav tables too; if one ever
    // moved in, the set equality above would fail for the right reason.
    expect(ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.locationStatus]).toBeUndefined()
    expect(ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.sitesRead]).toBeUndefined()
  })

  it('agrees with the lane on both routes and nav labels, value by value', () => {
    // `Record<CapabilityId, string>` already makes a MISSING entry a type error; this
    // pins the VALUES, which the type cannot, and reads them from the lane rather than
    // restating them here — a literal copied into both files drifts silently.
    for (const [shellId, laneId] of [
      [CAPABILITY.organizationSitesList, ORGANIZATION_CAPABILITY.settingsList],
      [CAPABILITY.organizationLocationsDetail, ORGANIZATION_CAPABILITY.locationDetail],
      [CAPABILITY.organizationWorkCentersList, ORGANIZATION_CAPABILITY.workCentersList],
      [CAPABILITY.organizationWorkCentersDetail, ORGANIZATION_CAPABILITY.workCenterDetail],
    ] as const) {
      expect(CAPABILITY_ROUTE[shellId]).toBe(ORGANIZATION_ROUTE[laneId])
      expect(CAPABILITY_NAV_LABEL[shellId]).toBe(ORGANIZATION_NAV_LABEL[laneId])
    }
  })

  it('matches the lane’s own link helper against the pattern the shell mounts', () => {
    /**
     * The strongest case here, because it is the one that fails silently in the running
     * app: the lane builds a card link with `locationDetailPath`, and the shell mounts a
     * pattern. If either side moves a segment, every row link lands on the not-found
     * block while both files still look internally consistent. So this matches a REAL
     * url against the REAL pattern instead of comparing two strings.
     */
    const pattern = CAPABILITY_ROUTE[CAPABILITY.organizationLocationsDetail]
    const url = locationDetailPath('7b1c0f2e-0000-4a00-8000-000000000001')
    const match = matchPath(pattern, url)
    expect(match).not.toBeNull()
    expect(match?.params.locationId).toBe('7b1c0f2e-0000-4a00-8000-000000000001')

    // ...and the list must not swallow the card. The card carries a further STATIC
    // segment, so no url can match both — the #172 P1-2 rule, checked rather than
    // asserted by ordering.
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.organizationSitesList], url)).toBeNull()

    // ORG-04: the work-centre card's link helper against its mounted pattern, and the static list never
    // swallowing a card url (nor the card the list).
    const cardPattern = CAPABILITY_ROUTE[CAPABILITY.organizationWorkCentersDetail]
    const cardUrl = workCenterDetailPath('7b1c0f2e-0000-4a00-8000-000000000004')
    expect(matchPath(cardPattern, cardUrl)?.params.workCenterId).toBe('7b1c0f2e-0000-4a00-8000-000000000004')
    expect(matchPath(CAPABILITY_ROUTE[CAPABILITY.organizationWorkCentersList], cardUrl)).toBeNull()
    expect(matchPath(cardPattern, CAPABILITY_ROUTE[CAPABILITY.organizationWorkCentersList])).toBeNull()
  })

  it('gives the card no nav entry, so the sidebar gains exactly two entries', () => {
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.organizationSitesList]).toBe('Organizasyon')
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.organizationLocationsDetail]).toBe('')
    // ORG-03 is the second, and it is a sidebar entry for a reason rather than by symmetry: its
    // route stands on `organization.work-centers.read` alone, so a holder of that and not
    // `organization.locations.read` never opens ORG-01 and could not be linked from it. The card
    // stays empty — it is still reached from a location row.
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.organizationWorkCentersList]).toBe('İş Merkezleri')
    // ORG-04 is a card, reached from a work-centre row: no sidebar entry.
    expect(CAPABILITY_NAV_LABEL[CAPABILITY.organizationWorkCentersDetail]).toBe('')
  })
})
