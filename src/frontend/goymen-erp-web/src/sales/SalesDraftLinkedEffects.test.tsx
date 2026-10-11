/**
 * S-04 "Bağlı etkiler": the active allocations per line from Inventory's server truth and the loading facts per
 * line from the Sales loading-progress read (#684); no number is shown unless a read succeeded and described
 * exactly this order. Component evidence, NOT_ENV: the API answers here are the test's.
 */

import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { SalesDraftLinkedEffects, SalesDraftLinkedEffectsUnbound } from './SalesDraftLinkedEffects'
import type { DraftLine } from './contract'
import { ORDER_DETAIL_TEXT } from './salesText'

const T = ORDER_DETAIL_TEXT.approval.linkedEffects

const COMPANY = '10000000-0000-4000-8000-000000000001'
const COMPANY_B = '10000000-0000-4000-8000-000000000002'
const ORDER = '20000000-0000-4000-8000-000000000001'
const ORDER_B = '20000000-0000-4000-8000-000000000002'
const LINE = '30000000-0000-4000-8000-000000000001'
const LINE_2 = '30000000-0000-4000-8000-000000000002'
const PRODUCT = '40000000-0000-4000-8000-000000000001'
const LABEL = '50000000-0000-4000-8000-000000000001'

const report = vi.fn()
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report }),
}))

const line = (lineId: string, lineNo: number): DraftLine => ({
  lineId, lineNo, productId: PRODUCT, productVersion: '7', productCode: 'P-1', productName: 'Rulo',
  orderUnitId: '70000000-0000-4000-8000-000000000001', quantity: '10', unitPrice: null,
  priceUnitId: null, requestedDeliveryDate: null, note: null, requestedDeliverySlices: null,
  discountPercent: null, priceDisclosure: 'Visible',
})

let sequence = 0
function allocation(status: 'Active' | 'Released', companyId = COMPANY, orderId = ORDER, lineId = LINE) {
  sequence += 1
  return {
    allocation: {
      allocationId: `60000000-0000-4000-8000-${String(sequence).padStart(12, '0')}`,
      companyId, version: '1', status, labelId: LABEL, productId: PRODUCT,
      demandKind: 'sales.order-line', demandId: orderId, demandLineId: lineId, demandProductVersion: '7',
      allocatedKg: '5', remainingKg: status === 'Active' ? '5' : '0', reason: 'Sipariş için ayırıldı.',
      reservedUntilUtc: null, createdAtUtc: '2026-09-24T10:00:00Z', updatedAtUtc: '2026-09-24T10:00:00Z',
    },
    labelVersion: '12',
  }
}

function answer(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

interface Call { readonly url: string; readonly companyId: string | null; readonly method: string; readonly headers: Headers }

const VERSION = '9007199254740993'
const UNIT = '70000000-0000-4000-8000-000000000001'
const isLoadingRead = (url: string) => url.endsWith('/loading-progress')

/** One line of a loading-progress answer; by default the owner's real zeros for a line of 10. */
function progressLine(lineId: string, lineNo: number, over: Record<string, unknown> = {}) {
  return {
    lineId, lineNo, orderUnitId: UNIT, ordered: '10', loadingInstructionLines: 0, instructed: '0', loaded: '0',
    shipped: '0', delivered: '0', ...over,
  }
}
const progress = (lines: readonly unknown[], draftVersion = VERSION) => answer({ draftVersion, lines })

/**
 * `handler` answers the allocation reads, as before. The loading read is answered by `loading` (default: no
 * instruction on line 1) and recorded apart, so the allocation assertions keep counting only their own requests.
 */
function stubFetch(
  handler: (url: string, call: Call) => Response | Promise<Response>,
  loading: (url: string, call: Call) => Response | Promise<Response> = () => progress([progressLine(LINE, 1)]),
) {
  const calls: Call[] = []
  const loadingCalls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    const headers = new Headers(init?.headers)
    const call = { url, companyId: headers.get('X-Company-Id'), method: (init?.method ?? 'GET').toUpperCase(), headers }
    if (isLoadingRead(url)) {
      loadingCalls.push(call)
      return loading(url, call)
    }
    calls.push(call)
    return handler(url, call)
  }))
  return Object.assign(calls, { loading: loadingCalls })
}

