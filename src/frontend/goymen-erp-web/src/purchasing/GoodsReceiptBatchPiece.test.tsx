/** AK-3b (KLN-D119; ALC-S16): the bulk goods receipt of a piece-unit line. Synthetic data only (SENTETIK), NOT_ENV. */
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { GoodsReceiptBatchPanel } from './GoodsReceiptBatchPanel'
import { submitGoodsReceiptBatch } from './goodsReceiptBatchApi'
import { buildGoodsReceiptBatchLabels, decodeGoodsReceiptBatch, decodeGoodsReceiptBatchPage, decodeGoodsReceiptBatchResponse,
  pieceCountInput, type GoodsReceiptBatchLabelDraft, type SubmitGoodsReceiptBatchRequest } from './goodsReceiptBatchContract'
import { GOODS_RECEIPT_BATCH_PROBLEM_TEXT, GOODS_RECEIPT_BATCH_TEXT as T, GOODS_RECEIPT_PIECE_VARIANCE_TEXT,
  goodsReceiptBatchProblemMessages } from './goodsReceiptBatchText'
import { GOODS_RECEIPT_TEXT } from './goodsReceiptText'

vi.mock('../shell/useAuthBoundary', () => ({ useAuthBoundary: () => ({ capture: () => ({ token: 1 }), report: vi.fn() }) }))

const COMPANY = '22222222-2222-4222-8222-222222222222'
const ORDER = '11111111-1111-4111-8111-111111111111'
const LINE = '77777777-7777-4777-8777-777777777777'
const BATCH = '99999999-9999-4999-8999-999999999999'
const LOCATION = '33333333-3333-4333-8333-333333333333'
const hex = (lineNo: number) => String(lineNo).padStart(12, '0')
const ids = (lineNo: number) => ({ acceptanceId: `aaaaaaaa-aaaa-4aaa-8aaa-${hex(lineNo)}`, lotId: `bbbbbbbb-bbbb-4bbb-8bbb-${hex(lineNo)}`,
  labelId: `cccccccc-cccc-4ccc-8ccc-${hex(lineNo)}`, movementId: `dddddddd-dddd-4ddd-8ddd-${hex(lineNo)}` })

function label(lineNo: number, over: Record<string, unknown> = {}) {
  return { lineNo, ...ids(lineNo), labelCode: `SENTETIK-ETK-${lineNo}`, labelCodeSource: 'SupplierLabelNumber',
    supplierLabelNumber: `SENTETIK-ETK-${lineNo}`, measuredQuantityKg: '254.85', heatNumber: null, coilNumber: null, pieceCount: 20, ...over }
}
/** 40 pieces × 12,345 kg = 493,8 kg theoretical; 2 × 254,85 = 509,7 kg weighed → +15,9 kg, outside a 2 % tolerance — still Posted. */
const comparison = { pieceCount: 40, kilogramsPerUnit: '12.345', theoreticalQuantityKg: '493.8', measuredQuantityKg: '509.7',
  differenceKg: '15.9', varianceOutcome: 'OutsideTolerance', tolerancePercent: '2' }
function pieceBatch(over: Record<string, unknown> = {}) {
  return { companyId: COMPANY, batchId: BATCH, purchaseOrderId: ORDER, purchaseOrderLineId: LINE, status: 'Posted', version: '1',
    expectedQuantityKg: '493.8', receivedQuantityKg: '509.7', differenceKg: '15.9', varianceOutcome: 'OutsideTolerance',
    tolerancePercent: '2', varianceAcknowledged: false, locationId: LOCATION, occurredAtUtc: '2026-11-22T06:00:00Z',
    preparedBy: 'SENTETIK-hazirlayan', preparedAtUtc: '2026-11-22T06:01:00Z', decidedBy: null, decidedAtUtc: null,
    decisionReason: null, labels: [label(1), label(2)], pieceKilograms: comparison, ...over }
}
const kgBatch = () => {
  const { pieceKilograms: _none, ...rest } = pieceBatch({ expectedQuantityKg: '500', receivedQuantityKg: '509.7', differenceKg: '9.7',
    varianceOutcome: 'WithinTolerance', varianceAcknowledged: true,
    labels: [label(1), label(2)].map(({ pieceCount: _pieces, ...item }) => item) })
  return rest
}
const row = (over: Partial<GoodsReceiptBatchLabelDraft> = {}): GoodsReceiptBatchLabelDraft => ({ supplierLabelNumber: 'SENTETIK-ETK-1',
  labelCode: '', measuredQuantityKg: '254,85', pieceCount: '20', heatNumber: '', coilNumber: '', ...over })
