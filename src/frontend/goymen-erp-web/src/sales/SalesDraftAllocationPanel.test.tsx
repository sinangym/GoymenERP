import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SalesDraftAllocationPanel } from './SalesDraftAllocationPanel'
import type { DraftLine } from './contract'

const COMPANY = '10000000-0000-4000-8000-000000000001'
const ORDER = '20000000-0000-4000-8000-000000000001'
const LINE = '30000000-0000-4000-8000-000000000001'
const PRODUCT = '40000000-0000-4000-8000-000000000001'
const LABEL = '50000000-0000-4000-8000-000000000001'
const ALLOCATION = '60000000-0000-4000-8000-000000000001'

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

const allocation = (status: 'Active' | 'Released', version: string) => ({
  allocationId: ALLOCATION, companyId: COMPANY, version, status, labelId: LABEL, productId: PRODUCT,
  demandKind: 'sales.order-line', demandId: ORDER, demandLineId: LINE, demandProductVersion: '7',
  allocatedKg: '5', remainingKg: status === 'Active' ? '5' : '0', reason: 'Sipariş için ayırıldı.',
  reservedUntilUtc: null, createdAtUtc: '2026-09-24T10:00:00Z', updatedAtUtc: '2026-09-24T10:00:00Z',
})

function answer(body: unknown, status = 200, replayed = false) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json',
    ...(replayed ? { 'Idempotency-Replayed': 'true' } : {}) } })
}

