import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SalesDraftAllocationPanel } from './SalesDraftAllocationPanel'
import { CREATE_ALLOCATION_422_CODES, decodeAllocationSuggestions, decodePhysicalAllocation } from './allocationContract'
import type { DraftLine } from './contract'

// KLN-D34/D39/D40 (STK-S30): label suggestions per Sales line, the "Muadil" flag and its server refusal. SENTETIK.
const COMPANY = '10000000-0000-4000-8000-000000000001'
const ORDER = '20000000-0000-4000-8000-000000000001'
const LINE = '30000000-0000-4000-8000-000000000001'
const PRODUCT = '40000000-0000-4000-8000-000000000001'
const OTHER_PRODUCT = '40000000-0000-4000-8000-000000000002'
const LABEL = '50000000-0000-4000-8000-000000000001'
const OTHER_LABEL = '50000000-0000-4000-8000-000000000002'
const SUGGESTIONS_PATH = '/api/v1/inventory/physical-allocations/suggestions'

const report = vi.fn()
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report }),
}))

const line: DraftLine = {
  lineId: LINE, lineNo: 1, productId: PRODUCT, productVersion: '7', productCode: 'P-1', productName: 'Rulo',
  orderUnitId: '70000000-0000-4000-8000-000000000001', quantity: '10', unitPrice: null,
  priceUnitId: null, requestedDeliveryDate: null, note: null, requestedDeliverySlices: null,
  discountPercent: null, priceDisclosure: 'Visible',
}

const scope = { companyId: COMPANY, salesOrderId: ORDER, salesOrderLineId: LINE, productId: PRODUCT, productVersion: '7' }

const sameItem = (overrides: Record<string, unknown> = {}) => ({
  labelId: LABEL, labelVersion: '11', labelCode: 'ETK-001', productId: PRODUCT, productVersion: '7',
  productCode: null, productName: null, kind: 'same-product', isSubstitute: false, unreservedKg: '40',
  maximumDeviationPercent: null, ...overrides,
})

const similarItem = (overrides: Record<string, unknown> = {}) => ({
  labelId: OTHER_LABEL, labelVersion: '4', labelCode: 'ETK-900', productId: OTHER_PRODUCT, productVersion: '2',
  productCode: 'P-2', productName: 'Benzer rulo', kind: 'similar-product', isSubstitute: true, unreservedKg: '25',
  maximumDeviationPercent: '4.00', ...overrides,
})

const suggestions = (items: unknown[], overrides: Record<string, unknown> = {}) => ({
  companyId: COMPANY, salesOrderId: ORDER, salesOrderLineId: LINE, productId: PRODUCT, productVersion: '7',
  similarProductMode: 'dimension-tolerance', aiSuggestions: 'disabled', truncated: false, items, ...overrides,
})

const allocation = (overrides: Record<string, unknown> = {}) => ({
  allocationId: '60000000-0000-4000-8000-000000000001', companyId: COMPANY, version: '1', status: 'Active',
  labelId: OTHER_LABEL, productId: PRODUCT, demandKind: 'sales.order-line', demandId: ORDER, demandLineId: LINE,
  demandProductVersion: '7', allocatedKg: '5', remainingKg: '5', reason: 'Muadil ile ayrıldı.',
  reservedUntilUtc: null, createdAtUtc: '2026-11-15T10:00:00Z', updatedAtUtc: '2026-11-15T10:00:00Z',
  isSubstitute: true, ...overrides,
})

function answer(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><SalesDraftAllocationPanel
    enabled canOpenLabelHistory={false} companyId={COMPANY} namespace="ctx-a" routeEntryGeneration={1}
    salesOrderId={ORDER} lines={[line]} /></QueryClientProvider>)
}

beforeEach(() => {
  const values = new Map<string, string>()
  Object.defineProperty(window, 'localStorage', {
    configurable: true,
    value: {
      getItem: (key: string) => values.get(key) ?? null,
      setItem: (key: string, value: string) => values.set(key, value),
      removeItem: (key: string) => values.delete(key),
      clear: () => values.clear(),
    },
  })
})

afterEach(() => {
  cleanup()
  window.localStorage.clear()
  vi.unstubAllGlobals()
  vi.clearAllMocks()
})

