/** Real shell/transport/decoder component evidence; synthetic bodies, not hosted browser/SQL evidence. */
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { fetchDraftRevisions } from './api'
import { SALES_CAPABILITY } from './capabilities'
import { REVISION_HISTORY_TEXT as T } from './salesText'
import detailRaw from './examples/detail-response.json?raw'
import historyRaw from './examples/revisions-response.json?raw'
import {
  COMPANY_A, COMPANY_B, deferred, installFetch, json, renderSalesApp,
} from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const OTHER_DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaab'
const path = `/satis/siparisler/${DRAFT}`
const history = () => JSON.parse(historyRaw)
const historyRead = (url: string) => url.endsWith('/revisions')

function detail(companyId = COMPANY_A, id = DRAFT, version = '9007199254740993') {
  const body = JSON.parse(detailRaw)
  body.draft.companyId = companyId
  body.draft.id = id
  body.draft.version = version
  return body
}

afterEach(() => {
  cleanup()
  window.localStorage.clear()
  window.sessionStorage.clear()
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('SalesDraftRevisionHistory in the detail', () => {
  it('waits for a successful detail, then reads with that company and no write metadata', async () => {
    const pending = deferred<Response>()
    const fetch = installFetch({ onDetail: () => pending.promise, onRevisions: () => json(history()) })
    renderSalesApp(path)
    await waitFor(() => expect(fetch.salesCalls().filter((c) => !historyRead(c.url))).toHaveLength(1))
    expect(fetch.salesCalls().filter((c) => historyRead(c.url))).toHaveLength(0)
    await act(async () => { pending.resolve(json(detail())) })
    await screen.findByTestId('sales-revision-Rev.0')
    const reads = fetch.salesCalls().filter((c) => historyRead(c.url))
    expect(reads).toHaveLength(1)
    expect(reads[0]).toMatchObject({ companyId: COMPANY_A, method: 'GET', idempotencyKey: null, body: null })
  })

  it('renders archival identities, instants, profile and exact line values, not current-product labels', async () => {
    installFetch({ onDetail: () => json(detail()), onRevisions: () => json(history()) })
    renderSalesApp(path)
    const revision = await screen.findByTestId('sales-revision-Rev.0')
    const r = history().items[0]
    for (const value of [r.submittedBy, r.approvedBy, r.archivedBy, r.moneyProfileId]) {
      expect(within(revision).getByText(value)).toBeTruthy()
    }
    const dates = Array.from(revision.querySelectorAll('time')).map((time) => time.getAttribute('datetime'))
    expect(dates).toEqual([r.submittedAtUtc, r.approvedAtUtc, r.archivedAtUtc])
    const cells = within(screen.getByTestId('sales-revision-Rev.0-line-1')).getAllByRole('cell').map((cell) => cell.textContent)
    expect(cells).toEqual([
      r.lines[0].lineId, r.lines[0].productId, '9007199254740993', r.lines[0].orderUnitId,
      // DEC-SAL-002: the quantity of the revision that followed; this archived line id is not a line of the current
      // detail, so nothing is shown as its successor (never an assumed value).
      '1000.000', T.unknown, '12.500000', r.lines[0].priceUnitId, '0',
    ])
    expect(within(revision).queryByRole('button')).toBeNull()
  })

  it('renders the server order and represents no archive without inventing Rev.0', async () => {
    installFetch({ onDetail: () => json(detail()) })
    const app = renderSalesApp(path)
    expect(await screen.findByText(T.empty)).toBeTruthy()
    expect(screen.queryByTestId('sales-revision-Rev.0')).toBeNull()
    app.unmount()
    const rows = history()
    rows.items = ['Rev.0', 'Rev.2', 'Rev.10'].map((revision) => ({ ...rows.items[0], revision }))
    installFetch({ onDetail: () => json(detail()), onRevisions: () => json(rows) })
    renderSalesApp(path)
    const section = await screen.findByTestId('sales-revision-Rev.10')
    expect(section).toBeTruthy()
    expect(within(screen.getByTestId('sales-revision-history')).getAllByRole('heading', { level: 3 }).map((h) => h.textContent))
      .toEqual(['Rev.0', 'Rev.2', 'Rev.10'])
  })

  it('uses server Withheld, never inferring price access from detail or create capability', async () => {
    const rows = history()
    Object.assign(rows.items[0].lines[0], { priceDisclosure: 'Withheld', unitPrice: null, priceUnitId: null, discountPercent: null })
    installFetch({ onDetail: () => json(detail()), onRevisions: () => json(rows) })
    renderSalesApp(path)
    const revision = await screen.findByTestId('sales-revision-Rev.0')
    expect(within(revision).getAllByText(T.withheld)).toHaveLength(3)
    expect(within(revision).queryByText('12.500000')).toBeNull()
    expect(within(revision).queryByText(history().items[0].lines[0].priceUnitId)).toBeNull()
    expect(within(revision).queryByText(T.notPriced)).toBeNull()
  })

  it('rejects a leaking Withheld payload and offers a read-only retry', async () => {
    const rows = history()
    rows.items[0].lines[0].priceDisclosure = 'Withheld'
    installFetch({ onDetail: () => json(detail()), onRevisions: () => json(rows) })
    renderSalesApp(path)
    await screen.findByText(`Beklenmeyen yanıt — ${T.subject}`)
    expect(screen.queryByTestId('sales-revision-Rev.0')).toBeNull()
    expect(screen.queryByText('12.500000')).toBeNull()
  })

  it.each([503, 'network'] as const)('retries %s at the same read URL without changing the draft', async (failure) => {
    let count = 0
    const fetch = installFetch({ onDetail: () => json(detail()), onRevisions: () => {
      count += 1
      if (count > 1) return json(history())
      if (failure === 'network') throw new TypeError('synthetic network failure')
      return json({ status: 503 }, 503)
    } })
    renderSalesApp(path)
    const panel = await screen.findByTestId('sales-revision-history')
    await userEvent.click(await within(panel).findByRole('button', { name: 'Tekrar dene' }))
    await screen.findByTestId('sales-revision-Rev.0')
    const reads = fetch.salesCalls().filter((c) => historyRead(c.url))
    expect(reads).toHaveLength(2)
    expect(reads[1]).toEqual(reads[0])
    expect(fetch.salesCalls().every((c) => c.method === 'GET' && c.idempotencyKey === null)).toBe(true)
  })

  it('keeps a code-less 404 distinct from empty history', async () => {
    installFetch({ onDetail: () => json(detail()), onRevisions: () => json({ status: 404, title: 'Not Found' }, 404) })
    renderSalesApp(path)
    await screen.findByText(`Bulunamadı — ${T.subject}`)
    expect(screen.queryByText(T.empty)).toBeNull()
    expect(screen.queryByTestId('sales-revision-Rev.0')).toBeNull()
  })

  it.each([401, 403])('routes history %s through the real auth boundary and closes the detail', async (status) => {
    installFetch({ onDetail: () => json(detail()), onRevisions: () => json({ status }, status) })
    renderSalesApp(path)
    if (status === 401) {
      expect(await screen.findByText('Oturum sonlandı — oturum')).toBeTruthy()
    } else {
      expect(await screen.findByText('Bulunamadı — sayfa')).toBeTruthy()
    }
    expect(screen.queryByTestId('sales-revision-Rev.0')).toBeNull()
    expect(screen.queryByTestId('sales-draft-version')).toBeNull()
  })

  it('does not read history when detail is forbidden or its capability is absent', async () => {
    const fetch = installFetch({ capabilities: [SALES_CAPABILITY.draftsList] })
    renderSalesApp(path)
    await screen.findByText('Bulunamadı — sayfa')
    expect(fetch.salesCalls()).toHaveLength(0)
  })

  it('does not read history when the detail is missing', async () => {
    const fetch = installFetch({ onDetail: () => json({ status: 404 }, 404) })
    renderSalesApp(path)
    await screen.findByText('Bulunamadı — sipariş taslağı')
    expect(fetch.salesCalls().some((c) => historyRead(c.url))).toBe(false)
  })

  it('drops a late history response after navigating to a different draft', async () => {
    const pending = deferred<Response>()
    const fetch = installFetch({
      onDetail: (url) => json(detail(COMPANY_A, url.endsWith(OTHER_DRAFT) ? OTHER_DRAFT : DRAFT)),
      onRevisions: (url) => url.includes(OTHER_DRAFT) ? json({ items: [] }) : pending.promise,
    })
    const app = renderSalesApp(path)
    await waitFor(() => expect(fetch.salesCalls().some((c) => historyRead(c.url))).toBe(true))
    await act(async () => { await app.router.navigate(`/satis/siparisler/${OTHER_DRAFT}`) })
    await screen.findByText(T.empty)
    await act(async () => { pending.resolve(json(history())) })
    expect(screen.queryByTestId('sales-revision-Rev.0')).toBeNull()
    expect(screen.getByText(T.empty)).toBeTruthy()
  })

  it.each([200, 401, 403])('does not resurrect a departed company response (%s) after switch and return', async (status) => {
    const pending = deferred<Response>()
    let aReads = 0
    const fetch = installFetch({
      onDetail: (_url, company) => json(detail(company ?? COMPANY_A)),
      onRevisions: (_url, company) => {
        if (company === COMPANY_A && ++aReads === 1) return pending.promise
        return json({ items: [] })
      },
    })
    renderSalesApp(path)
    await waitFor(() => expect(fetch.salesCalls().some((c) => historyRead(c.url))).toBe(true))
    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
    await waitFor(() => expect(fetch.salesCalls().some((c) => historyRead(c.url) && c.companyId === COMPANY_B)).toBe(true))
    await act(async () => { pending.resolve(status === 200 ? json(history()) : json({ status }, status)) })
    expect(screen.queryByTestId('sales-revision-Rev.0')).toBeNull()
    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_A)
    await waitFor(() => expect(aReads).toBe(2))
    await screen.findByText(T.empty)
    expect(screen.queryByTestId('sales-revision-Rev.0')).toBeNull()
  })

  it('refreshes history when a recorded change advances the parent detail version', async () => {
    let version = '1'
    let count = 0
    installFetch({
      onDetail: () => json(detail(COMPANY_A, DRAFT, version)),
      onRevisions: () => {
        count += 1
        return json(version === '1' ? { items: [] } : history())
      },
    })
    const app = renderSalesApp(path)
    await screen.findByText(T.empty)
    version = '2'
    await act(async () => {
      await app.queryClient.invalidateQueries({ predicate: (query) => query.queryKey[2] === 'sales-draft' })
    })
    await screen.findByTestId('sales-revision-Rev.0')
    expect(count).toBe(2)
    expect(screen.getByTestId('sales-draft-version').textContent).toBe('2')
  })

  it('abandons an in-flight history on unmount and rereads on a later route entry', async () => {
    const pending = deferred<Response>()
    let count = 0
    const fetch = installFetch({
      onDetail: () => json(detail()), onList: () => json({ items: [], nextCursor: null }),
      onRevisions: () => ++count === 1 ? pending.promise : json({ items: [] }),
    })
    const app = renderSalesApp(path)
    await waitFor(() => expect(fetch.salesCalls().some((c) => historyRead(c.url))).toBe(true))
    await act(async () => { await app.router.navigate('/satis/siparisler') })
    await screen.findByText('Kayıt yok — sipariş taslakları')
    await act(async () => { pending.resolve(json(history())) })
    expect(screen.queryByTestId('sales-revision-history')).toBeNull()
    await act(async () => { await app.router.navigate(path) })
    await screen.findByText(T.empty)
    expect(count).toBe(2)
  })
})

describe('fetchDraftRevisions transport', () => {
  it('strictly decodes a response and sends only a company-bound read', async () => {
    const fetch = vi.fn<typeof globalThis.fetch>(async () => json(history()))
    vi.stubGlobal('fetch', fetch)
    expect(await fetchDraftRevisions(DRAFT, COMPANY_A)).toEqual({ type: 'ok-single', data: history() })
    expect(fetch.mock.calls[0][0]).toBe(`/api/v1/sales/drafts/${DRAFT}/revisions`)
    const init = fetch.mock.calls[0][1] as RequestInit
    expect(init.headers).toMatchObject({ 'X-Company-Id': COMPANY_A })
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBeUndefined()
    expect(init.body).toBeUndefined()
  })

  it('treats a malformed body and an aborted read differently', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ items: [], extra: true })))
    expect(await fetchDraftRevisions(DRAFT, COMPANY_A)).toEqual({ type: 'malformed' })
    const controller = new AbortController()
    controller.abort()
    vi.stubGlobal('fetch', vi.fn(async () => { throw new DOMException('Aborted', 'AbortError') }))
    expect(await fetchDraftRevisions(DRAFT, COMPANY_A, controller.signal)).toEqual({ type: 'fence-stale' })
  })
})
