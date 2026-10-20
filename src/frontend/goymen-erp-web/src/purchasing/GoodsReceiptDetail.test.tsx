import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { GoodsReceiptDetail, GoodsReceiptDetailRow } from './GoodsReceiptDetail'
import { decodeGoodsReceiptDetail, type GoodsReceipt } from './goodsReceiptContract'
import { GOODS_RECEIPT_TEXT as T } from './goodsReceiptText'
import type { PurchaseOrderDetail } from './contract'
import { INVENTORY_STOCK_POSITIONS_CAPABILITY } from '../inventory/capabilities'

// Synthetic consumer tests, NOT_ENV: they prove neither real grants nor a real browser or API.
const boundary = vi.hoisted(() => ({ capture: vi.fn(() => ({ token: 1 })), report: vi.fn() }))
const shell = vi.hoisted(() => ({ capabilities: [] as string[] }))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))
vi.mock('../shell/shellState', async (original) => ({
  ...await original<typeof import('../shell/shellState')>(),
  hasCapability: (_state: unknown, id: string) => shell.capabilities.includes(id),
}))

const COMPANY = '11111111-1111-1111-1111-111111111111'
const ORDER = '22222222-2222-2222-2222-222222222222'
const LINE = '33333333-3333-3333-3333-333333333333'
const PRODUCT = '44444444-4444-4444-4444-444444444444'
const UNIT = '55555555-5555-5555-5555-555555555555'
const ACCEPTANCE = '66666666-6666-6666-6666-666666666666'
const OTHER = '77777777-7777-7777-7777-777777777777'
const LABEL = '88888888-8888-8888-8888-888888888888'
const LABEL_CODE = 'SENTETIK-ETIKET-2026-000123'
const NAME = T.detailOf('7', '2', '2026-09-25T10:00:00Z')
const ORDER_PATH = `/satinalma/siparisler/${ORDER}`
const DETAIL_URL = `/api/v1/purchasing/orders/${ORDER}/goods-receipts/${ACCEPTANCE}`

const order: PurchaseOrderDetail = {
  order: { id: ORDER, companyId: COMPANY, status: 'Approved', version: '4', supplierPartyId: PRODUCT, companyAccountId: UNIT,
    sourceRequestId: null, effectiveOn: '2026-09-25', createdAtUtc: '2026-09-25T09:00:00Z', updatedAtUtc: '2026-09-25T10:00:00Z',
    lineCount: 1 },
  lines: [{ lineId: LINE, lineNo: 7, kind: 'General', parentLineId: null, sourceRequestLineId: null, productId: PRODUCT,
    productVersion: '7', unitId: UNIT, ordered: '10', technical: '0', committed: '10', accepted: '2', remaining: '8' }],
  acceptances: [{ acceptanceId: ACCEPTANCE, lineId: LINE, quantity: '2', recordedAtUtc: '2026-09-25T10:00:00Z' }],
}
// P05.1D: synthetic ids of the stored quantity snapshot (`PurchaseGoodsReceiptQuantityV2`, #774); never printed.
const KG_UNIT = 'c5ef643e-a6e8-51f3-89f2-8e68b00cb983'
const PROFILE = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb'
type Stock = NonNullable<GoodsReceipt['stockItemQuantity']>
const stock = (patch: Record<string, unknown> = {}, base: Record<string, unknown> = {}): Stock => ({
  companyId: COMPANY, subject: { kind: 'Product', id: PRODUCT },
  source: { kind: 'purchasing.goods-acceptance', id: ACCEPTANCE, version: '4' },
  baseQuantity: { value: '1934.125', unitId: KG_UNIT, profileId: PROFILE, profileVersion: '3', dimension: 'Mass', scale: 3, ...base },
  alternateQuantity: null, ...patch,
} as unknown as Stock)
const receipt = (patch: Partial<GoodsReceipt> = {}): GoodsReceipt => ({
  companyId: COMPANY, acceptanceId: ACCEPTANCE, purchaseOrderId: ORDER, purchaseOrderLineId: LINE, purchaseOrderVersion: '4',
  productId: PRODUCT, productVersion: '7', unitId: UNIT, acceptedQuantity: '2', remainingQuantity: '8', lotId: OTHER, labelId: LABEL,
  labelCode: LABEL_CODE, labelVersion: '1', movementId: OTHER, locationId: OTHER, measuredQuantityKg: '1934.125',
  initialBlock: 'Free', occurredAtUtc: '2026-09-25T10:00:00Z', stockItemQuantity: stock(), ...patch,
})
const withoutStock = () => Object.fromEntries(Object.entries(receipt()).filter(([key]) => key !== 'stockItemQuantity'))
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

