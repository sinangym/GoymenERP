import { FINANCE_CAPABILITY } from '../../finance/capabilities'
import type { WorkInboxKind } from './types'

export const FINANCE_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'finance.party-credit-limit.v1', module: 'finance', capability: 'finance.party-credit-limit.work-decision' },
  { kind: 'finance.manual-bank-payment.v1', module: 'finance', capability: 'finance.manual-bank-payment.work-decision' },
  // WP1 D5: the native D-05 official invoice import is in the one inbox too.
  { kind: 'finance.official-invoice-import.v1', module: 'finance', capability: 'finance.official-invoice-import.work-decision' },
  // WP5: the remaining Finance approvals, each with the owner's existing approve control or route.
  { kind: 'finance.official-sales-invoice.v1', module: 'finance', capability: 'finance.official-sales-invoice.work-decision' },
  { kind: 'finance.manual-bank-payment-reversal.v1', module: 'finance', capability: 'finance.manual-bank-payment-reversal.work-decision' },
  { kind: 'finance.manual-receipt-reversal.v1', module: 'finance', capability: 'finance.manual-receipt-reversal.work-decision' },
  { kind: 'finance.expense-payment.v1', module: 'finance', capability: FINANCE_CAPABILITY.expensePaymentsApprove },
  { kind: 'finance.bank-account.v1', module: 'finance', capability: 'finance.bank-account.work-decision' },
  { kind: 'finance.bank-account-iban-change.v1', module: 'finance', capability: 'finance.bank-account-iban-change.work-decision' },
  { kind: 'finance.credit-account.v1', module: 'finance', capability: 'finance.credit-account.work-decision' },
  { kind: 'finance.bank-credit-limit.v1', module: 'finance', capability: FINANCE_CAPABILITY.creditsApprove },
  { kind: 'finance.credit-movement.v1', module: 'finance', capability: FINANCE_CAPABILITY.creditsApprove },
  { kind: 'finance.opening-balance.v1', module: 'finance', capability: FINANCE_CAPABILITY.openingBalancesApprove },
  { kind: 'finance.opening-balance-reversal.v1', module: 'finance', capability: FINANCE_CAPABILITY.openingBalancesApprove },
  { kind: 'finance.cash-exchange-book-value.v1', module: 'finance', capability: FINANCE_CAPABILITY.exchangeBookValuesApprove },
  // YOL-D41 / WP8: owner-authorized decision routes live in Work; no separate Finance page.
  { kind: 'finance.cash-account.v1', module: 'finance', capability: 'finance.cash-account.work-decision' },
  { kind: 'finance.cash-account-passivation.v1', module: 'finance', capability: 'finance.cash-account-passivation.work-decision' },
  { kind: 'finance.cash-account-reactivation.v1', module: 'finance', capability: 'finance.cash-account-reactivation.work-decision' },
]
