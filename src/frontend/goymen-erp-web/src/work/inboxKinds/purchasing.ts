import { PURCHASING_CAPABILITY } from '../../purchasing/capabilities'
import type { WorkInboxKind } from './types'

export const PURCHASING_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'purchasing.order.v1', module: 'purchasing', capability: PURCHASING_CAPABILITY.orderApprove },
  { kind: 'purchasing.incoming-invoice-variance.v1', module: 'purchasing', capability: PURCHASING_CAPABILITY.invoiceApproveVariance },
  // No frontend capability for `purchasing.goods-receipts.approve-variance` yet.
  { kind: 'purchasing.goods-receipt-batch.v1', module: 'purchasing', capability: null },
]
