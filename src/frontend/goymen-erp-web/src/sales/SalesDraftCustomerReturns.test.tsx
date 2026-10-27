/**
 * SAT-D8 / UI-70a on S-04: "Müşteri iadeleri" in the "Gerçekleşme / İrsaliye" tab shows per order line the APPROVED
 * customer-return total exactly as the server answers it from Logistics (API-10), in the line's order unit named by the
 * order itself (TEK-11). It reads afresh on every open, shows a distinct empty text only after the owner answered, and an
 * unreadable, forbidden or foreign answer shows no quantity at all — never a zero. Component evidence, NOT_ENV: the API
 * answers here are the harness's.
 */

import { afterEach, describe, expect, it, vi } from 'vitest'
import { cleanup, screen, waitFor, within } from '@testing-library/react'
import detailResponseRaw from './examples/detail-response.json?raw'
import { decodeCustomerReturns, decodeDraftDetail, isNonZeroDecimalText, salesDraftCustomerReturnsPath, type DraftDetail } from './contract'
import { ORDER_DETAIL_TEXT, ORDER_TABS_TEXT } from './salesText'
import { ALL_SALES, COMPANY_A, installFetch, json, renderSalesApp } from './testHarness'
import { SALES_CAPABILITY } from './capabilities'

const T = ORDER_DETAIL_TEXT.customerReturns
const DRAFT = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
const LINE_1 = '66666666-6666-6666-6666-666666666666'
const LINE_2 = '66666666-6666-4666-8666-666666666667'
const KG = '44444444-4444-4444-4444-444444444444'
const TON = '77777777-7777-4777-8777-777777777777'
const OTHER = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'
const PATH = `/satis/siparisler/${DRAFT}?sekme=gerceklesme`

type Body = Record<string, any>

const APPROVED = {
  submittedBy: 'satis-1',
  submittedAtUtc: '2026-09-19T09:00:00Z',
  decidedBy: 'onay-1',
  decidedAtUtc: '2026-09-19T09:05:00Z',
  rejectionReason: null,
}
const NOT_YET = { submittedBy: null, submittedAtUtc: null, decidedBy: null, decidedAtUtc: null, rejectionReason: null }

/** Two lines: the first names its unit ("KG"), the second's unit is not named by the order (shown as its stored id). */
function detail(state = 'Approved', revision = 'Rev.0', version = '4'): Body {
  const body = JSON.parse(detailResponseRaw) as Body
  body.draft = { ...body.draft, companyId: COMPANY_A, approvalState: state, revision, version, lineCount: 2 }
  body.approval = state === 'Approved' ? APPROVED : NOT_YET
  const base = body.lines[0]
  body.lines = [
    { ...base, orderUnitCode: 'KG' },
    {
      ...base, lineId: LINE_2, lineNo: 2, productCode: 'URUN-2', productName: 'İkinci ürün', orderUnitId: TON,
      quantity: '25.5', requestedDeliveryDate: null, requestedDeliverySlices: [],
    },
  ]
  return body
}

function returns(first = '0', second = '0', version = '4'): Body {
  return {
    draftVersion: version,
    lines: [
      { lineId: LINE_1, lineNo: 1, orderUnitId: KG, ordered: '1000', approvedReturned: first },
      { lineId: LINE_2, lineNo: 2, orderUnitId: TON, ordered: '25.5', approvedReturned: second },
    ],
  }
}

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

const returnCalls = <C extends { readonly url: string }>(calls: readonly C[]) => calls.filter((call) => call.url.endsWith('/customer-returns'))

