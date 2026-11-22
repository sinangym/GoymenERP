import { FINANCE_CAPABILITY } from '../../finance/capabilities'
import { FINANCE_SOURCE } from '../workText'
import type { WorkInboxKind } from './types'

export const FINANCE_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'finance.party-credit-limit.v1', module: 'finance', capability: FINANCE_CAPABILITY.creditLimitsApprove },
  { kind: 'finance.manual-bank-payment.v1', module: 'finance', capability: FINANCE_CAPABILITY.manualBankPaymentsApprove },
  // WP1 D5: the native D-05 official invoice import is in the one inbox too.
  { kind: 'finance.official-invoice-import.v1', module: 'finance', capability: FINANCE_SOURCE.capability },
]