interface Call { readonly url: string; readonly method: string }
function serve(answer: (call: Call) => Response | Promise<Response>) {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    const call = { url, method: init?.method ?? 'GET' }
    calls.push(call)
    return answer(call)
  }))
  return calls
}

function Address() {
  const location = useLocation()
  return <output aria-label="Adres">{location.pathname + location.search}</output>
}
function view(binding: string, acceptanceId = ACCEPTANCE) {
  return <GoodsReceiptDetail binding={binding} detail={order} acceptanceId={acceptanceId} name={NAME} />
}
function mount(entry = ORDER_PATH, binding = 'actor-a:session:company') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const wrap = (node: React.ReactNode) => <QueryClientProvider client={client}>
    <MemoryRouter initialEntries={[entry]}>{node}<Address /></MemoryRouter>
  </QueryClientProvider>
  const result = render(wrap(view(binding)))
  return { rebind: (next: string) => result.rerender(wrap(view(next))) }
}
const toggle = () => screen.getByRole('button', { name: NAME })
const region = () => screen.getByRole('region', { name: T.detailHeading })
const fact = (name: string) => {
  const term = within(region()).getByText(name)
  return term.parentElement!.querySelector('dd')!.textContent
}

beforeEach(() => { shell.capabilities = []; boundary.report.mockClear() })
afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('decodeGoodsReceiptDetail (PurchaseGoodsReceiptDetailV1)', () => {
  it('accepts exactly { receipt } for the receipt that was asked for', () => {
    expect(decodeGoodsReceiptDetail({ receipt: receipt() }, order, ACCEPTANCE)).toEqual(receipt())
    expect(decodeGoodsReceiptDetail({ receipt: receipt({ initialBlock: 'Blocked' }) }, order, ACCEPTANCE)?.initialBlock).toBe('Blocked')
  })

  it.each([
    ['the command answer', { receipt: receipt(), isReplay: false, originalCorrelationId: 'c' }],
    ['a page', { items: [receipt()], searchWindowFull: false }],
    ['a bare receipt', receipt()],
    ['an extra envelope key', { receipt: receipt(), detail: null }],
    ['null', null],
    ['another receipt', { receipt: receipt({ acceptanceId: OTHER }) }],
    ['another company', { receipt: receipt({ companyId: OTHER }) }],
    ['another order', { receipt: receipt({ purchaseOrderId: OTHER }) }],
    ['a line the order does not have', { receipt: receipt({ purchaseOrderLineId: OTHER }) }],
    ['another product on the line', { receipt: receipt({ productId: OTHER }) }],
    ['another unit on the line', { receipt: receipt({ unitId: OTHER }) }],
    ['an unknown first state', { receipt: { ...receipt(), initialBlock: 'Released' } }],
    ['a missing field', { receipt: { ...receipt(), labelCode: undefined } }],
    ['an extra receipt field', { receipt: { ...receipt(), labelName: 'x' } }],
    ['a zero accepted quantity', { receipt: receipt({ acceptedQuantity: '0' }) }],
  ])('refuses %s', (_name, body) => {
    expect(decodeGoodsReceiptDetail(body, order, ACCEPTANCE)).toBeNull()
  })
})

/**
 * P05.1D (backend #774 6f6968d6): `stockItemQuantity` is required and nullable. Explicit null = legacy receipt; an object
 * must be exactly this receipt's stored snapshot. Synthetic NOT_ENV decoder tests — not SQL or browser evidence.
 */