describe('S-04 customer returns (SAT-D8 / UI-70a)', () => {
  it('shows per line the approved returned quantity with its unit, in the realization tab, through a company-bound read-only GET', async () => {
    const harness = installFetch({ onDetail: () => json(detail()), onCustomerReturns: () => json(returns('12.5', '3')) })
    renderSalesApp(PATH)

    const section = await screen.findByRole('region', { name: T.heading })
    await within(section).findByTestId('sales-customer-returns-line-1')
    // The panel lives in "Gerçekleşme / İrsaliye" — no separate returns menu or tab exists.
    expect(screen.getByTestId('sales-order-tabpanel-gerceklesme').contains(section)).toBe(true)
    expect(screen.getByRole('tab', { name: ORDER_TABS_TEXT.tab.gerceklesme }).getAttribute('aria-selected')).toBe('true')
    expect(screen.queryByRole('tab', { name: /iade/i })).toBeNull()

    expect(screen.getByTestId('sales-customer-returns-line-1-returned').textContent).toBe('12.5')
    expect(screen.getByTestId('sales-customer-returns-line-1-unit').textContent).toBe('KG')
    expect(screen.getByTestId('sales-customer-returns-line-2-returned').textContent).toBe('3')
    // A unit the order does not name stays its stored id — never a guessed code.
    expect(screen.getByTestId('sales-customer-returns-line-2-unit').textContent).toBe(TON)
    expect(within(section).getByText('URUN-2 — İkinci ürün')).toBeTruthy()

    const [read] = returnCalls(harness.calls)
    expect(returnCalls(harness.calls)).toHaveLength(1)
    expect(read).toMatchObject({ method: 'GET', companyId: COMPANY_A, idempotencyKey: null, body: null })
    expect(read.url).toContain(`/sales/drafts/${DRAFT}/customer-returns`)
  })

  it('is accessible: a labelled region, a captioned table with column and row headers', async () => {
    installFetch({ onDetail: () => json(detail()), onCustomerReturns: () => json(returns('1', '0')) })
    renderSalesApp(PATH)

    const section = await screen.findByRole('region', { name: T.heading })
    const table = await within(section).findByRole('table', { name: T.caption })
    const columns = within(table).getAllByRole('columnheader').map((cell) => cell.textContent)
    expect(columns).toEqual([T.lineNo, T.product, T.ordered, T.approvedReturned, T.unit])
    expect(within(table).getAllByRole('rowheader').map((cell) => cell.textContent)).toEqual(['1', '2'])
    expect(section.getAttribute('aria-busy')).toBe('false')
  })

  it('says there is no approved return only after the owner answered zeros for every line', async () => {
    installFetch({ onDetail: () => json(detail()), onCustomerReturns: () => json(returns('0', '0.000')) })
    renderSalesApp(PATH)

    const empty = await screen.findByTestId('sales-customer-returns-empty')
    expect(empty.textContent).toBe(T.empty)
    expect(screen.queryByTestId('sales-customer-returns-table')).toBeNull()
  })

  it('shows a polite loading status while the owner is being read, then the answer', async () => {
    let release: (response: Response) => void = () => undefined
    installFetch({
      onDetail: () => json(detail()),
      onCustomerReturns: () => new Promise<Response>((resolve) => { release = resolve }),
    })
    renderSalesApp(PATH)

    const section = await screen.findByRole('region', { name: T.heading })
    const status = await within(section).findByRole('status')
    expect(status.getAttribute('data-state')).toBe('initial-loading')
    expect(status.getAttribute('aria-live')).toBe('polite')
    expect(section.getAttribute('aria-busy')).toBe('true')
    expect(within(section).queryByTestId('sales-customer-returns-empty')).toBeNull()

    release(json(returns('7', '0')))
    await waitFor(() => expect(screen.getByTestId('sales-customer-returns-line-1-returned').textContent).toBe('7'))
    expect(within(section).queryByRole('status')).toBeNull()
  })

  it('shows no quantity and no "nothing returned" when the owner cannot answer (503), and retries the same read-only URL', async () => {
    let failing = true
    const harness = installFetch({
      onDetail: () => json(detail()),
      onCustomerReturns: () => (failing ? json({ status: 503 }, 503) : json(returns('2', '0'))),
    })
    renderSalesApp(PATH)

    const section = await screen.findByRole('region', { name: T.heading })
    const alert = await within(section).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('service-unavailable')
    expect(within(section).getByTestId('sales-customer-returns-unreadable').textContent).toBe(T.unreadable)
    expect(within(section).queryByTestId('sales-customer-returns-empty')).toBeNull()
    expect(within(section).queryByTestId('sales-customer-returns-table')).toBeNull()

    failing = false
    within(alert).getByRole('button').click()
    await waitFor(() => expect(screen.getByTestId('sales-customer-returns-line-1-returned').textContent).toBe('2'))
    const reads = returnCalls(harness.calls)
    expect(reads).toHaveLength(2)
    expect(new Set(reads.map((call) => `${call.method} ${call.url}`)).size).toBe(1)
  })

  it('fails closed on 404: no quantity, no empty text, no retry', async () => {
    installFetch({ onDetail: () => json(detail()), onCustomerReturns: () => json({ status: 404 }, 404) })
    renderSalesApp(PATH)

    const section = await screen.findByRole('region', { name: T.heading })
    const alert = await within(section).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('not-found')
    expect(within(alert).queryByRole('button')).toBeNull()
    expect(within(section).queryByTestId('sales-customer-returns-empty')).toBeNull()
    expect(within(section).queryByTestId('sales-customer-returns-table')).toBeNull()
  })

  it.each([401, 403])('routes a %i through the real auth boundary: the protected detail closes and no quantity is left', async (status) => {
    installFetch({ onDetail: () => json(detail()), onCustomerReturns: () => json({ status }, status) })
    renderSalesApp(PATH)

    expect(await screen.findByText(status === 401 ? 'Oturum sonlandı — oturum' : 'Bulunamadı — sayfa')).toBeTruthy()
    expect(screen.queryByTestId('sales-customer-returns')).toBeNull()
    expect(screen.queryByTestId('sales-draft-version')).toBeNull()
  })

  it('refuses an answer that does not describe exactly this detail and shows it as unexpected, never as numbers', async () => {
    installFetch({ onDetail: () => json(detail()), onCustomerReturns: () => json(returns('2', '0', '3')) })
    renderSalesApp(PATH)

    const section = await screen.findByRole('region', { name: T.heading })
    const alert = await within(section).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('malformed-response')
    expect(within(section).queryByTestId('sales-customer-returns-table')).toBeNull()
    expect(within(section).queryByTestId('sales-customer-returns-empty')).toBeNull()
  })

  it('reads afresh when the order is opened again, so a return approved meanwhile shows', async () => {
    let approved = '0'
    const harness = installFetch({ onDetail: () => json(detail()), onCustomerReturns: () => json(returns(approved, '0')) })
    renderSalesApp(PATH)
    await screen.findByTestId('sales-customer-returns-empty')

    cleanup()
    approved = '40'
    renderSalesApp(PATH)

    await waitFor(() => expect(screen.getByTestId('sales-customer-returns-line-1-returned').textContent).toBe('40'))
    expect(returnCalls(harness.calls)).toHaveLength(2)
  })

  it('is not read for a never-approved draft, and is read for a revised draft back in preparation', async () => {
    const preparing = installFetch({ onDetail: () => json(detail('Preparing')) })
    renderSalesApp(PATH)
    await screen.findByRole('heading', { name: 'SO-2026-0001' })
    expect(screen.queryByTestId('sales-customer-returns')).toBeNull()
    expect(returnCalls(preparing.calls)).toHaveLength(0)

    cleanup()
    vi.unstubAllGlobals()
    const revised = installFetch({ onDetail: () => json(detail('Preparing', 'Rev.1')), onCustomerReturns: () => json(returns('1', '0')) })
    renderSalesApp(PATH)
    await waitFor(() => expect(screen.getByTestId('sales-customer-returns-line-1-returned').textContent).toBe('1'))
    expect(returnCalls(revised.calls)).toHaveLength(1)
  })

  it('is not read without the S-04 detail capability (API-10 publishes none of its own)', async () => {
    const harness = installFetch({
      capabilities: ALL_SALES.filter((id) => id !== SALES_CAPABILITY.draftsDetail),
      onDetail: () => json(detail()),
    })
    renderSalesApp(PATH)
    await screen.findByRole('alert')
    expect(screen.queryByTestId('sales-customer-returns')).toBeNull()
    expect(returnCalls(harness.calls)).toHaveLength(0)
  })
})