function view(props: {
  readonly allocationReadable?: boolean
  readonly companyId?: string
  readonly namespace?: string
  readonly salesOrderId?: string
  readonly draftVersion?: string
  readonly routeEntryGeneration?: number
  readonly lines?: readonly DraftLine[]
}) {
  const companyId = props.companyId ?? COMPANY
  const namespace = props.namespace ?? `ctx-${companyId}`
  const salesOrderId = props.salesOrderId ?? ORDER
  const generation = props.routeEntryGeneration ?? 1
  return (
    <SalesDraftLinkedEffects
      key={`effects:${namespace}:${generation}:${salesOrderId}`}
      allocationReadable={props.allocationReadable ?? true}
      companyId={companyId}
      namespace={namespace}
      routeEntryGeneration={generation}
      salesOrderId={salesOrderId}
      draftVersion={props.draftVersion ?? VERSION}
      lines={props.lines ?? [line(LINE, 1)]}
    />
  )
}

function mount(props: Parameters<typeof view>[0] = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const rendered = render(<QueryClientProvider client={client}>{view(props)}</QueryClientProvider>)
  return {
    client,
    ...rendered,
    update: (next: Parameters<typeof view>[0]) => rendered.rerender(<QueryClientProvider client={client}>{view(next)}</QueryClientProvider>),
  }
}

const deferred = <T,>() => {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

const allocationLine = (lineId = LINE) => screen.getByTestId(`sales-linked-allocation-line-${lineId}`)
const loadingBlock = () => screen.getByTestId('sales-linked-loading-progress')
const loadingState = () => loadingBlock().dataset.state
const loadingLine = (lineId = LINE) => screen.getByTestId(`sales-linked-loading-line-${lineId}`)
const facts = (lineId = LINE) => Object.fromEntries(
  [...loadingLine(lineId).querySelectorAll<HTMLElement>('[data-fact]')].map((cell) => [cell.dataset.fact, cell.textContent]))

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.clearAllMocks()
})

