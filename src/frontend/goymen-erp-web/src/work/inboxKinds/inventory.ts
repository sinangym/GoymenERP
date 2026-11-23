import { INVENTORY_TRANSFER_ACTION_ROUTE } from '../../inventory/capabilities'
import { INVENTORY_STOCK_COUNT_SOURCE, INVENTORY_STOCK_SCRAP_SOURCE } from '../workText'
import type { WorkInboxKind } from './types'

type TransferActionCapability = keyof typeof INVENTORY_TRANSFER_ACTION_ROUTE
const OVERAGE_APPROVE: TransferActionCapability = 'inventory.stock-transfer.overage-approve'
const LOSS_APPROVE: TransferActionCapability = 'inventory.stock-transfer.loss-approve'

/**
 * WP1 D5: the native D-05 stock count and stock scrap approvals, now listed in the one inbox. WP6: the transfer excess and
 * transit loss decisions, on the existing transfer approval screens' capabilities.
 */
export const INVENTORY_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'inventory.stock-count.v1', module: 'inventory', capability: INVENTORY_STOCK_COUNT_SOURCE.capability },
  { kind: 'inventory.stock-scrap.v1', module: 'inventory', capability: INVENTORY_STOCK_SCRAP_SOURCE.capability },
  { kind: 'inventory.transfer-overage.v1', module: 'inventory', capability: OVERAGE_APPROVE },
  { kind: 'inventory.transfer-loss.v1', module: 'inventory', capability: LOSS_APPROVE },
  // YOL-D41: no owner screen and no frontend capability; the Work inbox decision (WP8) covers it.
  { kind: 'inventory.opening-cost.v1', module: 'inventory', capability: null },
]
