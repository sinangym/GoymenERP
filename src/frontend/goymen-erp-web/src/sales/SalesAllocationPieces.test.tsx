import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SalesDraftAllocationPanel } from './SalesDraftAllocationPanel'
import {
  CREATE_ALLOCATION_422_CODES, CREATE_ALLOCATION_INSUFFICIENT_FREE_PIECES, decodeAllocationSuggestions,
  decodePhysicalAllocation, parseAllocationPieceCount,
} from './allocationContract'
import { ALLOCATION_TEXT as T } from './allocationText'
import type { DraftLine } from './contract'

// KLN-D119 / SAT-D434 (AK-4b): pieces beside kilograms on a physical sales allocation. SENTETIK.
const COMPANY = '10000000-0000-4000-8000-000000000001'
const ORDER = '20000000-0000-4000-8000-000000000001'
const LINE = '30000000-0000-4000-8000-000000000001'
const PRODUCT = '40000000-0000-4000-8000-000000000001'
const LABEL = '50000000-0000-4000-8000-000000000001'
const ALLOCATION = '60000000-0000-4000-8000-000000000001'
const LOT = '80000000-0000-4000-8000-000000000001'
const LOCATION = '90000000-0000-4000-8000-000000000001'
const SUGGESTIONS_PATH = '/api/v1/inventory/physical-allocations/suggestions'
const POSITIONS_PATH = '/api/v1/inventory/positions'

const report = vi.fn()
vi.mock('../shell/useAuthBoundary', () => ({
  useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report }),
}))

const line: DraftLine = {
  lineId: LINE, lineNo: 1, productId: PRODUCT, productVersion: '7', productCode: 'P-1', productName: 'Profil',
  orderUnitId: '70000000-0000-4000-8000-000000000001', quantity: '10', unitPrice: null,
  priceUnitId: null, requestedDeliveryDate: null, note: null, requestedDeliverySlices: null,
  discountPercent: null, priceDisclosure: 'Visible',
}

const scope = { companyId: COMPANY, salesOrderId: ORDER, salesOrderLineId: LINE, productId: PRODUCT, productVersion: '7' }

const allocation = (overrides: Record<string, unknown> = {}) => ({
  allocationId: ALLOCATION, companyId: COMPANY, version: '1', status: 'Active', labelId: LABEL, productId: PRODUCT,
  demandKind: 'sales.order-line', demandId: ORDER, demandLineId: LINE, demandProductVersion: '7',
  allocatedKg: '50', remainingKg: '50', reason: 'SENTETIK adetli tahsis', reservedUntilUtc: null,
  createdAtUtc: '2026-11-22T10:00:00Z', updatedAtUtc: '2026-11-22T10:00:00Z', isSubstitute: false, ...overrides,
})

const suggestion = (overrides: Record<string, unknown> = {}) => ({
  labelId: LABEL, labelVersion: '11', labelCode: 'ETK-001', productId: PRODUCT, productVersion: '7',
  productCode: null, productName: null, kind: 'same-product', isSubstitute: false, unreservedKg: '120',
  maximumDeviationPercent: null, ...overrides,
})

const suggestions = (items: unknown[]) => ({
  companyId: COMPANY, salesOrderId: ORDER, salesOrderLineId: LINE, productId: PRODUCT, productVersion: '7',
  similarProductMode: 'same-product-only', aiSuggestions: 'disabled', truncated: false, items, requestedKg: null,
})

const position = () => ({
  companyId: COMPANY, labelId: LABEL, labelVersion: '11', labelCode: 'ETK-001', productId: PRODUCT,
  lot: { id: LOT, heatNumber: 'D-1', coilNumber: null }, heatNumber: 'D-1', coilNumber: null,
  documentStatus: 'Posted', ownerKind: 'Company', ownerId: COMPANY, custodianKind: 'Company', custodianId: COMPANY,
  locationId: LOCATION, physicalKg: '120', freePhysicalKg: '120', blockedPhysicalKg: '0', activeAllocationKg: '0',
  availableKg: '120',
})

