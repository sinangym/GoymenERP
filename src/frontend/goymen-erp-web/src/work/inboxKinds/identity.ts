import { IDENTITY_ACCESS_GRANT_WORK_CAPABILITY } from '../workIdentityAccessGrantDecision'
import type { WorkInboxKind } from './types'

export const IDENTITY_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'identity.access-grant-request.v1', module: 'identity', capability: IDENTITY_ACCESS_GRANT_WORK_CAPABILITY },
]
