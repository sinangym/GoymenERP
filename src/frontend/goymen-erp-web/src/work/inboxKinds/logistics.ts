import { LOGISTICS_CAPABILITY } from '../../logistics/capabilities'
import type { WorkInboxKind } from './types'

export const LOGISTICS_INBOX_KINDS: readonly WorkInboxKind[] = [
  // The Work purpose uses the existing native dispatch approval permission; native detail remains separate.
  { kind: 'logistics.dispatch-note.v1', module: 'logistics', capability: 'logistics.dispatch-note.work-decision' },
  { kind: 'logistics.plan-excess.v1', module: 'logistics', capability: LOGISTICS_CAPABILITY.planExcessApprove },
  // The standalone human decision purpose uses the native weighing approval permission.
  { kind: 'logistics.weighing-difference.v1', module: 'logistics', capability: 'logistics.weighing-difference.work-decision' },
  // WP6: no frontend capability for `logistics.customer-returns.decision` yet (the shell declares the control; no panel).
  { kind: 'logistics.customer-return.v1', module: 'logistics', capability: null },
]
