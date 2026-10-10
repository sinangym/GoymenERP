/**
 * S-02 list and S-04 detail in the real shell, fed #257's own list/detail examples.
 * Component evidence, NOT_ENV.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import listResponseRaw from './examples/list-response.json?raw'
import detailResponseRaw from './examples/detail-response.json?raw'
import { SALES_CAPABILITY } from './capabilities'
import { CAPABILITY } from '../shell/capabilities'
import type { UseContactInformation } from './referenceSource'
import { ORDER_DETAIL_TEXT, ORDER_DRAFT_TEXT, ORDER_LIST_TEXT } from './salesText'
import { formatInstant } from './format'
import { DraftView } from './SalesDraftDetailPage'
import { decodeDraftDetailWithParty } from './contract'
import {
  ADDRESS,
  ADDRESS_TEXT,
  ALL_SALES,
  COMPANY_A,
  COMPANY_B,
  HARNESS_PARTY_DISPLAY,
  CONTACT,
  CURRENCY,
  controlledContactInformation,
  controlledSource,
  deferred,
  installFetch,
  json,
  renderSalesApp,
} from './testHarness'

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
    expect(within(row).getByTestId(`sales-row-commercial-stage-${DRAFT}`).textContent).toBe('Sipariş')
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
  it('renders the 360 summary as separate status facts and keeps long Cari, line due date and price in the normal layout flow', async () => {
    const longParty = {
      legalName: 'KARADENİZ ÇELİK DÖKÜM MAKİNA İMALAT SANAYİ VE DIŞ TİCARET ANONİM ŞİRKETİ',
      tradeName: 'Karadeniz Döküm Gebze Şubesi',
    }
    const rawDetail = JSON.parse(detailResponseRaw)
    const decoded = decodeDraftDetailWithParty({
      ...rawDetail,
      partyDisplay: { partyId: rawDetail.draft.partyId, code: HARNESS_PARTY_DISPLAY.code, ...longParty },
    }, DRAFT)
    expect(decoded).not.toBeNull()
    render(
      <DraftView
        detail={{ ...decoded!, partyDisplay: { ...decoded!.partyDisplay, ...longParty } }}
        canOpenParty={false}
        backLink={null}
      />,
    )

    expect(screen.getByTestId('sales-order-summary')).toBeTruthy()
    const statusFacts = screen.getByTestId('sales-order-status-facts')
    expect(within(statusFacts).getByTestId('sales-draft-lifecycle-state').textContent).toBe('Taslak')
    expect(within(statusFacts).getByTestId('sales-draft-approval-state').textContent).toBe('Hazırlanıyor')
    expect(within(statusFacts).getByTestId('sales-draft-revision').textContent).toBe('Rev.0')
    expect(within(statusFacts).getByTestId('sales-draft-version').textContent).toBe('9007199254740993')
    expect(within(statusFacts).getByTestId('sales-draft-party').textContent).toContain(longParty.legalName)

    const lineFacts = screen.getByTestId('sales-line-1-facts')
    const due = within(lineFacts).getByTestId('sales-line-1-delivery-date')
    const price = within(lineFacts).getByTestId('sales-line-1-price')
    expect(due.textContent).toBe('20.09.2026')
    expect(price.textContent).toBe(ORDER_DETAIL_TEXT.notPriced)
    expect(due.closest('[data-layout-field]')?.getAttribute('data-layout-field')).toBe('delivery-date')
    expect(price.closest('[data-layout-field]')?.getAttribute('data-layout-field')).toBe('price')
    expect(due.closest('[data-layout-field]')).not.toBe(price.closest('[data-layout-field]'))
  })

  it('keeps commercial revision and record version apart, and shows the 400 + 600 plan', async () => {
    installFetch({ onDetail: () => json(detailResponseRaw) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    expect(await screen.findByRole('heading', { name: 'SO-2026-0001' })).toBeTruthy()
    expect(screen.getByTestId('sales-draft-revision').textContent).toBe('Rev.0')
    expect(screen.getByTestId('sales-draft-version').textContent).toBe('9007199254740993')
    expect(screen.getByTestId('sales-draft-commercial-stage').textContent).toBe('Sipariş')

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

  it('a withheld price reads as withheld, never as unpriced or unknown', async () => {
    installFetch({
      onDetail: () =>
        json(
          tweakDetail((b) => {
            b.lines[0].priceDisclosure = 'Withheld'
            b.lines[0].unitPrice = null
            b.lines[0].priceUnitId = null
            b.lines[0].discountPercent = null
          }),
        ),
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const line = await screen.findByTestId('sales-line-1')
    expect(within(line).getByTestId('sales-line-1-price').textContent).toBe(ORDER_DETAIL_TEXT.priceWithheld)
    expect(within(line).getByTestId('sales-line-1-discount').textContent).toBe(ORDER_DETAIL_TEXT.priceWithheld)
    expect(within(line).getByTestId('sales-line-1-price').textContent).not.toBe(ORDER_DETAIL_TEXT.notPriced)
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

describe('SALES-A2a — the delivery block on the detail', () => {
  const D = ORDER_DETAIL_TEXT.delivery
  const path = `/satis/siparisler/${DRAFT}`
  const OTHER_CONTACT = 'bbbbbbbb-1111-4111-8111-000000000409'

  it('says nothing was chosen for the example, and shows the currency code from the currencies list', async () => {
    installFetch({ onDetail: () => json(detailResponseRaw) })
    renderSalesApp(path)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.getByTestId('sales-currency-code').textContent).toBe('TRY')
    expect(screen.getByTestId('sales-delivery-address-text').textContent).toBe(D.addressNotChosen)
    expect(screen.getByTestId('sales-delivery-contact-text').textContent).toBe(D.contactNotChosen)
    expect(screen.getByTestId('sales-delivery-none').textContent).toBe(D.notChosen)
    // The ids are still shown where they always were.
    expect(screen.getByTestId('sales-draft-deliveryAddress').textContent).toBe(ORDER_DETAIL_TEXT.unknown)
  })

  it('shows the owner’s current text for a stored reference, notes a moved version, and says when an id is no longer listed', async () => {
    installFetch({
      onDetail: () =>
        json(
          tweakDetail((b) => {
            b.deliveryAddress = { id: ADDRESS, expectedVersion: '3' }
            b.contact = { id: OTHER_CONTACT, expectedVersion: '1' }
          }),
        ),
    })
    renderSalesApp(path)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.getByTestId('sales-delivery-address-text').textContent).toBe(`${ADDRESS_TEXT} — ${D.versionChanged}`)
    expect(screen.getByTestId('sales-delivery-contact-text').textContent).toBe(`${OTHER_CONTACT} — ${D.notListed}`)
    expect(screen.queryByTestId('sales-delivery-none')).toBeNull()
    expect(screen.getByTestId('sales-draft-deliveryAddress').textContent).toBe(`${ADDRESS} (3)`)
  })

  it('a failing read names its reason beside the id and offers a retry; the draft stays shown', async () => {
    const retry = vi.fn()
    const failing: UseContactInformation = (companyId) =>
      companyId === '' ? { status: 'idle' } : { status: 'unavailable', reason: 'http-503', retry }
    installFetch({ onDetail: () => json(tweakDetail((b) => (b.contact = { id: CONTACT, expectedVersion: '2' }))) })
    renderSalesApp(path, controlledSource, { contactInformation: failing })
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    const contact = screen.getByTestId('sales-delivery-contact-text')
    expect(contact.textContent).toContain(`${CONTACT} — ${D.unreadable('http-503')}`)
    await userEvent.click(within(contact).getByRole('button', { name: ORDER_DRAFT_TEXT.references.retry }))
    expect(retry).toHaveBeenCalledTimes(1)
    expect(screen.getByTestId('sales-draft-version').textContent).toBe('9007199254740993')
  })

  it('without the create grant no reference read is asked; the ids are shown with the reason', async () => {
    const asked = vi.fn()
    const spying: UseContactInformation = (companyId, partyId, companyAccountId) => {
      if (companyId !== '') asked()
      return controlledContactInformation(companyId, partyId, companyAccountId)
    }
    installFetch({
      capabilities: [SALES_CAPABILITY.draftsList, SALES_CAPABILITY.draftsDetail],
      onDetail: () => json(tweakDetail((b) => (b.deliveryAddress = { id: ADDRESS, expectedVersion: '4' }))),
    })
    renderSalesApp(path, controlledSource, { contactInformation: spying })
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.getByTestId('sales-delivery-address-text').textContent).toBe(`${ADDRESS} — ${D.noGrant}`)
    expect(screen.getByTestId('sales-currency-code').textContent).toBe(`${CURRENCY} — ${D.noGrant}`)
    expect(asked).not.toHaveBeenCalled()
  })
})

/**
 * SALES-CARI-OPTIN (#626): the list and the detail read the record-bound Cari through the Sales opt-in contract and show
 * the code and name; the Cari card link follows the card's own capability. (The harness plays the #626 server.)
 */
