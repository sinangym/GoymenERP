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
const NO_SCREEN = ['finance.credit-account.v1', 'finance.cash-account.v1', 'finance.cash-account-passivation.v1', 'finance.cash-account-reactivation.v1']

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
      if (NO_SCREEN.includes(entry.kind)) expect(entry.capability).toBe(entry.kind.replace('.v1', '.work-decision'))
      else expect(WORK_INBOX_CAPABILITIES).toContain(entry.capability)
    }
    expect(FINANCE_INBOX_KINDS).toHaveLength(18)
    expect(ACCOUNTING_INBOX_KINDS).toHaveLength(12)
    expect(inboxModuleText('accounting')).toBe('Muhasebe')
  })

  it('lists the Inventory opening-cost decision with its WP8 owner capability (YOL-D41)', () => {
    const entry = WORK_INBOX_KINDS.find((candidate) => candidate.kind === 'inventory.opening-cost.v1')
    expect(entry).toEqual({ kind: 'inventory.opening-cost.v1', module: 'inventory', capability: 'inventory.opening-cost.work-decision' })
    expect(inboxKindText('inventory.opening-cost.v1')).toBe('Stok açılış maliyeti onayı')
    expect(inboxRuleText('inventory.opening-cost-approval-required')).not.toBeNull()
    expect(inboxPermissionText('inventory.opening-cost.approve')).toBe('Stok açılış maliyeti onayı')
  })

  it('opens the period reopening only with its own ROL-D12 purpose', () => {
    const reopening = ACCOUNTING_INBOX_KINDS.find((entry) => entry.kind === 'accounting.period-reopening.v1')
    expect(reopening?.capability).toBe('accounting.period-reopening.work-decision')
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

it('maps the single manual-payment kind to its native approve-only Work purpose without changing its permission words',()=>{
 expect(FINANCE_INBOX_KINDS.filter(row=>row.kind==='finance.manual-bank-payment.v1')).toEqual([{kind:'finance.manual-bank-payment.v1',module:'finance',capability:'finance.manual-bank-payment.work-decision'}])
 expect(inboxPermissionText('finance.manual-bank-payment.approve')).not.toBe('finance.manual-bank-payment.approve')
 expect(inboxRuleText('finance.manual-bank-payment-approval-required')).not.toBeNull()
})


it('maps IBAN approval to one standalone purpose without adding a bank read permission',()=>{
 expect(FINANCE_INBOX_KINDS.filter(row=>row.kind==='finance.bank-account-iban-change.v1')).toEqual([{kind:'finance.bank-account-iban-change.v1',module:'finance',capability:'finance.bank-account-iban-change.work-decision'}])
 expect(inboxPermissionText('finance.bank-account.approve')).not.toBe('finance.bank-account.approve')
 expect(inboxRuleText('finance.bank-account-iban-change-approval-required')).not.toBeNull()
})

it.each(['finance.manual-bank-payment-reversal.v1','finance.manual-receipt-reversal.v1'])('maps one reversal purpose without changing native permission words: %s',kind=>{
 expect(FINANCE_INBOX_KINDS.filter(row=>row.kind===kind)).toEqual([{kind,module:'finance',capability:kind.replace('.v1','.work-decision')}])
 expect(inboxPermissionText(kind==='finance.manual-bank-payment-reversal.v1'?'finance.manual-bank-payment.reverse.approve':'finance.manual-receipt.reverse.approve')).not.toContain('.approve')
})

it('maps the expense approval only to its standalone purpose while retaining native permission words',()=>{
 expect(FINANCE_INBOX_KINDS.filter(row=>row.kind==='finance.expense-payment.v1')).toEqual([{kind:'finance.expense-payment.v1',module:'finance',capability:'finance.expense-payment.work-decision'}])
 expect(inboxPermissionText('finance.expense-payment.approve')).not.toBe('finance.expense-payment.approve');expect(inboxRuleText('finance.expense-payment-approval-required')).not.toBeNull()
})


it.each(['finance.bank-credit-limit.v1','finance.opening-balance.v1','finance.opening-balance-reversal.v1'])('maps only the actual standalone decision authority for %s',kind=>{
 expect(FINANCE_INBOX_KINDS.filter(row=>row.kind===kind)).toEqual([{kind,module:'finance',capability:kind.replace('.v1','.work-decision')}])
 expect(inboxPermissionText(kind==='finance.bank-credit-limit.v1'?'finance.credits.approve':'finance.opening-balances.approve')).not.toContain('.approve')
})


it.each([
 ['finance.credit-movement.v1','finance.credit-movement.work-decision','finance.credits.approve'],
 ['finance.cash-exchange-book-value.v1','finance.cash-exchange-book-value.work-decision','accounting.normal-journal.draft.approve'],
])('maps %s only to the exact purpose capability without changing permission translation',(kind,capability,permission)=>{
 expect(FINANCE_INBOX_KINDS.filter(row=>row.kind===kind)).toEqual([{kind,module:'finance',capability}])
 expect(inboxPermissionText(permission)).not.toBe(permission)
})


it('maps one normal-journal kind to the standalone purpose and keeps native permission words',()=>{
 expect(ACCOUNTING_INBOX_KINDS.filter(row=>row.kind==='accounting.normal-journal-draft.v1')).toEqual([{kind:'accounting.normal-journal-draft.v1',module:'accounting',capability:'accounting.normal-journal-draft.work-decision'}])
 expect(inboxPermissionText('accounting.normal-journal.draft.approve')).not.toBe('accounting.normal-journal.draft.approve')
 expect(inboxRuleText('accounting.normal-journal-draft-approval-required')).not.toBeNull()
})

it.each(['accounting.period-opening.v1','accounting.period-reopening.v1'])('maps %s to one standalone purpose without changing native permission words',kind=>{
 expect(ACCOUNTING_INBOX_KINDS.filter(row=>row.kind===kind)).toEqual([{kind,module:'accounting',capability:kind.replace('.v1','.work-decision')}])
 expect(inboxPermissionText(kind==='accounting.period-opening.v1'?'accounting.periods.approve':'accounting.periods.reopen')).not.toContain('.periods.')
})

it('maps journal reversal to its standalone Work purpose while preserving native approve permission words',()=>{
 expect(ACCOUNTING_INBOX_KINDS.filter(row=>row.kind==='accounting.journal-reversal.v1')).toEqual([{kind:'accounting.journal-reversal.v1',module:'accounting',capability:'accounting.journal-reversal.work-decision'}])
 expect(inboxPermissionText('accounting.journal.reverse.approve')).not.toBe('accounting.journal.reverse.approve')
 expect(inboxRuleText('accounting.journal-reversal-approval-required')).not.toBeNull()
})
