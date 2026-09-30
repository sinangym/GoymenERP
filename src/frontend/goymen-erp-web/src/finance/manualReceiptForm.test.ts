import { describe, expect, it } from 'vitest'
import type { BankAccountView } from './contract'
import { EXAMPLE_ACCOUNT, EXAMPLE_BANK, EXAMPLE_COMPANY, EXAMPLE_PARTY, EXAMPLE_PROFILE, NORMAL_EXAMPLE } from './contractExamples'
import { isDateOnly } from './dateOnly'
import {
  bankNetPreview,
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
  ...emptyManualReceiptFields(),
  treasuryAccountId: EXAMPLE_BANK,
  amount: '1000',
  occurredOn: '2026-09-13',
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

describe('DEC-FIN-014 customer POS collection', () => {
  const POS: ManualReceiptFields = {
    ...FILLED,
    amount: '1000,00',
    collectionChannel: 'pos',
    commissionAmount: '25,50',
    commissionBearer: 'Company',
  }

  it('sends the gross as the amount and the commission and bearer as typed; the net is never sent', () => {
    const built = buildDraftSaveRequest(POS, { ...CONTEXT, scale: 2 })
    if (!built.ok) throw new Error('expected valid')
    expect(built.request.preparation.money.amount).toBe('1000.00')
    expect(built.request.preparation.pos).toEqual({ commissionAmount: '25.50', commissionBearer: 'Company' })
    expect(JSON.stringify(built.request)).not.toMatch(/bankNet/)
    expect(bankNetPreview(POS)).toBe('974.50')
  })

  it.each([
    ['blank commission', { commissionAmount: '' }, 'commissionAmount', 'required'],
    ['dotted commission', { commissionAmount: '1.000' }, 'commissionAmount', 'format'],
    ['too precise commission', { commissionAmount: '1,005' }, 'commissionAmount', 'scale-exceeded'],
    ['commission equal to the gross', { commissionAmount: '1000' }, 'commissionAmount', 'commission-not-below-gross'],
    ['no bearer chosen', { commissionBearer: '' }, 'commissionBearer', 'bearer-required'],
  ] as const)('refuses %s', (_label, patch, field, error) => {
    const built = buildDraftSaveRequest({ ...POS, ...patch }, { ...CONTEXT, scale: 2 })
    expect(built.ok).toBe(false)
    if (!built.ok) expect(built.errors[field]).toBe(error)
  })

  it('a bank transfer ignores leftover POS inputs and sends no pos member', () => {
    const built = buildDraftSaveRequest({ ...POS, collectionChannel: 'bank-transfer' }, CONTEXT)
    expect(built.ok && 'pos' in built.request.preparation).toBe(false)
    expect(bankNetPreview({ ...POS, collectionChannel: 'bank-transfer' })).toBeNull()
  })

  it('re-opens a POS draft with its commission and bearer', () => {
    const visible = (amount: string) => ({ disclosure: 'Visible', money: { amount, currencyId: BANK.currencyId } })
    const decoded = decodeSourceDetail({
      ...NORMAL_EXAMPLE.draftSaveResult.resource,
      pos: { commissionBearer: 'Customer', commission: visible('25.5'), bankNet: visible('974.5') },
    }, EXAMPLE_COMPANY)
    if (!decoded.ok) throw new Error('fixture must decode')
    expect(fieldsFromDraft(decoded.value)).toMatchObject({ collectionChannel: 'pos', commissionAmount: '25,5', commissionBearer: 'Customer' })
  })
})
