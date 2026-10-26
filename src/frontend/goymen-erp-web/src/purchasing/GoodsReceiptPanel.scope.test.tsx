import type { ComponentProps } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { GoodsReceiptPanel } from './GoodsReceiptPanel'
import { resetReceiptAttemptsForTests } from './goodsReceiptAttempt'
import type { GoodsReceipt, GoodsReceiptRequest } from './goodsReceiptContract'
import type { PurchaseOrderDetail } from './contract'
import { GOODS_RECEIPT_TEXT as T } from './goodsReceiptText'

// Synthetic consumer-boundary regressions, NOT_ENV. These do not prove real grants,
// SQL atomicity, document-policy readiness or hosted user acceptance.
const boundary = vi.hoisted(() => ({
  capture: vi.fn(() => ({ token: 1 })),
  report: vi.fn(),
}))
vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => boundary }))

const COMPANY = '11111111-1111-1111-1111-111111111111'
const ORDER = '22222222-2222-2222-2222-222222222222'
const LINE = '33333333-3333-3333-3333-333333333333'
const PRODUCT = '44444444-4444-4444-4444-444444444444'
const UNIT = '55555555-5555-5555-5555-555555555555'
const LOCATION = '66666666-6666-6666-6666-666666666666'
const OTHER_COMPANY = '77777777-7777-7777-7777-777777777777'
const OTHER_ORDER = '88888888-8888-8888-8888-888888888888'
const OTHER_LINE = '99999999-9999-9999-9999-999999999999'
const ACTOR_A = `actor-a:session-a:${COMPANY}`
const ACTOR_B = `actor-b:session-b:${COMPANY}`

const detail: PurchaseOrderDetail = {
  order: { id: ORDER, companyId: COMPANY, status: 'Approved', version: '3', supplierPartyId: PRODUCT,
    companyAccountId: UNIT, sourceRequestId: null, effectiveOn: '2026-09-25', createdAtUtc: '2026-09-25T09:00:00Z',
    updatedAtUtc: '2026-09-25T09:00:00Z', lineCount: 1 },
  lines: [{ lineId: LINE, lineNo: 1, kind: 'General', parentLineId: null, sourceRequestLineId: null,
    productId: PRODUCT, productVersion: '7', unitId: UNIT, ordered: '10', technical: '0', committed: '10', accepted: '0', remaining: '10' }],
  acceptances: [],
}

