import { describe, expect, it } from 'vitest'
import type { BankAccountView } from './contract'
import { EXAMPLE_ACCOUNT, EXAMPLE_BANK, EXAMPLE_COMPANY, EXAMPLE_PARTY, EXAMPLE_PROFILE, NORMAL_EXAMPLE } from './contractExamples'
import { isDateOnly } from './dateOnly'
import {
  buildDraftSaveRequest,
  emptyManualReceiptFields,
  fieldsFromDraft,
  type ManualReceiptFields,
} from './manualReceiptForm'
import { decodeSourceDetail } from './contract'
import { bankAccount } from './testHarness'

const BANK = bankAccount(EXAMPLE_COMPANY) as unknown as BankAccountView
const CONTEXT = {
  party: { partyId: EXAMPLE_PARTY, companyAccountId: EXAMPLE_ACCOUNT },
  identity: { draftId: null, expectedVersion: null },
  profile: EXAMPLE_PROFILE,
  bankAccounts: [BANK],
} as const

const FILLED: ManualReceiptFields = {
  treasuryAccountId: EXAMPLE_BANK,
  amount: '1000',
  occurredOn: '2026-09-13',
  valueOn: '',
  bankReference: '',
  description: '',
}

describe('buildDraftSaveRequest', () => {
  it('reports every missing required field at once, without inventing defaults', () => {
    expect(buildDraftSaveRequest(emptyManualReceiptFields(), CONTEXT)).toEqual({
      ok: false,
      errors: { treasuryAccountId: 'required', amount: 'required', occurredOn: 'required' },
    })
  })

  it('derives the currency from the picked bank account and never sends company or actor', () => {
    const built = buildDraftSaveRequest({ ...FILLED, amount: '1000,50', bankReference: '  ', valueOn: '2026-09-14' }, CONTEXT)
    if (!built.ok) throw new Error('expected valid')
    expect(built.request.preparation.money).toEqual({ amount: '1000.50', currencyId: BANK.currencyId })
    expect(built.request.preparation.valueOn).toBe('2026-09-14')
    expect(built.request.preparation.bankReference).toBeNull()
    expect(JSON.stringify(built.request)).not.toMatch(/companyId|actor/)
  })

  it('an update carries both draft id and expected version; a create carries neither', () => {
    const update = buildDraftSaveRequest(FILLED, { ...CONTEXT, identity: { draftId: NORMAL_EXAMPLE.record.draftId, expectedVersion: '9007199254740993' } })
    expect(update.ok && [update.request.draftId, update.request.expectedVersion]).toEqual([NORMAL_EXAMPLE.record.draftId, '9007199254740993'])
    const create = buildDraftSaveRequest(FILLED, CONTEXT)
    expect(create.ok && [create.request.draftId, create.request.expectedVersion]).toEqual([null, null])
  })

  it.each([
    ['inactive account', { isActive: false }],
    ['account not usable for receipts', { isUsableForReceipt: false }],
  ])('refuses an %s even if its id is submitted', (_label, patch) => {
    const built = buildDraftSaveRequest(FILLED, { ...CONTEXT, bankAccounts: [{ ...BANK, ...patch }] })
    expect(built).toEqual({ ok: false, errors: { treasuryAccountId: 'bank-account-ineligible' } })
  })

  it.each([
    ['zero amount', { amount: '0' }, 'amount', 'not-positive'],
    ['dotted amount', { amount: '1.000' }, 'amount', 'format'],
    ['impossible date', { occurredOn: '2026-02-30' }, 'occurredOn', 'date-format'],
    ['instant as date', { occurredOn: '2026-09-13T00:00:00Z' }, 'occurredOn', 'date-format'],
    ['bad value date', { valueOn: '13.09.2026' }, 'valueOn', 'date-format'],
  ] as const)('refuses %s', (_label, patch, field, error) => {
    const built = buildDraftSaveRequest({ ...FILLED, ...patch }, CONTEXT)
    expect(built.ok).toBe(false)
    if (!built.ok) expect(built.errors[field]).toBe(error)
  })
})

describe('fieldsFromDraft', () => {
  it('re-opens a persisted draft digit-for-digit, and a redacted amount as empty — never 0', () => {
    const decoded = decodeSourceDetail(NORMAL_EXAMPLE.draftSaveResult.resource, EXAMPLE_COMPANY)
    if (!decoded.ok) throw new Error('fixture must decode')
    expect(fieldsFromDraft(decoded.value)).toEqual({ ...FILLED, description: 'SENTETIK/NOT_ENV' })
    const redacted = { ...decoded.value, source: { ...decoded.value.source, amount: { disclosure: 'Redacted' as const, money: null } } }
    expect(fieldsFromDraft(redacted).amount).toBe('')
  })
})

describe('isDateOnly', () => {
  it('accepts a real day and a leap day, rejects a non-leap one', () => {
    expect(isDateOnly('2028-02-29')).toBe(true)
    expect(isDateOnly('2026-02-29')).toBe(false)
    expect(isDateOnly('2026-9-13')).toBe(false)
  })
})