function answer(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

/** One fetch double: the suggestion read, the label search, the allocation list and the create command. */
function install(options: {
  readonly item?: Record<string, unknown>
  readonly create?: (body: Record<string, unknown>) => Response
  readonly list?: () => unknown[]
}) {
  const posts: Record<string, unknown>[] = []
  const fetch = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://localhost')
    if ((init?.method ?? 'GET') === 'POST') {
      const body = JSON.parse(String(init!.body)) as Record<string, unknown>
      posts.push(body)
      return options.create?.(body) ?? answer(allocation({ allocationId: body.allocationId }), 201)
    }
    if (url.pathname === SUGGESTIONS_PATH) return answer(suggestions([options.item ?? suggestion()]))
    if (url.pathname === POSITIONS_PATH) return answer({ items: [position()], searchWindowFull: false })
    return answer({ items: options.list?.() ?? [], searchWindowFull: false })
  })
  vi.stubGlobal('fetch', fetch)
  return { fetch, posts }
}

function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  render(<QueryClientProvider client={client}><SalesDraftAllocationPanel
    enabled canOpenLabelHistory={false} companyId={COMPANY} namespace="ctx-a" routeEntryGeneration={1}
    salesOrderId={ORDER} lines={[line]} /></QueryClientProvider>)
}

async function openCard() {
  mount()
  const card = await screen.findByTestId(`sales-allocation-line-${LINE}`)
  expect(await within(card).findByText(T.empty)).toBeTruthy()
  return card
}

async function chooseSuggestion(card: HTMLElement) {
  await userEvent.click(within(card).getByRole('button', { name: T.suggest }))
  const row = await within(card).findByTestId(`sales-allocation-suggestion-${LABEL}`)
  await userEvent.click(within(row).getByRole('button', { name: `ETK-001 ${T.suggestionChooseAria}` }))
  return row
}

