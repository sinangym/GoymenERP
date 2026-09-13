/**
 * S-02 list and S-04 detail in the real shell, fed #257's own list/detail examples.
 * Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import listResponseRaw from './examples/list-response.json?raw'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SALES_CAPABILITY } from './capabilities'
import { ORDER_DETAIL_TEXT, ORDER_LIST_TEXT } from './salesText'
import { COMPANY_A, deferred, installFetch, json, renderSalesApp } from './testHarness'

const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'

afterEach(() => {
  cleanup()
  try {
    window.localStorage.clear()
  } catch {
    // no storage here
  }
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

function page(ids: readonly string[], nextCursor: string | null) {
  const example = JSON.parse(listResponseRaw)
  const row = example.items[0]
  return {
    items: ids.map((id, i) => ({ ...row, id, documentNo: `SO-2026-${String(i + 1).padStart(4, '0')}` })),
    nextCursor,
  }
}

const tweakDetail = (change: (body: any) => void) => {
  const body = JSON.parse(detailResponseRaw)
  change(body)
  return body
}

describe('S-02 list', () => {
  it('shows the example row, links it to the detail, and says where the list ends', async () => {
    const fetch = installFetch({ onList: () => json(listResponseRaw) })
    renderSalesApp('/satis/siparisler')
    const table = await screen.findByRole('table', { name: ORDER_LIST_TEXT.caption })
    const row = within(table).getAllByRole('row')[1]
    expect(within(row).getByRole('link', { name: 'SO-2026-0001' }).getAttribute('href')).toBe(`/satis/siparisler/${DRAFT}`)
    expect(within(row).getByText('13.09.2026')).toBeTruthy()
    expect(within(row).getByText('Rev.0')).toBeTruthy()
    expect(screen.getByText(ORDER_LIST_TEXT.end)).toBeTruthy()
    expect(fetch.salesCalls()[0].companyId).toBe(COMPANY_A)
  })

  it('an empty company is "no records", not an error', async () => {
    installFetch({ onList: () => json({ items: [], nextCursor: null }) })
    renderSalesApp('/satis/siparisler')
    expect(await screen.findByText('Kayıt yok — sipariş taslakları')).toBeTruthy()
  })

  it('a failed next page keeps the rows shown and retries the SAME cursor only when asked', async () => {
    const second = deferred<Response>()
    let listCalls = 0
    const fetch = installFetch({
      onList: (url) => {
        listCalls += 1
        if (!url.includes('cursor=')) return json(page(['aaaaaaaa-aaaa-aaaa-aaaa-000000000001'], 'cursor-2'))
        return listCalls === 2 ? json('{}', 503) : second.promise
      },
    })
    renderSalesApp('/satis/siparisler')
    await userEvent.click(await screen.findByRole('button', { name: ORDER_LIST_TEXT.loadMore }))

    expect(await screen.findByText(ORDER_LIST_TEXT.nextPageFailed)).toBeTruthy()
    expect(screen.getByRole('link', { name: 'SO-2026-0001' })).toBeTruthy()
    expect(fetch.salesCalls()).toHaveLength(2)

    await userEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    second.resolve(json(page(['aaaaaaaa-aaaa-aaaa-aaaa-000000000002'], null)))
    await waitFor(() => expect(within(screen.getByRole('table')).getAllByRole('link')).toHaveLength(2))
    const cursors = fetch.salesCalls().map((c) => new URL(c.url, 'http://x').searchParams.get('cursor'))
    expect(cursors).toEqual([null, 'cursor-2', 'cursor-2'])
    expect(screen.getByText(ORDER_LIST_TEXT.end)).toBeTruthy()
  })

  it('a row of another company is not shown: the page fails closed', async () => {
    installFetch({
      onList: () => json({ ...page([DRAFT], null), items: [{ ...page([DRAFT], null).items[0], companyId: '88888888-8888-8888-8888-888888888888' }] }),
    })
    renderSalesApp('/satis/siparisler')
    expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy()
    expect(screen.queryByRole('table')).toBeNull()
  })

  it('renders the number as plain text when the detail grant is absent', async () => {
    installFetch({ capabilities: [SALES_CAPABILITY.draftsList], onList: () => json(listResponseRaw) })
    renderSalesApp('/satis/siparisler')
    await screen.findByText('SO-2026-0001')
    expect(screen.queryByRole('link', { name: 'SO-2026-0001' })).toBeNull()
    expect(screen.queryByRole('link', { name: ORDER_LIST_TEXT.newDraft })).toBeNull()
  })
})

describe('S-04 detail', () => {
  it('keeps commercial revision and record version apart, and shows the 400 + 600 plan', async () => {
    installFetch({ onDetail: () => json(detailResponseRaw) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    expect(await screen.findByRole('heading', { name: 'SO-2026-0001' })).toBeTruthy()
    expect(screen.getByTestId('sales-draft-revision').textContent).toBe('Rev.0')
    expect(screen.getByTestId('sales-draft-version').textContent).toBe('9007199254740993')

    const line = screen.getByTestId('sales-line-1')
    const slices = within(line).getByRole('table')
    expect(within(slices).getAllByRole('row').map((r) => r.textContent)).toEqual([
      'TerminMiktar',
      '20.09.2026400',
      '22.09.2026600',
    ])
    expect(within(line).getByTestId('sales-line-1-unplanned').textContent).toBe(`${ORDER_DETAIL_TEXT.unplanned}: 0`)
    // Unknown is not zero, cash or none.
    expect(within(line).getByTestId('sales-line-1-price').textContent).toBe(ORDER_DETAIL_TEXT.notPriced)
    expect(screen.getByTestId('sales-draft-paymentTerms').textContent).toBe(ORDER_DETAIL_TEXT.unknown)
    expect(screen.getByTestId('sales-draft-header-due').textContent).toBe(ORDER_DETAIL_TEXT.unknown)
  })

  it('an unknown plan, an empty plan and a partial plan read differently', async () => {
    installFetch({
      onDetail: () =>
        json(
          tweakDetail((b) => {
            b.draft.lineCount = 3
            const base = b.lines[0]
            b.lines = [
              { ...base, requestedDeliverySlices: null },
              { ...base, lineId: '66666666-6666-6666-6666-666666666667', lineNo: 2, requestedDeliverySlices: [] },
              {
                ...base,
                lineId: '66666666-6666-6666-6666-666666666668',
                lineNo: 3,
                requestedDeliverySlices: [base.requestedDeliverySlices[0], { ...base.requestedDeliverySlices[0], sliceId: 'dddddddd-dddd-dddd-dddd-dddddddddddd' }],
              },
            ]
          }),
        ),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.getByTestId('sales-line-1-plan').textContent).toBe(ORDER_DETAIL_TEXT.planUnknown)
    expect(screen.getByTestId('sales-line-2-plan').textContent).toBe(ORDER_DETAIL_TEXT.planEmpty)
    expect(screen.getByTestId('sales-line-3-unplanned').textContent).toBe(`${ORDER_DETAIL_TEXT.unplanned}: 200`)
  })

  it('a 404 is the neutral not-found block, and a detail of another company fails closed', async () => {
    installFetch({ onDetail: () => json({ status: 404, title: 'Not Found' }, 404) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    expect(await screen.findByText('Bulunamadı — sipariş taslağı')).toBeTruthy()
    cleanup()
    vi.unstubAllGlobals()

    installFetch({ onDetail: () => json(tweakDetail((b) => (b.draft.companyId = '88888888-8888-8888-8888-888888888888'))) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    expect(await screen.findByText(/Beklenmeyen yanıt/)).toBeTruthy()
    expect(screen.queryByTestId('sales-draft-version')).toBeNull()
  })

  it('does not read `yeni` as a draft id', async () => {
    const fetch = installFetch({ capabilities: [SALES_CAPABILITY.draftsDetail] })
    renderSalesApp('/satis/siparisler/yeni')
    await waitFor(() => expect(screen.getByText('Bulunamadı — sayfa')).toBeTruthy())
    expect(fetch.salesCalls()).toHaveLength(0)
  })
})