const json = (body: unknown, status = 200, replay = false) => new Response(JSON.stringify(body), {
  status, headers: { 'Content-Type': 'application/json', ...(replay ? { 'Idempotency-Replayed': 'true' } : {}) },
})
function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((accept) => { resolve = accept })
  return { promise, resolve }
}
function options(source = detail) {
  return { source: { companyId: source.order.companyId, purchaseOrderId: source.order.id,
    purchaseOrderLineId: source.lines[0].lineId, purchaseOrderVersion: source.order.version,
    productId: PRODUCT, productVersion: '7', unitId: UNIT, effectiveOn: source.order.effectiveOn },
  locations: [{ locationId: LOCATION, locationCode: 'D-01', locationName: 'Receipt', siteName: 'SITE' }], searchWindowFull: false }
}
function request(): GoodsReceiptRequest {
  return { expectedPurchaseOrderVersion: '3', expectedProductVersion: '7', acceptanceId: LOCATION,
    acceptedQuantity: '2', measuredQuantityKg: '1.9', locationId: LOCATION, lotId: LINE,
    labelId: UNIT, labelCode: 'PRIVATE-LABEL', heatNumber: null, coilNumber: null,
    movementId: PRODUCT, occurredAtUtc: '2026-09-25T10:00:00Z' }
}
/** P05.1D: Inventory's stored stock quantity of a receipt (`PurchaseGoodsReceiptQuantityV2`, #774), synthetic ids. */
function snapshot(companyId: string, acceptanceId: string, measuredQuantityKg: string) {
  return { companyId, subject: { kind: 'Product', id: PRODUCT },
    source: { kind: 'purchasing.goods-acceptance', id: acceptanceId, version: '4' },
    baseQuantity: { value: measuredQuantityKg, unitId: 'c5ef643e-a6e8-51f3-89f2-8e68b00cb983',
      profileId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', profileVersion: '3', dimension: 'Mass', scale: 3 },
    alternateQuantity: null } as GoodsReceipt['stockItemQuantity']
}
function receipt(payload = request()): GoodsReceipt {
  return { companyId: COMPANY, acceptanceId: payload.acceptanceId, purchaseOrderId: ORDER, purchaseOrderLineId: LINE,
    purchaseOrderVersion: '4', productId: PRODUCT, productVersion: '7', unitId: UNIT,
    acceptedQuantity: payload.acceptedQuantity, remainingQuantity: '8', lotId: payload.lotId, labelId: payload.labelId,
    labelCode: payload.labelCode, labelVersion: '1', movementId: payload.movementId, locationId: payload.locationId,
    measuredQuantityKg: payload.measuredQuantityKg, initialBlock: 'Free', occurredAtUtc: payload.occurredAtUtc,
    stockItemQuantity: snapshot(COMPANY, payload.acceptanceId, payload.measuredQuantityKg) }
}
function comparison(source = receipt()) {
  return { companyId: source.companyId, acceptanceId: source.acceptanceId, purchaseOrderId: source.purchaseOrderId,
    purchaseOrderLineId: source.purchaseOrderLineId, purchaseOrderVersion: source.purchaseOrderVersion,
    productId: source.productId, productVersion: source.productVersion, unitId: source.unitId,
    acceptedQuantity: source.acceptedQuantity, measuredQuantityKg: source.measuredQuantityKg,
    comparisonStatus: 'SameKilogramUnit', acceptedQuantityKg: source.acceptedQuantity, measuredMinusAcceptedKg: '-0.1' }
}
function mount(overrides: Partial<ComponentProps<typeof GoodsReceiptPanel>> = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onRecorded = vi.fn()
  let props = { enabled: true, binding: ACTOR_A, detail, onRecorded, ...overrides }
  const tree = () => <QueryClientProvider client={client}><GoodsReceiptPanel {...props} /></QueryClientProvider>
  const view = render(tree())
  return { ...view, client, onRecorded,
    change(next: Partial<ComponentProps<typeof GoodsReceiptPanel>>) { props = { ...props, ...next }; view.rerender(tree()) },
  }
}
async function fill() {
  await screen.findByRole('option', { name: 'SITE / D-01 Receipt' })
  await userEvent.selectOptions(screen.getByLabelText(T.location), LOCATION)
  await userEvent.type(screen.getByLabelText(T.accepted), '2')
  await userEvent.type(screen.getByLabelText(T.measured), '1.9')
  await userEvent.type(screen.getByLabelText(T.label), 'PRIVATE-LABEL')
}
function reads(url: string) {
  return json(url.endsWith('goods-receipt-options') ? options() : { items: [], searchWindowFull: false })
}
async function settled(client: QueryClient) {
  await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))
  await waitFor(() => expect(client.isFetching()).toBe(0))
}

beforeEach(() => { boundary.capture.mockClear(); boundary.report.mockClear() })
afterEach(() => { cleanup(); resetReceiptAttemptsForTests(); vi.unstubAllGlobals() })

