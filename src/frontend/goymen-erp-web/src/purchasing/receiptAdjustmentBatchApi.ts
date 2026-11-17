import { PURCHASING_ORDERS_BASE } from './api'
import { post } from './receiptAdjustmentApi'
import { decodeReceiptReversalBatch, type ReceiptReversalBatchRequest } from './receiptAdjustmentBatchContract'

export function reverseReceiptBatch(company: string, order: string, request: ReceiptReversalBatchRequest, key: string) {
  return post(company, `${PURCHASING_ORDERS_BASE}/${encodeURIComponent(order)}/goods-receipts/reversal-batch`, request, key, 200,
    (body, replay) => decodeReceiptReversalBatch(body, company, order, request, replay))
}