describe('S-04 linked effects', () => {
  it('without the allocation capability asks Inventory nothing and shows no allocation number; loading is still read', async () => {
    const calls = stubFetch(() => { throw new Error('no allocation read expected') })
    mount({ allocationReadable: false })
    expect(screen.getByTestId('sales-linked-allocation').textContent).toBe(T.allocationNoAccess)
    expect(screen.getByTestId('sales-linked-allocation').textContent).not.toMatch(/\d/)
    await waitFor(() => expect(loadingState()).toBe('ready'))
    expect(calls).toHaveLength(0)
    expect(calls.loading).toHaveLength(1)
  })

  it('the unbound fallback reads nothing and shows no number', () => {
    const calls = stubFetch(() => { throw new Error('no read expected') })
    render(<SalesDraftLinkedEffectsUnbound />)
    expect(screen.getByTestId('sales-linked-allocation').textContent).toBe(T.unavailable)
    expect(screen.getByTestId('sales-linked-loading').textContent).toBe(T.loadingNotReadable)
    expect(screen.getByTestId('sales-approval-linked-effects').textContent).not.toMatch(/\d/)
    expect(calls).toHaveLength(0)
  })

  it('shows loading without a number until the server answers, then the server count 0', async () => {
    const pending = deferred<Response>()
    const calls = stubFetch(() => pending.promise)
    mount()
    expect(allocationLine().dataset.state).toBe('loading')
    expect(allocationLine().textContent).toBe(`${T.lineLabel(1)}: ${T.loading}`)
    pending.resolve(answer({ items: [], searchWindowFull: false }))
    await waitFor(() => expect(allocationLine().dataset.state).toBe('ready'))
    expect(allocationLine().textContent).toBe(`${T.lineLabel(1)}: ${T.active(0)}`)
    expect(calls).toHaveLength(1)
    const url = new URL(calls[0].url, 'http://localhost')
    expect(url.pathname).toBe('/api/v1/inventory/physical-allocations')
    expect(url.searchParams.get('salesOrderId')).toBe(ORDER)
    expect(url.searchParams.get('salesOrderLineId')).toBe(LINE)
    expect(calls[0].companyId).toBe(COMPANY)
  })

  it('counts only Active allocations, per line', async () => {
    stubFetch((url) => new URL(url, 'http://localhost').searchParams.get('salesOrderLineId') === LINE
      ? answer({ items: [allocation('Active'), allocation('Released'), allocation('Active')], searchWindowFull: false })
      : answer({ items: [allocation('Released', COMPANY, ORDER, LINE_2)], searchWindowFull: false }))
    mount({ lines: [line(LINE, 1), line(LINE_2, 2)] })
    await waitFor(() => expect(allocationLine(LINE).textContent).toBe(`${T.lineLabel(1)}: ${T.active(2)}`))
    await waitFor(() => expect(allocationLine(LINE_2).textContent).toBe(`${T.lineLabel(2)}: ${T.active(0)}`))
  })

  it('a full search window is a lower bound, never an exact count', async () => {
    stubFetch(() => answer({ items: [allocation('Active')], searchWindowFull: true }))
    mount()
    await waitFor(() => expect(allocationLine().dataset.state).toBe('lower-bound'))
    expect(allocationLine().textContent).toBe(`${T.lineLabel(1)}: ${T.activeAtLeast(1)}`)
  })

  it('a refused read says so without a number and reports the status to the auth boundary', async () => {
    stubFetch(() => answer({ status: 403 }, 403))
    mount()
    await waitFor(() => expect(allocationLine().dataset.state).toBe('forbidden'))
    expect(allocationLine().textContent).toBe(`${T.lineLabel(1)}: ${T.forbidden}`)
    expect(report).toHaveBeenCalledWith(403, { token: 1 })
  })

  it('an unavailable read offers a retry and shows the count only after the retry succeeds', async () => {
    let attempts = 0
    stubFetch(() => {
      attempts += 1
      return attempts === 1 ? answer({ status: 503 }, 503) : answer({ items: [allocation('Active')], searchWindowFull: false })
    })
    mount()
    await waitFor(() => expect(allocationLine().dataset.state).toBe('error'))
    expect(allocationLine().textContent).toContain(T.unavailable)
    expect(allocationLine().textContent).not.toContain(T.active(0))
    await userEvent.click(within(allocationLine()).getByRole('button', { name: T.retry }))
    await waitFor(() => expect(allocationLine().textContent).toBe(`${T.lineLabel(1)}: ${T.active(1)}`))
  })

  it('an answer of another company or order is unverifiable and shows no number', async () => {
    stubFetch(() => answer({ items: [allocation('Active', COMPANY_B)], searchWindowFull: false }))
    mount()
    await waitFor(() => expect(allocationLine().dataset.state).toBe('error'))
    expect(allocationLine().textContent).toContain(T.malformed)
  })

  it('an offline read shows no number', async () => {
    stubFetch(() => { throw new TypeError('Failed to fetch') })
    mount()
    await waitFor(() => expect(allocationLine().dataset.state).toBe('error'))
    expect(allocationLine().textContent).toContain(T.unavailable)
  })

  it('a company change discards the old answer: only the new company\'s read is shown', async () => {
    const first = deferred<Response>()
    const calls = stubFetch((_url, call) => call.companyId === COMPANY
      ? first.promise
      : answer({ items: [], searchWindowFull: false }))
    const { update } = mount()
    expect(allocationLine().dataset.state).toBe('loading')
    update({ companyId: COMPANY_B })
    await waitFor(() => expect(allocationLine().textContent).toBe(`${T.lineLabel(1)}: ${T.active(0)}`))
    first.resolve(answer({ items: [allocation('Active'), allocation('Active')], searchWindowFull: false }))
    await new Promise((done) => setTimeout(done, 20))
    expect(allocationLine().textContent).toBe(`${T.lineLabel(1)}: ${T.active(0)}`)
    expect(calls.map((call) => call.companyId)).toEqual([COMPANY, COMPANY_B])
  })

  it('an order change discards the old answer', async () => {
    const first = deferred<Response>()
    stubFetch((url) => new URL(url, 'http://localhost').searchParams.get('salesOrderId') === ORDER
      ? first.promise
      : answer({ items: [allocation('Active', COMPANY, ORDER_B)], searchWindowFull: false }))
    const { update } = mount()
    update({ salesOrderId: ORDER_B })
    await waitFor(() => expect(allocationLine().textContent).toBe(`${T.lineLabel(1)}: ${T.active(1)}`))
    first.resolve(answer({ items: [allocation('Active'), allocation('Active'), allocation('Active')], searchWindowFull: false }))
    await new Promise((done) => setTimeout(done, 20))
    expect(allocationLine().textContent).toBe(`${T.lineLabel(1)}: ${T.active(1)}`)
  })

  it('shares the allocation panel\'s query: a refetch of that key (a create or release there) refreshes the count', async () => {
    let active = 0
    stubFetch(() => answer({ items: Array.from({ length: active }, () => allocation('Active')), searchWindowFull: false }))
    const { client } = mount()
    await waitFor(() => expect(allocationLine().textContent).toBe(`${T.lineLabel(1)}: ${T.active(0)}`))
    active = 1
    await client.refetchQueries({ queryKey: [`ctx-${COMPANY}`, 1, 'physical-allocations', ORDER, LINE, PRODUCT, '7'] })
    await waitFor(() => expect(allocationLine().textContent).toBe(`${T.lineLabel(1)}: ${T.active(1)}`))
  })

  it('a draft without lines reads nothing and says so', () => {
    const calls = stubFetch(() => { throw new Error('no read expected') }, () => { throw new Error('no read expected') })
    mount({ lines: [] })
    expect(screen.getByTestId('sales-linked-allocation').textContent).toBe(T.noLines)
    expect(screen.getByTestId('sales-linked-loading').textContent).toBe(T.loadingNoLines)
    expect(calls).toHaveLength(0)
    expect(calls.loading).toHaveLength(0)
  })
})

