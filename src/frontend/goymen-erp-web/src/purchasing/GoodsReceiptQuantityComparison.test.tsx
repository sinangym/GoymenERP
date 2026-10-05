import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import { GoodsReceiptQuantityComparison } from './GoodsReceiptQuantityComparison'
import { decodeGoodsReceiptQuantityComparison } from './goodsReceiptComparisonContract'
import type { GoodsReceipt } from './goodsReceiptContract'
import { GOODS_RECEIPT_TEXT as T } from './goodsReceiptText'

const receipt: GoodsReceipt = {
  companyId: '11111111-1111-4111-8111-111111111111',
  acceptanceId: '22222222-2222-4222-8222-222222222222',
  purchaseOrderId: '33333333-3333-4333-8333-333333333333',
  purchaseOrderLineId: '44444444-4444-4444-8444-444444444444',
  purchaseOrderVersion: '7',
  productId: '55555555-5555-4555-8555-555555555555',
  productVersion: '3',
  unitId: '66666666-6666-4666-8666-666666666666',
  acceptedQuantity: '100.25',
  remainingQuantity: '40',
  lotId: '77777777-7777-4777-8777-777777777777',
  labelId: '88888888-8888-4888-8888-888888888888',
  labelCode: 'LBL-001',
  labelVersion: '2',
  movementId: '99999999-9999-4999-8999-999999999999',
  locationId: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',
  measuredQuantityKg: '101.75',
  initialBlock: 'Free',
  occurredAtUtc: '2026-10-05T08:00:00Z',
}

const comparison = {
  companyId: receipt.companyId,
  acceptanceId: receipt.acceptanceId,
  purchaseOrderId: receipt.purchaseOrderId,
  purchaseOrderLineId: receipt.purchaseOrderLineId,
  purchaseOrderVersion: receipt.purchaseOrderVersion,
  productId: receipt.productId,
  productVersion: receipt.productVersion,
  unitId: receipt.unitId,
  acceptedQuantity: receipt.acceptedQuantity,
  measuredQuantityKg: receipt.measuredQuantityKg,
  comparisonStatus: 'SameKilogramUnit',
  acceptedQuantityKg: receipt.acceptedQuantity,
  measuredMinusAcceptedKg: '1.5',
} as const

afterEach(cleanup)

describe('goods receipt quantity comparison companion', () => {
  it('renders both source quantities and the exact signed kilogram difference', () => {
    const decoded = decodeGoodsReceiptQuantityComparison(comparison, receipt)
    expect(decoded).not.toBeNull()
    render(<GoodsReceiptQuantityComparison comparison={decoded!} />)

    expect(screen.getByText(receipt.acceptedQuantity)).toBeTruthy()
    expect(screen.getByText(`${receipt.measuredQuantityKg} kg`)).toBeTruthy()
    expect(screen.getByText('+1.5 kg')).toBeTruthy()
    expect(screen.queryByText(T.comparisonUnavailable)).toBeNull()
  })

  it('keeps both source quantities visible and explains an unavailable comparison', () => {
    const decoded = decodeGoodsReceiptQuantityComparison({ ...comparison, comparisonStatus: 'Unavailable',
      acceptedQuantityKg: null, measuredMinusAcceptedKg: null }, receipt)
    expect(decoded).not.toBeNull()
    render(<GoodsReceiptQuantityComparison comparison={decoded!} />)

    expect(screen.getByText(receipt.acceptedQuantity)).toBeTruthy()
    expect(screen.getByText(`${receipt.measuredQuantityKg} kg`)).toBeTruthy()
    expect(screen.getByText(T.comparisonUnavailable)).toBeTruthy()
    expect(screen.queryByText(/uygun|uygunsuz|onay|tolerans/i)).toBeNull()
  })

  it.each([
    ['reserved conversion without proof', { ...comparison, comparisonStatus: 'ApprovedConversion' }],
    ['contradictory difference', { ...comparison, measuredMinusAcceptedKg: '1.4' }],
    ['different source quantity', { ...comparison, acceptedQuantity: '100' }],
    ['derived values on unavailable', { ...comparison, comparisonStatus: 'Unavailable', measuredMinusAcceptedKg: null }],
  ])('rejects %s', (_name, value) => {
    expect(decodeGoodsReceiptQuantityComparison(value, receipt)).toBeNull()
  })
})