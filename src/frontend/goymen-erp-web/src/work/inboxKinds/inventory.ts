import { INVENTORY_STOCK_COUNT_SOURCE, INVENTORY_STOCK_SCRAP_SOURCE } from '../workText'
import type { WorkInboxKind } from './types'

/** WP1 D5: the native D-05 stock count and stock scrap approvals, now listed in the one inbox. */
export const INVENTORY_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'inventory.stock-count.v1', module: 'inventory', capability: INVENTORY_STOCK_COUNT_SOURCE.capability },
  { kind: 'inventory.stock-scrap.v1', module: 'inventory', capability: INVENTORY_STOCK_SCRAP_SOURCE.capability },
]