describe('decodeGoodsReceiptDetail — P05.1D stored stock quantity', () => {
  const decode = (stockItemQuantity: unknown) => decodeGoodsReceiptDetail({ receipt: { ...receipt(), stockItemQuantity } }, order, ACCEPTANCE)

  it('keeps the validated snapshot as stored, and an explicit null as a legacy receipt', () => {
    expect(decode(stock())?.stockItemQuantity).toEqual(stock())
    expect(decode(null)?.stockItemQuantity).toBeNull()
    expect(decode(null)).not.toBeNull()
  })

  it('accepts the int32 scale also as its canonical string, and keeps it a number', () => {
    expect(decode(stock({}, { scale: '3' }))?.stockItemQuantity?.baseQuantity.scale).toBe(3)
    expect(decode(stock({}, { scale: 0 }))?.stockItemQuantity?.baseQuantity.scale).toBe(0)
  })

  it('refuses a receipt without the key — it is not a legacy receipt', () => {
    expect(decodeGoodsReceiptDetail({ receipt: withoutStock() }, order, ACCEPTANCE)).toBeNull()
  })

  it.each([
    ['zero instead of null', 0],
    ['an empty object', {}],
    ['a string', 'legacy'],
    ['another company', stock({ companyId: OTHER })],
    ['a subject that is not a Product', stock({ subject: { kind: 'Service', id: PRODUCT } })],
    ['another product', stock({ subject: { kind: 'Product', id: OTHER } })],
    ['an extra subject key', stock({ subject: { kind: 'Product', id: PRODUCT, version: '7' } })],
    ['another source kind', stock({ source: { kind: 'inventory.opening-count', id: ACCEPTANCE, version: '4' } })],
    ['another acceptance as source', stock({ source: { kind: 'purchasing.goods-acceptance', id: OTHER, version: '4' } })],
    ['the pre-receipt order version', stock({ source: { kind: 'purchasing.goods-acceptance', id: ACCEPTANCE, version: '3' } })],
    ['a non-canonical source version', stock({ source: { kind: 'purchasing.goods-acceptance', id: ACCEPTANCE, version: '04' } })],
    ['a value equal as a number but not as text', stock({}, { value: '1934.1250' })],
    ['a rounded value', stock({}, { value: '1934.13' })],
    ['a numeric value', stock({}, { value: 1934.125 })],
    ['a unit that is not a GUID', stock({}, { unitId: 'kg' })],
    ['an empty profile id', stock({}, { profileId: '00000000-0000-0000-0000-000000000000' })],
    ['profile version 0', stock({}, { profileVersion: '0' })],
    ['a non-canonical profile version', stock({}, { profileVersion: '03' })],
    ['a numeric profile version', stock({}, { profileVersion: 3 })],
    ['another dimension', stock({}, { dimension: 'Volume' })],
    ['scale 4', stock({}, { scale: 4 })],
    ['a negative scale', stock({}, { scale: -1 })],
    ['a fractional scale', stock({}, { scale: 1.5 })],
    ['a non-canonical scale string', stock({}, { scale: '03' })],
    ['an extra quantity key', stock({}, { nominal: '2000' })],
    ['a missing quantity key', { ...stock(), baseQuantity: { value: '1934.125', unitId: KG_UNIT, profileId: PROFILE, profileVersion: '3', dimension: 'Mass' } }],
    ['a converted alternate quantity', stock({ alternateQuantity: { quantity: stock().baseQuantity, conversionSource: stock().source } })],
    ['a missing alternateQuantity key', Object.fromEntries(Object.entries(stock()).filter(([key]) => key !== 'alternateQuantity'))],
    ['an extra snapshot key', stock({ currentProfileVersion: '4' })],
  ])('refuses %s', (_name, value) => {
    expect(decode(value)).toBeNull()
  })
})

