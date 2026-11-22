import { IDENTITY_ACCESS_CONTROL } from '../../identity/capabilities'
import type { WorkInboxKind } from './types'

export const IDENTITY_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'identity.access-grant-request.v1', module: 'identity', capability: IDENTITY_ACCESS_CONTROL.decide },
]
