import { describe, expect, it } from 'vitest'
import { WORK_INBOX_CAPABILITIES, WORK_INBOX_KINDS } from '../workInboxKinds'
import { inboxKindText, inboxModuleText, inboxPermissionText, inboxRoleText, inboxRuleText } from '../workInboxText'
import { ACCOUNTING_INBOX_KINDS } from './accounting'
import { FINANCE_INBOX_KINDS } from './finance'
import { FINANCE_ACCOUNTING_RULE_TEXT } from './financeAccountingText'

// The owner rule codes and reference roles the WP5 Finance and Accounting sources emit (backend WorkInbox folders).
const ROLES = [
  'bank-account', 'cash-account', 'cash-transfer', 'company-account', 'credit-account', 'credit-movement-proposal',
  'currency', 'customer', 'document', 'evidence-document', 'expense-item', 'lender', 'lender-party', 'manual-bank-payment',
  'manual-receipt', 'opening-balance', 'party', 'pdf-document', 'replaced-opening-movement', 'source-record', 'period',
  'period-closure', 'journal', 'manufacturing-order', 'debit-ledger-account', 'credit-ledger-account', 'invoice',
  'invoice-line', 'inventory-cost-proposal', 'invoice-cancellation', 'source-document',
]

// YOL-D41: approvals without an owner screen; decided from the inbox detail (WP8).
const NO_SCREEN = ['finance.cash-account.v1', 'finance.cash-account-passivation.v1', 'finance.cash-account-reactivation.v1']

const PERMISSIONS = [
  'finance.cash-account.approve', 'finance.cash-account.passivation.approve', 'finance.cash-account.reactivation.approve',
  'finance.official-sales-invoices.approve', 'finance.manual-bank-payment.reverse.approve',
  'finance.manual-receipt.reverse.approve', 'finance.expense-payment.approve', 'finance.bank-account.approve',
  'finance.credits.approve', 'finance.opening-balances.approve', 'accounting.periods.approve', 'accounting.periods.reopen',
  'accounting.normal-journal.draft.approve', 'accounting.journal.reverse.approve',
]

describe('WP5 Finance and Accounting inbox kinds', () => {
  it('lists each kind once, owned by its module, with Turkish words and an existing capability', () => {
    const kinds = [...FINANCE_INBOX_KINDS, ...ACCOUNTING_INBOX_KINDS]
    expect(new Set(WORK_INBOX_KINDS.map((entry) => entry.kind)).size).toBe(WORK_INBOX_KINDS.length)
    for (const entry of kinds) {
      expect(WORK_INBOX_KINDS).toContain(entry)
      expect(entry.kind.startsWith(`${entry.module}.`)).toBe(true)
      expect(entry.kind).toMatch(/\.v1$/)
      expect(inboxKindText(entry.kind)).not.toBe(entry.kind)
      if (NO_SCREEN.includes(entry.kind)) expect(entry.capability).toBeNull()
      else expect(WORK_INBOX_CAPABILITIES).toContain(entry.capability)
    }
    expect(FINANCE_INBOX_KINDS).toHaveLength(18)
    expect(ACCOUNTING_INBOX_KINDS).toHaveLength(10)
    expect(inboxModuleText('accounting')).toBe('Muhasebe')
  })

  it('lists the Inventory opening-cost decision without a capability (YOL-D41)', () => {
    const entry = WORK_INBOX_KINDS.find((candidate) => candidate.kind === 'inventory.opening-cost.v1')
    expect(entry).toEqual({ kind: 'inventory.opening-cost.v1', module: 'inventory', capability: null })
    expect(inboxKindText('inventory.opening-cost.v1')).toBe('Stok açılış maliyeti onayı')
    expect(inboxRuleText('inventory.opening-cost-approval-required')).not.toBeNull()
    expect(inboxPermissionText('inventory.opening-cost.approve')).toBe('Stok açılış maliyeti onayı')
  })

  it('opens the period reopening only with its own ROL-D12 control', () => {
    const reopening = ACCOUNTING_INBOX_KINDS.find((entry) => entry.kind === 'accounting.period-reopening.v1')
    expect(reopening?.capability).toBe('accounting.periods.reopen')
    expect(inboxPermissionText('accounting.periods.reopen')).toBe('Dönem yeniden açma onayı')
  })

  it('has words for every rule code, reference role and approve permission the sources emit', () => {
    for (const code of Object.keys(FINANCE_ACCOUNTING_RULE_TEXT)) {
      expect(code).toMatch(/^(finance|accounting|inventory)\./)
      expect(inboxRuleText(code)).not.toBeNull()
    }
    for (const role of ROLES) expect(inboxRoleText(role), role).not.toBeNull()
    for (const permission of PERMISSIONS) expect(inboxPermissionText(permission)).not.toBe(permission)
  })
})