function mount(enabled = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const view = render(<QueryClientProvider client={client}><SalesDraftAllocationPanel
    enabled={enabled} companyId={COMPANY} namespace="ctx-a" routeEntryGeneration={1}
    salesOrderId={ORDER} lines={[line]} /></QueryClientProvider>)
  return { client, ...view }
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

describe('Sales physical allocation consumer', () => {
  it('saves, closes, reopens, releases and reopens the same server record', async () => {
    let stored: ReturnType<typeof allocation> | null = null
    let hideRecordedOnNextGet = false
    const calls: { readonly method: string; readonly url: string; readonly body: any; readonly headers: Headers }[] = []
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      const method = init?.method ?? 'GET'
      const body = init?.body === undefined ? null : JSON.parse(String(init.body))
      const headers = new Headers(init?.headers)
      calls.push({ method, url, body, headers })
      if (method === 'GET') {
        const visible = stored !== null && !hideRecordedOnNextGet
        hideRecordedOnNextGet = false
        return answer({ items: visible ? [{ allocation: stored, labelVersion: stored.status === 'Active' ? '12' : '13' }] : [], searchWindowFull: false })
      }
      if (url.endsWith('/release')) {
        expect(body).toEqual({ expectedAllocationVersion: '1', reason: 'Sipariş değişti.' })
        stored = { ...allocation('Released', '2'), allocationId: stored!.allocationId }
        return answer(stored)
      }
      expect(body).toMatchObject({ salesOrderId: ORDER, salesOrderLineId: LINE, labelId: LABEL,
        expectedLabelVersion: '11', quantityKg: '5', reason: 'Sipariş için ayırıldı.', reservedUntilUtc: null })
      expect(body.allocationId).toMatch(/^[0-9a-f-]{36}$/)
      stored = { ...allocation('Active', '1'), allocationId: body.allocationId }
      hideRecordedOnNextGet = true
      return answer(stored, 201)
    }))

    const first = mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await userEvent.type(within(card).getByLabelText('Etiket kimliği'), LABEL)
    await userEvent.type(within(card).getByLabelText('Etiket sürümü'), '11')
    await userEvent.type(within(card).getByLabelText('Tahsis kg'), '5')
    await userEvent.type(within(card).getByLabelText('Tahsis nedeni'), 'Sipariş için ayırıldı.')
    await userEvent.click(within(card).getByRole('button', { name: 'Tahsis kaydet' }))
    await waitFor(() => expect(within(card).getByText('Aktif')).toBeTruthy())
    expect(calls.find((call) => call.method === 'POST')?.headers.get('X-Company-Id')).toBe(COMPANY)
    expect(calls.find((call) => call.method === 'POST')?.headers.get('Idempotency-Key')).toBeTruthy()

    first.unmount()
    mount()
    const reopened = await screen.findByTestId(`sales-allocation-${stored!.allocationId}`)
    expect(within(reopened).getByText('Aktif')).toBeTruthy()
    const reopenedCard = screen.getByTestId(`sales-allocation-line-${LINE}`)
    await userEvent.type(within(reopenedCard).getByLabelText('Serbest bırakma nedeni'), 'Sipariş değişti.')
    await userEvent.click(within(reopened).getByRole('button', { name: 'Tahsis serbest bırak' }))
    await waitFor(() => expect(within(reopenedCard).getByText('Serbest bırakıldı')).toBeTruthy())

    cleanup()
    mount()
    const final = await screen.findByTestId(`sales-allocation-${stored!.allocationId}`)
    expect(within(final).getByText('Serbest bırakıldı')).toBeTruthy()
    expect(within(final).queryByRole('button', { name: 'Tahsis serbest bırak' })).toBeNull()
    expect(calls.filter((call) => call.method === 'POST')).toHaveLength(2)
  })

  it('keeps the fresher GET state when the recorded allocation already exists', async () => {
    let createdId: string | null = null
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const method = init?.method ?? 'GET'
      if (method === 'GET') {
        const fresh = createdId === null ? [] : [{
          allocation: { ...allocation('Released', '2'), allocationId: createdId }, labelVersion: '13',
        }]
        return answer({ items: fresh, searchWindowFull: false })
      }
      const body = JSON.parse(String(init?.body))
      createdId = body.allocationId
      return answer({ ...allocation('Active', '1'), allocationId: createdId }, 201)
    }))

    mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await userEvent.type(within(card).getByLabelText('Etiket kimliği'), LABEL)
    await userEvent.type(within(card).getByLabelText('Etiket sürümü'), '11')
    await userEvent.type(within(card).getByLabelText('Tahsis kg'), '5')
    await userEvent.type(within(card).getByLabelText('Tahsis nedeni'), 'Sipariş için ayırıldı.')
    await userEvent.click(within(card).getByRole('button', { name: 'Tahsis kaydet' }))

    const recordedId = createdId
    if (recordedId === null) throw new Error('The create request was not recorded.')
    const row = await screen.findByTestId(`sales-allocation-${recordedId}`)
    expect(within(row).getByText('Serbest bırakıldı')).toBeTruthy()
    expect(within(row).getByText('2')).toBeTruthy()
    expect(within(row).queryByText('Aktif')).toBeNull()
  })

  it('drops a recorded fallback when the authorization scope generation changes', async () => {
    vi.stubGlobal('fetch', vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      if ((init?.method ?? 'GET') === 'GET') return answer({ items: [], searchWindowFull: false })
      const body = JSON.parse(String(init?.body))
      return answer({ ...allocation('Active', '1'), allocationId: body.allocationId }, 201)
    }))

    const first = mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await userEvent.type(within(card).getByLabelText('Etiket kimliği'), LABEL)
    await userEvent.type(within(card).getByLabelText('Etiket sürümü'), '11')
    await userEvent.type(within(card).getByLabelText('Tahsis kg'), '5')
    await userEvent.type(within(card).getByLabelText('Tahsis nedeni'), 'Sipariş için ayırıldı.')
    await userEvent.click(within(card).getByRole('button', { name: 'Tahsis kaydet' }))
    await waitFor(() => expect(within(card).getByText('Aktif')).toBeTruthy())

    first.rerender(<QueryClientProvider client={first.client}><SalesDraftAllocationPanel
      enabled companyId={COMPANY} namespace="ctx-b" routeEntryGeneration={2}
      salesOrderId={ORDER} lines={[line]} /></QueryClientProvider>)

    const remounted = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(remounted).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    expect(within(remounted).queryByText('Aktif')).toBeNull()
  })

  it('renders and reads nothing without the published capability decision', () => {
    const fetch = vi.fn()
    vi.stubGlobal('fetch', fetch)
    mount(false)
    expect(screen.queryByTestId('sales-allocations')).toBeNull()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('fails closed when server truth crosses the company boundary', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => answer({ items: [{ allocation: { ...allocation('Active', '1'),
      companyId: '99999999-9999-4999-8999-999999999999' }, labelVersion: '12' }], searchWindowFull: false })))
    mount()
    expect(await screen.findByText('Tahsis yanıtı doğrulanamadı; hiçbir kayıt gösterilmedi.')).toBeTruthy()
    expect(screen.queryByText(ALLOCATION)).toBeNull()
  })
})