// Read the canonical contract with Node's own fs (it lives outside the web package), as SalesDraftAllocationPanel.test.tsx
// does. Vitest runs from the web package.
const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const nodeFs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const nodePath = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }

interface Schema { readonly required?: readonly string[]; readonly properties?: Record<string, Record<string, unknown>> }
const openApi = () => JSON.parse(nodeFs.readFileSync(
  nodePath.resolve(nodeProcess.cwd(), '../../../contracts/openapi/goymen-erp-v1.json'), 'utf8')) as {
  readonly paths: Record<string, Record<string, { readonly operationId?: string; readonly responses?: Record<string, any> }>>
  readonly components: { readonly schemas: Record<string, Schema> }
}

describe('API-10 OpenAPI pin', () => {
  it('the decoder reads exactly the members the canonical contract declares, all required', () => {
    const document = openApi()
    const get = document.paths['/api/v1/sales/drafts/{id}/customer-returns']?.get
    expect(get?.operationId).toBe('GetSalesOrderDraftCustomerReturnsV1')
    expect(get?.responses?.['200']?.content?.['application/json']?.schema?.$ref)
      .toBe('#/components/schemas/SalesOrderDraftCustomerReturnsResponse')
    const response = document.components.schemas.SalesOrderDraftCustomerReturnsResponse
    const line = document.components.schemas.SalesDraftLineCustomerReturnV1
    expect([...(response.required ?? [])].sort()).toEqual(['draftVersion', 'lines'])
    expect(Object.keys(response.properties ?? {}).sort()).toEqual(['draftVersion', 'lines'])
    const lineKeys = ['approvedReturned', 'lineId', 'lineNo', 'orderUnitId', 'ordered']
    expect([...(line.required ?? [])].sort()).toEqual(lineKeys)
    expect(Object.keys(line.properties ?? {}).sort()).toEqual(lineKeys)
    expect(line.properties?.approvedReturned?.type).toBe('string')
    expect(line.properties?.ordered?.type).toBe('string')
    expect(line.properties?.lineId?.format).toBe('uuid')
    expect(line.properties?.orderUnitId?.format).toBe('uuid')
    expect(line.properties?.lineNo?.format).toBe('int32')
  })
})