describe('GoodsReceiptDetail — P05.1D "Stok miktarı" from the stored snapshot', () => {
  it('shows the stored stock quantity apart from the measured and order-unit facts, with its source and profile version', async () => {
    const calls = serve(() => json({ receipt: receipt() }))
    mount()
    await userEvent.click(toggle())
    await waitFor(() => expect(fact(T.detailLabel)).toBe(LABEL_CODE))
    const shown = within(region()).getByText(T.stockHeading).parentElement!
    expect(shown.getAttribute('data-stock-quantity')).toBe('snapshot')
    const text = shown.querySelector('dd')!.textContent
    expect(text).toContain(T.stockValue('1934.125'))
    expect(text).toContain(T.stockNote)
    expect(text).toContain(T.stockSource('4'))
    expect(text).toContain(T.stockProfile('3'))
    expect(text).toContain(T.stockScale(3))
    expect(text).toContain(T.stockAlternate)
    // The other facts keep their own meaning.
    expect(fact(T.detailMeasured)).toBe('1934.125 kg')
    expect(fact(T.detailAccepted)).toBe('2')
    // Only the receipt's own detail is read: no Product or Settings master is reread to describe the snapshot.
    expect(calls).toEqual([{ url: DETAIL_URL, method: 'GET' }])
    for (const id of [PROFILE, KG_UNIT, ACCEPTANCE, PRODUCT, COMPANY]) expect(region().textContent).not.toContain(id)
  })

  it('says a legacy receipt has no stored stock quantity instead of showing zero', async () => {
    serve(() => json({ receipt: receipt({ stockItemQuantity: null }) }))
    mount()
    await userEvent.click(toggle())
    await waitFor(() => expect(fact(T.detailLabel)).toBe(LABEL_CODE))
    const shown = within(region()).getByText(T.stockHeading).parentElement!
    expect(shown.getAttribute('data-stock-quantity')).toBe('legacy')
    expect(shown.querySelector('dd')!.textContent).toBe(T.stockLegacy)
    expect(region().textContent).not.toContain('0 kg')
    expect(region().textContent).not.toContain(T.stockProfile('3'))
  })

  it.each([
    ['a missing key', () => withoutStock()],
    ['a snapshot of another company', () => receipt({ stockItemQuantity: stock({ companyId: OTHER }) })],
    ['a snapshot of another product', () => receipt({ stockItemQuantity: stock({ subject: { kind: 'Product', id: OTHER } }) })],
    ['a snapshot of another version', () => receipt({ stockItemQuantity: stock({ source: { kind: 'purchasing.goods-acceptance', id: ACCEPTANCE, version: '5' } }) })],
    ['a snapshot with another quantity', () => receipt({ stockItemQuantity: stock({}, { value: '1934' }) })],
  ])('treats a 200 with %s as malformed: no receipt facts and no stock quantity', async (_name, body) => {
    serve(() => json({ receipt: body() }))
    mount()
    await userEvent.click(toggle())
    const alert = await within(await screen.findByRole('region', { name: T.detailHeading })).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('malformed-response')
    expect(region().textContent).not.toContain(LABEL_CODE)
    expect(within(region()).queryByText(T.stockHeading)).toBeNull()
  })

  it('reopens from the address with the same stored snapshot, read again from the receipt detail only', async () => {
    const calls = serve(() => json({ receipt: receipt() }))
    mount(`${ORDER_PATH}?kabul=${ACCEPTANCE}`)
    await waitFor(() => expect(fact(T.detailLabel)).toBe(LABEL_CODE))
    expect(within(region()).getByText(T.stockHeading).parentElement!.querySelector('dd')!.textContent)
      .toContain(T.stockValue('1934.125'))
    expect(calls).toEqual([{ url: DETAIL_URL, method: 'GET' }])
  })
})

