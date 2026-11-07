import type { StatusTone } from '../ui/StatusIndicator'
import type { PurchaseOrderStatus } from './contract'

/**
 * #932 review P3: the ONE order status tone, shared by the order list (P-03) and the order detail (P-05), so an order never
 * changes colour between the two screens. A Draft waits for approval (warning), an Approved order is live supply (success) and
 * a Cancelled one is closed (neutral). The tone is never the only signal: the text is `PURCHASE_ORDER_STATUS_LABEL`.
 */
export const PURCHASE_ORDER_STATUS_TONE: Readonly<Record<PurchaseOrderStatus, StatusTone>> = {
  Draft: 'warning',
  Approved: 'success',
  Cancelled: 'neutral',
}
