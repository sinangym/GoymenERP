import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GoodsReceiptPanel } from './GoodsReceiptPanel'
import { recordGoodsReceipt } from './goodsReceiptApi'
import { resetReceiptAttemptsForTests } from './goodsReceiptAttempt'
import { decodeGoodsReceiptPage, decodeGoodsReceiptResponse, receiptQuantityInput, type GoodsReceipt, type GoodsReceiptRequest } from './goodsReceiptContract'
import { GOODS_RECEIPT_TEXT as T } from './goodsReceiptText'
import type { PurchaseOrderDetail } from './contract'

vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: vi.fn() }) }))
const COMPANY = '11111111-1111-1111-1111-111111111111'
const ORDER = '22222222-2222-2222-2222-222222222222'
const LINE = '33333333-3333-3333-3333-333333333333'
const PRODUCT = '44444444-4444-4444-4444-444444444444'
const UNIT = '55555555-5555-5555-5555-555555555555'
const LOCATION = '66666666-6666-6666-6666-666666666666'
const detail: PurchaseOrderDetail = { order: { id: ORDER, companyId: COMPANY, status: 'Approved', version: '3', supplierPartyId: PRODUCT,
  companyAccountId: UNIT, sourceRequestId: null, effectiveOn: '2026-09-25', createdAtUtc: '2026-09-25T09:00:00Z',
  updatedAtUtc: '2026-09-25T09:00:00Z', lineCount: 1 }, lines: [{ lineId: LINE, lineNo: 1, kind: 'General', parentLineId: null,
    sourceRequestLineId: null, productId: PRODUCT, productVersion: '7', unitId: UNIT, ordered: '10', technical: '0', committed: '10', accepted: '0', remaining: '10' }],
  acceptances: [] }
const json = (body: unknown, status = 200, replay = false) => new Response(JSON.stringify(body), {
  status, headers: { 'Content-Type': 'application/json', ...(replay ? { 'Idempotency-Replayed': 'true' } : {}) },
})
function recorded(payload: GoodsReceiptRequest): GoodsReceipt {
  return { companyId: COMPANY, acceptanceId: payload.acceptanceId, purchaseOrderId: ORDER, purchaseOrderLineId: LINE,
    purchaseOrderVersion: '4', productId: PRODUCT, productVersion: '7', unitId: UNIT, acceptedQuantity: payload.acceptedQuantity,
    remainingQuantity: '8', lotId: payload.lotId, labelId: payload.labelId, labelCode: payload.labelCode, labelVersion: '1',
    movementId: payload.movementId, locationId: payload.locationId, measuredQuantityKg: payload.measuredQuantityKg,
    initialBlock: 'Free', occurredAtUtc: payload.occurredAtUtc }
}
function comparison(source: GoodsReceipt) {
  return { companyId: COMPANY, acceptanceId: source.acceptanceId, purchaseOrderId: ORDER, purchaseOrderLineId: LINE,
    purchaseOrderVersion: source.purchaseOrderVersion, productId: PRODUCT, productVersion: '7', unitId: UNIT,
    acceptedQuantity: source.acceptedQuantity, measuredQuantityKg: source.measuredQuantityKg,
    comparisonStatus: 'SameKilogramUnit', acceptedQuantityKg: source.acceptedQuantity, measuredMinusAcceptedKg: '-0.1' }
}
function fixture(unknownFirst = false) {
  const posts: { key: string; body: string }[] = []
  const receipts: GoodsReceipt[] = []
  const comparisonReads: string[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') {
      posts.push({ key: (init.headers as Record<string, string>)['Idempotency-Key'], body: init.body as string })
      const payload = JSON.parse(init.body as string) as GoodsReceiptRequest
      if (receipts.length === 0) receipts.push(recorded(payload))
      if (unknownFirst && posts.length === 1) return json({}, 503)
      return json({ receipt: receipts[0], isReplay: posts.length > 1, originalCorrelationId: 'original' }, 201, posts.length > 1)
    }
    if (url.endsWith('goods-receipt-options')) return json({ source: { companyId: COMPANY, purchaseOrderId: ORDER,
      purchaseOrderLineId: LINE, purchaseOrderVersion: '3', productId: PRODUCT, productVersion: '7', unitId: UNIT, effectiveOn: '2026-09-25' },
    locations: [{ locationId: LOCATION, locationCode: 'D-01', locationName: 'Mal Kabul', siteName: 'Fabrika' }], searchWindowFull: false })
    const compared = receipts.find((receipt) => url.endsWith(`/goods-receipts/${receipt.acceptanceId}/quantity-comparison`))
    if (compared !== undefined) { comparisonReads.push(compared.acceptanceId); return json(comparison(compared)) }
    return json({ items: receipts, searchWindowFull: false })
  }))
  return { posts, receipts, comparisonReads }
}
function mount(enabled = true) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><GoodsReceiptPanel enabled={enabled} binding="actor:session:company"
    detail={detail} onRecorded={vi.fn()} /></QueryClientProvider>)
}
async function fill() {
  await screen.findByRole('option', { name: 'Fabrika / D-01 Mal Kabul' })
  await userEvent.selectOptions(screen.getByLabelText(T.location), LOCATION)
  await userEvent.type(screen.getByLabelText(T.accepted), '2')
  await userEvent.type(screen.getByLabelText(T.measured), '1,9')
  await userEvent.type(screen.getByLabelText(T.label), 'SYNTHETIC-RECEIPT-001')
}
afterEach(() => { cleanup(); resetReceiptAttemptsForTests(); vi.unstubAllGlobals() })