/**
 * SALES-LOADING-PROGRESS-UI-01: the loading facts of each line, from `GET /sales/drafts/{id}/loading-progress` (#684).
 * One request per order; the answer is shown only as the facts of this exact detail; every other outcome is its own
 * state without a number.
 */
describe('S-04 linked effects — loading progress per line', () => {
  const allocations = () => answer({ items: [], searchWindowFull: false })
  const shownNumbers = () => screen.getByTestId('sales-linked-loading').querySelectorAll('[data-fact]')

  it('reads once for the whole order under the company, with no selector, and shows each line\'s facts as sent', async () => {
    const calls = stubFetch(allocations, () => progress([
      progressLine(LINE, 1, { loadingInstructionLines: 2, instructed: '7.5', loaded: '6.250', shipped: '4', delivered: '1.5' }),
      progressLine(LINE_2, 2),
    ]))
    mount({ lines: [line(LINE, 1), line(LINE_2, 2)] })
    expect(loadingState()).toBe('loading')
    expect(loadingBlock().textContent).toBe(T.loadingReading)
    expect(shownNumbers()).toHaveLength(0)

    await waitFor(() => expect(loadingState()).toBe('ready'))
    // Exact text: nothing is rounded, padded or converted ("6.250" stays "6.250").
    expect(facts(LINE)).toEqual({ ordered: '10', instructed: '7.5', loaded: '6.250', shipped: '4', delivered: '1.5' })
    expect(loadingLine(LINE).dataset.instructionLines).toBe('2')
    // A line without an instruction shows the owner's real zeros.
    expect(facts(LINE_2)).toEqual({ ordered: '10', instructed: '0', loaded: '0', shipped: '0', delivered: '0' })
    const table = within(loadingBlock()).getByRole('table', { name: T.loadingCaption })
    expect(within(table).getAllByRole('columnheader').map((header) => header.textContent)).toEqual([
      T.loadingLine, T.loadingOrdered, T.loadingInstructed, T.loadingLoaded, T.loadingShipped, T.loadingDelivered,
    ])
    expect(within(table).getAllByRole('rowheader').map((header) => header.textContent)).toEqual(['1', '2'])
    expect(loadingBlock().textContent).toContain(T.loadingNote)

    expect(calls.loading).toHaveLength(1)
    const [read] = calls.loading
    expect(new URL(read.url, 'http://localhost').pathname).toBe(`/api/v1/sales/drafts/${ORDER}/loading-progress`)
    expect(new URL(read.url, 'http://localhost').search).toBe('')
    expect(read.method).toBe('GET')
    expect(read.companyId).toBe(COMPANY)
    expect(read.headers.get('X-ERP-Sales-Read-Contract')).toBeNull()
    expect(read.headers.get('Idempotency-Key')).toBeNull()
  })

  it('shows the lines in the detail\'s order even when the answer lists them otherwise', async () => {
    stubFetch(allocations, () => progress([progressLine(LINE_2, 2), progressLine(LINE, 1)]))
    mount({ lines: [line(LINE, 1), line(LINE_2, 2)] })
    await waitFor(() => expect(loadingState()).toBe('ready'))
    expect(within(loadingBlock()).getAllByRole('rowheader').map((header) => header.textContent)).toEqual(['1', '2'])
  })

  it.each([
    ['another draft version', () => progress([progressLine(LINE, 1)], '9007199254740994')],
    ['a missing line', () => progress([progressLine(LINE, 1)])],
    ['an extra line', () => progress([progressLine(LINE, 1), progressLine(LINE_2, 2), progressLine(ORDER_B, 3)])],
    ['the same line twice', () => progress([progressLine(LINE, 1), progressLine(LINE, 1)])],
    ['a foreign line', () => progress([progressLine(LINE, 1), progressLine(ORDER_B, 2)])],
    ['another line number', () => progress([progressLine(LINE, 1), progressLine(LINE_2, 3)])],
    ['another order unit', () => progress([progressLine(LINE, 1), progressLine(LINE_2, 2, { orderUnitId: PRODUCT })])],
    ['another ordered quantity', () => progress([progressLine(LINE, 1), progressLine(LINE_2, 2, { ordered: '10.0' })])],
    ['more delivered than shipped', () => progress([progressLine(LINE, 1), progressLine(LINE_2, 2, { loadingInstructionLines: 1, instructed: '5', loaded: '5', shipped: '2', delivered: '3' })])],
    ['more instructed than ordered', () => progress([progressLine(LINE, 1), progressLine(LINE_2, 2, { loadingInstructionLines: 1, instructed: '10.001' })])],
    ['an instructed quantity without an instruction line', () => progress([progressLine(LINE, 1), progressLine(LINE_2, 2, { instructed: '1' })])],
    ['an instruction line without a quantity', () => progress([progressLine(LINE, 1), progressLine(LINE_2, 2, { loadingInstructionLines: 1 })])],
    ['a quantity as a number', () => progress([progressLine(LINE, 1), progressLine(LINE_2, 2, { loaded: 0 })])],
    ['an unknown member', () => progress([progressLine(LINE, 1), progressLine(LINE_2, 2, { remaining: '10' })])],
    ['a body that is not JSON', () => new Response('<html>', { status: 200 })],
  ])('an answer with %s is unverifiable: no quantity of any line is shown', async (_name, loading) => {
    stubFetch(allocations, loading)
    mount({ lines: [line(LINE, 1), line(LINE_2, 2)] })
    await waitFor(() => expect(loadingState()).toBe('malformed'))
    expect(loadingBlock().textContent).toContain(T.loadingMalformed)
    expect(shownNumbers()).toHaveLength(0)
    expect(screen.getByTestId('sales-linked-loading').textContent).not.toMatch(/\d/)
    expect(within(loadingBlock()).getByRole('button', { name: T.retry })).toBeTruthy()
  })

  it.each([
    [401, 'forbidden', T.loadingForbidden],
    [403, 'forbidden', T.loadingForbidden],
    [404, 'not-found', T.loadingNotFound],
  ])('a %i answer is definite: its own text, no number, no retry, reported to the auth boundary', async (status, state, text) => {
    const calls = stubFetch(allocations, () => answer({ status }, status))
    mount()
    await waitFor(() => expect(loadingState()).toBe(state))
    expect(loadingBlock().textContent).toBe(text)
    expect(shownNumbers()).toHaveLength(0)
    expect(within(loadingBlock()).queryByRole('button')).toBeNull()
    expect(report).toHaveBeenCalledWith(status, { token: 1 })
    expect(calls.loading).toHaveLength(1)
  })

  it.each([
    ['a 503', () => answer({ status: 503 }, 503)],
    ['no connection', () => { throw new TypeError('Failed to fetch') }],
  ])('%s shows no number and offers a retry that asks the same read again', async (_name, failing) => {
    let attempts = 0
    const calls = stubFetch(allocations, () => {
      attempts += 1
      if (attempts === 1) return failing()
      return progress([progressLine(LINE, 1, { loadingInstructionLines: 1, instructed: '10', loaded: '10', shipped: '10', delivered: '10' })])
    })
    mount()
    await waitFor(() => expect(loadingState()).toBe('error'))
    expect(loadingBlock().textContent).toContain(T.loadingUnavailable)
    expect(shownNumbers()).toHaveLength(0)
    await userEvent.click(within(loadingBlock()).getByRole('button', { name: T.retry }))
    await waitFor(() => expect(loadingState()).toBe('ready'))
    expect(facts()).toEqual({ ordered: '10', instructed: '10', loaded: '10', shipped: '10', delivered: '10' })
    expect(calls.loading.map((call) => call.url)).toEqual([calls.loading[0].url, calls.loading[0].url])
    expect(calls.loading.every((call) => call.method === 'GET' && call.companyId === COMPANY)).toBe(true)
  })

  it('an unverifiable answer can be read again and is shown once it verifies', async () => {
    let attempts = 0
    stubFetch(allocations, () => {
      attempts += 1
      return attempts === 1 ? progress([progressLine(LINE, 1)], '1') : progress([progressLine(LINE, 1)])
    })
    mount()
    await waitFor(() => expect(loadingState()).toBe('malformed'))
    await userEvent.click(within(loadingBlock()).getByRole('button', { name: T.retry }))
    await waitFor(() => expect(loadingState()).toBe('ready'))
  })

  it('the loading read does not depend on the allocation capability or on the allocation reads', async () => {
    const calls = stubFetch(() => answer({ status: 503 }, 503))
    mount()
    await waitFor(() => expect(loadingState()).toBe('ready'))
    await waitFor(() => expect(allocationLine().dataset.state).toBe('error'))
    expect(facts()).toEqual({ ordered: '10', instructed: '0', loaded: '0', shipped: '0', delivered: '0' })
    expect(calls.loading).toHaveLength(1)
  })

  it.each([
    ['company', { companyId: COMPANY_B }, (call: Call) => call.companyId === COMPANY],
    ['actor (context namespace)', { namespace: 'ctx-other-actor' }, undefined],
    ['route entry', { routeEntryGeneration: 2 }, undefined],
  ] as const)('a %s change discards the old loading answer: only the new read is shown', async (_name, next, isOld) => {
    const first = deferred<Response>()
    let reads = 0
    const calls = stubFetch(allocations, (_url, call) => {
      reads += 1
      const old = isOld === undefined ? reads === 1 : isOld(call)
      return old ? first.promise : progress([progressLine(LINE, 1)])
    })
    const { update } = mount()
    await waitFor(() => expect(calls.loading).toHaveLength(1))
    update(next)
    await waitFor(() => expect(loadingState()).toBe('ready'))
    first.resolve(progress([progressLine(LINE, 1, { loadingInstructionLines: 1, instructed: '9', loaded: '9', shipped: '9', delivered: '9' })]))
    await new Promise((done) => setTimeout(done, 20))
    expect(facts()).toEqual({ ordered: '10', instructed: '0', loaded: '0', shipped: '0', delivered: '0' })
    expect(calls.loading).toHaveLength(2)
  })

  it('an order change reads the new order and never shows the old order\'s late answer', async () => {
    const first = deferred<Response>()
    const calls = stubFetch(allocations, (url) => url.includes(ORDER_B) ? progress([progressLine(LINE, 1)]) : first.promise)
    const { update } = mount()
    await waitFor(() => expect(calls.loading).toHaveLength(1))
    update({ salesOrderId: ORDER_B })
    await waitFor(() => expect(loadingState()).toBe('ready'))
    first.resolve(progress([progressLine(LINE, 1, { loadingInstructionLines: 1, instructed: '9', loaded: '9', shipped: '9', delivered: '9' })]))
    await new Promise((done) => setTimeout(done, 20))
    expect(facts().instructed).toBe('0')
    expect(calls.loading.map((call) => new URL(call.url, 'http://localhost').pathname)).toEqual([
      `/api/v1/sales/drafts/${ORDER}/loading-progress`, `/api/v1/sales/drafts/${ORDER_B}/loading-progress`,
    ])
  })

  it('a new detail version is a new read; the answer of the old version is not this detail\'s', async () => {
    const calls = stubFetch(allocations, () => progress([progressLine(LINE, 1)], calls.loading.length === 1 ? VERSION : '9007199254740994'))
    const { update } = mount()
    await waitFor(() => expect(loadingState()).toBe('ready'))
    update({ draftVersion: '9007199254740994' })
    await waitFor(() => expect(calls.loading).toHaveLength(2))
    await waitFor(() => expect(loadingState()).toBe('ready'))
  })

  it('the unbound fallback reads no loading and shows no number', () => {
    const calls = stubFetch(() => { throw new Error('no read expected') }, () => { throw new Error('no read expected') })
    render(<SalesDraftLinkedEffectsUnbound />)
    expect(screen.getByTestId('sales-linked-loading').textContent).toBe(T.loadingNotReadable)
    expect(calls.loading).toHaveLength(0)
  })
})