describe('GoodsReceiptDetail — "Ayrıntı" on a recorded receipt', () => {
  it('reads nothing until asked, then one query-less GET for the same order and receipt', async () => {
    const calls = serve(() => json({ receipt: receipt() }))
    mount()
    expect(toggle().getAttribute('aria-expanded')).toBe('false')
    expect(toggle().textContent).toBe(T.detail)
    expect(screen.queryByRole('region', { name: T.detailHeading })).toBeNull()
    expect(calls).toEqual([])

    await userEvent.click(toggle())
    await waitFor(() => expect(fact(T.detailLabel)).toBe(LABEL_CODE))
    expect(calls).toEqual([{ url: DETAIL_URL, method: 'GET' }])
    expect(toggle().getAttribute('aria-expanded')).toBe('true')
    expect(toggle().getAttribute('aria-controls')).toBe(region().parentElement!.id)
    expect(fact(T.detailLine)).toBe('7')
    expect(fact(T.detailAccepted)).toBe('2')
    expect(fact(T.detailRemaining)).toBe('8')
    // The measured kilograms are shown as answered: no rounding, and the unit stays with the number.
    expect(fact(T.detailMeasured)).toBe('1934.125 kg')
    expect(fact(T.detailInitial)).toBe(T.detailFree)
    expect(within(region()).getByText(T.detailOccurred).parentElement!.querySelector('time')!.getAttribute('dateTime'))
      .toBe('2026-09-25T10:00:00Z')
    // No internal identifier is printed.
    for (const id of [ACCEPTANCE, LABEL, OTHER, LINE, PRODUCT, UNIT]) expect(region().textContent).not.toContain(id)
  })

  it('keeps a historical Blocked receipt Blocked', async () => {
    serve(() => json({ receipt: receipt({ initialBlock: 'Blocked' }) }))
    mount()
    await userEvent.click(toggle())
    await waitFor(() => expect(fact(T.detailInitial)).toBe(T.detailBlocked))
  })

  it('opens and closes from the keyboard and leaves the focus on its button', async () => {
    serve(() => json({ receipt: receipt() }))
    mount()
    toggle().focus()
    await userEvent.keyboard('{Enter}')
    await screen.findByRole('region', { name: T.detailHeading })
    await userEvent.keyboard(' ')
    expect(screen.queryByRole('region', { name: T.detailHeading })).toBeNull()
    expect(toggle().getAttribute('aria-expanded')).toBe('false')
    expect(document.activeElement).toBe(toggle())
  })

  it('offers the label card only to a caller Inventory already lets in', async () => {
    serve(() => json({ receipt: receipt() }))
    mount()
    await userEvent.click(toggle())
    await waitFor(() => expect(fact(T.detailLabel)).toBe(LABEL_CODE))
    expect(within(region()).queryByRole('link')).toBeNull()
    cleanup()

    shell.capabilities = [INVENTORY_STOCK_POSITIONS_CAPABILITY]
    mount()
    await userEvent.click(toggle())
    const link = await within(await screen.findByRole('region', { name: T.detailHeading }))
      .findByRole('link', { name: T.detailLabelLink(LABEL_CODE) })
    expect(link.getAttribute('href')).toBe(`/stok/etiket/${LABEL}`)
    expect(link.textContent).toBe(LABEL_CODE)
  })

  it.each([
    [404, 'not-found', 'Bulunamadı', false],
    [403, 'forbidden', 'Yetkiniz yok', false],
    [503, 'service-unavailable', 'Servis geçici olarak kullanılamıyor', true],
  ] as const)('says a %i apart from the others', async (status, kind, title, retryable) => {
    // The 404 carries no error code; none is expected.
    serve(() => json(status === 404 ? { status: 404 } : { status, code: 'x' }, status))
    mount()
    await userEvent.click(toggle())
    const alert = await within(await screen.findByRole('region', { name: T.detailHeading })).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe(kind)
    expect(alert.textContent).toContain(title)
    expect(within(alert).queryByRole('button', { name: 'Tekrar dene' }) !== null).toBe(retryable)
    expect(within(region()).queryByText(LABEL_CODE)).toBeNull()
    expect(boundary.report).toHaveBeenCalledWith(status, { token: 1 })
  })

  it('refuses a 200 that is not this receipt and retries only the same GET', async () => {
    let body: unknown = { receipt: receipt({ acceptanceId: OTHER }) }
    const calls = serve(() => json(body))
    mount()
    await userEvent.click(toggle())
    const alert = await within(await screen.findByRole('region', { name: T.detailHeading })).findByRole('alert')
    expect(alert.getAttribute('data-state')).toBe('malformed-response')
    expect(region().textContent).not.toContain(LABEL_CODE)

    body = { receipt: receipt() }
    await userEvent.click(within(alert).getByRole('button', { name: 'Tekrar dene' }))
    await waitFor(() => expect(fact(T.detailLabel)).toBe(LABEL_CODE))
    expect(calls).toEqual([{ url: DETAIL_URL, method: 'GET' }, { url: DETAIL_URL, method: 'GET' }])
  })

  it('shows a loading state, and never the answer of another actor or company', async () => {
    const answers: ((response: Response) => void)[] = []
    const calls = serve(() => new Promise<Response>((resolve) => { answers.push(resolve) }))
    const mounted = mount()
    await userEvent.click(toggle())
    expect((await within(region()).findByRole('status')).getAttribute('data-state')).toBe('initial-loading')

    mounted.rebind('actor-b:session:company')
    await waitFor(() => expect(calls).toHaveLength(2))
    // The first actor's answer arrives late: it is not shown under the second actor.
    answers[0](json({ receipt: receipt({ labelCode: 'ESKI-AKTOR-ETIKETI' }) }))
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(region().textContent).not.toContain('ESKI-AKTOR-ETIKETI')
    expect(within(region()).getByRole('status').getAttribute('data-state')).toBe('initial-loading')

    answers[1](json({ receipt: receipt() }))
    await waitFor(() => expect(fact(T.detailLabel)).toBe(LABEL_CODE))
    expect(region().textContent).not.toContain('ESKI-AKTOR-ETIKETI')
  })

  it('keeps the open receipt in the address, so Back from the label reopens it', async () => {
    const calls = serve(() => json({ receipt: receipt() }))
    mount(`${ORDER_PATH}?sekme=1`)
    await userEvent.click(toggle())
    expect(screen.getByRole('status', { name: 'Adres' }).textContent).toBe(`${ORDER_PATH}?sekme=1&kabul=${ACCEPTANCE}`)
    await userEvent.click(toggle())
    expect(screen.getByRole('status', { name: 'Adres' }).textContent).toBe(`${ORDER_PATH}?sekme=1`)
    cleanup()

    calls.length = 0
    mount(`${ORDER_PATH}?kabul=${ACCEPTANCE}`)
    await waitFor(() => expect(fact(T.detailLabel)).toBe(LABEL_CODE))
    expect(calls).toEqual([{ url: DETAIL_URL, method: 'GET' }])
  })

  it('as a table row it is one full-width cell under its acceptance, with the same control inside', async () => {
    const calls = serve(() => json({ receipt: receipt() }))
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[ORDER_PATH]}>
      <table><tbody>
        <GoodsReceiptDetailRow colSpan={4} binding="actor-a:session:company" detail={order} acceptanceId={ACCEPTANCE} name={NAME} />
      </tbody></table>
    </MemoryRouter></QueryClientProvider>)
    const row = screen.getByTestId(`purchase-order-acceptance-detail-${ACCEPTANCE}`)
    expect(row.tagName).toBe('TR')
    expect(within(row).getAllByRole('cell')).toHaveLength(1)
    expect(within(row).getByRole('cell').getAttribute('colspan')).toBe('4')
    expect(calls).toEqual([])
    await userEvent.click(within(row).getByRole('button', { name: NAME }))
    await waitFor(() => expect(fact(T.detailLabel)).toBe(LABEL_CODE))
    expect(row.contains(region())).toBe(true)
    expect(calls).toEqual([{ url: DETAIL_URL, method: 'GET' }])
  })

  it('does not open, or read, for another receipt named in the address', async () => {
    const calls = serve(() => json({ receipt: receipt() }))
    mount(`${ORDER_PATH}?kabul=${OTHER}`)
    await new Promise((resolve) => setTimeout(resolve, 20))
    expect(toggle().getAttribute('aria-expanded')).toBe('false')
    expect(calls).toEqual([])
  })
})

describe('goodsReceiptDetail.module.css', () => {
  it('cuts no text and keeps a quantity whole', () => {
    const node = (globalThis as unknown as { process: { getBuiltinModule(name: string): unknown; cwd(): string } }).process
    const fs = node.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: string): string }
    const path = node.getBuiltinModule('node:path') as { resolve(...parts: string[]): string }
    const css = fs.readFileSync(path.resolve(node.cwd(), 'src/purchasing/goodsReceiptDetail.module.css'), 'utf8')
    expect(css).not.toMatch(/text-overflow|line-clamp|text-transform|overflow:\s*hidden/)
    expect(css).toMatch(/\.facts \.quantity \{[^}]*white-space: nowrap/)
    expect(css).toMatch(/\.facts dd \{[^}]*overflow-wrap: anywhere/)
  })
})
