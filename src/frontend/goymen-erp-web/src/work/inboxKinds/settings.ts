import type { WorkInboxKind } from './types'

/**
 * WP7: the Ayarlar policy publications. The shell snapshot has no publish capability for these families yet (only their
 * prepare/read screens), so none opens the inbox route on its own; their items are listed when the server returns them.
 */
export const SETTINGS_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'settings.finance-approval-policy.v1', module: 'settings', capability: null },
  { kind: 'settings.cash-count-difference-policy.v1', module: 'settings', capability: null },
  { kind: 'settings.bank-institution-policy.v1', module: 'settings', capability: null },
  { kind: 'settings.collateral-rate-policy.v1', module: 'settings', capability: null },
  { kind: 'settings.manufacturing-parameter-profile.v1', module: 'settings', capability: null },
  { kind: 'settings.manufacturing-tolerance-profile.v1', module: 'settings', capability: null },
]