describe('decodeCustomerReturns', () => {
  const draftDetail = decodeDraftDetail(detail(), DRAFT) as DraftDetail

  it('decodes the path and keeps decimal text verbatim, in the detail’s line order', () => {
    expect(salesDraftCustomerReturnsPath('abc')).toBe('/sales/drafts/abc/customer-returns')
    expect(draftDetail).not.toBeNull()
    const body = returns('12.500', '0')
    body.lines.reverse()
    body.lines[0].lineId = LINE_2.toUpperCase()
    expect(decodeCustomerReturns(body, draftDetail)).toEqual({
      draftVersion: '4',
      lines: [
        { lineId: LINE_1, lineNo: 1, orderUnitId: KG, ordered: '1000', approvedReturned: '12.500' },
        { lineId: LINE_2.toUpperCase(), lineNo: 2, orderUnitId: TON, ordered: '25.5', approvedReturned: '0' },
      ],
    })
  })

  it.each([
    ['a non-object body', () => null],
    ['an array body', () => []],
    ['an unknown root member', (body: Body) => { body.total = '1' }],
    ['a missing root member', (body: Body) => { delete body.draftVersion }],
    ['a numeric version', (body: Body) => { body.draftVersion = 4 }],
    ['another version', (body: Body) => { body.draftVersion = '5' }],
    ['lines that are not an array', (body: Body) => { body.lines = {} }],
    ['an unknown line member', (body: Body) => { body.lines[0].returnNo = 'IA-1' }],
    ['a missing line member', (body: Body) => { delete body.lines[0].approvedReturned }],
    ['a null quantity', (body: Body) => { body.lines[0].approvedReturned = null }],
    ['a number for a quantity', (body: Body) => { body.lines[0].approvedReturned = 4 }],
    ['a negative quantity', (body: Body) => { body.lines[0].approvedReturned = '-1' }],
    ['an exponent', (body: Body) => { body.lines[0].approvedReturned = '1e3' }],
    ['a padded quantity', (body: Body) => { body.lines[0].approvedReturned = ' 1' }],
    ['a comma decimal', (body: Body) => { body.lines[0].approvedReturned = '1,5' }],
    ['a zero ordered quantity', (body: Body) => { body.lines[0].ordered = '0' }],
    ['another ordered quantity', (body: Body) => { body.lines[0].ordered = '999' }],
    ['a string line number', (body: Body) => { body.lines[0].lineNo = '1' }],
    ['a zero line number', (body: Body) => { body.lines[0].lineNo = 0 }],
    ['a fractional line number', (body: Body) => { body.lines[0].lineNo = 1.5 }],
    ['another line number', (body: Body) => { body.lines[0].lineNo = 3 }],
    ['a malformed line id', (body: Body) => { body.lines[0].lineId = 'line-1' }],
    ['another line', (body: Body) => { body.lines[0].lineId = OTHER }],
    ['a repeated line', (body: Body) => { body.lines[1] = { ...body.lines[0] } }],
    ['another unit', (body: Body) => { body.lines[0].orderUnitId = TON }],
    ['a missing line', (body: Body) => { body.lines.pop() }],
    ['an extra line', (body: Body) => { body.lines.push({ ...body.lines[0], lineId: OTHER, lineNo: 3 }) }],
  ])('refuses %s', (_, mutate) => {
    const body = returns('2', '0')
    const replaced: unknown = mutate(body)
    expect(decodeCustomerReturns(replaced === undefined ? body : replaced, draftDetail)).toBeNull()
  })

  it('tells a returned quantity from a zero as text', () => {
    expect(['0', '0.000', '00'].map(isNonZeroDecimalText)).toEqual([false, false, false])
    expect(['0.001', '1', '10.0'].map(isNonZeroDecimalText)).toEqual([true, true, true])
  })
})