describe('the record-bound Cari on the list and the detail', () => {
  const PARTY_TEXT = `${HARNESS_PARTY_DISPLAY.code} — ${HARNESS_PARTY_DISPLAY.legalName} (${HARNESS_PARTY_DISPLAY.tradeName})`
  const PARTY_ID = JSON.parse(listResponseRaw).items[0].partyId as string

  it('the list shows the Cari code and name as plain text for a reader without the Cari card capability', async () => {
    const fetch = installFetch({ onList: () => json(listResponseRaw) })
    renderSalesApp('/satis/siparisler')
    const table = await screen.findByRole('table', { name: ORDER_LIST_TEXT.caption })
    expect(within(table).getByRole('columnheader', { name: ORDER_LIST_TEXT.party })).toBeTruthy()
    const cell = within(table).getByTestId(`sales-row-party-${DRAFT}`)
    expect(cell.textContent).toBe(PARTY_TEXT)
    expect(within(cell).queryByRole('link')).toBeNull()
    const list = fetch.salesCalls().filter((call) => call.method === 'GET')
    expect(list.map((call) => call.readContract)).toEqual(['record-party-display-v1'])
    // The display comes with the Sales read; no Cari read is made for it.
    expect(fetch.calls.some((call) => /counterpart|\/cari/.test(call.url))).toBe(false)
  })

  it('the list links the Cari to its card only when the shell publishes the card capability', async () => {
    installFetch({ capabilities: [...ALL_SALES, CAPABILITY.cariPartiesDetail], onList: () => json(listResponseRaw) })
    renderSalesApp('/satis/siparisler')
    const cell = await screen.findByTestId(`sales-row-party-${DRAFT}`)
    expect(within(cell).getByRole('link', { name: PARTY_TEXT }).getAttribute('href')).toBe(`/cari/${PARTY_ID}`)
  })

  it('the detail shows the same Cari instead of the bare id, and links it only with the card capability', async () => {
    const fetch = installFetch({ onDetail: () => json(detailResponseRaw) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const party = await screen.findByTestId('sales-draft-party')
    expect(party.textContent).toBe(PARTY_TEXT)
    expect(within(party).queryByRole('link')).toBeNull()
    expect(screen.queryByText(JSON.parse(detailResponseRaw).draft.partyId)).toBeNull()
    const detailRead = fetch.salesCalls().find((call) => call.method === 'GET' && call.url.endsWith(`/sales/drafts/${DRAFT}`))
    expect(detailRead?.readContract).toBe('record-party-display-v1')
    // The other reads of the detail keep their own contracts.
    expect(fetch.salesCalls().filter((call) => call !== detailRead).every((call) => call.readContract === null)).toBe(true)
    cleanup()
    installFetch({ capabilities: [...ALL_SALES, CAPABILITY.cariPartiesDetail], onDetail: () => json(detailResponseRaw) })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    const linked = await screen.findByTestId('sales-draft-party')
    expect(within(linked).getByRole('link', { name: PARTY_TEXT }).getAttribute('href')).toBe(`/cari/${PARTY_ID}`)
  })

  it('a detail answer the server did not acknowledge is not shown as the legacy record', async () => {
    installFetch({ onDetail: () => json(detailResponseRaw) })
    // A server that ignores the selector: the legacy body, no acknowledgment.
    const fetchMock = vi.mocked(fetch)
    const original = fetchMock.getMockImplementation()!
    fetchMock.mockImplementation(async (input, init) => {
      const url = typeof input === 'string' ? input : input.toString()
      if (url.endsWith(`/sales/drafts/${DRAFT}`)) return json(detailResponseRaw)
      return original(input, init)
    })
    renderSalesApp(`/satis/siparisler/${DRAFT}`)
    await waitFor(() => expect(document.querySelector('[data-state="malformed-response"]')).not.toBeNull())
    expect(screen.queryByTestId('sales-draft-party')).toBeNull()
  })
})

/**
 * SALES-DS-01 (GOY-DS-001): the same S-02 list on the design system. The facts, their order, the server filter, the
 * cursor, the Cari link rule and the context fence stay as they were; only the layout is new — the shared DataTable on
 * a wide screen and one labelled card per row at the shell's phone width.
 */
describe('SALES-DS-01: the S-02 list on GOY-DS-001', () => {
  const LONG_PARTY = {
    code: 'CARI-İZM-0004567',
    legalName: 'KARADENİZ ÇELİK DÖKÜM MAKİNA İMALAT SANAYİ VE DIŞ TİCARET ANONİM ŞİRKETİ',
    tradeName: 'Karadeniz Döküm Gebze Şubesi',
  }
  const LONG_PARTY_TEXT = `${LONG_PARTY.code} — ${LONG_PARTY.legalName} (${LONG_PARTY.tradeName})`
  const PARTY_ID = JSON.parse(listResponseRaw).items[0].partyId as string
  const ids = (n: number) => `aaaaaaaa-aaaa-aaaa-aaaa-${String(n).padStart(12, '0')}`

  /** A row of the #257 example with the given id and changes; the long Cari is the server's record-bound display. */
  function draftRow(n: number, change: Record<string, unknown> = {}) {
    const example = JSON.parse(listResponseRaw).items[0]
    return {
      ...example,
      id: ids(n),
      documentNo: `SO-2026-${String(n).padStart(4, '0')}`,
      partyDisplay: { partyId: example.partyId, ...LONG_PARTY },
      ...change,
    }
  }

  /** Every lifecycle × approval pair the contract allows on a list row (a cancelled draft is an approved one). */
  const STATE_ROWS = [
    draftRow(1, { approvalState: 'Preparing', commercialStage: 'Quote', businessDate: '2026-10-01', revision: 'Rev.0', lineCount: 1 }),
    draftRow(2, { approvalState: 'PendingApproval', businessDate: '2026-10-02', revision: 'Rev.1', lineCount: 12 }),
    draftRow(3, { approvalState: 'Approved', businessDate: '2026-10-03', revision: 'Rev.2', lineCount: 3 }),
    draftRow(4, { approvalState: 'Rejected', businessDate: '2026-10-04' }),
    draftRow(5, { approvalState: 'Withdrawn', businessDate: '2026-10-05' }),
    draftRow(6, { state: 'Cancelled', approvalState: 'Approved', businessDate: '2026-10-06' }),
  ]
  const STATE_TEXTS = [
    ['Taslak · Hazırlanıyor', 'neutral'],
    ['Taslak · Onay bekliyor', 'warning'],
    ['Taslak · Onaylı', 'success'],
    ['Taslak · Reddedildi', 'danger'],
    ['Taslak · Geri çekildi', 'neutral'],
    ['İptal edildi · Onaylı', 'neutral'],
  ] as const

  /** A controllable viewport for the shell's phone width (`(max-width: 767px)`); `false` is an explicit desktop. */
  function stubViewport(phone: boolean) {
    const viewport = { phone }
    const listeners = new Set<() => void>()
    vi.stubGlobal('matchMedia', vi.fn((query: string) => ({
      get matches() { return viewport.phone },
      media: query, onchange: null,
      addEventListener: (_type: string, listener: () => void) => listeners.add(listener),
      removeEventListener: (_type: string, listener: () => void) => listeners.delete(listener),
      addListener: () => {}, removeListener: () => {}, dispatchEvent: () => false,
    })))
    return {
      set(next: boolean) {
        viewport.phone = next
        act(() => { for (const listener of [...listeners]) listener() })
      },
    }
  }

  const listReads = (fetch: ReturnType<typeof installFetch>) => fetch.salesCalls()
    .filter((call) => call.method === 'GET')
    .map((call) => {
      const params = new URL(call.url, 'http://x').searchParams
      return { companyId: call.companyId, cursor: params.get('cursor'), approvalState: params.get('approvalState') }
    })

  it('draws one semantic DataTable: the kept columns in their order, one row per draft, every fact as the server sent it', async () => {
    stubViewport(false)
    installFetch({ capabilities: [...ALL_SALES, CAPABILITY.cariPartiesDetail], onList: () => json({ items: STATE_ROWS, nextCursor: null }) })
    renderSalesApp('/satis/siparisler')
    const table = await screen.findByRole('table', { name: ORDER_LIST_TEXT.caption })

    expect(within(table).getAllByRole('columnheader').map((header) => [header.textContent, header.getAttribute('scope')])).toEqual([
      ORDER_LIST_TEXT.documentNo, ORDER_LIST_TEXT.party, ORDER_LIST_TEXT.businessDate, ORDER_LIST_TEXT.commercialStage,
      ORDER_LIST_TEXT.state, ORDER_LIST_TEXT.revision, ORDER_LIST_TEXT.lineCount, ORDER_LIST_TEXT.createdAt,
    ].map((text) => [text, 'col']))
    const body = within(table).getAllByRole('row').slice(1)
    expect(body.map((row) => row.getAttribute('data-draft-id'))).toEqual(STATE_ROWS.map((row) => row.id))
    expect(within(body[1]).getAllByRole('cell').map((cell) => cell.textContent)).toEqual([
      'SO-2026-0002', LONG_PARTY_TEXT, '02.10.2026', 'Sipariş', 'Taslak · Onay bekliyor', 'Rev.1', '12', formatInstant(STATE_ROWS[1].createdAtUtc),
    ])
    expect(within(body[0]).getByTestId(`sales-row-commercial-stage-${ids(1)}`).textContent).toBe('Teklif')
    // The whole Cari, never cut: the full text in the cell and as the link's name, with its card link.
    const party = within(body[0]).getByTestId(`sales-row-party-${ids(1)}`)
    expect(within(party).getByRole('link', { name: LONG_PARTY_TEXT }).getAttribute('href')).toBe(`/cari/${PARTY_ID}`)
    expect(party.closest('td')).not.toBeNull()
    // No amount, total, count or search was added.
    expect(within(table).queryByText(/Tutar|KDV|Toplam|Kur/)).toBeNull()
    expect(screen.queryByRole('searchbox')).toBeNull()
    expect(screen.getByText(ORDER_LIST_TEXT.end)).toBeTruthy()
  })

  it('shows the lifecycle beside the approval in each StatusIndicator; the dot is decorative and the text is exact', async () => {
    stubViewport(false)
    installFetch({ onList: () => json({ items: STATE_ROWS, nextCursor: null }) })
    renderSalesApp('/satis/siparisler')
    await screen.findByRole('table', { name: ORDER_LIST_TEXT.caption })

    for (const [index, row] of STATE_ROWS.entries()) {
      const text = screen.getByTestId(`sales-row-approval-${row.id}`)
      const indicator = text.parentElement!
      expect([text.textContent, indicator.getAttribute('data-tone')]).toEqual(STATE_TEXTS[index])
      expect(indicator.querySelector('[aria-hidden="true"]')?.textContent).toBe('')
      expect(indicator.getAttribute('role')).toBeNull()
      expect(indicator.getAttribute('aria-live')).toBeNull()
    }
  })

  it('keeps the create action a link and the next page a plain button: no nested interactive element, no form to submit', async () => {
    stubViewport(false)
    installFetch({ onList: () => json({ items: [draftRow(1)], nextCursor: 'cursor-2' }) })
    renderSalesApp('/satis/siparisler')
    await screen.findByRole('table', { name: ORDER_LIST_TEXT.caption })
    const page = screen.getByRole('region', { name: ORDER_LIST_TEXT.heading })

    const create = within(page).getByRole('link', { name: ORDER_LIST_TEXT.newDraft })
    expect(create.getAttribute('href')).toBe('/satis/siparisler/yeni')
    expect(create.querySelector('button, a, input, select')).toBeNull()
    expect(create.closest('button')).toBeNull()
    const more = within(page).getByRole('button', { name: ORDER_LIST_TEXT.loadMore })
    expect(more.getAttribute('type')).toBe('button')
    expect(more.querySelector('a, button')).toBeNull()
    expect(page.querySelector('form')).toBeNull()
    for (const link of within(page).getAllByRole('link')) expect(link.querySelector('a, button')).toBeNull()

    // The filter is one labelled Field control, reachable by keyboard after the create link.
    const filter = within(page).getByLabelText(ORDER_LIST_TEXT.filter)
    expect(filter.id).toBe('sales-approval-filter')
    expect(within(filter).getAllByRole('option').map((option) => option.textContent))
      .toEqual(['Tümü', 'Hazırlanıyor', 'Onay bekliyor', 'Onaylı', 'Reddedildi', 'Geri çekildi'])
    create.focus()
    await userEvent.tab()
    expect(document.activeElement).toBe(filter)
    await userEvent.tab()
    expect(document.activeElement).toBe(within(page).getByRole('link', { name: 'SO-2026-0001' }))
  })

  it('the filter is the server\'s: it restarts at the first page without a cursor and drops the rows of the old filter', async () => {
    stubViewport(false)
    const fetch = installFetch({
      onList: (url) => {
        const params = new URL(url, 'http://x').searchParams
        if (params.get('approvalState') === 'Approved') return json({ items: [draftRow(9, { approvalState: 'Approved' })], nextCursor: null })
        return params.get('cursor') === null
          ? json({ items: [draftRow(1)], nextCursor: 'cursor-2' })
          : json({ items: [draftRow(2)], nextCursor: null })
      },
    })
    renderSalesApp('/satis/siparisler')
    await userEvent.click(await screen.findByRole('button', { name: ORDER_LIST_TEXT.loadMore }))
    await screen.findByRole('link', { name: 'SO-2026-0002' })

    await userEvent.selectOptions(screen.getByLabelText(ORDER_LIST_TEXT.filter), 'Approved')
    await screen.findByRole('link', { name: 'SO-2026-0009' })
    expect(screen.queryByRole('link', { name: 'SO-2026-0001' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'SO-2026-0002' })).toBeNull()
    expect(screen.getByTestId(`sales-row-approval-${ids(9)}`).textContent).toBe('Taslak · Onaylı')
    expect(listReads(fetch)).toEqual([
      { companyId: COMPANY_A, cursor: null, approvalState: null },
      { companyId: COMPANY_A, cursor: 'cursor-2', approvalState: null },
      { companyId: COMPANY_A, cursor: null, approvalState: 'Approved' },
    ])
    expect(screen.getByText(ORDER_LIST_TEXT.end)).toBeTruthy()
  })

  it('on a phone each draft is one labelled card with the same facts; the long Cari is whole and there is no table', async () => {
    stubViewport(true)
    installFetch({ capabilities: [...ALL_SALES, CAPABILITY.cariPartiesDetail], onList: () => json({ items: STATE_ROWS, nextCursor: null }) })
    renderSalesApp('/satis/siparisler')
    const cards = await screen.findByRole('list', { name: ORDER_LIST_TEXT.caption })
    expect(screen.queryByRole('table')).toBeNull()

    const items = within(cards).getAllByRole('listitem')
    expect(items.map((item) => item.getAttribute('data-draft-id'))).toEqual(STATE_ROWS.map((row) => row.id))
    const second = items[1]
    expect(within(second).getByRole('link', { name: 'SO-2026-0002' }).getAttribute('href')).toBe(`/satis/siparisler/${ids(2)}`)
    expect(within(second).getByTestId(`sales-row-approval-${ids(2)}`).textContent).toBe('Taslak · Onay bekliyor')
    // Every value sits under its own label (dt → dd), in the table's order after the number and the state.
    const facts = within(second).getAllByRole('term').map((term) => [term.textContent, term.nextElementSibling?.textContent])
    expect(facts).toEqual([
      [ORDER_LIST_TEXT.party, LONG_PARTY_TEXT],
      [ORDER_LIST_TEXT.businessDate, '02.10.2026'],
      [ORDER_LIST_TEXT.commercialStage, 'Sipariş'],
      [ORDER_LIST_TEXT.revision, 'Rev.1'],
      [ORDER_LIST_TEXT.lineCount, '12'],
      [ORDER_LIST_TEXT.createdAt, formatInstant(STATE_ROWS[1].createdAtUtc)],
    ])
    const party = within(second).getByTestId(`sales-row-party-${ids(2)}`)
    expect(within(party).getByRole('link', { name: LONG_PARTY_TEXT }).getAttribute('href')).toBe(`/cari/${PARTY_ID}`)
    for (const [index, row] of STATE_ROWS.entries()) {
      expect(screen.getByTestId(`sales-row-approval-${row.id}`).textContent).toBe(STATE_TEXTS[index][0])
    }
  })

  it('crossing the phone width swaps the layout over the same rows without a new read, and a failed next page keeps the cards', async () => {
    const viewport = stubViewport(false)
    let reads = 0
    const fetch = installFetch({
      onList: (url) => {
        reads += 1
        if (!url.includes('cursor=')) return json({ items: [draftRow(1)], nextCursor: 'cursor-2' })
        return reads === 2 ? json('{}', 503) : json({ items: [draftRow(2)], nextCursor: null })
      },
    })
    renderSalesApp('/satis/siparisler')
    await screen.findByRole('table', { name: ORDER_LIST_TEXT.caption })
    viewport.set(true)
    expect(screen.queryByRole('table')).toBeNull()
    expect(screen.getByRole('list', { name: ORDER_LIST_TEXT.caption })).toBeTruthy()

    await userEvent.click(screen.getByRole('button', { name: ORDER_LIST_TEXT.loadMore }))
    expect(await screen.findByText(ORDER_LIST_TEXT.nextPageFailed)).toBeTruthy()
    expect(within(screen.getByRole('list', { name: ORDER_LIST_TEXT.caption })).getAllByRole('listitem')).toHaveLength(1)
    await userEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }))
    await waitFor(() => expect(within(screen.getByRole('list', { name: ORDER_LIST_TEXT.caption })).getAllByRole('listitem')).toHaveLength(2))

    viewport.set(false)
    const table = screen.getByRole('table', { name: ORDER_LIST_TEXT.caption })
    expect(within(table).getAllByRole('row').slice(1).map((row) => row.getAttribute('data-draft-id'))).toEqual([ids(1), ids(2)])
    expect(listReads(fetch).map((read) => read.cursor)).toEqual([null, 'cursor-2', 'cursor-2'])
  })

  it('a reader with only the list grant gets plain text on the table and the card: no detail, Cari or create link', async () => {
    for (const phone of [false, true]) {
      stubViewport(phone)
      installFetch({ capabilities: [SALES_CAPABILITY.draftsList], onList: () => json({ items: [draftRow(1)], nextCursor: null }) })
      renderSalesApp('/satis/siparisler')
      const party = await screen.findByTestId(`sales-row-party-${ids(1)}`)
      expect(party.textContent).toBe(LONG_PARTY_TEXT)
      const page = screen.getByRole('region', { name: ORDER_LIST_TEXT.heading })
      expect(within(page).queryAllByRole('link')).toEqual([])
      expect(within(page).getByText('SO-2026-0001')).toBeTruthy()
      cleanup()
      vi.unstubAllGlobals()
    }
  })

  it('a company switch starts the new company from its first page; the old company\'s rows and cursor never show', async () => {
    stubViewport(false)
    const fetch = installFetch({
      onList: (url, companyId) => {
        if (companyId === COMPANY_B) return json({ items: [draftRow(7, { companyId: COMPANY_B, documentNo: 'SO-B-0007' })], nextCursor: null })
        return url.includes('cursor=')
          ? json({ items: [draftRow(2)], nextCursor: null })
          : json({ items: [draftRow(1)], nextCursor: 'cursor-a' })
      },
    })
    renderSalesApp('/satis/siparisler')
    await userEvent.click(await screen.findByRole('button', { name: ORDER_LIST_TEXT.loadMore }))
    await screen.findByRole('link', { name: 'SO-2026-0002' })

    await userEvent.selectOptions(screen.getByLabelText('Şirket'), COMPANY_B)
    await screen.findByRole('link', { name: 'SO-B-0007' })
    expect(screen.queryByRole('link', { name: 'SO-2026-0001' })).toBeNull()
    expect(screen.queryByRole('link', { name: 'SO-2026-0002' })).toBeNull()
    expect(listReads(fetch).at(-1)).toEqual({ companyId: COMPANY_B, cursor: null, approvalState: null })
    expect(screen.getByText(ORDER_LIST_TEXT.end)).toBeTruthy()
  })
})
