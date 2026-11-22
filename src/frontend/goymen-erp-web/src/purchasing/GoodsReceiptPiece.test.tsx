/** AK-3b (BS-16/BS-17, golden TA4): the piece receipt's theoretical vs weighed kg. Synthetic data only (SENTETIK), NOT_ENV. */
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GoodsReceiptPanel } from './GoodsReceiptPanel'
import { PieceKilogramsComparison } from './PieceKilogramsComparison'
import { resetReceiptAttemptsForTests } from './goodsReceiptAttempt'
import { decodeGoodsReceiptResponse, type GoodsReceipt, type GoodsReceiptRequest } from './goodsReceiptContract'
import { GOODS_RECEIPT_PROBLEM_TEXT, GOODS_RECEIPT_TEXT as T } from './goodsReceiptText'
import { decodePieceKilograms, type PieceKilograms } from './pieceKilogramsContract'
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
    sourceRequestLineId: null, productId: PRODUCT, productVersion: '7', unitId: UNIT, ordered: '100', technical: '0', committed: '100', accepted: '0', remaining: '100' }],
  acceptances: [] }

/** Golden TA4 shape: 40 pieces × 12,345 kg = 493,8 kg theoretical; 509,7 kg weighed → +15,9 kg. */
const outside: PieceKilograms = { pieceCount: 40, kilogramsPerUnit: '12.345', theoreticalQuantityKg: '493.8', measuredQuantityKg: '509.7',
  differenceKg: '15.9', varianceOutcome: 'OutsideTolerance', tolerancePercent: '2' }
const within2: PieceKilograms = { ...outside, measuredQuantityKg: '500.16', differenceKg: '6.36', varianceOutcome: 'WithinTolerance' }
const short: PieceKilograms = { ...outside, measuredQuantityKg: '469.11', differenceKg: '-24.69', varianceOutcome: 'ToleranceNotSet', tolerancePercent: null }
const exact: PieceKilograms = { ...outside, measuredQuantityKg: '493.8', differenceKg: '0', varianceOutcome: 'Exact' }

const json = (body: unknown, status = 200, replay = false) => new Response(JSON.stringify(body), {
  status, headers: { 'Content-Type': 'application/json', ...(replay ? { 'Idempotency-Replayed': 'true' } : {}) },
})
function recorded(payload: GoodsReceiptRequest): GoodsReceipt {
  return { companyId: COMPANY, acceptanceId: payload.acceptanceId, purchaseOrderId: ORDER, purchaseOrderLineId: LINE,
    purchaseOrderVersion: '4', productId: PRODUCT, productVersion: '7', unitId: UNIT, acceptedQuantity: payload.acceptedQuantity,
    remainingQuantity: '60', lotId: payload.lotId, labelId: payload.labelId, labelCode: payload.labelCode, labelVersion: '1',
    movementId: payload.movementId, locationId: payload.locationId, measuredQuantityKg: payload.measuredQuantityKg,
    initialBlock: 'Free', occurredAtUtc: payload.occurredAtUtc }
}
const request: GoodsReceiptRequest = { expectedPurchaseOrderVersion: '3', expectedProductVersion: '7',
  acceptanceId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', acceptedQuantity: '40', measuredQuantityKg: '509.7', locationId: LOCATION,
  lotId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb', labelId: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', labelCode: 'SENTETIK-ADET-001',
  heatNumber: null, coilNumber: null, movementId: 'dddddddd-dddd-4ddd-8ddd-dddddddddddd', occurredAtUtc: '2026-11-22T10:00:00.000Z' }
const answer = (extra: Record<string, unknown> = {}) => ({ receipt: recorded(request), isReplay: false, originalCorrelationId: 'SENTETIK-c', ...extra })

/** The POST answers `respond(payload, attempt)`; reads answer the options, an empty history and nothing else. */
function server(respond: (payload: GoodsReceiptRequest, attempt: number) => Response) {
  const posts: string[] = []
  vi.stubGlobal('fetch', vi.fn(async (url: string, init?: RequestInit) => {
    if (init?.method === 'POST') { posts.push(init.body as string); return respond(JSON.parse(init.body as string) as GoodsReceiptRequest, posts.length) }
    if (url.endsWith('goods-receipt-options')) return json({ source: { companyId: COMPANY, purchaseOrderId: ORDER,
      purchaseOrderLineId: LINE, purchaseOrderVersion: '3', productId: PRODUCT, productVersion: '7', unitId: UNIT, effectiveOn: '2026-09-25' },
    locations: [{ locationId: LOCATION, locationCode: 'D-01', locationName: 'Mal Kabul', siteName: 'Fabrika' }], searchWindowFull: false,
    futureSupply: { companyId: COMPANY, purchaseOrderId: ORDER, purchaseOrderGeneralLineId: LINE, unitId: UNIT, activeAllocatedQuantity: '0' } })
    return json({ items: [], searchWindowFull: false })
  }))
  return posts
}
function mount() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
  return render(<QueryClientProvider client={client}><GoodsReceiptPanel enabled binding="actor:session:company:gen-1"
    attemptBinding="actor:session:company" detail={detail} onRecorded={vi.fn()} /></QueryClientProvider>)
}
async function record(measured: string) {
  await screen.findByRole('option', { name: 'Fabrika / D-01 Mal Kabul' })
  await userEvent.selectOptions(screen.getByLabelText(T.location), LOCATION)
  await userEvent.type(screen.getByLabelText(T.accepted), '40')
  await userEvent.type(screen.getByLabelText(T.measured), measured)
  await userEvent.type(screen.getByLabelText(T.label), 'SENTETIK-ADET-001')
  await userEvent.click(screen.getByRole('button', { name: T.save }))
}
afterEach(() => { cleanup(); resetReceiptAttemptsForTests(); vi.unstubAllGlobals() })

