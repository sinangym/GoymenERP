import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SalesDraftAllocationPanel } from './SalesDraftAllocationPanel'
import { CREATE_ALLOCATION_422_CODES, decodePhysicalAllocation, isGuid } from './allocationContract'
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

const LOT = '80000000-0000-4000-8000-000000000001'
const LOCATION = '90000000-0000-4000-8000-000000000001'
const POSITIONS_PATH = '/api/v1/inventory/positions'

const position = (overrides: Record<string, unknown> = {}) => ({
  companyId: COMPANY, labelId: LABEL, labelVersion: '11', labelCode: 'ETK-001', productId: PRODUCT,
  lot: { id: LOT, heatNumber: 'D-1', coilNumber: null }, heatNumber: 'D-1', coilNumber: null,
  documentStatus: 'Posted', ownerKind: 'Company', ownerId: COMPANY, custodianKind: 'Company', custodianId: COMPANY,
  locationId: LOCATION, physicalKg: '20', freePhysicalKg: '20', blockedPhysicalKg: '0', activeAllocationKg: '0',
  availableKg: '20', ...overrides,
})

type FetchHandler = (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>

/** Answers the picker's Inventory label search; every other request goes to the allocation handler. */
function withPositions(handler: FetchHandler,
  positions: FetchHandler = async () => answer({ items: [position()], searchWindowFull: false })) {
  return vi.fn(async (input: RequestInfo | URL, init?: RequestInit) =>
    new URL(String(input), 'http://localhost').pathname === POSITIONS_PATH ? positions(input, init) : handler(input, init))
}

async function pickLabel(card: HTMLElement) {
  await userEvent.type(within(card).getByLabelText('Etiket kodu ara'), 'ETK')
  await userEvent.click(within(card).getByRole('button', { name: 'Etiketleri getir' }))
  await userEvent.click(await within(card).findByRole('button', { name: 'ETK-001 etiketini seç' }))
}

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

// Read the canonical contract with Node's own fs (it lives outside the web package), as
// src/manufacturing/openapiContract.test.ts does. Vitest runs from the web package.
const nodeProcess = (globalThis as unknown as {
  readonly process: { readonly cwd: () => string; readonly getBuiltinModule: (name: string) => unknown }
}).process
const nodeFs = nodeProcess.getBuiltinModule('node:fs') as { readFileSync(path: string, encoding: 'utf8'): string }
const nodePath = nodeProcess.getBuiltinModule('node:path') as { resolve(...segments: string[]): string }

function declaredCreate422Codes(): readonly string[] {
  const document = JSON.parse(nodeFs.readFileSync(
    nodePath.resolve(nodeProcess.cwd(), '../../../contracts/openapi/goymen-erp-v1.json'), 'utf8')) as {
    readonly paths: Record<string, Record<string, { readonly responses?: Record<string, Record<string, unknown>> }>>
  }
  const codes = document.paths['/api/v1/inventory/physical-allocations']?.post?.responses?.['422']?.['x-erp-problem-codes']
  if (!Array.isArray(codes)) throw new Error('OpenAPI declares no 422 problem codes for the allocation create route.')
  return codes as string[]
}

async function submitCreate(card: HTMLElement) {
  await pickLabel(card)
  await userEvent.type(within(card).getByLabelText('Tahsis kg'), '5')
  await userEvent.type(within(card).getByLabelText('Tahsis nedeni'), 'Sipariş için ayırıldı.')
  await userEvent.click(within(card).getByRole('button', { name: 'Tahsis kaydet' }))
}

function refuseCreateWith(status: number, body: unknown) {
  const fetch = withPositions(async (_input: RequestInfo | URL, init?: RequestInit) => {
    if ((init?.method ?? 'GET') === 'GET') return answer({ items: [], searchWindowFull: false })
    return answer(body, status)
  })
  vi.stubGlobal('fetch', fetch)
  return fetch
}

const OWNER_REFUSAL = 'Bu etiketteki stok şirketin kendi malı ve kendi gözetiminde değil; satış siparişine ayrılamaz. Tahsis kaydedilmedi.'

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
  it('accepts canonical non-nil ids without requiring RFC version bits', () => {
    expect(isGuid(COMPANY)).toBe(true)
    expect(isGuid('00000000-0000-0000-0000-000000000000')).toBe(false)
    expect(decodePhysicalAllocation(allocation('Active', '1'), {
      companyId: COMPANY,
      salesOrderId: ORDER,
      salesOrderLineId: LINE,
      productId: PRODUCT,
      productVersion: '7',
    })).not.toBeNull()
  })

  it('saves, closes, reopens, releases and reopens the same server record', async () => {
    let stored: ReturnType<typeof allocation> | null = null
    let hideRecordedOnNextGet = false
    const calls: { readonly method: string; readonly url: string; readonly body: any; readonly headers: Headers }[] = []
    vi.stubGlobal('fetch', withPositions(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      const method = init?.method ?? 'GET'
      const body = init?.body === undefined ? null : JSON.parse(String(init.body))
      const headers = new Headers(init?.headers)
      calls.push({ method, url, body, headers })
      if (method === 'GET') {
        const visible = hideRecordedOnNextGet ? null : stored
        hideRecordedOnNextGet = false
        return answer({ items: visible === null ? [] : [{ allocation: visible,
          labelVersion: visible.status === 'Active' ? '12' : '13' }], searchWindowFull: false })
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
    await pickLabel(card)
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
    vi.stubGlobal('fetch', withPositions(async (_input: RequestInfo | URL, init?: RequestInit) => {
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
    await pickLabel(card)
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
    vi.stubGlobal('fetch', withPositions(async (_input: RequestInfo | URL, init?: RequestInit) => {
      if ((init?.method ?? 'GET') === 'GET') return answer({ items: [], searchWindowFull: false })
      const body = JSON.parse(String(init?.body))
      return answer({ ...allocation('Active', '1'), allocationId: body.allocationId }, 201)
    }))

    const first = mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await pickLabel(card)
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

  it('drops a recorded fallback and reads again when the product scope changes', async () => {
    const fetch = withPositions(async (_input: RequestInfo | URL, init?: RequestInit) => {
      if ((init?.method ?? 'GET') === 'GET') return answer({ items: [], searchWindowFull: false })
      const body = JSON.parse(String(init?.body))
      return answer({ ...allocation('Active', '1'), allocationId: body.allocationId }, 201)
    })
    vi.stubGlobal('fetch', fetch)

    const first = mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await pickLabel(card)
    await userEvent.type(within(card).getByLabelText('Tahsis kg'), '5')
    await userEvent.type(within(card).getByLabelText('Tahsis nedeni'), 'Sipariş için ayırıldı.')
    await userEvent.click(within(card).getByRole('button', { name: 'Tahsis kaydet' }))
    await waitFor(() => expect(within(card).getByText('Aktif')).toBeTruthy())
    const readsBeforeProductChange = fetch.mock.calls.filter(([, init]) => (init?.method ?? 'GET') === 'GET').length

    const changedLine = {
      ...line,
      productId: '40000000-0000-4000-8000-000000000002',
      productVersion: '8',
    }
    first.rerender(<QueryClientProvider client={first.client}><SalesDraftAllocationPanel
      enabled companyId={COMPANY} namespace="ctx-a" routeEntryGeneration={1}
      salesOrderId={ORDER} lines={[changedLine]} /></QueryClientProvider>)

    const remounted = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(remounted).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    expect(within(remounted).queryByText('Aktif')).toBeNull()
    await waitFor(() => expect(fetch.mock.calls.filter(([, init]) => (init?.method ?? 'GET') === 'GET').length)
      .toBeGreaterThan(readsBeforeProductChange))
  })

  it('declares exactly the 422 problem codes the regenerated OpenAPI contract publishes for create', () => {
    expect([...Object.values(CREATE_ALLOCATION_422_CODES)].sort()).toEqual([...declaredCreate422Codes()].sort())
  })

  it('shows the stock-ownership refusal as a definite refusal with no retry', async () => {
    const fetch = refuseCreateWith(422, { title: 'Unprocessable Entity', status: 422,
      code: CREATE_ALLOCATION_422_CODES.ownerNotCompany })
    mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await submitCreate(card)

    const notice = await within(card).findByTestId('sales-allocation-notice')
    expect(notice.textContent).toBe(OWNER_REFUSAL)
    expect(notice.getAttribute('data-tone')).toBe('error')
    expect(within(card).queryByText('Tahsis işlemi kaydedilmedi.')).toBeNull()
    expect(within(card).queryByRole('button', { name: 'Aynı tahsis denemesini tekrar gönder' })).toBeNull()
    expect(within(card).queryByRole('button', { name: 'Tekrar oku' })).toBeNull()
    expect(within(card).getByRole('button', { name: 'Tahsis kaydet' })).toBeTruthy()
    expect(within(card).queryByText('Aktif')).toBeNull()
    expect(within(card).getByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    expect(fetch.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(1)
    expect(report).toHaveBeenCalledWith(422, { token: 1 })
  })

  it.each([
    ['another declared 422 code', { status: 422, code: CREATE_ALLOCATION_422_CODES.invalidReference }],
    ['an undeclared 422 code', { status: 422, code: 'inventory_something_else' }],
    ['a 422 without a code', { status: 422 }],
  ])('keeps the generic definite failure for %s', async (_name, body) => {
    refuseCreateWith(422, body)
    mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await submitCreate(card)

    const notice = await within(card).findByTestId('sales-allocation-notice')
    expect(notice.textContent).toBe('Tahsis işlemi kaydedilmedi.')
    expect(notice.getAttribute('data-tone')).toBe('error')
    expect(within(card).queryByText(OWNER_REFUSAL)).toBeNull()
    expect(within(card).queryByRole('button', { name: 'Aynı tahsis denemesini tekrar gönder' })).toBeNull()
  })

  it('does not show the stock-ownership text when the code arrives on a non-422 status', async () => {
    refuseCreateWith(409, { status: 409, code: CREATE_ALLOCATION_422_CODES.ownerNotCompany })
    mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await submitCreate(card)

    const notice = await within(card).findByTestId('sales-allocation-notice')
    expect(notice.textContent).toBe('Kayıt güncel sunucu durumuyla çakıştı; tahsisler yeniden okundu.')
    expect(within(card).queryByText(OWNER_REFUSAL)).toBeNull()
  })

  it('renders and reads nothing without the published capability decision', () => {
    const fetch = vi.fn()
    vi.stubGlobal('fetch', fetch)
    mount(false)
    expect(screen.queryByTestId('sales-allocations')).toBeNull()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('fails closed when server truth crosses the company boundary', async () => {
    vi.stubGlobal('fetch', withPositions(async () => answer({ items: [{ allocation: { ...allocation('Active', '1'),
      companyId: '99999999-9999-4999-8999-999999999999' }, labelVersion: '12' }], searchWindowFull: false })))
    mount()
    expect(await screen.findByText('Tahsis yanıtı doğrulanamadı; hiçbir kayıt gösterilmedi.')).toBeTruthy()
    expect(screen.queryByText(ALLOCATION)).toBeNull()
  })
})

describe('UI-13 allocation label picker in the panel', () => {
  it('reads no label until the user searches, then sends the picked label id and version', async () => {
    const posts: any[] = []
    const fetch = withPositions(async (_input, init) => {
      if ((init?.method ?? 'GET') === 'GET') return answer({ items: [], searchWindowFull: false })
      const body = JSON.parse(String(init?.body))
      posts.push(body)
      return answer({ ...allocation('Active', '1'), allocationId: body.allocationId }, 201)
    }, async () => answer({ items: [position(), position({ labelId: '50000000-0000-4000-8000-000000000002',
      labelCode: 'ETK-002', labelVersion: '4', availableKg: '7.5', freePhysicalKg: '20', activeAllocationKg: '12.5' })],
    searchWindowFull: false }))
    vi.stubGlobal('fetch', fetch)

    mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    const searches = () => fetch.mock.calls.filter(([input]) => String(input).includes(POSITIONS_PATH))
    expect(searches()).toHaveLength(0)
    expect(within(card).queryByLabelText('Etiket kimliği')).toBeNull()

    await userEvent.type(within(card).getByLabelText('Etiket kodu ara'), 'ETK{Enter}')
    await userEvent.click(await within(card).findByRole('button', { name: 'ETK-002 etiketini seç' }))
    expect(searches()).toHaveLength(1)
    const url = new URL(String(searches()[0][0]), 'http://localhost')
    expect(Object.fromEntries(url.searchParams)).toEqual({ limit: '25', productId: PRODUCT, q: 'ETK' })
    expect(new Headers(searches()[0][1]?.headers).get('X-Company-Id')).toBe(COMPANY)
    expect(within(card).getByTestId(`sales-allocation-label-selected-${LINE}`).textContent)
      .toContain('ETK-002 · sürüm 4 · kullanılabilir 7.5 kg')
    expect(within(card).getByRole('button', { name: 'ETK-002 etiketi seçildi' }).getAttribute('aria-pressed')).toBe('true')
    expect(within(card).getByRole('button', { name: 'ETK-001 etiketini seç' }).getAttribute('aria-pressed')).toBe('false')

    await userEvent.type(within(card).getByLabelText('Tahsis kg'), '5')
    await userEvent.type(within(card).getByLabelText('Tahsis nedeni'), 'Sipariş için ayırıldı.')
    await userEvent.click(within(card).getByRole('button', { name: 'Tahsis kaydet' }))
    await waitFor(() => expect(posts).toHaveLength(1))
    expect(posts[0]).toMatchObject({ labelId: '50000000-0000-4000-8000-000000000002', expectedLabelVersion: '4',
      salesOrderId: ORDER, salesOrderLineId: LINE, quantityKg: '5' })
    // The recorded allocation makes the picked version stale: the picker starts over from a fresh read.
    await waitFor(() => expect(within(card).getByTestId(`sales-allocation-label-selected-${LINE}`).textContent)
      .toBe('Henüz etiket seçilmedi.'))
    expect(within(card).queryByRole('button', { name: 'ETK-002 etiketini seç' })).toBeNull()
  })

  it('refuses to save without a picked label and sends no command', async () => {
    const fetch = withPositions(async () => answer({ items: [], searchWindowFull: false }))
    vi.stubGlobal('fetch', fetch)
    mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await userEvent.type(within(card).getByLabelText('Tahsis kg'), '5')
    await userEvent.type(within(card).getByLabelText('Tahsis nedeni'), 'Sipariş için ayırıldı.')
    await userEvent.click(within(card).getByRole('button', { name: 'Tahsis kaydet' }))
    expect((await within(card).findByTestId('sales-allocation-notice')).textContent)
      .toBe('Etiket seçimi, miktar ve neden zorunludur.')
    expect(fetch.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(0)
  })

  it('hides the picker while an unverified create waits and retries the stored body with the same key', async () => {
    const posts: { body: string; key: string | null }[] = []
    const fetch = withPositions(async (_input, init) => {
      if ((init?.method ?? 'GET') === 'GET') return answer({ items: [], searchWindowFull: false })
      posts.push({ body: String(init?.body), key: new Headers(init?.headers).get('Idempotency-Key') })
      if (posts.length === 1) return answer({ status: 503 }, 503)
      return answer({ ...allocation('Active', '1'), allocationId: JSON.parse(String(init?.body)).allocationId }, 201)
    })
    vi.stubGlobal('fetch', fetch)
    mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await submitCreate(card)

    const retry = await within(card).findByRole('button', { name: 'Aynı tahsis denemesini tekrar gönder' })
    expect(within(card).queryByTestId(`sales-allocation-label-picker-${LINE}`)).toBeNull()
    await userEvent.click(retry)
    await waitFor(() => expect(posts).toHaveLength(2))
    expect(posts[1]).toEqual(posts[0])
    expect(posts[0].key).toBeTruthy()
    expect(JSON.parse(posts[1].body)).toMatchObject({ labelId: LABEL, expectedLabelVersion: '11' })
    // After the recorded answer the picker is back, empty.
    await waitFor(() => expect(within(card).getByTestId(`sales-allocation-label-selected-${LINE}`).textContent)
      .toBe('Henüz etiket seçilmedi.'))
  })

  it('clears a stale pick after a version conflict', async () => {
    refuseCreateWith(409, { status: 409 })
    mount()
    const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
    expect(await within(card).findByText('Bu sipariş satırı için tahsis yok.')).toBeTruthy()
    await submitCreate(card)
    expect((await within(card).findByTestId('sales-allocation-notice')).textContent)
      .toBe('Kayıt güncel sunucu durumuyla çakıştı; tahsisler yeniden okundu.')
    expect(within(card).getByTestId(`sales-allocation-label-selected-${LINE}`).textContent).toBe('Henüz etiket seçilmedi.')
  })
})
