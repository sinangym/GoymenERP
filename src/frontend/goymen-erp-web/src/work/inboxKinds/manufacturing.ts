import { MANUFACTURING_CAPABILITY } from '../../manufacturing/capabilities'
import type { WorkInboxKind } from './types'

export const MANUFACTURING_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'manufacturing.plan.v1', module: 'manufacturing', capability: MANUFACTURING_CAPABILITY.planApprove },
]