describe('allocation suggestion decoder', () => {
  it('accepts the line’s own labels or, alone, flagged similar labels within a configured tolerance', () => {
    expect(decodeAllocationSuggestions(suggestions([sameItem()], { similarProductMode: 'same-product-only' }), scope)?.items)
      .toHaveLength(1)
    const similar = decodeAllocationSuggestions(suggestions([similarItem()]), scope)
    expect(similar?.items[0]).toMatchObject({ kind: 'similar-product', isSubstitute: true, maximumDeviationPercent: '4.00' })
  })

  it.each([
    ['another line', suggestions([sameItem()], { salesOrderLineId: ORDER })],
    ['another product version', suggestions([sameItem()], { productVersion: '8' })],
    ['an extra key', { ...suggestions([sameItem()]), hint: 'x' }],
    ['a similar label without a tolerance', suggestions([similarItem()], { similarProductMode: 'same-product-only' })],
    ['a similar label beside an own-product label', suggestions([sameItem(), similarItem()])],
    ['an unflagged similar label', suggestions([similarItem({ isSubstitute: false })])],
    ['a flagged own-product label', suggestions([sameItem({ isSubstitute: true })])],
    ['a zero unreserved quantity', suggestions([sameItem({ unreservedKg: '0' })])],
    ['a duplicated label', suggestions([sameItem(), sameItem()])],
  ])('rejects %s', (_name, body) => {
    expect(decodeAllocationSuggestions(body, scope)).toBeNull()
  })

  it('keeps the allocation’s line product and reads the substitute flag strictly', () => {
    expect(decodePhysicalAllocation(allocation(), scope)?.isSubstitute).toBe(true)
    const legacy: Record<string, unknown> = { ...allocation() }
    delete legacy.isSubstitute
    expect(decodePhysicalAllocation(legacy, scope)?.isSubstitute).toBe(false)
    expect(decodePhysicalAllocation(allocation({ isSubstitute: 'true' }), scope)).toBeNull()
    expect(decodePhysicalAllocation(allocation({ productId: OTHER_PRODUCT }), scope)).toBeNull()
  })
})

