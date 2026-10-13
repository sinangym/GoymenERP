import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { GoodsReceiptDetail } from './GoodsReceiptDetail'
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
const receipt = (patch: Partial<GoodsReceipt> = {}): GoodsReceipt => ({
  companyId: COMPANY, acceptanceId: ACCEPTANCE, purchaseOrderId: ORDER, purchaseOrderLineId: LINE, purchaseOrderVersion: '4',
  productId: PRODUCT, productVersion: '7', unitId: UNIT, acceptedQuantity: '2', remainingQuantity: '8', lotId: OTHER, labelId: LABEL,
  labelCode: LABEL_CODE, labelVersion: '1', movementId: OTHER, locationId: OTHER, measuredQuantityKg: '1934.125',
  initialBlock: 'Free', occurredAtUtc: '2026-09-25T10:00:00Z', ...patch,
})
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
  return <GoodsReceiptDetail binding={binding} detail={order} acceptanceId={acceptanceId} labelCode={LABEL_CODE} />
}
function mount(entry = ORDER_PATH, binding = 'actor-a:session:company') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const wrap = (node: React.ReactNode) => <QueryClientProvider client={client}>
    <MemoryRouter initialEntries={[entry]}>{node}<Address /></MemoryRouter>
  </QueryClientProvider>
  const result = render(wrap(view(binding)))
  return { rebind: (next: string) => result.rerender(wrap(view(next))) }
}
const toggle = () => screen.getByRole('button', { name: T.detailOf(LABEL_CODE) })
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