function sequence() { let next = 0; return () => `eeeeeeee-eeee-4eee-8eee-${hex(++next)}` }

afterEach(() => { cleanup(); vi.unstubAllGlobals() })

describe('AK-3b piece bulk receipt decoder', () => {
  it('accepts a posted piece batch outside the tolerance without an acknowledgement or an approver', () => {
    expect(decodeGoodsReceiptBatch(pieceBatch(), COMPANY, ORDER)).toEqual(pieceBatch())
    expect(decodeGoodsReceiptBatchPage({ items: [pieceBatch()], searchWindowFull: false }, COMPANY, ORDER)?.items[0].labels[0].pieceCount).toBe(20)
    // Inside the tolerance and never acknowledged: a piece batch needs no acknowledgement.
    expect(decodeGoodsReceiptBatch(pieceBatch({ receivedQuantityKg: '500.16', differenceKg: '6.36', varianceOutcome: 'WithinTolerance',
      labels: [label(1, { measuredQuantityKg: '250.08' }), label(2, { measuredQuantityKg: '250.08' })],
      pieceKilograms: { ...comparison, measuredQuantityKg: '500.16', differenceKg: '6.36', varianceOutcome: 'WithinTolerance' } }), COMPANY, ORDER))
      .not.toBeNull()
    expect(decodeGoodsReceiptBatch(pieceBatch({ tolerancePercent: null, varianceOutcome: 'ToleranceNotSet',
      pieceKilograms: { ...comparison, tolerancePercent: null, varianceOutcome: 'ToleranceNotSet' } }), COMPANY, ORDER)).not.toBeNull()
  })

  it('still accepts a kilogram batch without any piece member', () => {
    expect(decodeGoodsReceiptBatch(kgBatch(), COMPANY, ORDER)).toEqual(kgBatch())
  })

  it.each<[string, unknown]>([
    ['a null comparison', pieceBatch({ pieceKilograms: null })],
    ['a malformed comparison', pieceBatch({ pieceKilograms: { ...comparison, varianceOutcome: 'Guessed' } })],
    ['an extra comparison member', pieceBatch({ pieceKilograms: { ...comparison, cost: '1' } })],
    ['a piece batch waiting for approval', pieceBatch({ status: 'PendingApproval' })],
    ['a decided piece batch', pieceBatch({ decidedBy: 'SENTETIK-onaylayan', decidedAtUtc: '2026-11-22T07:00:00Z' })],
    ['a comparison with another theoretical kg', pieceBatch({ pieceKilograms: { ...comparison, theoreticalQuantityKg: '493.9' } })],
    ['a comparison with another weighed kg', pieceBatch({ pieceKilograms: { ...comparison, measuredQuantityKg: '509.8' } })],
    ['a comparison with another difference', pieceBatch({ pieceKilograms: { ...comparison, differenceKg: '16' } })],
    ['a comparison with another outcome', pieceBatch({ pieceKilograms: { ...comparison, varianceOutcome: 'WithinTolerance' } })],
    ['a comparison with another tolerance', pieceBatch({ pieceKilograms: { ...comparison, tolerancePercent: '3' } })],
    ['a comparison whose pieces are not the labels\' sum', pieceBatch({ pieceKilograms: { ...comparison, pieceCount: 41 } })],
    ['a piece batch label without its piece count', pieceBatch({ labels: [label(1), (({ pieceCount: _p, ...item }) => item)(label(2))] })],
    ['a fractional piece count', pieceBatch({ labels: [label(1, { pieceCount: 19.5 }), label(2, { pieceCount: 20.5 })] })],
    ['a text piece count', pieceBatch({ labels: [label(1, { pieceCount: '20' }), label(2)] })],
    ['a zero piece count', pieceBatch({ labels: [label(1, { pieceCount: 0 }), label(2, { pieceCount: 40 })] })],
    ['a kilogram batch whose label carries a piece count', { ...kgBatch(), labels: [label(1), label(2)] }],
  ])('refuses %s as a whole', (_, value) => {
    expect(decodeGoodsReceiptBatch(value, COMPANY, ORDER)).toBeNull()
  })
})

