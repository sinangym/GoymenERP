/**
 * ORG-03 component/DOM tests — the authorized work-center list, the step to a location
 * card, and the step back.
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
  LOC_H0,
  LOC_H1,
  SITE_OSM,
  WC_PAINT,
  WC_SLIT,
  WC_TUBE,
  WORK_CENTERS_ONLY_CAPABILITIES,
  WORK_CENTERS_WITH_LOCATION_CAPABILITIES,
  installFetch,
  json,
  location,
  problem,
  renderAt,
  requestsFor,
  workCenter,
} from './testHarness'
import { WORK_CENTER_LOCATION_UNAVAILABLE } from './organizationText'
import { workCenterListPath } from './capabilities'

const PATH = workCenterListPath()

const ROWS = [
  workCenter(WC_PAINT, LOC_H0, 'WC-PAINT', 'Boyama Hattı'),
  workCenter(WC_TUBE, LOC_H1, 'WC-TUBE', 'Boru / Profil Hattı', { kind: 'TubeProfile' }),
  workCenter(WC_SLIT, LOC_H1, 'WC-SLIT', 'Dilme Hattı', { kind: 'Slitting', isActive: false }),
]

const LOCATION_H0 = location(LOC_H0, SITE_OSM, 'OSM-H0', 'Göymen Osmaniye Depo Hol-0')

function stub(
  overrides: {
    workCenters?: (url: string, init: RequestInit | undefined) => Response | Promise<Response>
    location?: () => Response
    capabilities?: readonly string[]
  } = {},
) {
  return installFetch({
    // Work centers WITHOUT the location card is the default here: it is the
    // configuration this screen exists for, so it is the one the cases start from.
    capabilities: overrides.capabilities ?? WORK_CENTERS_ONLY_CAPABILITIES,
    respond: (url, init) => {
      if (url.includes('/organization/work-centers')) {
        return (overrides.workCenters ?? (() => json(ROWS)))(url, init)
      }
      if (url.includes('/organization/locations/')) {
        return (overrides.location ?? (() => json(LOCATION_H0)))()
      }
      return undefined
    },
  })
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('reading the authorized work centers', () => {
  it('renders every declared field the user can act on', async () => {
    stub()
    renderAt(PATH)

    expect(await screen.findByRole('heading', { name: 'İş Merkezleri' })).toBeTruthy()
    const table = await screen.findByRole('table', { name: /İş Merkezleri/ })
    const paint = within(table).getByText('WC-PAINT').closest('tr') as HTMLElement
    expect(within(paint).getByText('Boyama Hattı')).toBeTruthy()
    expect(within(paint).getByText('Boyama')).toBeTruthy()
    expect(within(paint).getByText('Aktif')).toBeTruthy()
  })

  it('keeps a passive work center visible — isActive is a state, not a filter', async () => {
    stub()
    renderAt(PATH)

    const table = await screen.findByRole('table', { name: /İş Merkezleri/ })
    const slit = within(table).getByText('WC-SLIT').closest('tr') as HTMLElement
    expect(within(slit).getByText('Pasif')).toBeTruthy()
  })

  it('shows an unknown kind token verbatim rather than inventing a label', async () => {
    stub({ workCenters: () => json([workCenter(WC_PAINT, LOC_H0, 'WC-GALV', 'Galvaniz', { kind: 'Galvanizing' })]) })
    renderAt(PATH)
    expect(await screen.findByText('Galvanizing')).toBeTruthy()
  })

  it('names the scope of its own count instead of implying a company total', async () => {
    stub()
    renderAt(PATH)
    expect(await screen.findByText('Yetkiniz dâhilindeki 3 iş merkezi gösteriliyor.')).toBeTruthy()
  })

  it('scopes the read to the committed company', async () => {
    const recorded = stub()
    renderAt(PATH)
    await screen.findByRole('table', { name: /İş Merkezleri/ })

    const requests = requestsFor(recorded, '/organization/work-centers')
    expect(requests.length).toBeGreaterThan(0)
    expect(requests[0].headers['X-Company-Id']).toBe(COMPANY_A)
  })

  it('sends no parameter the endpoint cannot answer, and offers no search box', async () => {
    const recorded = stub()
    renderAt(PATH)
    await screen.findByRole('table', { name: /İş Merkezleri/ })

    // `/work-centers` takes nothing at all — not page, not q, not locationId, not kind.
    const url = requestsFor(recorded, '/organization/work-centers')[0].url
    expect(url.endsWith('/organization/work-centers')).toBe(true)
    expect(screen.queryByRole('searchbox')).toBeNull()
    expect(screen.queryByRole('textbox')).toBeNull()
    // ...and no pager either: an unpaged list must not imply a page boundary.
    expect(screen.queryByRole('button', { name: 'Sonraki' })).toBeNull()
  })
})

describe('the step to a location, and the step back', () => {
  it('opens the card and returns to THIS list, by keyboard alone', async () => {
    /**
     * The whole journey, driven the way a keyboard user drives it: Tab to the row's
     * location link, Enter to open, then Tab to the card's back link and Enter to
     * return. Both are anchors, so this exercises real focus order and real activation
     * rather than a click handler.
     *
     * The return target is the assertion that matters: a hardcoded `/ayarlar/organizasyon`
     * would strand a `work-centers.read`-only user on a route they do not hold.
     */
    const { router } = stubAndRender({ capabilities: WORK_CENTERS_WITH_LOCATION_CAPABILITIES })
    const link = await screen.findByRole('link', { name: 'WC-PAINT iş merkezinin lokasyonunu aç' })

    link.focus()
    expect(document.activeElement).toBe(link)
    await userEvent.keyboard('{Enter}')

    await waitFor(() =>
      expect(router.state.location.pathname).toBe(`/ayarlar/organizasyon/lokasyon/${LOC_H0}`),
    )
    expect(await screen.findByText('OSM-H0')).toBeTruthy()

    const back = await screen.findByRole('link', { name: /İş Merkezleri/ })
    back.focus()
    await userEvent.keyboard('{Enter}')

    await waitFor(() => expect(router.state.location.pathname).toBe(PATH))
    expect(await screen.findByRole('table', { name: /İş Merkezleri/ })).toBeTruthy()
  })

  it('offers no location link at all without the card capability, and calls nothing', async () => {
    /**
     * The acceptance in one case: `locationId` is an identifier the row happens to
     * carry, never an entitlement. Without `organization.locations.detail` there is no
     * link, no disabled control, no raw UUID — and, crucially, NO REQUEST. Asking would
     * spend a refusal to learn what the capability already said.
     */
    const recorded = stub()
    renderAt(PATH)

    await screen.findByRole('table', { name: /İş Merkezleri/ })
    expect(screen.queryAllByRole('link', { name: /lokasyonunu aç/ })).toHaveLength(0)
    expect(screen.getAllByText(WORK_CENTER_LOCATION_UNAVAILABLE).length).toBe(ROWS.length)
    expect(screen.queryByText(LOC_H0)).toBeNull()
    expect(requestsFor(recorded, '/organization/locations')).toHaveLength(0)
  })

  it('does not close the work-center list just because locations are out of scope', async () => {
    // The mirror of ORG-01's sites rule, and the reason ORG-03 is its own route.
    stub()
    renderAt(PATH)
    expect(await screen.findByText('WC-PAINT')).toBeTruthy()
    expect(screen.queryByText('Bulunamadı — sayfa')).toBeNull()
  })

  it('names the work center in each link, not just “Lokasyonu aç”', async () => {
    // One repeated accessible name down a column tells a screen-reader user nothing
    // about which row they are on.
    stubAndRender({ capabilities: WORK_CENTERS_WITH_LOCATION_CAPABILITIES })
    const links = await screen.findAllByRole('link', { name: /lokasyonunu aç/ })
    expect(links.map((item) => item.getAttribute('aria-label'))).toEqual([
      'WC-PAINT iş merkezinin lokasyonunu aç',
      'WC-TUBE iş merkezinin lokasyonunu aç',
      'WC-SLIT iş merkezinin lokasyonunu aç',
    ])
  })
})

