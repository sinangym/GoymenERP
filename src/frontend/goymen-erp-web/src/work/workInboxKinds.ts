/**
 * W1-A Work inbox — the approval kinds the inbox lists (one per owner source) and, for each, the EXISTING frontend
 * capability of the owner's own approve screen or control. No capability is new: the inbox route opens for an actor
 * holding any one of them (or one of the B-25 queue's), and the server re-authorizes every source regardless.
 *
 * A kind with `capability: null` has no frontend capability yet (its owner publishes no approve control in the shell
 * snapshot); its items are still listed when the server returns them, but holding only its permission does not open the
 * route.
 */

import { FINANCE_CAPABILITY } from '../finance/capabilities'
import { TRANSFER_DISPATCH_CAPABILITIES } from '../inventory/transferDispatchApi'
import { LOGISTICS_CAPABILITY } from '../logistics/capabilities'
import { MANUFACTURING_CAPABILITY } from '../manufacturing/capabilities'
import { PURCHASING_CAPABILITY } from '../purchasing/capabilities'
import { SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY, SALES_RISK_EXCEPTION_APPROVE_CAPABILITY } from '../sales/capabilities'

export interface WorkInboxKind {
  readonly kind: string
  readonly module: string
  /** The owner's existing approve capability in the shell snapshot, or null when the frontend has none. */
  readonly capability: string | null
}

export const WORK_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'sales.price-floor-exception.v1', module: 'sales', capability: SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY },
  // No frontend capability for `sales.orders.discount-exception.approve` yet.
  { kind: 'sales.discount-exception.v1', module: 'sales', capability: null },
  { kind: 'sales.risk-exception.v1', module: 'sales', capability: SALES_RISK_EXCEPTION_APPROVE_CAPABILITY },
  // The Inventory transfer dispatch control requires `logistics.dispatch-notes.approve` (sufficient, not necessary).
  { kind: 'logistics.dispatch-note.v1', module: 'logistics', capability: TRANSFER_DISPATCH_CAPABILITIES.finalize },
  { kind: 'logistics.plan-excess.v1', module: 'logistics', capability: LOGISTICS_CAPABILITY.planExcessApprove },
  // No frontend capability for `logistics.weighing-differences.approve` yet.
  { kind: 'logistics.weighing-difference.v1', module: 'logistics', capability: null },
  { kind: 'manufacturing.plan.v1', module: 'manufacturing', capability: MANUFACTURING_CAPABILITY.planApprove },
  { kind: 'purchasing.order.v1', module: 'purchasing', capability: PURCHASING_CAPABILITY.orderApprove },
  { kind: 'purchasing.incoming-invoice-variance.v1', module: 'purchasing', capability: PURCHASING_CAPABILITY.invoiceApproveVariance },
  // No frontend capability for `purchasing.goods-receipts.approve-variance` yet.
  { kind: 'purchasing.goods-receipt-batch.v1', module: 'purchasing', capability: null },
  { kind: 'finance.party-credit-limit.v1', module: 'finance', capability: FINANCE_CAPABILITY.creditLimitsApprove },
  { kind: 'finance.manual-bank-payment.v1', module: 'finance', capability: FINANCE_CAPABILITY.manualBankPaymentsApprove },
]

/** The existing capabilities any one of which shows the inbox view (and opens the route with the queue's own). */
export const WORK_INBOX_CAPABILITIES: readonly string[] = WORK_INBOX_KINDS
  .map((entry) => entry.capability)
  .filter((capability): capability is string => capability !== null)