describe('AK-3b piece kilograms decoder', () => {
  it('accepts the server comparison for each outcome exactly as sent', () => {
    for (const value of [outside, within2, short, exact]) expect(decodePieceKilograms(value)).toEqual(value)
    expect(decodePieceKilograms({ ...exact, tolerancePercent: null })).not.toBeNull()
  })

  it.each<[string, unknown]>([
    ['not an object', [outside]],
    ['null', null],
    ['a missing member', (({ tolerancePercent: _dropped, ...rest }) => rest)(outside)],
    ['an extra member', { ...outside, cost: '1.00' }],
    ['a fractional piece count', { ...outside, pieceCount: 40.5 }],
    ['a zero piece count', { ...outside, pieceCount: 0 }],
    ['a text piece count', { ...outside, pieceCount: '40' }],
    ['a numeric kg', { ...outside, measuredQuantityKg: 509.7 }],
    ['a non-canonical kg', { ...outside, theoreticalQuantityKg: '493.80' }],
    ['a zero kg per piece', { ...outside, kilogramsPerUnit: '0' }],
    ['a negative zero difference', { ...exact, differenceKg: '-0' }],
    ['an unknown outcome', { ...outside, varianceOutcome: 'Small' }],
    ['an exact outcome with a difference', { ...outside, varianceOutcome: 'Exact' }],
    ['a difference called exact', { ...exact, differenceKg: '1' }],
    ['a not-set outcome beside a tolerance', { ...short, tolerancePercent: '2' }],
    ['an inside-tolerance outcome without a tolerance', { ...within2, tolerancePercent: null }],
    ['a numeric tolerance', { ...outside, tolerancePercent: 2 }],
  ])('refuses %s as a whole', (_, value) => {
    expect(decodePieceKilograms(value)).toBeNull()
  })
})

describe('AK-3b receipt response with the recorded comparison', () => {
  it('keeps the comparison the server sent and treats its absence as "no comparison"', () => {
    expect(decodeGoodsReceiptResponse(answer({ pieceKilograms: outside }), detail, LINE, request, false)?.pieceKilograms).toEqual(outside)
    const plain = decodeGoodsReceiptResponse(answer(), detail, LINE, request, false)
    expect(plain).not.toBeNull()
    expect(plain?.pieceKilograms).toBeUndefined()
    // A replay of a receipt older than AK-3b carries none; a replay with one shows the recorded value.
    expect(decodeGoodsReceiptResponse(answer({ isReplay: true }), detail, LINE, request, true)?.pieceKilograms).toBeUndefined()
    expect(decodeGoodsReceiptResponse(answer({ isReplay: true, pieceKilograms: outside }), detail, LINE, request, true)?.pieceKilograms).toEqual(outside)
  })

  it.each<[string, unknown]>([
    ['a null comparison', null],
    ['a malformed comparison', { ...outside, varianceOutcome: 'Guessed' }],
    ['a comparison for another piece count', { ...outside, pieceCount: 41 }],
    ['a comparison for another weighed kg', { ...outside, measuredQuantityKg: '509.8', differenceKg: '16' }],
  ])('refuses the whole answer on %s, never a partial receipt', (_, pieceKilograms) => {
    expect(decodeGoodsReceiptResponse(answer({ pieceKilograms }), detail, LINE, request, false)).toBeNull()
  })
})

