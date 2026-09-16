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
  ORGANIZATION_MOUNTED_NAV_LABEL,
  ORGANIZATION_MOUNTED_ROUTE,
  ORGANIZATION_NAV_LABEL,
  ORGANIZATION_NAV_LABEL_AWAITING_SHELL,
  ORGANIZATION_ROUTE,
  ORGANIZATION_ROUTE_AWAITING_SHELL,
  locationDetailPath,
  mergeMountedTables,
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

  it('has no route still waiting on the shell — ORG-03 is mirrored', () => {
    // This lane emptied the table in its OWN commit, after the shell owner's commit
    // declared the id (WORKCENTER_SHELL_MOUNT_HANDOFF.md §4(2)). The next route that
    // waits on the shell has to change this line, which is the point of it.
    expect(Object.keys(ORGANIZATION_ROUTE_AWAITING_SHELL)).toEqual([])
    expect(Object.keys(ORGANIZATION_NAV_LABEL_AWAITING_SHELL)).toEqual([])
    expect(ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.workCenterList]).toBe(
      '/ayarlar/organizasyon/is-merkezleri',
    )
  })

  it('resolves ORG-03 through the union in BOTH handshake states, today', () => {
    /**
     * The failure this pins is the one TypeScript cannot: `Record<string, string>` types
     * a MISSING key as `string`, so a helper reading the table the entry has just left
     * returns `undefined` and every link built from it lands on the not-found block —
     * with `tsc` still green.
     *
     * A check against today's tables cannot see it: while the entry still waits in the
     * awaiting table, reading that table and reading the union give the same value
     * (measured by the #268 delta closure — the regressed helper left every test green).
     * So both states are BUILT here, through the same `mergeMountedTables` the app uses,
     * and handed to the same `workCenterListPath`. The value is a SENTINEL, never the real
     * path, so a helper that ignores the table it is given and reads module state instead
     * cannot pass by coincidence.
     */
    const id = ORGANIZATION_CAPABILITY.workCenterList
    const sentinel = '/sentinel/is-merkezleri'
    const beforeMove = mergeMountedTables({}, { [id]: sentinel })
    const afterMove = mergeMountedTables({ [id]: sentinel }, {})

    expect(workCenterListPath(beforeMove)).toBe(sentinel)
    expect(workCenterListPath(afterMove)).toBe(sentinel)
  })

  it('builds the tables the app reads with that same helper', () => {
    // Ties the two cases above to production: the constants ARE the helper's output over
    // today's tables, and the parameterless call reads exactly that union.
    expect(ORGANIZATION_MOUNTED_ROUTE).toEqual(
      mergeMountedTables(ORGANIZATION_ROUTE, ORGANIZATION_ROUTE_AWAITING_SHELL),
    )
    expect(ORGANIZATION_MOUNTED_NAV_LABEL).toEqual(
      mergeMountedTables(ORGANIZATION_NAV_LABEL, ORGANIZATION_NAV_LABEL_AWAITING_SHELL),
    )
    expect(workCenterListPath()).toBe(
      ORGANIZATION_MOUNTED_ROUTE[ORGANIZATION_CAPABILITY.workCenterList],
    )
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
    // Asserted through the union, so the LANE's own follow-up commit (which empties the
    // awaiting tables) does not have to edit this line as well. The route-table
    // expectation above is the one that deliberately forces that commit to happen.
    expect(ORGANIZATION_MOUNTED_NAV_LABEL[ORGANIZATION_CAPABILITY.workCenterList]).toBe(
      'İş Merkezleri',
    )
  })

  it('publishes one union per decision, which is what the shell compares against', () => {
    // The shell's drift test reads these unions rather than the transitional tables, so
    // its commit stays inside `src/shell/**` and this lane merges its tables later.
    expect([...Object.keys(ORGANIZATION_MOUNTED_NAV_LABEL)].sort()).toEqual(
      [...Object.keys(ORGANIZATION_MOUNTED_ROUTE)].sort(),
    )
    expect(ORGANIZATION_MOUNTED_NAV_LABEL[ORGANIZATION_CAPABILITY.workCenterList]).toBe(
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
