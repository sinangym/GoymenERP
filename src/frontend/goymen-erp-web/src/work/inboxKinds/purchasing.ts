import { PURCHASING_CAPABILITY } from '../../purchasing/capabilities'
import type { WorkInboxKind } from './types'

export const PURCHASING_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'purchasing.order.v1', module: 'purchasing', capability: PURCHASING_CAPABILITY.orderApprove },
  { kind: 'purchasing.incoming-invoice-variance.v1', module: 'purchasing', capability: PURCHASING_CAPABILITY.invoiceApproveVariance },
  // No frontend capability for `purchasing.goods-receipts.approve-variance` yet.
  { kind: 'purchasing.goods-receipt-batch.v1', module: 'purchasing', capability: null },
  // WP7
  { kind: 'purchasing.request.v1', module: 'purchasing', capability: 'purchasing.request.work-decision' },
  // SAT-D396: the supplier return cost exception is decided under the order approval permission.
  { kind: 'purchasing.supplier-return-cost-exception.v1', module: 'purchasing', capability: PURCHASING_CAPABILITY.orderApprove },
  { kind: 'purchasing.service-order.v1', module: 'purchasing', capability: PURCHASING_CAPABILITY.serviceOrderApprove },
]
