import { afterEach, describe, expect, it, vi } from 'vitest'
import { decodeSupplierReturn } from './supplierReturnContract'
import { decodeSupplierReturnLineagePreview, kgThousandths } from './supplierReturnLineageContract'
import { fetchSupplierReturnLineagePreview } from './supplierReturnLineageApi'

const id = (n: number) => `${n.toString().padStart(8, '0')}-1111-4111-8111-111111111111`
const COMPANY = id(1), ORDER = id(2), ACCEPTANCE = id(10), SECOND = id(11), LABEL = id(30), EVENT = id(40)
const asked = { companyId: COMPANY, orderId: ORDER, acceptanceId: ACCEPTANCE, sourceLabelId: LABEL, quantityKg: '300', pieceCount: null }

function part(over: Record<string, unknown> = {}) {
  return { goodsAcceptanceId: ACCEPTANCE, purchaseOrderId: ORDER, purchaseOrderLineId: id(3), quantityKg: '180', pieceCount: null,
    costSource: 'InvoiceUnitCost', receivedKg: '600', openKg: '600', pendingRemainingKg: '360',
    invoiced: [{ sourceEventId: EVENT, invoiceId: id(41), invoiceLineId: id(42), remainingKg: '240' }], selectionRequired: true, ...over }
}
function preview(over: Record<string, unknown> = {}) {
  return { companyId: COMPANY, purchaseOrderId: ORDER, goodsAcceptanceId: ACCEPTANCE, sourceLabelId: LABEL, lineageMode: 'Lineage',
    quantityKg: '300', pieceCount: null, tracksPieces: false, freeKg: '300', blockedKg: '0', freePieces: null, blockedPieces: null,
    parts: [part(), part({ goodsAcceptanceId: SECOND, purchaseOrderId: id(5), quantityKg: '120', receivedKg: '400', openKg: '400',
      pendingRemainingKg: '400', invoiced: [], selectionRequired: false })], ...over }
}
function lineageReturn(over: Record<string, unknown> = {}) {
  return { companyId: COMPANY, returnId: id(20), returnLineId: id(21), purchaseOrderId: ORDER, purchaseOrderLineId: id(3),
    goodsAcceptanceId: ACCEPTANCE, inventoryMovementId: id(22), status: 'Recorded', version: '1', sourceBlock: 'Free', quantityKg: '300',
    costBasis: 'InvoiceUnitCost', unitCostTryPerKg: '19.9936', priceDisclosure: 'Visible', replacementExpected: false, reason: 'SENTETIK iade',
    occurredAtUtc: '2026-11-22T06:00:00Z', requestedBy: 'SENTETIK-giren', requestedAtUtc: '2026-11-22T06:01:00Z', decidedBy: null,
    decidedAtUtc: null, decisionReason: null, sourceLabelId: LABEL, lineageMode: 'Lineage',
    lineageParts: [storedPart(), storedPart({ goodsAcceptanceId: SECOND, purchaseOrderId: id(5), purchaseOrderLineId: id(6),
      returnLineId: id(23), inventoryMovementId: id(24), quantityKg: '120', unitCostTryPerKg: '21.44' })], ...over }
}
function storedPart(over: Record<string, unknown> = {}) {
  return { goodsAcceptanceId: ACCEPTANCE, purchaseOrderId: ORDER, purchaseOrderLineId: id(3), returnLineId: id(21),
    inventoryMovementId: id(22), quantityKg: '180', costBasis: 'InvoiceUnitCost', unitCostTryPerKg: '19.9936', ...over }
}

afterEach(() => { vi.unstubAllGlobals() })