describe('Purchasing goods receipt consumer, synthetic NOT_ENV', () => {
  it('does not expose a receipt form/read when the central control is unpublished', () => {
    fixture(); mount(false)
    expect(screen.queryByText(T.heading)).toBeNull()
    expect(fetch).not.toHaveBeenCalled()
  })
  it('records independent quantities with hidden identities and reopens the same Free receipt without quality release', async () => {
    const server = fixture()
    const page = mount()
    expect(screen.getByText(T.note).textContent).toContain('şimdilik zorunlu değildir')
    await fill()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    await screen.findByRole('status')
    await waitFor(() => expect(server.posts).toHaveLength(1))
    const sent = JSON.parse(server.posts[0].body)
    expect(sent).toMatchObject({ expectedPurchaseOrderVersion: '3', expectedProductVersion: '7', acceptedQuantity: '2', measuredQuantityKg: '1.9', locationId: LOCATION })
    expect(sent).not.toHaveProperty('initialBlock')
    expect(sent).not.toHaveProperty('requiredDocuments')
    expect(sent).not.toHaveProperty('ownerId')
    page.unmount()
    mount()
    expect((await screen.findByTestId(`goods-receipt-${server.receipts[0].acceptanceId}`)).textContent).toContain(T.initialFree)
    const comparisonCard = await screen.findByTestId(`goods-receipt-comparison-${server.receipts[0].acceptanceId}`)
    expect(comparisonCard.textContent).toContain('-0.1')
    expect(server.comparisonReads).toEqual([server.receipts[0].acceptanceId, server.receipts[0].acceptanceId])
    expect(screen.getByText(/SYNTHETIC-RECEIPT-001/).textContent).toContain('SYNTHETIC-RECEIPT-001')
    expect(screen.queryByRole('button', { name: /serbest bırak/i })).toBeNull()
  })
  it('unknown outcome survives closing/reopening and retries the exact key, ids, versions and physical facts', async () => {
    const server = fixture(true)
    const page = mount()
    await fill()
    await userEvent.click(screen.getByRole('button', { name: T.save }))
    await screen.findByText(T.unknown)
    page.unmount()
    mount()
    await screen.findByText(T.unknown)
    await userEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(server.posts).toHaveLength(2))
    expect(server.posts[1]).toEqual(server.posts[0])
    await screen.findByText(new RegExp(T.replayed))
    expect(server.receipts).toHaveLength(1)
  })
  it('accepts only an exact, identity-bound 201 response and preserves replay state', async () => {
    const payload: GoodsReceiptRequest = { expectedPurchaseOrderVersion: '3', expectedProductVersion: '7', acceptanceId: LOCATION,
      acceptedQuantity: '2', measuredQuantityKg: '1.9', locationId: LOCATION, lotId: LINE, labelId: UNIT, labelCode: 'SYNTHETIC',
      heatNumber: null, coilNumber: null, movementId: PRODUCT, occurredAtUtc: '2026-09-25T10:00:00Z' }
    const response = { receipt: recorded(payload), isReplay: true, originalCorrelationId: 'original' }
    vi.stubGlobal('fetch', vi.fn(async () => json(response, 201, true)))
    expect(await recordGoodsReceipt(detail, LINE, payload, 'same-key')).toEqual({ type: 'applied', response })
  })
  it.each([
    ['legacy 200 success', 200, 'matching'],
    ['malformed 201 success', 201, 'malformed'],
    ['wrong-identity 201 success', 201, 'wrong-identity'],
  ] as const)('keeps %s unresolved', async (_name, status, shape) => {
    const payload: GoodsReceiptRequest = { expectedPurchaseOrderVersion: '3', expectedProductVersion: '7', acceptanceId: LOCATION,
      acceptedQuantity: '2', measuredQuantityKg: '1.9', locationId: LOCATION, lotId: LINE, labelId: UNIT, labelCode: 'SYNTHETIC',
      heatNumber: null, coilNumber: null, movementId: PRODUCT, occurredAtUtc: '2026-09-25T10:00:00Z' }
    const matching = { receipt: recorded(payload), isReplay: false, originalCorrelationId: 'original' }
    const body = shape === 'malformed' ? {} : shape === 'wrong-identity'
      ? { ...matching, receipt: { ...matching.receipt, acceptanceId: ORDER } }
      : matching
    vi.stubGlobal('fetch', vi.fn(async () => json(body, status)))
    expect(await recordGoodsReceipt(detail, LINE, payload, 'same-key')).toEqual({ type: 'unknown' })
  })
  it('refuses a new Blocked receipt but preserves the immutable Blocked state on a matching historical replay', () => {
    const request: GoodsReceiptRequest = { expectedPurchaseOrderVersion: '3', expectedProductVersion: '7', acceptanceId: LOCATION,
      acceptedQuantity: '2', measuredQuantityKg: '1.9', locationId: LOCATION, lotId: LINE, labelId: UNIT, labelCode: 'SYNTHETIC',
      heatNumber: null, coilNumber: null, movementId: PRODUCT, occurredAtUtc: '2026-09-25T10:00:00Z' }
    const answer = { receipt: { ...recorded(request), initialBlock: 'Blocked' }, isReplay: false, originalCorrelationId: 'c' }
    expect(decodeGoodsReceiptResponse(answer, detail, LINE, request, false)).toBeNull()
    expect(decodeGoodsReceiptResponse({ ...answer, isReplay: true }, detail, LINE, request, true)?.receipt.initialBlock).toBe('Blocked')
    expect(decodeGoodsReceiptResponse({ ...answer, isReplay: true }, detail, LINE, request, false)).toBeNull()
  })
  it('reopens a historical Blocked receipt without changing its stored state or submitting a release', async () => {
    const server = fixture()
    const request: GoodsReceiptRequest = { expectedPurchaseOrderVersion: '3', expectedProductVersion: '7', acceptanceId: LOCATION,
      acceptedQuantity: '2', measuredQuantityKg: '1.9', locationId: LOCATION, lotId: LINE, labelId: UNIT, labelCode: 'HISTORICAL',
      heatNumber: null, coilNumber: null, movementId: PRODUCT, occurredAtUtc: '2026-09-25T10:00:00Z' }
    server.receipts.push({ ...recorded(request), initialBlock: 'Blocked' })
    mount()
    expect((await screen.findByTestId(`goods-receipt-${LOCATION}`)).textContent).toContain(T.initialBlocked)
    expect(server.posts).toHaveLength(0)
    expect(screen.queryByRole('button', { name: /serbest bırak/i })).toBeNull()
  })
  it('rejects unknown historical block values instead of describing them as Free', () => {
    const request: GoodsReceiptRequest = { expectedPurchaseOrderVersion: '3', expectedProductVersion: '7', acceptanceId: LOCATION,
      acceptedQuantity: '2', measuredQuantityKg: '1.9', locationId: LOCATION, lotId: LINE, labelId: UNIT, labelCode: 'SYNTHETIC',
      heatNumber: null, coilNumber: null, movementId: PRODUCT, occurredAtUtc: '2026-09-25T10:00:00Z' }
    expect(decodeGoodsReceiptPage({ items: [{ ...recorded(request), initialBlock: 'Unknown' }], searchWindowFull: false }, detail)).toBeNull()
  })
  it.each(['0', '-1', '1.0001', '1,2.3', '1000000000000000'])('refuses invalid/rounded quantity %s', (value) => {
    expect(receiptQuantityInput(value)).toBeNull()
  })
})