describe('Goods receipt scope and late-result protection, synthetic NOT_ENV', () => {
  it.each([401, 403])('reports a %s read against its auth snapshot without rendering protected payload', async (status) => {
    vi.stubGlobal('fetch', vi.fn(async () => json({ items: [receipt()], searchWindowFull: false }, status)))
    const page = mount()
    await settled(page.client)
    expect(boundary.report).toHaveBeenCalledWith(status, { token: 1 })
    expect(screen.queryByText('PRIVATE-LABEL')).toBeNull()
    expect(screen.queryByRole('option', { name: 'SITE / D-01 Receipt' })).toBeNull()
    expect((screen.getByRole('button', { name: T.save }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('rejects foreign-company history and option facts rather than using them in the current company', async () => {
    const foreign = { ...detail, order: { ...detail.order, companyId: OTHER_COMPANY } }
    vi.stubGlobal('fetch', vi.fn(async (url: string) => json(url.endsWith('goods-receipt-options')
      ? options(foreign) : { items: [{ ...receipt(), companyId: OTHER_COMPANY }], searchWindowFull: false })))
    const page = mount()
    await settled(page.client)
    expect(screen.queryByText('PRIVATE-LABEL')).toBeNull()
    expect(screen.queryByRole('option', { name: 'SITE / D-01 Receipt' })).toBeNull()
    expect((screen.getByRole('button', { name: T.save }) as HTMLButtonElement).disabled).toBe(true)
  })

  it('P05.1D: refuses a history item whose stored stock quantity names another company, even when the receipt does not', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url: string) => json(url.endsWith('goods-receipt-options')
      ? options() : { items: [{ ...receipt(), stockItemQuantity: snapshot(OTHER_COMPANY, LOCATION, '1.9') }], searchWindowFull: false })))
    const page = mount()
    await settled(page.client)
    expect(screen.queryByText('PRIVATE-LABEL')).toBeNull()
    expect(screen.queryByTestId(`goods-receipt-${LOCATION}`)).toBeNull()
    // The receipt form stays bound to the authorized company's own options.
    expect(await screen.findByRole('option', { name: 'SITE / D-01 Receipt' })).toBeTruthy()
  })

  it('P05.1D: a stored stock quantity does not widen authority — a 403 history read still shows nothing of it', async () => {
    vi.stubGlobal('fetch', vi.fn(async (url: string) => (url.endsWith('goods-receipt-options')
      ? json(options()) : json({ items: [receipt()], searchWindowFull: false }, 403))))
    const page = mount()
    await settled(page.client)
    expect(boundary.report).toHaveBeenCalledWith(403, { token: 1 })
    expect(screen.queryByText('PRIVATE-LABEL')).toBeNull()
    expect(screen.queryByText(/1\.9 kg/)).toBeNull()
  })

  it('does not publish a late history answer from the previous company', async () => {
    const oldHistory = deferred<Response>()
    const next: PurchaseOrderDetail = { ...detail, order: { ...detail.order, id: OTHER_ORDER, companyId: OTHER_COMPANY },
      lines: [{ ...detail.lines[0], lineId: OTHER_LINE }] }
    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      if (url.includes(OTHER_ORDER)) return json(url.endsWith('goods-receipt-options') ? options(next) : { items: [], searchWindowFull: false })
      return url.endsWith('goods-receipt-options') ? json(options()) : oldHistory.promise
    }))
    const page = mount()
    await screen.findByRole('option', { name: 'SITE / D-01 Receipt' })
    page.change({ binding: `actor-a:session-a:${OTHER_COMPANY}`, detail: next })
    await screen.findByText(T.empty)
    await act(async () => { oldHistory.resolve(json({ items: [receipt()], searchWindowFull: false })) })
    expect(screen.queryByText('PRIVATE-LABEL')).toBeNull()
    expect(screen.queryByTestId(`goods-receipt-${LOCATION}`)).toBeNull()
    expect(screen.queryByText(T.stockSummary('1.9'))).toBeNull()
    expect(page.onRecorded).not.toHaveBeenCalled()
  })

  it.each<[string, Partial<ComponentProps<typeof GoodsReceiptPanel>>]>([
    ['another actor/session of the same company', { binding: ACTOR_B }],
    ['another company', { binding: `actor-a:session-a:${OTHER_COMPANY}`, detail: { ...detail,
      order: { ...detail.order, id: OTHER_ORDER, companyId: OTHER_COMPANY }, lines: [{ ...detail.lines[0], lineId: OTHER_LINE }] } }],
  ])('P05.1D: a shown save message with its stock quantity disappears when the scope changes to %s', async (_name, next) => {
    const posts: GoodsReceiptRequest[] = []
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method !== 'POST') {
        if (url.includes(OTHER_ORDER)) return json(url.endsWith('goods-receipt-options') ? options(next.detail ?? detail) : { items: [], searchWindowFull: false })
        return reads(url)
      }
      const payload = JSON.parse(init.body as string) as GoodsReceiptRequest
      posts.push(payload)
      return json({ receipt: receipt(payload), isReplay: false, originalCorrelationId: 'original' }, 201)
    }))
    const page = mount()
    await fill()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    expect(await screen.findByText(T.stockSummary('1.9'))).toBeTruthy()
    page.change(next)
    await waitFor(() => expect(screen.queryByText(T.stockSummary('1.9'))).toBeNull())
    expect(screen.queryByText(new RegExp(T.saved))).toBeNull()
    expect(posts).toHaveLength(1)
  })

  it('does not publish a late comparison after the actor binding changes and the fresh read is denied', async () => {
    const oldComparison = deferred<Response>()
    let comparisonCalls = 0
    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      if (url.endsWith('goods-receipt-options')) return json(options())
      if (url.endsWith('/quantity-comparison')) {
        comparisonCalls += 1
        return comparisonCalls === 1 ? oldComparison.promise : json({ code: 'forbidden' }, 403)
      }
      return json({ items: [receipt()], searchWindowFull: false })
    }))
    const page = mount()
    await screen.findByTestId(`goods-receipt-${LOCATION}`)
    await waitFor(() => expect(comparisonCalls).toBe(1))
    page.change({ binding: ACTOR_B })
    await waitFor(() => expect(boundary.report).toHaveBeenCalledWith(403, { token: 1 }))
    await act(async () => { oldComparison.resolve(json(comparison())) })
    expect(screen.queryByTestId(`goods-receipt-comparison-${LOCATION}`)).toBeNull()
    expect(screen.queryByText('-0.1', { exact: true })).toBeNull()
  })

  it('does not acknowledge a late write after the capability has been removed', async () => {
    const response = deferred<Response>()
    const posts: GoodsReceiptRequest[] = []
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method !== 'POST') return reads(url)
      posts.push(JSON.parse(init.body as string) as GoodsReceiptRequest)
      return response.promise
    }))
    const page = mount()
    await fill()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    await waitFor(() => expect(posts).toHaveLength(1))
    page.change({ enabled: false })
    await act(async () => { response.resolve(json({ receipt: receipt(posts[0]), isReplay: false, originalCorrelationId: 'original' }, 201)) })
    expect(screen.queryByText(T.heading)).toBeNull()
    expect(screen.queryByText('PRIVATE-LABEL')).toBeNull()
    expect(page.onRecorded).not.toHaveBeenCalled()
    expect(posts).toHaveLength(1)
  })

  it('keeps an unknown attempt with its original actor/session when the shell remounts another session', async () => {
    const posts: string[] = []
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method !== 'POST') return reads(url)
      posts.push(init.body as string)
      return json({}, 503)
    }))
    const first = mount()
    await fill()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.unknown)
    first.unmount()
    const other = mount({ binding: ACTOR_B })
    await screen.findByRole('option', { name: 'SITE / D-01 Receipt' })
    expect(screen.queryByText(T.unknown)).toBeNull()
    expect(screen.queryByRole('button', { name: T.retry })).toBeNull()
    expect((screen.getByLabelText(T.label) as HTMLInputElement).value).toBe('')
    other.unmount()
    mount()
    await screen.findByText(T.unknown)
    expect((screen.getByLabelText(T.label) as HTMLInputElement).value).toBe('PRIVATE-LABEL')
    expect(screen.getByRole('button', { name: T.retry })).toBeTruthy()
    expect(posts).toHaveLength(1)
  })

  it('withholds stale-version location options instead of silently upgrading the command source', async () => {
    const stale = options()
    vi.stubGlobal('fetch', vi.fn(async (url: string) => json(url.endsWith('goods-receipt-options')
      ? { ...stale, source: { ...stale.source, purchaseOrderVersion: '2' } } : { items: [], searchWindowFull: false })))
    const page = mount()
    await settled(page.client)
    expect(screen.queryByRole('option', { name: 'SITE / D-01 Receipt' })).toBeNull()
    expect((screen.getByRole('button', { name: T.save }) as HTMLButtonElement).disabled).toBe(true)
  })

  it.each([401, 403])('reports a first-write %s refusal without advertising a successful receipt or retry', async (status) => {
    const posts: string[] = []
    vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
      if (init?.method !== 'POST') return reads(url)
      posts.push(init.body as string)
      return json({ code: status === 401 ? 'unauthorized' : 'forbidden', status, title: 'refused' }, status)
    }))
    const page = mount()
    await fill()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.failed)
    expect(boundary.report).toHaveBeenCalledWith(status, { token: 1 })
    expect(page.onRecorded).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: T.retry })).toBeNull()
    expect(posts).toHaveLength(1)
  })
})