describe('AK-3b piece bulk receipt request', () => {
  it('reads a whole positive piece count as canonical text and nothing else', () => {
    expect(pieceCountInput(' 40 ')).toBe('40')
    expect(pieceCountInput('007')).toBe('7')
    expect(pieceCountInput('2147483647')).toBe('2147483647')
    for (const bad of ['', '0', '000', '-1', '+3', '2.5', '2,5', '40.0', '1e3', '4 0', 'kırk', '2147483648', '99999999999']) {
      expect(pieceCountInput(bad)).toBeNull()
    }
  })

  it('sends acceptedQuantity for every label of a piece line, as whole canonical text, beside the weighed kg', () => {
    const built = buildGoodsReceiptBatchLabels([row(), row({ supplierLabelNumber: '', labelCode: 'SENTETIK-GR-2', pieceCount: '020', heatNumber: ' SENTETIK-H ' })],
      true, sequence(), '100')
    expect(built).toEqual({ ok: true, totalPieces: '40', labels: [
      { acceptanceId: `eeeeeeee-eeee-4eee-8eee-${hex(1)}`, lotId: `eeeeeeee-eeee-4eee-8eee-${hex(2)}`, labelId: `eeeeeeee-eeee-4eee-8eee-${hex(3)}`,
        movementId: `eeeeeeee-eeee-4eee-8eee-${hex(4)}`, supplierLabelNumber: 'SENTETIK-ETK-1', labelCode: null, measuredQuantityKg: '254.85',
        heatNumber: null, coilNumber: null, acceptedQuantity: '20' },
      { acceptanceId: `eeeeeeee-eeee-4eee-8eee-${hex(5)}`, lotId: `eeeeeeee-eeee-4eee-8eee-${hex(6)}`, labelId: `eeeeeeee-eeee-4eee-8eee-${hex(7)}`,
        movementId: `eeeeeeee-eeee-4eee-8eee-${hex(8)}`, supplierLabelNumber: null, labelCode: 'SENTETIK-GR-2', measuredQuantityKg: '254.85',
        heatNumber: 'SENTETIK-H', coilNumber: null, acceptedQuantity: '20' },
    ] })
  })

  it('leaves acceptedQuantity out entirely for a kilogram line — absent, not null and not ""', () => {
    const built = buildGoodsReceiptBatchLabels([row({ pieceCount: '20' }), row({ pieceCount: '' })], false, sequence())
    expect(built.ok).toBe(true)
    if (!built.ok) return
    expect(built.totalPieces).toBeNull()
    for (const item of built.labels) expect(Object.hasOwn(item, 'acceptedQuantity')).toBe(false)
    expect(JSON.stringify(built.labels)).not.toContain('acceptedQuantity')
  })

  it.each(['', '0', '2,5', '2.5', '-3', 'on'])('refuses the piece count "%s" on a piece line and names the row', (pieceCount) => {
    expect(buildGoodsReceiptBatchLabels([row(), row({ pieceCount })], true, sequence()))
      .toEqual({ ok: false, exceedsRemaining: false, errors: [{ index: 1, field: 'acceptedQuantity' }] })
  })

  it('refuses a missing or malformed weighed kg whatever the line unit, and an empty batch', () => {
    expect(buildGoodsReceiptBatchLabels([row({ measuredQuantityKg: '' })], true, sequence()))
      .toEqual({ ok: false, exceedsRemaining: false, errors: [{ index: 0, field: 'measuredQuantityKg' }] })
    expect(buildGoodsReceiptBatchLabels([row({ measuredQuantityKg: '1,2345' })], false, sequence()).ok).toBe(false)
    expect(buildGoodsReceiptBatchLabels([row({ measuredQuantityKg: '0' })], false, sequence()).ok).toBe(false)
    expect(buildGoodsReceiptBatchLabels([], true, sequence()).ok).toBe(false)
  })

  it('checks the total pieces against the remaining pieces before sending (the server stays the authority)', () => {
    expect(buildGoodsReceiptBatchLabels([row(), row()], true, sequence(), '40')).toMatchObject({ ok: true, totalPieces: '40' })
    expect(buildGoodsReceiptBatchLabels([row(), row({ pieceCount: '21' })], true, sequence(), '40'))
      .toEqual({ ok: false, exceedsRemaining: true, errors: [] })
    // A kilogram line is never measured in pieces.
    expect(buildGoodsReceiptBatchLabels([row(), row()], false, sequence(), '1').ok).toBe(true)
  })
})

