/**
 * ORG-04 component cases — one work centre's read-only card.
 *
 * COMPONENT/DOM EVIDENCE ONLY. The harness stubs `fetch` with synthetic fixtures, so nothing here is evidence of
 * real API, real SQL or browser acceptance — a fixture is not a record. What these cases establish is what the
 * card renders, what it does NOT send (no write, no location probe), and where its links lead.
 */

import { act } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import {
  COMPANY_A,
  COMPANY_B,
  LOC_H0,
  LOC_H1,
  SITE_OSM,
  WC_PAINT,
  WC_SLIT,
  WORK_CENTER_CARD_CAPABILITIES,
  WORK_CENTER_CARD_WITH_LOCATION_CAPABILITIES,
  WORK_CENTERS_ONLY_CAPABILITIES,
  installFetch,
  json,
  location,
  problem,
  renderAt,
  requestsFor,
  workCenter,
  workCenterDetail,
  type RecordedRequest,
} from './testHarness'
import { ORGANIZATION_CAPABILITY, workCenterDetailPath, workCenterListPath } from './capabilities'
import { DETAIL_ORIGIN, workCenterCardBackTarget, workCenterCardOrigin } from './detailReturn'

const LIST = workCenterListPath()
const CARD_RE = /\/organization\/work-centers\/[^/?]+$/
const LIST_RE = /\/organization\/work-centers$/

afterEach(() => {
  vi.unstubAllGlobals()
})

