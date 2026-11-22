import { TRANSFER_DISPATCH_CAPABILITIES } from '../../inventory/transferDispatchApi'
import { LOGISTICS_CAPABILITY } from '../../logistics/capabilities'
import type { WorkInboxKind } from './types'

export const LOGISTICS_INBOX_KINDS: readonly WorkInboxKind[] = [
  // The Inventory transfer dispatch control requires `logistics.dispatch-notes.approve` (sufficient, not necessary).
  { kind: 'logistics.dispatch-note.v1', module: 'logistics', capability: TRANSFER_DISPATCH_CAPABILITIES.finalize },
  { kind: 'logistics.plan-excess.v1', module: 'logistics', capability: LOGISTICS_CAPABILITY.planExcessApprove },
  // No frontend capability for `logistics.weighing-differences.approve` yet.
  { kind: 'logistics.weighing-difference.v1', module: 'logistics', capability: null },
  // WP6: no frontend capability for `logistics.customer-returns.decision` yet (the shell declares the control; no panel).
  { kind: 'logistics.customer-return.v1', module: 'logistics', capability: null },
]
