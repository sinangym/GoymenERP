import { ACCOUNTING_CAPABILITY } from '../../accounting/capabilities'
import type { WorkInboxKind } from './types'

/** WP5: the Accounting approvals in the one inbox, each with the owner's existing approve control or route. */
export const ACCOUNTING_INBOX_KINDS: readonly WorkInboxKind[] = [
  { kind: 'accounting.period-opening.v1', module: 'accounting', capability: ACCOUNTING_CAPABILITY.periodsApprove },
  // ROL-D12: the reopening approval has its own name.
  { kind: 'accounting.period-reopening.v1', module: 'accounting', capability: ACCOUNTING_CAPABILITY.periodsReopen },
  { kind: 'accounting.normal-journal-draft.v1', module: 'accounting', capability: ACCOUNTING_CAPABILITY.normalDraftApprove },
  { kind: 'accounting.journal-reversal.v1', module: 'accounting', capability: ACCOUNTING_CAPABILITY.journalReversalApprove },
  { kind: 'accounting.manufacturing-cost-difference.v1', module: 'accounting', capability: ACCOUNTING_CAPABILITY.costDifferenceApprove },
  { kind: 'accounting.invoice-line-cogs-correction.v1', module: 'accounting', capability: ACCOUNTING_CAPABILITY.invoiceLineCogsApprove },
  { kind: 'accounting.invoiced-shipment-cost.v1', module: 'accounting', capability: ACCOUNTING_CAPABILITY.invoicedShipmentApprove },
  { kind: 'accounting.purchase-cost-difference.v1', module: 'accounting', capability: ACCOUNTING_CAPABILITY.purchaseCostApprove },
  { kind: 'accounting.inventory-cost-cancellation.v1', module: 'accounting', capability: ACCOUNTING_CAPABILITY.invoicedShipmentReverseApprove },
  { kind: 'accounting.source-voucher.v1', module: 'accounting', capability: ACCOUNTING_CAPABILITY.sourceVouchersApprove },
]