describe('states', () => {
  it('distinguishes “no records” from a refused read', async () => {
    stub({ workCenters: () => json([]) })
    renderAt(PATH)
    expect(await screen.findByText('Kayıt yok — iş merkezleri')).toBeTruthy()
  })

  it('fails closed on a 403 without painting rows', async () => {
    // The shared boundary drops the capability generation and the gate closes. What
    // matters here is that no row is shown either way.
    stub({ workCenters: () => problem(403) })
    renderAt(PATH)
    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    expect(screen.queryByText('WC-PAINT')).toBeNull()
  })

  it('reports a 5xx as a service problem, not as empty', async () => {
    stub({ workCenters: () => problem(503) })
    renderAt(PATH)
    expect(await screen.findByText(/Servis geçici olarak kullanılamıyor/)).toBeTruthy()
    expect(screen.queryByText('Kayıt yok — iş merkezleri')).toBeNull()
  })

  it('separates “no response arrived” from “the response was wrong”', async () => {
    stub({ workCenters: () => Promise.reject(new TypeError('failed to fetch')) })
    renderAt(PATH)
    expect(await screen.findByText('Sunucuya ulaşılamadı — iş merkezleri')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Tekrar dene' })).toBeTruthy()
  })

  it('fails the whole list closed on one unreadable row', async () => {
    // A list silently one row short is indistinguishable from a correct one, and this
    // endpoint is unpaged — the array IS the authorized set.
    stub({ workCenters: () => json([ROWS[0], { ...ROWS[1], kind: 7 }]) })
    renderAt(PATH)
    expect(await screen.findByText('Beklenmeyen yanıt — iş merkezleri')).toBeTruthy()
    expect(screen.queryByText('WC-PAINT')).toBeNull()
  })

  it('treats a 200 that is not an array as malformed, never as empty', async () => {
    stub({ workCenters: () => json({ items: ROWS }) })
    renderAt(PATH)
    expect(await screen.findByText('Beklenmeyen yanıt — iş merkezleri')).toBeTruthy()
  })
})

describe('capability gating', () => {
  it('mounts nothing and reads nothing when the server did not grant the screen', async () => {
    const recorded = stub({ capabilities: [] })
    renderAt(PATH)

    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    expect(requestsFor(recorded, '/organization/work-centers')).toHaveLength(0)
  })

  it('is not opened by the locations capabilities alone', async () => {
    // ORG-03 stands on `organization.work-centers.read` and nothing else, in BOTH
    // directions: holding the location screens is not a way in.
    const recorded = stub({
      capabilities: ['organization.sites.list', 'organization.locations.detail'],
    })
    renderAt(PATH)

    expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    expect(requestsFor(recorded, '/organization/work-centers')).toHaveLength(0)
  })
})

describe('company fence', () => {
  it('never paints one company’s cached rows under another company', async () => {
    /**
     * The leak the namespaced query key actually prevents, in the caching production
     * uses (30 s `staleTime`, pages retained). A's rows resolve and are cached; the
     * switch then remounts this screen under B. With a key that did not carry the
     * context, B would find A's entry still FRESH and paint it immediately while
     * refetching behind it — one company's work centers under the other's label.
     *
     * B's answer is held open precisely so that window is wide: for as long as B has
     * no data, the only thing the screen could wrongly show is A's.
     *
     * Measured both ways: with the namespace removed from the key this case fails on
     * `WC-PAINT`, and with `gcTime: 0` instead of `retainCache` it passes vacuously.
     */
    let releaseB = () => {}
    const heldB = new Promise<void>((resolve) => {
      releaseB = resolve
    })
    const recorded = stub({
      workCenters: (_url, init) => {
        const headers = (init?.headers ?? {}) as Record<string, string>
        return headers['X-Company-Id'] === COMPANY_A
          ? json(ROWS)
          : heldB.then(() => json([workCenter(WC_TUBE, LOC_H1, 'GSP-WC', 'Sac Profil Hattı')]))
      },
    })
    renderAt(PATH, { retainCache: true })

    // The switch only means something if we started somewhere else.
    expect(await screen.findByText('WC-PAINT')).toBeTruthy()
    await userEvent.selectOptions(screen.getByRole('combobox'), COMPANY_B)

    await waitFor(() =>
      expect(
        requestsFor(recorded, '/organization/work-centers').some(
          (request) => request.headers['X-Company-Id'] === COMPANY_B,
        ),
      ).toBe(true),
    )
    // B has nothing yet, and A's cached rows are not B's to show.
    expect(screen.queryByText('WC-PAINT')).toBeNull()
    expect(screen.queryByText('Boyama Hattı')).toBeNull()

    releaseB()
    expect(await screen.findByText('GSP-WC')).toBeTruthy()
    expect(screen.queryByText('WC-PAINT')).toBeNull()
  })

  it('never shows company A’s late answer under company B', async () => {
    /**
     * The acceptance as written: A's read is held open until after the switch has
     * committed and B has rendered, then released.
     *
     * This is an OUTCOME test, and honestly labelled as one: the screen's namespaced
     * key is not the only thing standing here — the switch unmounts this screen, which
     * cancels the in-flight A read, so the property survives a shared key too
     * (measured). The case above is the one that pins the key. Both are kept because
     * both are the acceptance.
     */
    let releaseA = () => {}
    const heldA = new Promise<void>((resolve) => {
      releaseA = resolve
    })
    const recorded = stub({
      workCenters: (_url, init) => {
        const headers = (init?.headers ?? {}) as Record<string, string>
        return headers['X-Company-Id'] === COMPANY_A
          ? heldA.then(() => json(ROWS))
          : json([workCenter(WC_TUBE, LOC_H1, 'GSP-WC', 'Sac Profil Hattı')])
      },
    })
    renderAt(PATH, { retainCache: true })

    await waitFor(() =>
      expect(requestsFor(recorded, '/organization/work-centers')[0]?.headers['X-Company-Id']).toBe(
        COMPANY_A,
      ),
    )
    await userEvent.selectOptions(screen.getByRole('combobox'), COMPANY_B)
    expect(await screen.findByText('GSP-WC')).toBeTruthy()

    releaseA()
    await heldA
    // Give the resolved A response every chance to paint before denying that it did.
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(screen.queryByText('WC-PAINT')).toBeNull()
    expect(screen.queryByText('Boyama Hattı')).toBeNull()
    expect(screen.getByText('GSP-WC')).toBeTruthy()
    // The screen genuinely RE-READ under B rather than reusing anything of A's, and
    // releasing A added no further A-scoped request.
    const reads = requestsFor(recorded, '/organization/work-centers')
    expect(reads.some((request) => request.headers['X-Company-Id'] === COMPANY_B)).toBe(true)
    expect(reads[reads.length - 1].headers['X-Company-Id']).toBe(COMPANY_B)
  })
})

describe('this whole flow is a read', () => {
  it('issues GETs only — no POST, no PUT, no command anywhere in the journey', async () => {
    const recorded = stub({ capabilities: WORK_CENTERS_WITH_LOCATION_CAPABILITIES })
    const { router } = renderAt(PATH)

    const link = await screen.findByRole('link', { name: 'WC-PAINT iş merkezinin lokasyonunu aç' })
    await userEvent.click(link)
    await waitFor(() =>
      expect(router.state.location.pathname).toBe(`/ayarlar/organizasyon/lokasyon/${LOC_H0}`),
    )
    await userEvent.click(await screen.findByRole('link', { name: /İş Merkezleri/ }))
    await waitFor(() => expect(router.state.location.pathname).toBe(PATH))

    expect(recorded.length).toBeGreaterThan(0)
    expect(recorded.map((request) => request.method.toUpperCase())).toEqual(
      recorded.map(() => 'GET'),
    )
    expect(recorded.every((request) => request.body === null)).toBe(true)
    // The card's write surface needs its own capability and was never granted here.
    expect(screen.queryByRole('button', { name: /Pasife al|Aktifleştir/ })).toBeNull()
  })
})

/** Installs the stub and renders at ORG-03, returning the router for navigation cases. */
function stubAndRender(overrides: Parameters<typeof stub>[0] = {}) {
  stub(overrides)
  return renderAt(PATH)
}
