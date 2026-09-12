/**
 * ORG-01 component/DOM tests.
 *
 * These run against FIXTURES through the real shell, gate and fence. They are not
 * real-API, real-SQL or browser acceptance and are never reported as such.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  COMPANY_A,
  COMPANY_B,
  LOC_GAZ,
  LOC_H0,
  LOC_H1,
  LOC_ORPHAN,
  LOCATIONS_ONLY_CAPABILITIES,
  READ_CAPABILITIES,
  SITE_GAZ,
  SITE_OSM,
  SITE_UNSEEN,
  installFetch,
  json,
  location,
  page,
  problem,
  renderAt,
  requestsFor,
  site,
} from './testHarness'
import { UNKNOWN_SITE_HEADING } from './organizationText'

const PATH = '/ayarlar/organizasyon'

const SITES = [
  site(SITE_OSM, 'SITE-OSM', 'Göymen Osmaniye Yerleşkesi'),
  site(SITE_GAZ, 'SITE-GAZ', 'Göymen Gaziantep Depo', 'Warehouse'),
]

const ROWS = [
  location(LOC_H0, SITE_OSM, 'OSM-H0', 'Göymen Osmaniye Depo Hol-0'),
  location(LOC_H1, SITE_OSM, 'OSM-H1', 'Göymen Osmaniye Depo Hol-1', { isActive: false }),
  location(LOC_GAZ, SITE_GAZ, 'GAZ-DEP', 'Göymen Gaziantep Depo'),
]

function stub(overrides: {
  sites?: () => Response
  locations?: (url: string) => Response
  capabilities?: readonly string[]
} = {}) {
  return installFetch({
    capabilities: overrides.capabilities,
    respond: (url) => {
      if (url.includes('/organization/sites')) return (overrides.sites ?? (() => json(SITES)))()
      if (url.includes('/organization/locations')) {
        return (overrides.locations ?? (() => json(page(ROWS))))(url)
      }
      return undefined
    },
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('reading the organization', () => {
  it('renders facilities and this page’s locations grouped under them', async () => {
    stub()
    renderAt(PATH)

    expect(await screen.findByRole('heading', { name: 'Organizasyon' })).toBeTruthy()
    expect(await screen.findByText('Göymen Osmaniye Yerleşkesi')).toBeTruthy()

    const osm = await screen.findByRole('table', { name: /SITE-OSM/ })
    expect(within(osm).getByRole('link', { name: 'OSM-H0' })).toBeTruthy()
    expect(within(osm).getByRole('link', { name: 'OSM-H1' })).toBeTruthy()

    const gaz = screen.getByRole('table', { name: /SITE-GAZ/ })
    expect(within(gaz).getByRole('link', { name: 'GAZ-DEP' })).toBeTruthy()
  })

  it('keeps passive locations visible — deactivation is a state, not a delete', async () => {
    stub()
    renderAt(PATH)

    const osm = await screen.findByRole('table', { name: /SITE-OSM/ })
    const row = within(osm).getByRole('link', { name: 'OSM-H1' }).closest('tr')
    expect(row).toBeTruthy()
    expect(within(row as HTMLElement).getByText('Pasif')).toBeTruthy()
  })

  it('states the page boundary instead of implying the count is everything', async () => {
    stub({ locations: () => json(page(ROWS, { totalCount: 15 })) })
    renderAt(PATH)
    expect(await screen.findByText(/Yetkiniz dâhilindeki 15 lokasyondan 1–3 arası/)).toBeTruthy()
  })

  it('scopes both reads to the committed company', async () => {
    const recorded = stub()
    renderAt(PATH)
    await screen.findByRole('table', { name: /SITE-OSM/ })

    for (const fragment of ['/organization/sites', '/organization/locations']) {
      const requests = requestsFor(recorded, fragment)
      expect(requests.length).toBeGreaterThan(0)
      expect(requests[0].headers['X-Company-Id']).toBe(COMPANY_A)
    }
  })

  it('asks for page 1 with the default size and no invented filter parameter', async () => {
    const recorded = stub()
    renderAt(PATH)
    await screen.findByRole('table', { name: /SITE-OSM/ })

    const url = requestsFor(recorded, '/organization/locations')[0].url
    expect(url).toContain('page=1')
    expect(url).toContain('pageSize=25')
    // The endpoint has no q/siteId. Sending one would invent a server capability.
    expect(url).not.toContain('q=')
    expect(url).not.toContain('siteId=')
    expect(url).not.toContain('cursor=')
  })

  it('offers no search box, because the endpoint cannot search', async () => {
    stub()
    renderAt(PATH)
    await screen.findByRole('table', { name: /SITE-OSM/ })
    expect(screen.queryByRole('searchbox')).toBeNull()
    expect(screen.queryByRole('textbox')).toBeNull()
  })
})

describe('scope differences between the two reads', () => {
  it('shows a location whose facility is not readable, under its own heading', async () => {
    // locations.read and sites.read are separately scoped, so this is legitimate.
    stub({
      locations: () =>
        json(page([location(LOC_ORPHAN, SITE_UNSEEN, 'EXT-ALYA', 'Alya Depo', { isExternal: true })])),
    })
    renderAt(PATH)

    const table = await screen.findByRole('table', { name: new RegExp(UNKNOWN_SITE_HEADING) })
    expect(within(table).getByRole('link', { name: 'EXT-ALYA' })).toBeTruthy()
  })

  it('groups under the unknown heading when /sites returns a SUBSET, not an error', async () => {
    /**
     * The common real case, and the one this screen must survive: `/sites` is scoped
     * by `organization.sites.read` and answers 200 with fewer facilities than the
     * locations reference. No error is involved, so nothing closes — the rows stay
     * visible under their own heading rather than being dropped or mis-filed.
     */
    stub({
      sites: () => json([site(SITE_GAZ, 'SITE-GAZ', 'Göymen Gaziantep Depo', 'Warehouse')]),
    })
    renderAt(PATH)

    const unknown = await screen.findByRole('table', { name: new RegExp(UNKNOWN_SITE_HEADING) })
    expect(within(unknown).getByRole('link', { name: 'OSM-H0' })).toBeTruthy()
    expect(screen.getByRole('table', { name: /SITE-GAZ/ })).toBeTruthy()
  })

  it('never requests /sites without the optional capability, and still lists locations', async () => {
    /**
     * The final mapping: the ORG-01 route stands on `organization.locations.read`
     * alone; facility labels sit behind their own `organization.sites.read` control.
     * Without it the request is NOT ISSUED — the capability is the server's own
     * statement that this read is unavailable here, so there is nothing to ask, and
     * asking-then-discarding-the-403 would make the client the judge of which refusals
     * count.
     */
    const recorded = stub({ capabilities: LOCATIONS_ONLY_CAPABILITIES })
    renderAt(PATH)

    const unknown = await screen.findByRole('table', { name: new RegExp(UNKNOWN_SITE_HEADING) })
    expect(within(unknown).getByRole('link', { name: 'OSM-H0' })).toBeTruthy()
    expect(screen.getByText('Yetkiniz yok — tesis listesi')).toBeTruthy()
    // The entitled half of the screen is intact...
    expect(screen.queryByText('Bulunamadı — sayfa')).toBeNull()
    // ...and no facilities request was ever made.
    expect(requestsFor(recorded, '/organization/sites')).toHaveLength(0)
  })

  it('still fails closed when a GRANTED /sites read is refused 403', async () => {
    /**
     * Nothing is special-cased once the capability is held: a 403 on a read we were
     * told we could make is a genuine refusal and goes through the ordinary
     * capture/report path into the shared guard, exactly like the locations read.
     */
    stub({ sites: () => problem(403) })
    renderAt(PATH)

    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    expect(screen.queryByRole('table', { name: /SITE-OSM/ })).toBeNull()
  })
})

