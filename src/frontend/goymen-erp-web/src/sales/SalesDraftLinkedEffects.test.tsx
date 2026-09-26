/**
 * S-04 "Bağlı etkiler": the active allocations per line from Inventory's server truth, the loading effect
 * named as not readable, and no number shown unless a read succeeded. Component evidence, NOT_ENV: the API
 * answers here are the test's.
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

interface Call { readonly url: string; readonly companyId: string | null }

function stubFetch(handler: (url: string, call: Call) => Response | Promise<Response>) {
  const calls: Call[] = []
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input)
    const call = { url, companyId: new Headers(init?.headers).get('X-Company-Id') }
    calls.push(call)
    return handler(url, call)
  }))
  return calls
}

function view(props: {
  readonly allocationReadable?: boolean
  readonly companyId?: string
  readonly namespace?: string
  readonly salesOrderId?: string
  readonly lines?: readonly DraftLine[]
}) {
  const companyId = props.companyId ?? COMPANY
  const namespace = props.namespace ?? `ctx-${companyId}`
  const salesOrderId = props.salesOrderId ?? ORDER
  return (
    <SalesDraftLinkedEffects
      key={`effects:${namespace}:1:${salesOrderId}`}
      allocationReadable={props.allocationReadable ?? true}
      companyId={companyId}
      namespace={namespace}
      routeEntryGeneration={1}
      salesOrderId={salesOrderId}
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

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  vi.clearAllMocks()
})

describe('S-04 linked effects', () => {
  it('without the allocation capability asks nothing and shows no number; loading is named as not readable', () => {
    const calls = stubFetch(() => { throw new Error('no read expected') })
    mount({ allocationReadable: false })
    expect(screen.getByTestId('sales-linked-allocation').textContent).toBe(T.allocationNoAccess)
    expect(screen.getByTestId('sales-linked-loading').textContent).toBe(T.loadingNotReadable)
    expect(screen.getByTestId('sales-approval-linked-effects').textContent).not.toMatch(/\d/)
    expect(calls).toHaveLength(0)
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
    const calls = stubFetch(() => { throw new Error('no read expected') })
    mount({ lines: [] })
    expect(screen.getByTestId('sales-linked-allocation').textContent).toBe(T.noLines)
    expect(calls).toHaveLength(0)
  })
})