describe('supplier return lineage preview decoder (AK-8b; SAT-D433, SAT-D440, SAT-D442, K3-B2), SENTETIK', () => {
  it('accepts the golden I01 two-receipt plan, a refusal code, a quantity plan and the golden I10 piece plan', () => {
    expect(decodeSupplierReturnLineagePreview(preview(), asked)).toEqual(preview())
    expect(decodeSupplierReturnLineagePreview(preview({ refusal: 'purchasing_supplier_return_lineage_receipt_limit_exceeded' }), asked)?.refusal)
      .toBe('purchasing_supplier_return_lineage_receipt_limit_exceeded')
    expect(decodeSupplierReturnLineagePreview(preview({ lineageMode: 'Quantity',
      parts: [part({ quantityKg: '300', costSource: 'MovingAverage', invoiced: [], selectionRequired: false })] }), asked)?.lineageMode).toBe('Quantity')
    const pieces = preview({ quantityKg: '100', pieceCount: 10, tracksPieces: true, freePieces: 20, blockedPieces: 0,
      parts: [part({ quantityKg: '42', pieceCount: 5 }), part({ goodsAcceptanceId: SECOND, quantityKg: '28', pieceCount: 2 }),
        part({ goodsAcceptanceId: id(12), quantityKg: '30', pieceCount: 3 })] })
    expect(decodeSupplierReturnLineagePreview(pieces, { ...asked, quantityKg: '100', pieceCount: 10 })?.parts.map((item) => item.pieceCount))
      .toEqual([5, 2, 3])
  })

  it('refuses another company, order, receipt or label and an answer to another question', () => {
    expect(decodeSupplierReturnLineagePreview(preview({ companyId: id(99) }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ purchaseOrderId: id(99) }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ goodsAcceptanceId: id(99) }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ sourceLabelId: id(99) }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ quantityKg: '299' }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview(), { ...asked, pieceCount: 3 })).toBeNull()
  })

  it('refuses extra, missing, mistyped and null fields, unknown names and non-canonical numbers', () => {
    expect(decodeSupplierReturnLineagePreview({ ...preview(), extra: 1 }, asked)).toBeNull()
    const { freeKg: _freeKg, ...missing } = preview()
    expect(decodeSupplierReturnLineagePreview(missing, asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ lineageMode: 'Proportional' }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ freeKg: 300 }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ freeKg: '300.0' }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ parts: null }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ parts: [] }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ refusal: 'Not A Code' }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ parts: [part({ costSource: 'OrderPrice' }), preview().parts[1]] }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ parts: [{ ...part(), unitCostTryPerKg: '19.9936' }, preview().parts[1]] }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ parts: [part({ selectionRequired: 'yes' }), preview().parts[1]] }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ parts: [part({ invoiced: [{ sourceEventId: EVENT, remainingKg: '240' }] }), preview().parts[1]] }), asked)).toBeNull()
  })

  it('refuses parts that do not add up to the asked kg or pieces, a repeated receipt, and pieces without a piece-tracking label', () => {
    expect(decodeSupplierReturnLineagePreview(preview({ parts: [part({ quantityKg: '179.999' }), preview().parts[1]] }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ parts: [part(), part({ quantityKg: '120' })] }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ parts: [part({ pieceCount: 5 }), preview().parts[1]] }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ tracksPieces: true, freePieces: 3, blockedPieces: 0 }), asked)).toBeNull()
    expect(decodeSupplierReturnLineagePreview(preview({ lineageMode: 'Quantity' }), asked)).toBeNull()
    expect(kgThousandths('33.333') + kgThousandths('66.667')).toBe(kgThousandths('100'))
  })
})