describe('AK-3b comparison rendering per outcome', () => {
  it('warns outside the tolerance and shows every figure as the server sent it, the difference signed', () => {
    render(<PieceKilogramsComparison comparison={outside} testId="SENTETIK-comparison" />)
    const card = screen.getByTestId('SENTETIK-comparison')
    expect(card.getAttribute('data-outcome')).toBe('OutsideTolerance')
    expect(within(card).getAllByRole('definition').map((cell) => cell.textContent))
      .toEqual(['40', '12.345 kg', '493.8 kg', '509.7 kg', '+15.9 kg', '2', T.pieceOutcomeText.OutsideTolerance])
    expect(within(card).getByRole('alert').textContent).toBe(T.pieceOutsideTolerance)
    expect(within(card).queryByRole('note')).toBeNull()
  })

  it('notes a missing tolerance without an alarm and keeps the negative difference as sent', () => {
    render(<PieceKilogramsComparison comparison={short} testId="SENTETIK-comparison" />)
    const card = screen.getByTestId('SENTETIK-comparison')
    expect(card.textContent).toContain('-24.69 kg')
    expect(card.textContent).toContain(T.pieceToleranceNotSet)
    expect(within(card).getByRole('note').textContent).toBe(T.pieceToleranceNotSetNote)
    expect(T.pieceToleranceNotSetNote).toContain('Ayarlar\'da tolerans tanımlı değil')
    expect(within(card).queryByRole('alert')).toBeNull()
  })

  it.each([['Exact', exact, '0 kg'], ['WithinTolerance', within2, '+6.36 kg']] as const)('shows nothing alarming for %s', (_, comparison, difference) => {
    render(<PieceKilogramsComparison comparison={comparison} testId="SENTETIK-comparison" />)
    const card = screen.getByTestId('SENTETIK-comparison')
    expect(card.textContent).toContain(difference)
    expect(within(card).queryByRole('alert')).toBeNull()
    expect(within(card).queryByRole('note')).toBeNull()
  })
})

describe('AK-3b goods receipt screen', () => {
  it('shows the recorded comparison and the warning after a successful record, and never blocks the receipt', async () => {
    const posts = server((payload) => json({ receipt: recorded(payload), isReplay: false, originalCorrelationId: 'SENTETIK-c',
      pieceKilograms: outside }, 201))
    mount()
    await record('509,7')
    const card = await screen.findByTestId('goods-receipt-line-1-piece-kilograms')
    expect(card.textContent).toContain('493.8 kg')
    expect(card.textContent).toContain('+15.9 kg')
    expect(within(card).getByRole('alert').textContent).toBe(T.pieceOutsideTolerance)
    expect(screen.getByRole('status').textContent).toContain(T.saved)
    expect(posts).toHaveLength(1)
    expect(JSON.parse(posts[0])).toMatchObject({ acceptedQuantity: '40', measuredQuantityKg: '509.7' })
  })

  it('shows no comparison for a kilogram line (the member is absent)', async () => {
    server((payload) => json({ receipt: recorded(payload), isReplay: false, originalCorrelationId: 'SENTETIK-c' }, 201))
    mount()
    await record('509,7')
    await screen.findByRole('status')
    expect(screen.queryByTestId('goods-receipt-line-1-piece-kilograms')).toBeNull()
  })

  it('on a replay shows the value recorded at the first attempt, as sent (503 first, then the replay)', async () => {
    const posts = server((payload, attempt) => attempt === 1 ? json({}, 503)
      : json({ receipt: recorded(payload), isReplay: true, originalCorrelationId: 'SENTETIK-c', pieceKilograms: outside }, 201, true))
    mount()
    await record('509,7')
    await screen.findByText(T.unknown)
    expect(screen.queryByTestId('goods-receipt-line-1-piece-kilograms')).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: T.retry }))
    const card = await screen.findByTestId('goods-receipt-line-1-piece-kilograms')
    expect(card.textContent).toContain('+15.9 kg')
    expect(posts[1]).toEqual(posts[0])
    expect(screen.getByRole('status').textContent).toContain(T.replayed)
  })

  it('keeps a malformed comparison an unknown outcome with the same attempt (fail closed)', async () => {
    const posts = server((payload) => json({ receipt: recorded(payload), isReplay: false, originalCorrelationId: 'SENTETIK-c',
      pieceKilograms: { ...outside, varianceOutcome: 'Guessed' } }, 201))
    mount()
    await record('509,7')
    await screen.findByText(T.unknown)
    expect(screen.queryByTestId('goods-receipt-line-1-piece-kilograms')).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: T.retry }))
    await waitFor(() => expect(posts).toHaveLength(2))
    expect(posts[1]).toEqual(posts[0])
  })

  it.each([
    [400, 'purchasing_receipt_piece_count_invalid'],
    [409, 'purchasing_receipt_theoretical_kilograms_missing'],
  ])('names the piece-line refusal %i %s in user words', async (status, code) => {
    server(() => json({ code }, status))
    mount()
    await record('509,7')
    expect((await screen.findByRole('alert')).textContent).toBe(GOODS_RECEIPT_PROBLEM_TEXT[code])
  })

  it('keeps the generic refusal text for any other code', async () => {
    server(() => json({ code: 'purchasing_acceptance_order_state_conflict' }, 409))
    mount()
    await record('509,7')
    expect((await screen.findByRole('alert')).textContent).toBe(T.failed)
  })
})