describe('AK-3b piece bulk receipt submit', () => {
  const labels = [1, 2].map((lineNo) => ({ ...ids(lineNo), supplierLabelNumber: `SENTETIK-ETK-${lineNo}`, labelCode: null,
    measuredQuantityKg: '254.85', heatNumber: null, coilNumber: null, acceptedQuantity: '20' }))
  const request: SubmitGoodsReceiptBatchRequest = { expectedPurchaseOrderVersion: '3', expectedProductVersion: '7', batchId: BATCH,
    locationId: LOCATION, occurredAtUtc: '2026-11-22T06:00:00.000Z', varianceAcknowledged: false, labels }
  const kgRequest: SubmitGoodsReceiptBatchRequest = { ...request, varianceAcknowledged: true,
    labels: labels.map(({ acceptedQuantity: _none, ...item }) => item) }
  const answer = (batch: unknown = pieceBatch(), extra: Record<string, unknown> = {}) =>
    ({ batch, isReplay: false, originalCorrelationId: 'SENTETIK-c', futureSupplyReleases: [], ...extra })

  it('decodes the posted piece batch that was sent and refuses any other', () => {
    expect(decodeGoodsReceiptBatchResponse(answer(), COMPANY, ORDER, LINE, request, false)?.batch.pieceKilograms).toEqual(comparison)
    expect(decodeGoodsReceiptBatchResponse(answer(kgBatch()), COMPANY, ORDER, LINE, kgRequest, false)?.batch.pieceKilograms).toBeUndefined()
    expect(decodeGoodsReceiptBatchResponse(answer(), COMPANY, ORDER, LINE, request, true)).toBeNull()
    expect(decodeGoodsReceiptBatchResponse(answer(pieceBatch(), { extra: 1 }), COMPANY, ORDER, LINE, request, false)).toBeNull()
    expect(decodeGoodsReceiptBatchResponse(answer(pieceBatch({ batchId: LOCATION })), COMPANY, ORDER, LINE, request, false)).toBeNull()
    expect(decodeGoodsReceiptBatchResponse(answer(), COMPANY, ORDER, LOCATION, request, false)).toBeNull()
    // A kilogram answer to a piece request, a piece answer to a kilogram request, another piece count.
    expect(decodeGoodsReceiptBatchResponse(answer(kgBatch()), COMPANY, ORDER, LINE, request, false)).toBeNull()
    expect(decodeGoodsReceiptBatchResponse(answer(), COMPANY, ORDER, LINE, kgRequest, false)).toBeNull()
    expect(decodeGoodsReceiptBatchResponse(answer(pieceBatch({ labels: [label(1, { pieceCount: 19 }), label(2, { pieceCount: 21 })] })),
      COMPANY, ORDER, LINE, request, false)).toBeNull()
  })

  it('posts the body exactly as built with the idempotency key and answers the applied batch', async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(answer()), { status: 201 }))
    vi.stubGlobal('fetch', fetchMock)
    const outcome = await submitGoodsReceiptBatch(COMPANY, ORDER, LINE, request, 'SENTETIK-key')
    expect(outcome.type).toBe('applied')
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit]
    expect(url.endsWith(`/purchasing/orders/${ORDER}/lines/${LINE}/goods-receipt-batches`)).toBe(true)
    expect(init.method).toBe('POST')
    expect((init.headers as Record<string, string>)['Idempotency-Key']).toBe('SENTETIK-key')
    expect(JSON.parse(init.body as string)).toEqual(request)
    expect((JSON.parse(init.body as string) as SubmitGoodsReceiptBatchRequest).labels.every((item) => item.acceptedQuantity === '20')).toBe(true)
  })

  it('a kilogram request goes out without the member', async () => {
    const fetchMock = vi.fn(async () => new Response(JSON.stringify(answer(kgBatch())), { status: 201 }))
    vi.stubGlobal('fetch', fetchMock)
    expect((await submitGoodsReceiptBatch(COMPANY, ORDER, LINE, kgRequest, 'SENTETIK-key')).type).toBe('applied')
    expect((fetchMock.mock.calls[0] as unknown as [string, RequestInit])[1].body as string).not.toContain('acceptedQuantity')
  })

  it('reads a refusal with its code and field problems, and keeps 503, a malformed 201 and no answer unknown', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ code: 'purchasing_receipt_piece_count_invalid',
      errors: [{ field: 'labels[1].acceptedQuantity', code: 'purchasing_receipt_piece_count_invalid' }] }), { status: 400 }))
    expect(await submitGoodsReceiptBatch(COMPANY, ORDER, LINE, request, 'k')).toEqual({ type: 'failed', status: 400,
      code: 'purchasing_receipt_piece_count_invalid', fields: [{ field: 'labels[1].acceptedQuantity', code: 'purchasing_receipt_piece_count_invalid' }] })
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({ code: 'purchasing_goods_receipt_batch_exceeds_remaining' }), { status: 409 }))
    expect(await submitGoodsReceiptBatch(COMPANY, ORDER, LINE, request, 'k')).toEqual({ type: 'failed', status: 409,
      code: 'purchasing_goods_receipt_batch_exceeds_remaining', fields: [] })
    fetchMock.mockResolvedValueOnce(new Response('{}', { status: 503 }))
    expect(await submitGoodsReceiptBatch(COMPANY, ORDER, LINE, request, 'k')).toEqual({ type: 'unknown', reason: 'service-unavailable' })
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify(answer(pieceBatch({ status: 'PendingApproval' }))), { status: 201 }))
    expect(await submitGoodsReceiptBatch(COMPANY, ORDER, LINE, request, 'k')).toEqual({ type: 'unknown', reason: 'malformed' })
    fetchMock.mockRejectedValueOnce(new TypeError('SENTETIK no answer'))
    expect((await submitGoodsReceiptBatch(COMPANY, ORDER, LINE, request, 'k')).type).toBe('unknown')
  })

  it('maps the problem codes to Turkish messages, field-level where the server named a label', () => {
    expect(goodsReceiptBatchProblemMessages(400, 'purchasing_receipt_piece_count_invalid', [
      { field: 'labels[0].acceptedQuantity', code: 'purchasing_receipt_piece_count_invalid' },
      { field: 'labels[2].acceptedQuantity', code: 'purchasing_receipt_piece_count_invalid' },
    ])).toEqual([
      T.labelProblem(1, GOODS_RECEIPT_BATCH_PROBLEM_TEXT.purchasing_receipt_piece_count_invalid),
      T.labelProblem(3, GOODS_RECEIPT_BATCH_PROBLEM_TEXT.purchasing_receipt_piece_count_invalid),
    ])
    expect(goodsReceiptBatchProblemMessages(400, 'purchasing_acceptance_measured_kg_mismatch',
      [{ field: 'labels[1].acceptedQuantity', code: 'purchasing_acceptance_measured_kg_mismatch' }]))
      .toEqual([T.labelProblem(2, GOODS_RECEIPT_BATCH_PROBLEM_TEXT.purchasing_acceptance_measured_kg_mismatch)])
    expect(goodsReceiptBatchProblemMessages(409, 'purchasing_receipt_theoretical_kilograms_missing', []))
      .toEqual([GOODS_RECEIPT_BATCH_PROBLEM_TEXT.purchasing_receipt_theoretical_kilograms_missing])
    expect(goodsReceiptBatchProblemMessages(409, 'purchasing_goods_receipt_batch_exceeds_remaining',
      [{ field: 'labels', code: 'purchasing_goods_receipt_batch_exceeds_remaining' }]))
      .toEqual([GOODS_RECEIPT_BATCH_PROBLEM_TEXT.purchasing_goods_receipt_batch_exceeds_remaining])
    expect(goodsReceiptBatchProblemMessages(409, 'purchasing_version_conflict', [])).toEqual([T.submitFailed('purchasing_version_conflict')])
    expect(goodsReceiptBatchProblemMessages(422, null, [])).toEqual([T.submitFailed(422)])
  })
})

