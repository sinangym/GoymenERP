import { describe, expect, it } from 'vitest'
import {
  emptyManualReceiptFields,
  isDateOnly,
  receiptAttemptIdentity,
  validateManualReceipt,
  type ManualReceiptFields,
  type ReceiptAttemptBinding,
} from './manualReceiptForm'

const FILLED: ManualReceiptFields = {
  partyId: 'party-a',
  companyAccountId: 'account-a',
  treasuryAccountId: 'bank-a',
  currencyId: 'currency-a',
  amount: '1000',
  occurredOn: '2026-09-13',
  valueOn: '',
  bankReference: '',
}

const BINDING: ReceiptAttemptBinding = {
  sessionEpoch: 1,
  actorId: 'actor-a',
  companyId: 'company-a',
  operation: 'record',
}

function payloadOf(fields: ManualReceiptFields) {
  const result = validateManualReceipt(fields)
  if (!result.ok) throw new Error(`expected valid fields: ${JSON.stringify(result.errors)}`)
  return result.payload
}

describe('validateManualReceipt', () => {
  it('reports every missing required field at once, without inventing defaults', () => {
    const result = validateManualReceipt(emptyManualReceiptFields())
    expect(result).toEqual({
      ok: false,
      errors: {
        partyId: 'required',
        companyAccountId: 'required',
        treasuryAccountId: 'required',
        currencyId: 'required',
        amount: 'required',
        occurredOn: 'required',
      },
    })
  })

  it('normalises the payload: wire amount, blank optionals become null', () => {
    expect(payloadOf({ ...FILLED, amount: '1000,50', bankReference: '   ' })).toEqual({
      partyId: 'party-a',
      companyAccountId: 'account-a',
      treasuryAccountId: 'bank-a',
      currencyId: 'currency-a',
      amount: '1000.50',
      occurredOn: '2026-09-13',
      valueOn: null,
      bankReference: null,
    })
  })

  it('never carries company, actor, direction, scale or unallocated amount in the payload', () => {
    expect(Object.keys(payloadOf(FILLED)).sort()).toEqual([
      'amount',
      'bankReference',
      'companyAccountId',
      'currencyId',
      'occurredOn',
      'partyId',
      'treasuryAccountId',
      'valueOn',
    ])
  })

  it('keeps value date separate from the transaction date', () => {
    expect(payloadOf({ ...FILLED, valueOn: '2026-09-14' }).valueOn).toBe('2026-09-14')
    expect(payloadOf(FILLED).valueOn).toBeNull()
  })

  it.each([
    ['zero amount', { amount: '0' }, 'amount', 'not-positive'],
    ['dotted amount', { amount: '1.000' }, 'amount', 'format'],
    ['negative amount', { amount: '-1000' }, 'amount', 'format'],
    ['impossible date', { occurredOn: '2026-02-30' }, 'occurredOn', 'date-format'],
    ['instant as date', { occurredOn: '2026-09-13T00:00:00Z' }, 'occurredOn', 'date-format'],
    ['bad value date', { valueOn: '13.09.2026' }, 'valueOn', 'date-format'],
  ] as const)('refuses %s', (_label, patch, field, error) => {
    const result = validateManualReceipt({ ...FILLED, ...patch })
    expect(result.ok).toBe(false)
    if (!result.ok) expect(result.errors[field]).toBe(error)
  })
})

describe('isDateOnly', () => {
  it('accepts a real day and a leap day, rejects a non-leap one', () => {
    expect(isDateOnly('2028-02-29')).toBe(true)
    expect(isDateOnly('2026-02-29')).toBe(false)
    expect(isDateOnly('2026-9-13')).toBe(false)
  })
})

describe('receiptAttemptIdentity — what makes an attempt the same attempt', () => {
  const base = receiptAttemptIdentity(BINDING, payloadOf(FILLED))

  it('is stable for the same binding and a re-normalised identical form', () => {
    expect(receiptAttemptIdentity(BINDING, payloadOf({ ...FILLED, amount: ' 1000 ' }))).toBe(base)
  })

  it.each([
    ['another company', { ...BINDING, companyId: 'company-b' }],
    ['another actor', { ...BINDING, actorId: 'actor-b' }],
    ['a new session', { ...BINDING, sessionEpoch: 2 }],
    ['draft save instead of record', { ...BINDING, operation: 'draft-save' as const }],
  ])('changes under %s', (_label, binding) => {
    expect(receiptAttemptIdentity(binding, payloadOf(FILLED))).not.toBe(base)
  })

  it.each([
    ['one amount digit', { amount: '1001' }],
    ['a trailing fraction zero', { amount: '1000,0' }],
    ['the bank account', { treasuryAccountId: 'bank-b' }],
    ['the Cari account', { companyAccountId: 'account-b' }],
    ['the value date', { valueOn: '2026-09-14' }],
    ['the bank reference', { bankReference: 'ref' }],
  ])('changes when %s changes', (_label, patch) => {
    expect(receiptAttemptIdentity(BINDING, payloadOf({ ...FILLED, ...patch }))).not.toBe(base)
  })
})