function respond(
  card: (url: string) => Response,
  rows: readonly unknown[] = [workCenter(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1')],
) {
  return (url: string) => {
    if (CARD_RE.test(url)) return card(url)
    if (LIST_RE.test(url)) return json(rows)
    if (url.includes(`/organization/locations/${LOC_H0}`)) return json(location(LOC_H0, SITE_OSM, 'OSM-H0', 'Hol-0'))
    return undefined
  }
}

const cardReads = (recorded: readonly RecordedRequest[]) => recorded.filter((request) => CARD_RE.test(request.url))
const locationReads = (recorded: readonly RecordedRequest[]) => requestsFor(recorded, '/organization/locations')

describe('ORG-04 work centre card', () => {
  it('shows the record’s own facts and nothing it does not carry', async () => {
    const recorded = installFetch({
      capabilities: WORK_CENTER_CARD_CAPABILITIES,
      respond: respond(() => json(workCenterDetail(WC_SLIT, LOC_H1, 'DL-02', 'Dilme Hattı 2', {
        kind: 'Slitting',
        isActive: false,
        version: 7,
      }))),
    })
    renderAt(workCenterDetailPath(WC_SLIT))

    const card = await screen.findByRole('heading', { name: 'İş merkezi' })
    expect(card).toBeTruthy()
    expect(await screen.findByText('DL-02')).toBeTruthy()
    expect(screen.getByText('Dilme Hattı 2')).toBeTruthy()
    expect(screen.getByText('Dilme')).toBeTruthy()
    // Passive is a state, not a delete: the card stays readable and says so in words.
    expect(screen.getByText('Pasif')).toBeTruthy()
    // The version is kept for a stale check, never shown as a figure; no capacity or location name is invented.
    expect(screen.queryByText('7')).toBeNull()
    expect(screen.queryByText(/kapasite:/i)).toBeNull()
    expect(screen.queryByText('Hol-1')).toBeNull()

    // Exactly one read, with the active company; nothing written.
    const [read] = cardReads(recorded)
    expect(cardReads(recorded)).toHaveLength(1)
    expect(read.url).toMatch(new RegExp(`/api/v1/organization/work-centers/${WC_SLIT}$`))
    expect(read.headers['X-Company-Id']).toBe(COMPANY_A)
    expect(recorded.filter((request) => request.method !== 'GET')).toHaveLength(0)
  })

  it('states the location scope and asks nothing about it when the server says the card will not open', async () => {
    const recorded = installFetch({
      capabilities: WORK_CENTER_CARD_WITH_LOCATION_CAPABILITIES,
      respond: respond(() => json(workCenterDetail(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1'))),
    })
    renderAt(workCenterDetailPath(WC_PAINT))

    expect(await screen.findByText('Lokasyon kartı bu yetkiyle açılamıyor')).toBeTruthy()
    expect(screen.queryByRole('link', { name: 'Lokasyon kartını aç' })).toBeNull()
    // No probe: a GET that 404s would turn the deliberate silence into an existence oracle.
    expect(locationReads(recorded)).toEqual([])
  })

  it('keeps the location closed without the location card capability, even when the flag is true', async () => {
    const recorded = installFetch({
      capabilities: WORK_CENTER_CARD_CAPABILITIES,
      respond: respond(() =>
        json(workCenterDetail(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1', { canOpenLocationDetail: true })),
      ),
    })
    renderAt(workCenterDetailPath(WC_PAINT))

    expect(await screen.findByText('Lokasyon kartı bu yetkiyle açılamıyor')).toBeTruthy()
    expect(screen.queryByRole('link', { name: 'Lokasyon kartını aç' })).toBeNull()
    expect(locationReads(recorded)).toEqual([])
  })

  it('opens the location card when the server allows it, and that card returns to the work-centre list', async () => {
    installFetch({
      capabilities: WORK_CENTER_CARD_WITH_LOCATION_CAPABILITIES,
      respond: respond(() =>
        json(workCenterDetail(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1', { canOpenLocationDetail: true })),
      ),
    })
    const { router } = renderAt(workCenterDetailPath(WC_PAINT))

    const link = await screen.findByRole('link', { name: 'Lokasyon kartını aç' })
    expect(link.getAttribute('href')).toBe(`/ayarlar/organizasyon/lokasyon/${LOC_H0}`)
    await userEvent.click(link)
    expect(await screen.findByText('OSM-H0')).toBeTruthy()

    // The location card's own back link (#434) honours the ORG-03 token the card carried onward.
    const back = within(screen.getByRole('navigation', { name: 'Geri' })).getByRole('link', { name: /İş Merkezleri/ })
    expect(back.getAttribute('href')).toBe(LIST)
    await userEvent.click(back)
    expect(await screen.findByRole('table')).toBeTruthy()
    expect(router.state.location.pathname).toBe(LIST)
  })

  it('is opened from its list row and returns to the same filtered list', async () => {
    installFetch({
      capabilities: WORK_CENTER_CARD_CAPABILITIES,
      respond: respond(
        () => json(workCenterDetail(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1')),
        [
          workCenter(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1'),
          workCenter(WC_SLIT, LOC_H1, 'DL-02', 'Dilme Hattı 2', { kind: 'Slitting' }),
        ],
      ),
    })
    const { router } = renderAt(workCenterListPath(LOC_H0))

    const row = await screen.findByRole('link', { name: 'İş merkezi kartını aç: BY-01' })
    expect(row.getAttribute('href')).toBe(workCenterDetailPath(WC_PAINT))
    await userEvent.click(row)
    expect(await screen.findByRole('heading', { name: 'İş merkezi' })).toBeTruthy()
    expect(router.state.location.pathname).toBe(workCenterDetailPath(WC_PAINT))

    const back = within(screen.getByRole('navigation', { name: 'Geri' })).getByRole('link', { name: /İş Merkezleri/ })
    expect(back.getAttribute('href')).toBe(workCenterListPath(LOC_H0))
    await userEvent.click(back)
    expect(await screen.findByText('BY-01')).toBeTruthy()
    expect(screen.queryByText('DL-02')).toBeNull()
    expect(router.state.location.search).toBe(`?locationId=${LOC_H0}`)
  })

  it('returns to the whole list when opened without a recorded origin', async () => {
    installFetch({
      capabilities: WORK_CENTER_CARD_CAPABILITIES,
      respond: respond(() => json(workCenterDetail(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1'))),
    })
    renderAt(workCenterDetailPath(WC_PAINT))
    await screen.findByText('BY-01')
    const back = within(screen.getByRole('navigation', { name: 'Geri' })).getByRole('link', { name: /İş Merkezleri/ })
    expect(back.getAttribute('href')).toBe(LIST)
  })

  it('shows the same neutral not-found for an unknown, foreign or out-of-scope id (a code-less 404)', async () => {
    installFetch({ capabilities: WORK_CENTER_CARD_CAPABILITIES, respond: respond(() => problem(404)) })
    renderAt(workCenterDetailPath(WC_PAINT))
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(screen.queryByText('BY-01')).toBeNull()
  })

  it('never asks for an id the routes could not have produced', async () => {
    const recorded = installFetch({ capabilities: WORK_CENTER_CARD_CAPABILITIES, respond: respond(() => problem(404)) })
    renderAt('/ayarlar/organizasyon/is-merkezleri/not-an-id')
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(cardReads(recorded)).toEqual([])
  })

  it('does not mount, and asks nothing, without the card capability', async () => {
    const recorded = installFetch({
      capabilities: WORK_CENTERS_ONLY_CAPABILITIES,
      respond: respond(() => json(workCenterDetail(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1'))),
    })
    renderAt(workCenterDetailPath(WC_PAINT))
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    expect(cardReads(recorded)).toEqual([])
    expect(screen.queryByText('BY-01')).toBeNull()
  })

  it.each([
    ['another record', () => workCenterDetail(WC_SLIT, LOC_H0, 'DL-02', 'Dilme Hattı 2')],
    ['a missing version', () => workCenter(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1')],
    ['a zero version', () => workCenterDetail(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1', { version: 0 })],
    ['a string version', () => ({ ...workCenterDetail(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1'), version: '1' })],
    ['a missing location flag', () => {
      const { canOpenLocationDetail: _dropped, ...rest } = workCenterDetail(WC_PAINT, LOC_H0, 'BY-01', 'Boya')
      return rest
    }],
  ])('shows no card at all for %s', async (_case, body) => {
    installFetch({ capabilities: WORK_CENTER_CARD_CAPABILITIES, respond: respond(() => json(body())) })
    renderAt(workCenterDetailPath(WC_PAINT))
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByText('BY-01')).toBeNull()
    expect(screen.queryByText('DL-02')).toBeNull()
  })

  it.each([401, 403])('never exposes the card after the auth boundary receives %s', async (status) => {
    installFetch({ capabilities: WORK_CENTER_CARD_CAPABILITIES, respond: respond(() => json({ status }, status)) })
    renderAt(workCenterDetailPath(WC_PAINT))
    await screen.findByRole('alert')
    expect(screen.queryByText('BY-01')).toBeNull()
  })

  it('offers a retry for a network error and shows the card on the next read', async () => {
    let calls = 0
    installFetch({
      capabilities: WORK_CENTER_CARD_CAPABILITIES,
      respond: respond(() => {
        calls += 1
        return calls === 1 ? problem(503) : json(workCenterDetail(WC_PAINT, LOC_H0, 'BY-01', 'Boya Hattı 1'))
      }),
    })
    renderAt(workCenterDetailPath(WC_PAINT))
    await userEvent.click(await screen.findByRole('button', { name: 'Tekrar dene' }))
    expect(await screen.findByText('BY-01')).toBeTruthy()
  })

  it('ignores a late company-A answer after switching to company B', async () => {
    installFetch({ capabilities: WORK_CENTER_CARD_CAPABILITIES, respond: respond(() => problem(404)) })
    const normalFetch = globalThis.fetch
    let complete: (value: Response) => void = () => {
      throw new Error('Deferred read was not started')
    }
    const delayed = new Promise<Response>((resolve) => {
      complete = resolve
    })
    const calls: string[] = []
    vi.stubGlobal(
      'fetch',
      vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
        const url = typeof input === 'string' ? input : input.toString()
        if (CARD_RE.test(url)) {
          const company = new Headers(init?.headers).get('X-Company-Id') ?? ''
          calls.push(company)
          if (company === COMPANY_A) return delayed
        }
        return normalFetch(input, init)
      }),
    )
    renderAt(workCenterDetailPath(WC_PAINT))
    await waitFor(() => expect(calls).toContain(COMPANY_A))
    fireEvent.change(screen.getByRole('combobox', { name: 'Şirket' }), { target: { value: COMPANY_B } })
    await waitFor(() => expect(calls).toContain(COMPANY_B))
    await waitFor(() => expect(document.querySelector('[data-state="not-found"]')).not.toBeNull())
    await act(async () => {
      complete(json(workCenterDetail(WC_PAINT, LOC_H0, 'OLD-A', 'Eski şirket')))
    })
    expect(screen.queryByText('OLD-A')).toBeNull()
  })
})

describe('ORG-04 return tokens', () => {
  const ns = 'ns-1'

  it('honours only the exact ORG-03 tokens of the current context', () => {
    expect(workCenterCardOrigin({ ns, from: DETAIL_ORIGIN.workCenters }, ns)).toEqual({
      ns,
      from: DETAIL_ORIGIN.workCenters,
    })
    expect(workCenterCardOrigin({ ns, from: DETAIL_ORIGIN.workCenters, locationId: LOC_H0 }, ns)).toEqual({
      ns,
      from: DETAIL_ORIGIN.workCenters,
      locationId: LOC_H0,
    })
    for (const state of [
      null,
      'path',
      [],
      { ns: 'other', from: DETAIL_ORIGIN.workCenters },
      { ns, from: DETAIL_ORIGIN.locations },
      { ns, from: DETAIL_ORIGIN.workCenters, locationId: 'not-an-id' },
      // Uppercase is not the canonical form the routes emit.
      { ns, from: DETAIL_ORIGIN.workCenters, locationId: 'AAAAAAAA-1111-1111-1111-111111111111' },
      { ns, from: DETAIL_ORIGIN.workCenters, locationId: '00000000-0000-0000-0000-000000000000' },
      { ns, from: DETAIL_ORIGIN.workCenters, path: '/elsewhere' },
      { ns, from: DETAIL_ORIGIN.workCenters, locationId: LOC_H0, path: '/elsewhere' },
    ]) {
      expect(workCenterCardOrigin(state, ns)).toBeUndefined()
    }
    expect(workCenterCardOrigin({ ns, from: DETAIL_ORIGIN.workCenters }, null)).toBeUndefined()
  })

  it('goes back to the list, restoring only its location filter', () => {
    expect(workCenterCardBackTarget(undefined)).toEqual({ path: LIST, label: 'İş Merkezleri' })
    expect(workCenterCardBackTarget({ ns, from: DETAIL_ORIGIN.workCenters })).toEqual({ path: LIST, label: 'İş Merkezleri' })
    expect(workCenterCardBackTarget({ ns, from: DETAIL_ORIGIN.workCenters, locationId: LOC_H0 })).toEqual({
      path: workCenterListPath(LOC_H0),
      label: 'İş Merkezleri',
    })
  })

  it('publishes the card on its own id and route', () => {
    expect(ORGANIZATION_CAPABILITY.workCenterDetail).toBe('organization.work-centers.detail')
    expect(workCenterDetailPath(WC_PAINT)).toBe(`/ayarlar/organizasyon/is-merkezleri/${WC_PAINT}`)
  })
})