describe('AK-3b piece batch in the bulk goods receipt list', () => {
  function mount(body: unknown, status = 200) {
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify(body), { status })))
    const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0, gcTime: 0 } } })
    render(<QueryClientProvider client={client}><GoodsReceiptBatchPanel fenceKey="SENTETIK-fence" companyId={COMPANY} orderId={ORDER}
      orderVersion="3" /></QueryClientProvider>)
    fireEvent.click(screen.getByRole('button', { name: T.show }))
  }

  it('shows each label\'s piece count, the theoretical kg and the server\'s warning — no acknowledgement or approval wording', async () => {
    mount({ items: [pieceBatch()], searchWindowFull: false })
    const card = await screen.findByTestId('purchase-order-goods-receipt-batch-1')
    const table = within(card).getByRole('table', { name: T.labelsCaption(1) })
    expect(within(table).getAllByRole('columnheader').map((cell) => cell.textContent)).toContain(T.pieceCount)
    expect(within(table).getAllByRole('row').slice(1).map((line) => within(line).getAllByRole('cell')[4].textContent)).toEqual(['20', '20'])
    expect(card.textContent).toContain(T.theoreticalKg)
    expect(card.textContent).not.toContain(T.expectedKg)
    expect(card.textContent).not.toContain(T.acknowledged)
    expect(card.textContent).toContain(GOODS_RECEIPT_PIECE_VARIANCE_TEXT.OutsideTolerance)
    expect(card.textContent).toContain(T.pieceBatchNote)
    const compared = within(card).getByTestId('purchase-order-goods-receipt-batch-1-piece-kilograms')
    expect(compared.textContent).toContain('+15.9 kg')
    expect(within(compared).getByRole('alert').textContent).toBe(GOODS_RECEIPT_TEXT.pieceOutsideTolerance)
  })

  it('keeps a kilogram batch as before: no piece column, no comparison', async () => {
    mount({ items: [kgBatch()], searchWindowFull: false })
    const card = await screen.findByTestId('purchase-order-goods-receipt-batch-1')
    expect(within(card).queryByRole('columnheader', { name: T.pieceCount })).toBeNull()
    expect(card.textContent).toContain(T.expectedKg)
    expect(card.textContent).toContain(T.acknowledged)
    expect(within(card).queryByTestId('purchase-order-goods-receipt-batch-1-piece-kilograms')).toBeNull()
  })

  it.each([
    ['empty', { items: [], searchWindowFull: false }, 200, 'empty-no-data'],
    ['malformed (a piece batch waiting for approval)', { items: [pieceBatch({ status: 'PendingApproval' })], searchWindowFull: false }, 200, 'malformed-response'],
    ['conflict', { code: 'SENTETIK' }, 409, 'conflict'],
    ['service unavailable', { code: 'service_unavailable' }, 503, 'service-unavailable'],
  ])('draws the %s state', async (_, body, status, state) => {
    mount(body, status)
    await screen.findByText((_content, element) => element?.getAttribute('data-state') === state)
    expect(screen.queryByTestId('purchase-order-goods-receipt-batch-1')).toBeNull()
  })
})