describe('Sales line label suggestions', () => {
  it('shows the label’s product and the Muadil flag, and confirms the substitute through the create command', async () => {
    let stored: ReturnType<typeof allocation> | null = null
    const posts: any[] = []
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost')
      if ((init?.method ?? 'GET') === 'POST') {
        const body = JSON.parse(String(init!.body))
        posts.push(body)
        stored = allocation({ allocationId: body.allocationId })
        return answer(stored, 201)
      }
      if (url.pathname === SUGGESTIONS_PATH) {
        expect(url.searchParams.get('salesOrderLineId')).toBe(LINE)
        expect(new Headers(init?.headers).get('X-Company-Id')).toBe(COMPANY)
        return answer(suggestions([similarItem()]))
      }
      return answer({ items: stored === null ? [] : [{ allocation: stored, labelVersion: '5' }], searchWindowFull: false })
    }))

    mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await userEvent.click(within(card).getByRole('button', { name: 'Etiket öner' }))
    const row = await within(card).findByTestId(`sales-allocation-suggestion-${OTHER_LABEL}`)
    expect(within(row).getByText(/P-2 — Benzer rulo/)).toBeTruthy()
    expect(within(row).getByText('Muadil')).toBeTruthy()
    expect(within(row).getByText('%4.00')).toBeTruthy()
    expect(within(card).getByText(/Yapay zekâ önerisi kapalı/)).toBeTruthy()

    await userEvent.click(within(row).getByRole('button', { name: 'ETK-900 önerisini seç' }))
    expect(within(card).getByTestId(`sales-allocation-substitute-${LINE}`).textContent).toContain('P-2 — Benzer rulo')
    await userEvent.type(within(card).getByLabelText('Tahsis kg'), '5')
    await userEvent.type(within(card).getByLabelText('Tahsis nedeni'), 'Muadil ile ayrıldı.')
    await userEvent.click(within(card).getByRole('button', { name: 'Tahsis kaydet' }))

    await waitFor(() => expect(within(card).getByText('Aktif')).toBeTruthy())
    expect(posts).toHaveLength(1)
    expect(posts[0]).toMatchObject({ labelId: OTHER_LABEL, expectedLabelVersion: '4', quantityKg: '5', isSubstitute: true })
    const recorded = within(card).getByTestId(`sales-allocation-${posts[0].allocationId}`)
    expect(within(recorded).getByText('Muadil')).toBeTruthy()
  })

  it('sends no substitute flag for an own-product suggestion and shows the line’s product', async () => {
    const posts: any[] = []
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost')
      if ((init?.method ?? 'GET') === 'POST') {
        const body = JSON.parse(String(init!.body))
        posts.push(body)
        return answer(allocation({ allocationId: body.allocationId, labelId: LABEL, isSubstitute: false }), 201)
      }
      if (url.pathname === SUGGESTIONS_PATH) {
        return answer(suggestions([sameItem()], { similarProductMode: 'same-product-only' }))
      }
      return answer({ items: [], searchWindowFull: false })
    }))

    mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await userEvent.click(within(card).getByRole('button', { name: 'Etiket öner' }))
    const row = await within(card).findByTestId(`sales-allocation-suggestion-${LABEL}`)
    expect(within(row).getByText('P-1 — Rulo')).toBeTruthy()
    expect(within(row).queryByText('Muadil')).toBeNull()
    expect(within(card).getByText(/yalnız bu satırın ürünü önerilir/)).toBeTruthy()

    await userEvent.click(within(row).getByRole('button', { name: 'ETK-001 önerisini seç' }))
    expect(within(card).queryByTestId(`sales-allocation-substitute-${LINE}`)).toBeNull()
    await userEvent.type(within(card).getByLabelText('Tahsis kg'), '5')
    await userEvent.type(within(card).getByLabelText('Tahsis nedeni'), 'Sipariş için ayırıldı.')
    await userEvent.click(within(card).getByRole('button', { name: 'Tahsis kaydet' }))

    await waitFor(() => expect(posts).toHaveLength(1))
    expect('isSubstitute' in posts[0]).toBe(false)
  })

  it('shows the server’s similarity refusal as a definite refusal', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost')
      if ((init?.method ?? 'GET') === 'POST') {
        return answer({ status: 422, code: CREATE_ALLOCATION_422_CODES.substituteNotSimilar }, 422)
      }
      if (url.pathname === SUGGESTIONS_PATH) return answer(suggestions([similarItem()]))
      return answer({ items: [], searchWindowFull: false })
    }))

    mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await userEvent.click(within(card).getByRole('button', { name: 'Etiket öner' }))
    await userEvent.click(await within(card).findByRole('button', { name: 'ETK-900 önerisini seç' }))
    await userEvent.type(within(card).getByLabelText('Tahsis kg'), '5')
    await userEvent.type(within(card).getByLabelText('Tahsis nedeni'), 'Muadil ile ayrıldı.')
    await userEvent.click(within(card).getByRole('button', { name: 'Tahsis kaydet' }))

    const notice = await within(card).findByTestId('sales-allocation-notice')
    expect(notice.textContent).toContain('ölçü toleransı içinde benzemiyor')
    expect(within(card).queryByRole('button', { name: 'Aynı tahsis denemesini tekrar gönder' })).toBeNull()
  })

  it.each([
    [403, 'Etiket önerisi yetkisi yok ya da artık geçerli değil.'],
    [404, 'Sipariş satırı onaylı değil ya da bulunamadı; öneri gösterilmedi.'],
    [503, 'Etiket önerileri şu anda okunamıyor.'],
  ])('shows a %s suggestion refusal without any suggestion', async (status, text) => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) =>
      new URL(String(input), 'http://localhost').pathname === SUGGESTIONS_PATH
        ? answer({ status }, status)
        : answer({ items: [], searchWindowFull: false })))

    mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await userEvent.click(within(card).getByRole('button', { name: 'Etiket öner' }))

    expect((await within(card).findByRole('alert')).textContent).toBe(text)
    expect(within(card).queryByRole('button', { name: /önerisini seç$/ })).toBeNull()
    expect(report).toHaveBeenCalledWith(status, { token: 1 })
  })

  it('reports a malformed or empty answer honestly', async () => {
    let body: unknown = suggestions([sameItem({ productId: OTHER_PRODUCT })])
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) =>
      new URL(String(input), 'http://localhost').pathname === SUGGESTIONS_PATH
        ? answer(body)
        : answer({ items: [], searchWindowFull: false })))

    mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await userEvent.click(within(card).getByRole('button', { name: 'Etiket öner' }))
    expect((await within(card).findByRole('alert')).textContent)
      .toBe('Öneri yanıtı doğrulanamadı; hiçbir öneri gösterilmedi.')

    body = suggestions([], { truncated: true })
    await userEvent.click(within(card).getByRole('button', { name: 'Önerileri yenile' }))
    expect(await within(card).findByTestId(`sales-allocation-suggestions-empty-${LINE}`)).toBeTruthy()
    expect(within(card).getByText(/liste eksik olabilir/)).toBeTruthy()
  })
})
