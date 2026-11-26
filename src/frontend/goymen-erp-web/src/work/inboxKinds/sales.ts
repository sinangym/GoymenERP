import {
  SALES_CUSTOMER_RETURN_APPROVE_CAPABILITY,
  SALES_INVOICE_APPROVE_CAPABILITY,
  SALES_OVER_DELIVERY_APPROVE_CAPABILITY,
  SALES_POOL_CAPABILITY_ID,
  SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY,
  SALES_RISK_EXCEPTION_APPROVE_CAPABILITY,
  SALES_WORK_DECISION_CAPABILITY,
} from '../../sales/capabilities'
import type { WorkInboxKind } from './types'

/** Sales approval kinds of the Work inbox (one file per module so packages add their kinds without touching the others). */
export const SALES_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'sales.price-floor-exception.v1', module: 'sales', capability: SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY },
  // No frontend capability for `sales.orders.discount-exception.approve` yet.
  { kind: 'sales.discount-exception.v1', module: 'sales', capability: null },
  { kind: 'sales.risk-exception.v1', module: 'sales', capability: SALES_RISK_EXCEPTION_APPROVE_CAPABILITY },
  // WP1 D5: the native D-05 sales order draft is in the one inbox too (the owner's `sales.orders.approve`).
  { kind: 'sales.order-draft.v1', module: 'sales', capability: SALES_WORK_DECISION_CAPABILITY.order },
  // WP4: the remaining Sales approval kinds, each with the owner's existing approve control (none is new).
  { kind: 'sales.request.v1', module: 'sales', capability: SALES_POOL_CAPABILITY_ID.requestsDecide },
  { kind: 'sales.invoice.v1', module: 'sales', capability: SALES_WORK_DECISION_CAPABILITY.invoice },
  // The subcontract service invoice is decided under the same `sales.invoices.approve` control (on the order screen).
  { kind: 'sales.subcontract-invoice.v1', module: 'sales', capability: SALES_INVOICE_APPROVE_CAPABILITY },
  { kind: 'sales.customer-return.v1', module: 'sales', capability: SALES_CUSTOMER_RETURN_APPROVE_CAPABILITY },
  // S-11 is not routed: its `sales.contracts.discount-approve` control is not published by the shell yet.
  { kind: 'sales.contract-discount.v1', module: 'sales', capability: null },
  // SAT-D476 / SAT-D475: only a REQUEST waits — the conversion request of an expired quote (decided by the quote's own
  // expired-conversion approval) and the over-delivery request the shipment gate opened (decided on the order screen).
  { kind: 'sales.quote-conversion-request.v1', module: 'sales', capability: SALES_POOL_CAPABILITY_ID.quotesApproveExpired },
  { kind: 'sales.over-delivery-request.v1', module: 'sales', capability: SALES_OVER_DELIVERY_APPROVE_CAPABILITY },
]