describe('states', () => {
  it('distinguishes “no records” from a forbidden read', async () => {
    stub({ locations: () => json(page([], { totalCount: 0 })) })
    renderAt(PATH)
    expect(await screen.findByText('Kayıt yok — lokasyonlar')).toBeTruthy()
  })

  it('fails closed on a 403 from the locations read, without painting rows', async () => {
    // Same shared-boundary path as above: the capability is dropped and the gate
    // closes. What matters for this screen is that no data is shown either way.
    stub({ locations: () => problem(403) })
    renderAt(PATH)
    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    expect(screen.queryByRole('link', { name: 'OSM-H0' })).toBeNull()
  })

  it('fails closed on a version it cannot vouch for', async () => {
    // An int64 past 2^53 has already lost precision in JSON.parse. Rendering the
    // row would mean offering a write with a version we cannot stand behind.
    stub({
      locations: () =>
        json(page([{ ...location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0'), version: 9007199254740994 }])),
    })
    renderAt(PATH)
    expect(await screen.findByText('Beklenmeyen yanıt — lokasyonlar')).toBeTruthy()
  })

  it('reports a 5xx as a service problem, not as empty', async () => {
    stub({ locations: () => problem(503) })
    renderAt(PATH)
    expect(await screen.findByText(/Servis geçici olarak kullanılamıyor/)).toBeTruthy()
  })
})

describe('paging', () => {
  it('requests the next page from the SERVER’s echoed values', async () => {
    const recorded = stub({
      locations: (url) =>
        url.includes('page=2')
          ? json(page([location(LOC_GAZ, SITE_GAZ, 'GAZ-DEP', 'Gaziantep')], { page: 2, pageSize: 3, totalCount: 8 }))
          : json(page(ROWS, { page: 1, pageSize: 3, totalCount: 8 })),
    })
    renderAt(PATH)

    const next = await screen.findByRole('button', { name: 'Sonraki' })
    await userEvent.click(next)

    await waitFor(() =>
      expect(requestsFor(recorded, '/organization/locations').some((r) => r.url.includes('page=2'))).toBe(
        true,
      ),
    )
    expect(await screen.findByText('Sayfa 2 / 3')).toBeTruthy()
  })

  it('hides the pager when everything fits on one page', async () => {
    stub({ locations: () => json(page(ROWS, { totalCount: 3 })) })
    renderAt(PATH)
    await screen.findByRole('table', { name: /SITE-OSM/ })
    expect(screen.queryByRole('button', { name: 'Sonraki' })).toBeNull()
  })

  it('does not call a page past the end “no records”', async () => {
    // The records exist; this page just has none. "Kayıt yok" would be false.
    const recorded = stub({
      locations: (url) =>
        url.includes('page=2')
          ? json(page([], { page: 2, pageSize: 3, totalCount: 8 }))
          : json(page(ROWS, { page: 1, pageSize: 3, totalCount: 8 })),
    })
    renderAt(PATH)
    await userEvent.click(await screen.findByRole('button', { name: 'Sonraki' }))

    expect(await screen.findByText('Bu sayfada kayıt yok.')).toBeTruthy()
    expect(screen.queryByText('Kayıt yok — lokasyonlar')).toBeNull()
    await waitFor(() =>
      expect(requestsFor(recorded, '/organization/locations').some((r) => r.url.includes('page=2'))).toBe(
        true,
      ),
    )
  })
})

describe('company switch', () => {
  it('re-reads under the new company and shows none of the old company’s rows', async () => {
    /**
     * The fence. A switch advances the shell's request generation and this screen is
     * keyed on the context namespace, so it remounts: the previous company's rows
     * cannot survive under the new company's label, and the page number resets with
     * them (page 3 of company A means nothing in company B).
     */
    const recorded = stub({
      locations: (url) =>
        url.includes('pageSize')
          ? json(page(ROWS))
          : json(page(ROWS)),
    })
    const { router } = renderAt(PATH)
    await screen.findByRole('table', { name: /SITE-OSM/ })
    const before = requestsFor(recorded, '/organization/locations').length
    // The switch only means something if we STARTED somewhere else. Asserted rather
    // than assumed: the shell bootstraps from a stored company preference, so a leak
    // from a previous case would otherwise turn this into a no-op that still passes.
    expect(requestsFor(recorded, '/organization/locations')[0].headers['X-Company-Id']).toBe(
      COMPANY_A,
    )

    await userEvent.selectOptions(screen.getByRole('combobox'), COMPANY_B)

    await waitFor(() => {
      const after = requestsFor(recorded, '/organization/locations')
      expect(after.length).toBeGreaterThan(before)
      expect(after[after.length - 1].headers['X-Company-Id']).toBe(COMPANY_B)
    })
    expect(router.state.location.pathname).toBe(PATH)
  })

  it('asks for page 1 again after a switch, never the previous company’s page', async () => {
    const recorded = stub({
      locations: (url) =>
        url.includes('page=2')
          ? json(page([location(LOC_GAZ, SITE_GAZ, 'GAZ-DEP', 'Gaziantep')], { page: 2, pageSize: 3, totalCount: 8 }))
          : json(page(ROWS, { page: 1, pageSize: 3, totalCount: 8 })),
    })
    renderAt(PATH)
    await userEvent.click(await screen.findByRole('button', { name: 'Sonraki' }))
    await waitFor(() =>
      expect(requestsFor(recorded, '/organization/locations').some((r) => r.url.includes('page=2'))).toBe(true),
    )
    // Same guard as above, and this is the case that actually caught the leak on CI:
    // with a stale preference the run started in B, so the page=2 read WAS a B read
    // and "never the previous company's page" failed for the right reason.
    expect(
      requestsFor(recorded, '/organization/locations').every(
        (r) => r.headers['X-Company-Id'] === COMPANY_A,
      ),
    ).toBe(true)

    await userEvent.selectOptions(screen.getByRole('combobox'), COMPANY_B)

    await waitFor(() => {
      const forB = requestsFor(recorded, '/organization/locations').filter(
        (r) => r.headers['X-Company-Id'] === COMPANY_B,
      )
      expect(forB.length).toBeGreaterThan(0)
      expect(forB.every((r) => r.url.includes('page=1'))).toBe(true)
    })
  })
})

describe('capability gating', () => {
  it('mounts nothing and reads nothing when the server did not grant the screen', async () => {
    const recorded = stub({ capabilities: [] })
    renderAt(PATH)

    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    // Not showing it is not enough: the screen must not have CALLED anything either.
    expect(requestsFor(recorded, '/organization/sites')).toHaveLength(0)
    expect(requestsFor(recorded, '/organization/locations')).toHaveLength(0)
  })

  it('renders for a read-only holder', async () => {
    stub({ capabilities: READ_CAPABILITIES })
    renderAt(PATH)
    expect(await screen.findByRole('table', { name: /SITE-OSM/ })).toBeTruthy()
  })
})

describe('returning from a location card', () => {
  /** Page 1 and page 2 hold different rows, so "which page am I on" is observable. */
  function pagedStub() {
    return stub({
      locations: (url) =>
        url.includes('page=2')
          ? json(page([location(LOC_GAZ, SITE_GAZ, 'GAZ-DEP', 'Gaziantep')], { page: 2, pageSize: 3, totalCount: 6 }))
          : json(page(ROWS, { page: 1, pageSize: 3, totalCount: 6 })),
    })
  }

  it('comes back to the page the card was opened from, not page 1', async () => {
    /**
     * The real round-trip, not a cache flag: page 2 → card → back. Losing the page here
     * is the difference between "I changed one of 200 locations" and "I have to find my
     * place again after every single change".
     */
    pagedStub()
    renderAt(PATH)

    await userEvent.click(await screen.findByRole('button', { name: 'Sonraki' }))
    expect(await screen.findByText('Sayfa 2 / 2')).toBeTruthy()

    await userEvent.click(await screen.findByRole('link', { name: 'GAZ-DEP' }))
    expect(await screen.findByRole('heading', { name: 'Lokasyon' })).toBeTruthy()

    // EXACT match, not /Organizasyon/: with the real shell mount the sidebar also
    // carries an "Organizasyon" nav link, and a loose pattern finds both.
    await userEvent.click(screen.getByRole('link', { name: '← Organizasyon' }))

    expect(await screen.findByText('Sayfa 2 / 2')).toBeTruthy()
    expect(screen.getByRole('link', { name: 'GAZ-DEP' })).toBeTruthy()
  })

  it('still starts on page 1 for a fresh entry', async () => {
    pagedStub()
    renderAt(PATH)
    expect(await screen.findByText('Sayfa 1 / 2')).toBeTruthy()
  })

  it('resets to page 1 when the company changes, even carrying a return page', async () => {
    /**
     * The return state is bound to the context namespace, so a switch cannot restore
     * company A's page 3 in company B. That makes "same context returns to its page"
     * and "a switch resets to page 1" one rule rather than two competing ones.
     */
    const recorded = pagedStub()
    renderAt(PATH)

    await userEvent.click(await screen.findByRole('button', { name: 'Sonraki' }))
    await userEvent.click(await screen.findByRole('link', { name: 'GAZ-DEP' }))
    await screen.findByRole('heading', { name: 'Lokasyon' })
    // EXACT match, not /Organizasyon/: with the real shell mount the sidebar also
    // carries an "Organizasyon" nav link, and a loose pattern finds both.
    await userEvent.click(screen.getByRole('link', { name: '← Organizasyon' }))
    expect(await screen.findByText('Sayfa 2 / 2')).toBeTruthy()

    await userEvent.selectOptions(screen.getByRole('combobox'), COMPANY_B)

    await waitFor(() => {
      const forB = requestsFor(recorded, '/organization/locations').filter(
        (request) => request.headers['X-Company-Id'] === COMPANY_B,
      )
      expect(forB.length).toBeGreaterThan(0)
      expect(forB.every((request) => request.url.includes('page=1'))).toBe(true)
    })
  })
})
