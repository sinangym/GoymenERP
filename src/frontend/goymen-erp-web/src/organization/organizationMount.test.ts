/**
 * What this lane MOUNTS, and which half of the shell handshake each route is in.
 *
 * `shell/organizationMountDrift.test.ts` (the shared-integration owner's file) pins
 * `ORGANIZATION_ROUTE` against `shell/capabilities.ts`. This is the other side of the
 * same seam: it pins the lane's own tables against the routes actually mounted, so a
 * route can never be added to `organizationRoutes.tsx` without appearing in exactly one
 * of the two tables — and so the pending entry cannot be quietly forgotten.
 */

import { describe, expect, it } from 'vitest'
import { matchPath } from 'react-router'
import {
  ORGANIZATION_CAPABILITY,
  ORGANIZATION_MOUNTED_ROUTE,
  ORGANIZATION_NAV_LABEL,
  ORGANIZATION_NAV_LABEL_AWAITING_SHELL,
  ORGANIZATION_ROUTE,
  ORGANIZATION_ROUTE_AWAITING_SHELL,
  locationDetailPath,
  workCenterListPath,
} from './capabilities'
import { organizationRoutes } from './organizationRoutes'

const MOUNTED = organizationRoutes.map((route) => `/${route.path}`)

describe('the lane’s route tables', () => {
  it('mounts exactly the routes the two tables declare, and nothing else', () => {
    expect([...MOUNTED].sort()).toEqual([...Object.values(ORGANIZATION_MOUNTED_ROUTE)].sort())
  })

  it('keeps the shell-mirrored and awaiting-shell tables disjoint', () => {
    /**
     * They must not overlap in either direction: an id in both would make the shared
     * drift test pass while the handoff still looked outstanding, and an id in neither
     * would be a route with no owner on the shell side at all.
     */
    const mirrored = Object.keys(ORGANIZATION_ROUTE)
    const pending = Object.keys(ORGANIZATION_ROUTE_AWAITING_SHELL)
    expect(mirrored.filter((id) => pending.includes(id))).toEqual([])
    expect([...mirrored, ...pending].sort()).toEqual(
      [...Object.keys(ORGANIZATION_MOUNTED_ROUTE)].sort(),
    )
  })

  it('has exactly one route still waiting on the shell — ORG-03', () => {
    // When the shell owner applies WORKCENTER_SHELL_MOUNT_HANDOFF.md this becomes an
    // empty table and this expectation is the line they update, deliberately.
    expect(Object.keys(ORGANIZATION_ROUTE_AWAITING_SHELL)).toEqual([
      ORGANIZATION_CAPABILITY.workCenterList,
    ])
  })

  it('gives every mounted route a nav label decision, in one table or the other', () => {
    const labelled = [
      ...Object.keys(ORGANIZATION_NAV_LABEL),
      ...Object.keys(ORGANIZATION_NAV_LABEL_AWAITING_SHELL),
    ]
    expect([...labelled].sort()).toEqual([...Object.keys(ORGANIZATION_MOUNTED_ROUTE)].sort())
    // ORG-03 is the second and last sidebar entry this lane asks for; the card is
    // reached from a row, never from the sidebar.
    expect(ORGANIZATION_NAV_LABEL[ORGANIZATION_CAPABILITY.settingsList]).toBe('Organizasyon')
    expect(ORGANIZATION_NAV_LABEL[ORGANIZATION_CAPABILITY.locationDetail]).toBe('')
    expect(ORGANIZATION_NAV_LABEL_AWAITING_SHELL[ORGANIZATION_CAPABILITY.workCenterList]).toBe(
      'İş Merkezleri',
    )
  })
})

describe('no path can be read as another’s record id', () => {
  it('never lets the work-center path match the location card pattern', () => {
    /**
     * The #172 P1-2 rule, checked rather than asserted by ordering: a static segment
     * must never be readable as a record id. `is-merkezleri` sits one level under
     * `/ayarlar/organizasyon`, the card sits two, so neither can swallow the other.
     */
    const card = ORGANIZATION_MOUNTED_ROUTE[ORGANIZATION_CAPABILITY.locationDetail]
    expect(matchPath(card, workCenterListPath())).toBeNull()
    const list = ORGANIZATION_MOUNTED_ROUTE[ORGANIZATION_CAPABILITY.settingsList]
    expect(matchPath(list, workCenterListPath())).toBeNull()
  })

  it('never lets a location card url match the work-center route', () => {
    const url = locationDetailPath('7b1c0f2e-0000-4a00-8000-000000000001')
    expect(matchPath(workCenterListPath(), url)).toBeNull()
  })

  it('matches the lane’s own link helper against the pattern it mounts', () => {
    // The failure that is silent in the running app: a moved segment sends every link
    // to the not-found block while both files still read as internally consistent.
    expect(matchPath(workCenterListPath(), workCenterListPath())).not.toBeNull()
  })

  it('declares the path the backend registry declares, character for character', () => {
    /**
     * The value is pinned here because a mismatch with
     * `OrganizationShellCapabilityProvider.WorkCenterListRoutePath` would publish a
     * capability for a route nothing mounts. The real cross-language parity test is
     * Backend's; this is the frontend half stated explicitly rather than implied.
     */
    expect(workCenterListPath()).toBe('/ayarlar/organizasyon/is-merkezleri')
    expect(ORGANIZATION_CAPABILITY.workCenterList).toBe('organization.work-centers.list')
  })
})
