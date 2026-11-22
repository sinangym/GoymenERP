import { SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY, SALES_RISK_EXCEPTION_APPROVE_CAPABILITY } from '../../sales/capabilities'
import { SALES_SOURCE } from '../workText'
import type { WorkInboxKind } from './types'

/** Sales approval kinds of the Work inbox (one file per module so packages add their kinds without touching the others). */
export const SALES_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'sales.price-floor-exception.v1', module: 'sales', capability: SALES_PRICE_FLOOR_EXCEPTION_APPROVE_CAPABILITY },
  // No frontend capability for `sales.orders.discount-exception.approve` yet.
  { kind: 'sales.discount-exception.v1', module: 'sales', capability: null },
  { kind: 'sales.risk-exception.v1', module: 'sales', capability: SALES_RISK_EXCEPTION_APPROVE_CAPABILITY },
  // WP1 D5: the native D-05 sales order draft is in the one inbox too (the owner's `sales.orders.approve`).
  { kind: 'sales.order-draft.v1', module: 'sales', capability: SALES_SOURCE.capability },
]
