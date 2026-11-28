import type { WorkInboxKind } from './types'

/**
 * WP1 D5: the native D-05 stock count and stock scrap approvals, now listed in the one inbox. WP6: the transfer excess and
 * transit loss decisions. WP8 connects both decisions to their standalone owner-authorized Work purposes.
 */
export const INVENTORY_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'inventory.stock-count.v1', module: 'inventory', capability: 'inventory.stock-count.work-decision' },
  { kind: 'inventory.stock-scrap.v1', module: 'inventory', capability: 'inventory.stock-scrap.work-decision' },
  { kind: 'inventory.transfer-overage.v1', module: 'inventory', capability: 'inventory.transfer-overage.work-decision' },
  { kind: 'inventory.transfer-loss.v1', module: 'inventory', capability: 'inventory.transfer-loss.work-decision' },
  // WP8 / YOL-D41: the decision is on the owner-authorized Work detail route.
  { kind: 'inventory.opening-cost.v1', module: 'inventory', capability: 'inventory.opening-cost.work-decision' },
]
