/**
 * ORG-03 component cases.
 *
 * COMPONENT/DOM EVIDENCE ONLY. The harness stubs `fetch` with synthetic fixtures, so
 * nothing here is evidence of real API, real SQL or browser acceptance — a fixture is
 * not a record. What these cases CAN establish is what the screen renders and, just as
 * importantly, what it does not SEND, because "did not show it" and "did not ask for it"
 * are different guarantees and only the second one is about scope.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  COMPANY_A,
  LOC_H0,
  LOC_H1,
  READ_CAPABILITIES,
  SITE_OSM,
  WC_PAINT,
  WC_SLIT,
  WC_UNKNOWN_KIND,
  WORK_CENTERS_ONLY_CAPABILITIES,
  installFetch,
  json,
  location,
  renderAt,
  requestsFor,
  workCenter,
} from './testHarness'
import { ORGANIZATION_CAPABILITY, ORGANIZATION_NAV_LABEL, ORGANIZATION_ROUTE } from './capabilities'

const PATH = ORGANIZATION_ROUTE[ORGANIZATION_CAPABILITY.workCentersList]

afterEach(() => {
  vi.unstubAllGlobals()
})

function respondWith(rows: readonly unknown[]) {
  return (url: string) => (url.includes('/organization/work-centers') ? json(rows) : undefined)
}

describe('ORG-03 work centre list', () => {
  it('renders every row the server returned, passive ones included', async () => {
    installFetch({
      capabilities: WORK_CENTERS_ONLY_CAPABILITIES,
      respond: respondWith([
        workCenter(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1'),
        workCenter(WC_SLIT, LOC_H1, 'DL-02', 'Dilme Hattı 2', {
          kind: 'Slitting',
          isActive: false,
        }),
      ]),
    })
    renderAt(PATH)

    const table = await screen.findByRole('table')
    expect(await within(table).findByText('BY-01')).toBeTruthy()
    // A passive work centre is a STATE, not a deletion: the server returns it and the
    // screen shows it. Dropping it would silently under-report the company's plant.
    expect(within(table).getByText('DL-02')).toBeTruthy()
    expect(within(table).getByText('Pasif')).toBeTruthy()
    expect(within(table).getByText('Dilme')).toBeTruthy()
  })

  it('shows an unrecognised kind token verbatim instead of guessing a label', async () => {
    installFetch({
      capabilities: WORK_CENTERS_ONLY_CAPABILITIES,
      respond: respondWith([
        workCenter(WC_UNKNOWN_KIND, LOC_H0, 'XX-09', 'Yeni Hat', { kind: 'Galvanizing' }),
      ]),
    })
    renderAt(PATH)

    // The backend adding a kind is a fact about the data. Inventing Turkish for it would
    // be inventing meaning, and hiding the row would hide a work centre that exists.
    expect(await screen.findByText('Galvanizing')).toBeTruthy()
  })

  it('links the location card only when the SERVER said this caller may open it', async () => {
    const recorded = installFetch({
      capabilities: WORK_CENTERS_ONLY_CAPABILITIES,
      respond: respondWith([
        workCenter(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1', { canOpenLocationDetail: true }),
        workCenter(WC_SLIT, LOC_H1, 'DL-02', 'Dilme Hattı 2', { canOpenLocationDetail: false }),
      ]),
    })
    renderAt(PATH)

    const table = await screen.findByRole('table')
    const links = within(table).getAllByRole('link')
    // EXACTLY one link, and it points at the openable row's location — not at the other.
    expect(links).toHaveLength(1)
    expect(links[0].getAttribute('href')).toBe(`/ayarlar/organizasyon/lokasyon/${LOC_H0}`)
    expect(within(table).getByText('Lokasyon kartı bu yetkiyle açılamıyor')).toBeTruthy()

    // And the closed row is not probed. The screen HAS that locationId, so it could have
    // fetched the card to find out — the whole point of the server's flag is that it does
    // not need to, and a probe would turn a deliberate 404 into an existence oracle.
    expect(requestsFor(recorded, `/organization/locations/${LOC_H1}`)).toEqual([])
    expect(requestsFor(recorded, `/organization/locations/${LOC_H0}`)).toEqual([])
  })

  it('returns from an opened location card to this work-center list', async () => {
    installFetch({
      capabilities: [
        ...READ_CAPABILITIES,
        ORGANIZATION_CAPABILITY.workCentersList,
      ],
      respond: (url) => {
        if (url.includes('/organization/work-centers')) {
          return json([
            workCenter(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1', {
              canOpenLocationDetail: true,
            }),
          ])
        }
        if (url.includes(`/organization/locations/${LOC_H0}`)) {
          return json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0'))
        }
        return undefined
      },
    })
    const { router } = renderAt(PATH)

    await userEvent.click(await screen.findByRole('link', { name: 'Lokasyon kartını aç' }))
    expect(await screen.findByText('OSM-H0')).toBeTruthy()

    const back = within(screen.getByRole('navigation', { name: 'Geri' })).getByRole('link', {
      name: /İş Merkezleri/,
    })
    expect(back.getAttribute('href')).toBe(PATH)
    await userEvent.click(back)

    expect(await screen.findByRole('table')).toBeTruthy()
    expect(router.state.location.pathname).toBe(PATH)
  })

  it('says "no records" for an empty list rather than showing an empty table', async () => {
    installFetch({ capabilities: WORK_CENTERS_ONLY_CAPABILITIES, respond: respondWith([]) })
    renderAt(PATH)

    expect(await screen.findByRole('status')).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('treats one malformed row as a malformed LIST, not as a shorter list', async () => {
    installFetch({
      capabilities: WORK_CENTERS_ONLY_CAPABILITIES,
      respond: respondWith([
        workCenter(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1'),
        // `canOpenLocationDetail` missing: the one field whose absence must NOT default.
        { id: WC_SLIT, locationId: LOC_H1, code: 'DL-02', name: 'Dilme', kind: 'Slitting', isActive: true },
      ]),
    })
    renderAt(PATH)

    // The endpoint is unpaged, so the array IS the caller's whole authorized set. A list
    // silently one row short cannot be told apart from a correct one, so the screen
    // refuses the response instead of rendering the half it could decode.
    expect(await screen.findByRole('alert')).toBeTruthy()
    expect(screen.queryByText('BY-01')).toBeNull()
  })

  it('never asks for work centres without the capability, and shows not-found instead', async () => {
    const recorded = installFetch({
      capabilities: [],
      respond: respondWith([workCenter(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1')]),
    })
    renderAt(PATH)

    await screen.findByRole('alert')
    // Neutral not-found, exactly as an unknown path gets: an ungranted route must not be
    // distinguishable from one that does not exist.
    expect(screen.queryByRole('heading', { name: 'İş Merkezleri' })).toBeNull()
    expect(requestsFor(recorded, '/organization/work-centers')).toEqual([])
  })

  it('sends the active company on the read', async () => {
    const recorded = installFetch({
      capabilities: WORK_CENTERS_ONLY_CAPABILITIES,
      respond: respondWith([workCenter(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1')]),
    })
    renderAt(PATH)

    await screen.findByRole('table')
    const reads = requestsFor(recorded, '/organization/work-centers')
    expect(reads).toHaveLength(1)
    // The company filter is not a UI concern: the server scopes by this header and
    // re-authorizes regardless, but omitting it would be this screen asking the wrong
    // question rather than the server answering the wrong one.
    expect(reads[0]?.headers['X-Company-Id']).toBe(COMPANY_A)
  })

  it('carries a nav label, because a screen reachable from nowhere is not reachable (K5)', () => {
    expect(ORGANIZATION_NAV_LABEL[ORGANIZATION_CAPABILITY.workCentersList]).toBe('İş Merkezleri')
    expect(PATH).toBe('/ayarlar/organizasyon/is-merkezleri')
  })
})
