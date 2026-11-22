import { FINANCE_CAPABILITY } from '../../finance/capabilities'
import { FINANCE_SOURCE } from '../workText'
import type { WorkInboxKind } from './types'

export const FINANCE_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'finance.party-credit-limit.v1', module: 'finance', capability: FINANCE_CAPABILITY.creditLimitsApprove },
  { kind: 'finance.manual-bank-payment.v1', module: 'finance', capability: FINANCE_CAPABILITY.manualBankPaymentsApprove },
  // WP1 D5: the native D-05 official invoice import is in the one inbox too.
  { kind: 'finance.official-invoice-import.v1', module: 'finance', capability: FINANCE_SOURCE.capability },
  // WP5: the remaining Finance approvals, each with the owner's existing approve control or route.
  { kind: 'finance.official-sales-invoice.v1', module: 'finance', capability: FINANCE_CAPABILITY.officialInvoicesApprove },
  { kind: 'finance.manual-bank-payment-reversal.v1', module: 'finance', capability: FINANCE_CAPABILITY.manualBankPaymentsReverseApprove },
  { kind: 'finance.manual-receipt-reversal.v1', module: 'finance', capability: FINANCE_CAPABILITY.receiptsReverseApprove },
  { kind: 'finance.expense-payment.v1', module: 'finance', capability: FINANCE_CAPABILITY.expensePaymentsApprove },
  { kind: 'finance.bank-account.v1', module: 'finance', capability: FINANCE_CAPABILITY.bankAccountsApprove },
  { kind: 'finance.bank-account-iban-change.v1', module: 'finance', capability: FINANCE_CAPABILITY.bankAccountsApprove },
  { kind: 'finance.credit-account.v1', module: 'finance', capability: FINANCE_CAPABILITY.creditsApprove },
  { kind: 'finance.bank-credit-limit.v1', module: 'finance', capability: FINANCE_CAPABILITY.creditsApprove },
  { kind: 'finance.credit-movement.v1', module: 'finance', capability: FINANCE_CAPABILITY.creditsApprove },
  { kind: 'finance.opening-balance.v1', module: 'finance', capability: FINANCE_CAPABILITY.openingBalancesApprove },
  { kind: 'finance.opening-balance-reversal.v1', module: 'finance', capability: FINANCE_CAPABILITY.openingBalancesApprove },
  { kind: 'finance.cash-exchange-book-value.v1', module: 'finance', capability: FINANCE_CAPABILITY.exchangeBookValuesApprove },
]