async function fillAndSave(card: HTMLElement, pieces?: string) {
  await userEvent.type(within(card).getByLabelText(T.quantity), '50')
  await userEvent.type(within(card).getByLabelText(T.reason), 'SENTETIK adetli tahsis')
  if (pieces !== undefined) await userEvent.type(within(card).getByLabelText(T.pieceCount), pieces)
  await userEvent.click(within(card).getByRole('button', { name: T.create }))
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

describe('KLN-D119 allocation piece decoders', () => {
  it('reads the piece figures when present and leaves them undefined when absent', () => {
    const withPieces = decodePhysicalAllocation(allocation({ allocatedPieces: 12, remainingPieces: 8 }), scope)
    expect([withPieces?.allocatedPieces, withPieces?.remainingPieces]).toEqual([12, 8])
    const kgOnly = decodePhysicalAllocation(allocation(), scope)
    expect(kgOnly).not.toBeNull()
    expect([kgOnly?.allocatedPieces, kgOnly?.remainingPieces]).toEqual([undefined, undefined])
  })

  it.each([
    ['a null count', { allocatedPieces: null, remainingPieces: 8 }],
    ['a text count', { allocatedPieces: '12', remainingPieces: 8 }],
    ['a fractional count', { allocatedPieces: 12, remainingPieces: 7.5 }],
    ['a negative count', { allocatedPieces: 12, remainingPieces: -1 }],
    ['only the allocated figure', { allocatedPieces: 12 }],
    ['only the remaining figure', { remainingPieces: 8 }],
    ['more remaining than allocated', { allocatedPieces: 8, remainingPieces: 12 }],
  ])('fails closed on %s', (_name, overrides) => {
    expect(decodePhysicalAllocation(allocation(overrides), scope)).toBeNull()
  })

  it('reads a suggestion’s unreserved pieces strictly', () => {
    expect(decodeAllocationSuggestions(suggestions([suggestion({ unreservedPieces: 24 })]), scope)?.items[0].unreservedPieces)
      .toBe(24)
    const kgOnly = decodeAllocationSuggestions(suggestions([suggestion()]), scope)
    expect(kgOnly).not.toBeNull()
    expect(kgOnly?.items[0].unreservedPieces).toBeUndefined()
    for (const bad of [null, '24', 2.5, -1, true]) {
      expect(decodeAllocationSuggestions(suggestions([suggestion({ unreservedPieces: bad })]), scope)).toBeNull()
    }
    expect(decodeAllocationSuggestions(suggestions([suggestion({ unreservedPiece: 24 })]), scope)).toBeNull()
  })

  it('takes only a whole number of at least one as a typed piece count', () => {
    expect(parseAllocationPieceCount('12')).toBe(12)
    for (const bad of ['', '0', '-1', '1.5', '1,5', '01', ' 3', 'on', '99999999999']) {
      expect(parseAllocationPieceCount(bad)).toBeNull()
    }
  })
})

describe('KLN-D119 allocation create with pieces', () => {
  it('asks a required piece count for a piece-tracking suggestion and sends it', async () => {
    const recorded = { current: false }
    const { posts } = install({
      item: suggestion({ unreservedPieces: 24 }),
      create: (body) => {
        recorded.current = true
        return answer(allocation({ allocationId: body.allocationId, allocatedPieces: 12, remainingPieces: 12 }), 201)
      },
    })
    const card = await openCard()
    expect(within(card).queryByLabelText(T.pieceCount)).toBeNull()
    const row = await chooseSuggestion(card)
    expect(within(row).getByTestId(`sales-allocation-suggestion-pieces-${LABEL}`).textContent).toBe(T.pieces(24))
    const field = within(card).getByLabelText(T.pieceCount) as HTMLInputElement
    expect(field.required).toBe(true)
    expect(within(card).getByText(T.pieceCountRequiredHint(24))).toBeTruthy()

    await fillAndSave(card)
    expect((await within(card).findByTestId('sales-allocation-notice')).textContent).toBe(T.pieceCountMissing)
    expect(posts).toHaveLength(0)

    await userEvent.type(within(card).getByLabelText(T.pieceCount), '1.5')
    await userEvent.click(within(card).getByRole('button', { name: T.create }))
    expect(within(card).getByTestId('sales-allocation-notice').textContent).toBe(T.pieceCountValidation)
    expect(posts).toHaveLength(0)

    await userEvent.clear(within(card).getByLabelText(T.pieceCount))
    await userEvent.type(within(card).getByLabelText(T.pieceCount), '25')
    await userEvent.click(within(card).getByRole('button', { name: T.create }))
    expect(within(card).getByTestId('sales-allocation-notice').textContent).toBe(T.pieceCountExceeds(24))
    expect(posts).toHaveLength(0)

    await userEvent.clear(within(card).getByLabelText(T.pieceCount))
    await userEvent.type(within(card).getByLabelText(T.pieceCount), '12')
    await userEvent.click(within(card).getByRole('button', { name: T.create }))
    await waitFor(() => expect(posts).toHaveLength(1))
    expect(posts[0]).toMatchObject({ labelId: LABEL, expectedLabelVersion: '11', quantityKg: '50', pieceCount: 12 })
    expect((await within(card).findByTestId(`sales-allocation-allocated-pieces-${posts[0].allocationId}`)).textContent)
      .toBe(T.pieces(12))
    expect(within(card).getByTestId(`sales-allocation-remaining-pieces-${posts[0].allocationId}`).textContent)
      .toBe(T.pieces(12))
    expect(recorded.current).toBe(true)
  })

  it('shows no piece field and sends no piece count for a kg-only suggestion', async () => {
    const { posts } = install({})
    const card = await openCard()
    const row = await chooseSuggestion(card)
    expect(within(row).queryByTestId(`sales-allocation-suggestion-pieces-${LABEL}`)).toBeNull()
    expect(within(card).queryByLabelText(T.pieceCount)).toBeNull()
    await fillAndSave(card)
    await waitFor(() => expect(posts).toHaveLength(1))
    expect('pieceCount' in posts[0]).toBe(false)
    const recordedRow = await within(card).findByTestId(`sales-allocation-${posts[0].allocationId}`)
    expect(within(recordedRow).queryByText(/adet/)).toBeNull()
  })

  it('leaves the piece count to the server for a label picked from the search, which carries no piece figure', async () => {
    const { posts } = install({
      create: () => answer({ title: 'Unprocessable Entity', status: 422, code: CREATE_ALLOCATION_422_CODES.pieceCountRequired }, 422),
    })
    const card = await openCard()
    await userEvent.click(within(card).getByRole('button', { name: T.labelSearchSubmit }))
    await userEvent.click(await within(card).findByRole('button', { name: `ETK-001 ${T.labelChooseAria}` }))
    const field = within(card).getByLabelText(T.pieceCount) as HTMLInputElement
    expect(field.required).toBe(false)
    expect(within(card).getByText(T.pieceCountUnknownHint)).toBeTruthy()

    await fillAndSave(card)
    await waitFor(() => expect(posts).toHaveLength(1))
    expect('pieceCount' in posts[0]).toBe(false)
    expect((await within(card).findByTestId('sales-allocation-notice')).textContent).toBe(T.pieceCountRequired)

    await userEvent.type(within(card).getByLabelText(T.pieceCount), '6')
    await userEvent.click(within(card).getByRole('button', { name: T.create }))
    await waitFor(() => expect(posts).toHaveLength(2))
    expect(posts[1]).toMatchObject({ pieceCount: 6 })
  })

  it('shows the pieces an existing allocation reserved and still holds', async () => {
    install({ list: () => [{ allocation: allocation({ allocatedPieces: 12, remainingPieces: 5 }), labelVersion: '12' }] })
    mount()
    const row = await screen.findByTestId(`sales-allocation-${ALLOCATION}`)
    expect(within(row).getByTestId(`sales-allocation-allocated-pieces-${ALLOCATION}`).textContent).toBe(T.pieces(12))
    expect(within(row).getByTestId(`sales-allocation-remaining-pieces-${ALLOCATION}`).textContent).toBe(T.pieces(5))
  })

  it('shows the malformed state and no row when a listed allocation carries a mistyped piece figure', async () => {
    install({ list: () => [{ allocation: allocation({ allocatedPieces: '12', remainingPieces: 5 }), labelVersion: '12' }] })
    mount()
    expect(await screen.findByText(T.malformed)).toBeTruthy()
    expect(screen.queryByTestId(`sales-allocation-${ALLOCATION}`)).toBeNull()
  })

  it.each([
    [422, CREATE_ALLOCATION_422_CODES.pieceCountRequired, T.pieceCountRequired],
    [422, CREATE_ALLOCATION_422_CODES.pieceCountNotTracked, T.pieceCountNotTracked],
    [422, CREATE_ALLOCATION_422_CODES.pieceCountInvalid, T.pieceCountInvalid],
    [409, CREATE_ALLOCATION_INSUFFICIENT_FREE_PIECES, T.insufficientFreePieces],
  ])('tells the %i %s refusal in Turkish as a definite refusal', async (status, code, text) => {
    const { posts } = install({
      item: suggestion({ unreservedPieces: 24 }),
      create: () => answer({ title: 'Refused', status, code }, status),
    })
    const card = await openCard()
    await chooseSuggestion(card)
    await fillAndSave(card, '12')
    await waitFor(() => expect(posts).toHaveLength(1))
    const notice = await within(card).findByTestId('sales-allocation-notice')
    await waitFor(() => expect(notice.textContent).toBe(text))
    expect(notice.getAttribute('data-tone')).toBe('error')
    expect(text).not.toContain(code)
    expect(within(card).queryByRole('button', { name: T.retryCreate })).toBeNull()
  })

  it('keeps the unknown outcome and the same body, pieces included, when the service is unavailable', async () => {
    const keys: (string | null)[] = []
    const { posts, fetch } = install({
      item: suggestion({ unreservedPieces: 24 }),
      create: () => answer({ title: 'Service Unavailable', status: 503 }, 503),
    })
    const card = await openCard()
    await chooseSuggestion(card)
    await fillAndSave(card, '12')
    expect((await within(card).findByTestId('sales-allocation-notice')).textContent).toBe(T.unknown)
    await userEvent.click(within(card).getByRole('button', { name: T.retryCreate }))
    await waitFor(() => expect(posts).toHaveLength(2))
    for (const [, init] of fetch.mock.calls) {
      if (init?.method === 'POST') keys.push(new Headers(init.headers).get('Idempotency-Key'))
    }
    expect(keys[0]).not.toBeNull()
    expect(keys[1]).toBe(keys[0])
    expect(posts[1]).toEqual(posts[0])
    expect(posts[1].pieceCount).toBe(12)
  })
})
