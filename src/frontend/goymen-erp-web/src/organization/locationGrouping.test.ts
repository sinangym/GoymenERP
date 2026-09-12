import { describe, expect, it } from 'vitest'
import { groupLocations } from './locationGrouping'
import { UNKNOWN_SITE_HEADING } from './organizationText'
import type { LocationSummary, SiteSummary } from './contract'

const SITE_OSM = '11111111-1111-1111-1111-111111111111'
const SITE_GAZ = '22222222-2222-2222-2222-222222222222'
const SITE_UNSEEN = '77777777-7777-7777-7777-777777777777'

function site(id: string, code: string, name: string, type = 'Factory'): SiteSummary {
  return { id, code, name, type, isActive: true }
}

function loc(id: string, siteId: string, code: string): LocationSummary {
  return { id, siteId, code, name: `${code} adı`, isExternal: false, isActive: true, version: 1 }
}

const SITES = [site(SITE_OSM, 'SITE-OSM', 'Osmaniye'), site(SITE_GAZ, 'SITE-GAZ', 'Gaziantep', 'Warehouse')]

describe('groupLocations', () => {
  it('groups rows under their facility with a code — name heading', () => {
    const groups = groupLocations([loc('a', SITE_OSM, 'OSM-H0'), loc('b', SITE_OSM, 'OSM-H1')], SITES)
    expect(groups).toHaveLength(1)
    expect(groups[0].heading).toBe('SITE-OSM — Osmaniye')
    expect(groups[0].rows.map((r) => r.code)).toEqual(['OSM-H0', 'OSM-H1'])
  })

  it('labels the facility type in Turkish', () => {
    const groups = groupLocations([loc('c', SITE_GAZ, 'GAZ-DEP')], SITES)
    expect(groups[0].subheading).toBe('Depo')
  })

  it('keeps the server’s row order within and across groups', () => {
    // The server orders by Code; we must not re-sort and invent a different order.
    const groups = groupLocations(
      [loc('a', SITE_OSM, 'OSM-H0'), loc('c', SITE_GAZ, 'GAZ-DEP'), loc('b', SITE_OSM, 'OSM-H1')],
      SITES,
    )
    expect(groups.map((g) => g.siteId)).toEqual([SITE_OSM, SITE_GAZ])
    expect(groups[0].rows.map((r) => r.code)).toEqual(['OSM-H0', 'OSM-H1'])
  })

  it('keeps a location whose site is NOT readable under its own heading', () => {
    /**
     * /sites and /locations have DIFFERENT permission scopes, so holding a location
     * whose facility you may not read is legitimate. Dropping the row would hide a
     * location the user IS entitled to see; filing it under a guessed facility would
     * be inventing a fact.
     */
    const groups = groupLocations([loc('x', SITE_UNSEEN, 'EXT-ALYA')], SITES)
    expect(groups).toHaveLength(1)
    expect(groups[0].heading).toBe(UNKNOWN_SITE_HEADING)
    expect(groups[0].subheading).toBeNull()
    expect(groups[0].rows.map((r) => r.code)).toEqual(['EXT-ALYA'])
  })

  it('survives an empty sites response by grouping everything as unreadable-site', () => {
    // A user with locations.read but not sites.read still sees their locations.
    const groups = groupLocations([loc('a', SITE_OSM, 'OSM-H0')], [])
    expect(groups[0].heading).toBe(UNKNOWN_SITE_HEADING)
    expect(groups[0].rows).toHaveLength(1)
  })

  it('returns nothing for an empty page', () => {
    expect(groupLocations([], SITES)).toEqual([])
  })

  it('never invents a group for a facility with no rows on this page', () => {
    // A group is rows-in-hand, not "this site's locations" — an empty group would
    // read as "this facility has none", which the page boundary does not license.
    const groups = groupLocations([loc('a', SITE_OSM, 'OSM-H0')], SITES)
    expect(groups.map((g) => g.siteId)).toEqual([SITE_OSM])
  })
})
