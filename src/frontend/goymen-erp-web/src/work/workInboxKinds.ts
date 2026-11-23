/**
 * W1-A / WP1 Work inbox — the approval kinds the one inbox lists (one per owner source, the four native D-05 kinds
 * included: D5) and, for each, the EXISTING frontend capability of the owner's own approve screen or control. No
 * capability is new: the inbox route opens for an actor holding any one of them, and the server re-authorizes every source
 * regardless. The list is split per module (`inboxKinds/*.ts`) so packages add their kinds without touching each other.
 *
 * A kind with `capability: null` has no frontend capability yet (its owner publishes no approve control in the shell
 * snapshot); its items are still listed when the server returns them, but holding only its permission does not open the
 * route.
 */

import { ACCOUNTING_INBOX_KINDS } from './inboxKinds/accounting'
import { FINANCE_INBOX_KINDS } from './inboxKinds/finance'
import { IDENTITY_INBOX_KINDS } from './inboxKinds/identity'
import { INVENTORY_INBOX_KINDS } from './inboxKinds/inventory'
import { LOGISTICS_INBOX_KINDS } from './inboxKinds/logistics'
import { MANUFACTURING_INBOX_KINDS } from './inboxKinds/manufacturing'
import { MASTER_DATA_INBOX_KINDS } from './inboxKinds/masterData'
import { PURCHASING_INBOX_KINDS } from './inboxKinds/purchasing'
import { SALES_INBOX_KINDS } from './inboxKinds/sales'
import { SETTINGS_INBOX_KINDS } from './inboxKinds/settings'
import type { WorkInboxKind } from './inboxKinds/types'

export type { WorkInboxKind } from './inboxKinds/types'

export const WORK_INBOX_KINDS: readonly WorkInboxKind[] = [
  ...SALES_INBOX_KINDS,
  ...LOGISTICS_INBOX_KINDS,
  ...MANUFACTURING_INBOX_KINDS,
  ...PURCHASING_INBOX_KINDS,
  ...FINANCE_INBOX_KINDS,
  ...ACCOUNTING_INBOX_KINDS,
  ...INVENTORY_INBOX_KINDS,
  ...IDENTITY_INBOX_KINDS,
  ...SETTINGS_INBOX_KINDS,
  ...MASTER_DATA_INBOX_KINDS,
]

/** The existing capabilities any one of which shows the inbox view (and opens the route). */
export const WORK_INBOX_CAPABILITIES: readonly string[] = [...new Set(WORK_INBOX_KINDS
  .map((entry) => entry.capability)
  .filter((capability): capability is string => capability !== null))]