describe('supplier return decoder with a source label and per-receipt parts (AK-8/AK-8b), SENTETIK', () => {
  it('accepts a lineage return with its two parts, an explicit K3-B2 choice, pieces, and a quantity return', () => {
    expect(decodeSupplierReturn(lineageReturn(), COMPANY, ORDER)).toEqual(lineageReturn())
    const chosen = lineageReturn({ lineageParts: [storedPart({ pendingKg: '80', invoiced: [{ sourceEventId: EVENT, quantityKg: '100' }] }),
      lineageReturn().lineageParts[1]] })
    expect(decodeSupplierReturn(chosen, COMPANY, ORDER)?.lineageParts?.[0].pendingKg).toBe('80')
    const pieces = lineageReturn({ pieceCount: 7, lineageParts: [storedPart({ pieceCount: 5 }), { ...lineageReturn().lineageParts[1], pieceCount: 2 }] })
    expect(decodeSupplierReturn(pieces, COMPANY, ORDER)?.pieceCount).toBe(7)
    expect(decodeSupplierReturn(lineageReturn({ lineageMode: 'Quantity', lineageParts: [storedPart({ quantityKg: '300' })] }), COMPANY, ORDER))
      .not.toBeNull()
    const { lineageParts: _parts, ...earlier } = lineageReturn()
    expect(decodeSupplierReturn(earlier, COMPANY, ORDER)).not.toBeNull()
  })

  it('withholds every part price together with the document price', () => {
    const withheld = lineageReturn({ priceDisclosure: 'Withheld', unitCostTryPerKg: null,
      lineageParts: lineageReturn().lineageParts.map((item) => ({ ...item, unitCostTryPerKg: null })) })
    expect(decodeSupplierReturn(withheld, COMPANY, ORDER)).not.toBeNull()
    expect(decodeSupplierReturn(lineageReturn({ priceDisclosure: 'Withheld', unitCostTryPerKg: null }), COMPANY, ORDER)).toBeNull()
  })

  it('refuses a half pair, parts or pieces without a label, parts that do not add up, and a foreign own part', () => {
    const { lineageMode: _mode, lineageParts: _parts, ...noMode } = lineageReturn()
    expect(decodeSupplierReturn(noMode, COMPANY, ORDER)).toBeNull()
    const { sourceLabelId: _label, lineageMode: _mode2, ...partsOnly } = lineageReturn()
    expect(decodeSupplierReturn(partsOnly, COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(lineageReturn({ lineageMode: 'Proportional' }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(lineageReturn({ quantityKg: '301' }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(lineageReturn({ pieceCount: 7 }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(lineageReturn({ pieceCount: 0 }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(lineageReturn({ lineageParts: [storedPart({ returnLineId: id(77) }), lineageReturn().lineageParts[1]] }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(lineageReturn({ lineageParts: [storedPart({ pendingKg: '80', invoiced: [{ sourceEventId: EVENT, quantityKg: '99' }] }),
      lineageReturn().lineageParts[1]] }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(lineageReturn({ lineageParts: [{ ...storedPart(), extra: true }, lineageReturn().lineageParts[1]] }), COMPANY, ORDER)).toBeNull()
    expect(decodeSupplierReturn(lineageReturn({ expectedPurchaseOrderVersion: '4', undoQuantity: '2' }), COMPANY, ORDER)).toBeNull()
  })
})

describe('supplier return lineage preview read (AK-8b), SENTETIK', () => {
  it('asks exactly the label, the kg and the optional pieces, and classifies every answer', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(preview()), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    expect((await fetchSupplierReturnLineagePreview(asked)).type).toBe('ok-single')
    const url = String(fetchMock.mock.calls[0][0])
    expect(url).toContain(`/orders/${ORDER}/goods-receipts/${ACCEPTANCE}/supplier-return-lineage-preview?sourceLabelId=${LABEL}&quantityKg=300`)
    expect(url).not.toContain('pieceCount')

    fetchMock.mockResolvedValue(new Response(JSON.stringify({ code: 'purchasing_supplier_return_lineage_unavailable' }), { status: 503 }))
    expect(await fetchSupplierReturnLineagePreview({ ...asked, pieceCount: 4 })).toMatchObject({ type: 'http', status: 503 })
    expect(String(fetchMock.mock.calls[1][0])).toContain('&pieceCount=4')
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ ...preview(), companyId: id(99) }), { status: 200 }))
    expect((await fetchSupplierReturnLineagePreview(asked)).type).toBe('malformed')
    fetchMock.mockRejectedValue(new TypeError('SENTETIK network'))
    expect((await fetchSupplierReturnLineagePreview(asked)).type).toBe('network-error')
    const controller = new AbortController()
    controller.abort()
    expect((await fetchSupplierReturnLineagePreview(asked, controller.signal)).type).toBe('fence-stale')
  })
})
