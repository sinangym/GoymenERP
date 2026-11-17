import { describe, expect, it } from 'vitest'
import {
  decodeCashAccountList,
  decodeExpenseDraftContext,
  decodeExpenseItemCatalog,
  decodeExpensePaymentCommandResult,
  decodeExpensePaymentFor,
  decodeExpensePaymentPage,
  decodeExpensePaymentView,
} from './expensePaymentContract'

// SENTETIK decoder fixtures: shapes only, no real company, account or person.
const COMPANY = '11111111-1111-4111-8111-111111111111'
const OTHER = '99999999-9999-4999-8999-999999999999'
const PAYMENT = '33333333-3333-4333-8333-333333333333'
const ACCOUNT = '44444444-4444-4444-8444-444444444444'
const ITEM = '55555555-5555-4555-8555-555555555555'
const CURRENCY = '22222222-2222-4222-8222-222222222222'
const PROFILE = '66666666-6666-4666-8666-666666666666'

const preparation = (patch: Record<string, unknown> = {}) => ({
  sourceKind: 'Bank',
  treasuryAccountId: ACCOUNT,
  expenseItemId: ITEM,
  money: { amount: '125.50', currencyId: CURRENCY },
  profile: { profileId: PROFILE, version: '3' },
  occurredOn: '2026-11-10',
  valueOn: null,
  reference: null,
  description: 'SENTETIK kırtasiye gideri',
  ...patch,
})

const draft = (patch: Record<string, unknown> = {}) => ({
  id: PAYMENT,
  companyId: COMPANY,
  status: 'Draft',
  preparation: preparation(),
  preparedBy: 'maker-a',
  preparedAtUtc: '2026-11-10T08:00:00+00:00',
  approvedBy: null,
  approvedAtUtc: null,
  recordedBy: null,
  recordedAtUtc: null,
  paymentConfirmed: false,
  version: '1',
  ...patch,
})

const approved = (patch: Record<string, unknown> = {}) =>
  draft({ status: 'Approved', approvedBy: 'checker-b', approvedAtUtc: '2026-11-10T09:00:00+00:00', version: '2', ...patch })

const recorded = (patch: Record<string, unknown> = {}) => approved({
  status: 'Recorded', recordedBy: 'checker-b', recordedAtUtc: '2026-11-10T10:00:00+00:00', paymentConfirmed: true,
  version: '3', ...patch,
})

describe('expense payment view decoder', () => {
  it('accepts each consistent status', () => {
    expect(decodeExpensePaymentView(draft(), COMPANY).ok).toBe(true)
    expect(decodeExpensePaymentView(approved(), COMPANY).ok).toBe(true)
    expect(decodeExpensePaymentView(recorded(), COMPANY).ok).toBe(true)
  })

  it('refuses a missing or an extra key', () => {
    const { version: _version, ...missing } = draft()
    expect(decodeExpensePaymentView(missing, COMPANY).ok).toBe(false)
    expect(decodeExpensePaymentView({ ...draft(), partyId: ACCOUNT }, COMPANY).ok).toBe(false)
    expect(decodeExpensePaymentView(draft({ preparation: { ...preparation(), partyId: ACCOUNT } }), COMPANY).ok).toBe(false)
  })

  it('refuses another company', () => {
    expect(decodeExpensePaymentView(draft(), OTHER).ok).toBe(false)
  })

  it('refuses a payment without a description (FNS-D25)', () => {
    expect(decodeExpensePaymentView(draft({ preparation: preparation({ description: '' }) }), COMPANY).ok).toBe(false)
    expect(decodeExpensePaymentView(draft({ preparation: preparation({ description: '   ' }) }), COMPANY).ok).toBe(false)
    expect(decodeExpensePaymentView(draft({ preparation: preparation({ description: null }) }), COMPANY).ok).toBe(false)
    expect(decodeExpensePaymentView(draft({ preparation: preparation({ description: 'x'.repeat(1001) }) }), COMPANY).ok)
      .toBe(false)
  })

  it('refuses a missing expense item identity, an unknown source and a non-positive amount', () => {
    expect(decodeExpensePaymentView(draft({ preparation: preparation({ expenseItemId: null }) }), COMPANY).ok).toBe(false)
    expect(decodeExpensePaymentView(draft({ preparation: preparation({ sourceKind: 'Card' }) }), COMPANY).ok).toBe(false)
    expect(decodeExpensePaymentView(
      draft({ preparation: preparation({ money: { amount: '0', currencyId: CURRENCY } }) }), COMPANY).ok).toBe(false)
    expect(decodeExpensePaymentView(
      draft({ preparation: preparation({ money: { amount: 125.5, currencyId: CURRENCY } }) }), COMPANY).ok).toBe(false)
  })

  it('refuses a status that its facts contradict', () => {
    expect(decodeExpensePaymentView(draft({ approvedBy: 'checker-b' }), COMPANY).ok).toBe(false)
    expect(decodeExpensePaymentView(approved({ approvedBy: 'maker-a' }), COMPANY).ok).toBe(false)
    expect(decodeExpensePaymentView(recorded({ paymentConfirmed: false }), COMPANY).ok).toBe(false)
    expect(decodeExpensePaymentView(approved({ recordedBy: 'checker-b' }), COMPANY).ok).toBe(false)
  })

  it('takes the detail only for the payment asked for', () => {
    expect(decodeExpensePaymentFor(draft(), COMPANY, PAYMENT)).not.toBeNull()
    expect(decodeExpensePaymentFor(draft(), COMPANY, ACCOUNT)).toBeNull()
  })
})

describe('expense payment page decoder', () => {
  it('accepts a page and its cursor', () => {
    const page = decodeExpensePaymentPage({ companyId: COMPANY, items: [draft()], nextCursor: 'opaque' }, COMPANY)
    expect(page?.items).toHaveLength(1)
    expect(page?.nextCursor).toBe('opaque')
  })

  it('refuses a duplicate row, an empty cursor and another company', () => {
    expect(decodeExpensePaymentPage({ companyId: COMPANY, items: [draft(), draft()], nextCursor: null }, COMPANY)).toBeNull()
    expect(decodeExpensePaymentPage({ companyId: COMPANY, items: [], nextCursor: '' }, COMPANY)).toBeNull()
    expect(decodeExpensePaymentPage({ companyId: OTHER, items: [], nextCursor: null }, COMPANY)).toBeNull()
  })
})

describe('expense payment command result decoder', () => {
  const result = (patch: Record<string, unknown> = {}) => ({
    outcome: 'Succeeded', resource: draft(), isReplay: false, originalCorrelationId: 'corr-1', issues: [], ...patch,
  })

  it('accepts a success and a refusal without a resource', () => {
    expect(decodeExpensePaymentCommandResult(result(), COMPANY).ok).toBe(true)
    expect(decodeExpensePaymentCommandResult(result({
      outcome: 'ValidationFailed', resource: null,
      issues: [{ field: 'preparation.description', code: 'expense_payment_description_required' }],
    }), COMPANY).ok).toBe(true)
  })

  it('refuses a refusal carrying a resource, an unknown outcome and a malformed issue', () => {
    expect(decodeExpensePaymentCommandResult(result({ outcome: 'ValidationFailed' }), COMPANY).ok).toBe(false)
    expect(decodeExpensePaymentCommandResult(result({ outcome: 'Done' }), COMPANY).ok).toBe(false)
    expect(decodeExpensePaymentCommandResult(result({
      outcome: 'ValidationFailed', resource: null, issues: [{ code: 'x' }],
    }), COMPANY).ok).toBe(false)
  })

  it('refuses a replay without its original correlation id', () => {
    expect(decodeExpensePaymentCommandResult(result({ isReplay: true, originalCorrelationId: '' }), COMPANY).ok).toBe(false)
  })
})

describe('expense item catalog decoder', () => {
  it('accepts the single item by identity and code', () => {
    expect(decodeExpenseItemCatalog({ companyId: COMPANY, items: [{ id: ITEM, code: 'general-expense' }] }, COMPANY)?.items)
      .toEqual([{ id: ITEM, code: 'general-expense' }])
  })

  it('refuses a free-text label, a duplicate id and another company', () => {
    expect(decodeExpenseItemCatalog({ companyId: COMPANY, items: [{ id: ITEM, code: 'Genel Masraf' }] }, COMPANY)).toBeNull()
    expect(decodeExpenseItemCatalog({
      companyId: COMPANY, items: [{ id: ITEM, code: 'a' }, { id: ITEM, code: 'b' }],
    }, COMPANY)).toBeNull()
    expect(decodeExpenseItemCatalog({ companyId: OTHER, items: [] }, COMPANY)).toBeNull()
  })
})

describe('draft context decoder', () => {
  const request = { sourceKind: 'Cash' as const, treasuryAccountId: ACCOUNT, occurredOn: '2026-11-10' }
  const context = (patch: Record<string, unknown> = {}) => ({
    companyId: COMPANY, sourceKind: 'Cash', treasuryAccountId: ACCOUNT, currencyId: CURRENCY,
    profile: { profileId: PROFILE, version: '3' }, occurredOn: '2026-11-10', ...patch,
  })

  it('accepts the answer to the question asked', () => {
    expect(decodeExpenseDraftContext(context(), COMPANY, request)?.currencyId).toBe(CURRENCY)
  })

  it('refuses an answer for another source, account or date', () => {
    expect(decodeExpenseDraftContext(context({ sourceKind: 'Bank' }), COMPANY, request)).toBeNull()
    expect(decodeExpenseDraftContext(context({ treasuryAccountId: PAYMENT }), COMPANY, request)).toBeNull()
    expect(decodeExpenseDraftContext(context({ occurredOn: '2026-11-11' }), COMPANY, request)).toBeNull()
    expect(decodeExpenseDraftContext(context({ profile: null }), COMPANY, request)).toBeNull()
  })
})

describe('cash account list decoder', () => {
  const item = (patch: Record<string, unknown> = {}) => ({
    account: {
      id: ACCOUNT, companyId: COMPANY, currencyId: CURRENCY, displayName: 'SENTETIK Merkez Kasa', status: 'Approved',
      isActive: true, rejectionReason: null, pendingChange: null, version: '2', ...patch,
    },
    isOwnProposal: false,
    isOwnPendingChange: false,
  })

  it('reduces a valid list to what the screen offers', () => {
    expect(decodeCashAccountList({ companyId: COMPANY, items: [item()] }, COMPANY)?.items).toEqual([{
      id: ACCOUNT, currencyId: CURRENCY, displayName: 'SENTETIK Merkez Kasa', status: 'Approved', isActive: true,
      passivationPending: false,
    }])
  })

  it('marks only a pending passivation (FNS-D50), not a pending reactivation', () => {
    expect(decodeCashAccountList({ companyId: COMPANY, items: [item({ pendingChange: 'Passivation' })] }, COMPANY)
      ?.items.map((account) => account.passivationPending)).toEqual([true])
    expect(decodeCashAccountList({
      companyId: COMPANY, items: [item({ status: 'Passive', isActive: false, pendingChange: 'Reactivation' })],
    }, COMPANY)?.items.map((account) => account.passivationPending)).toEqual([false])
  })

  it('refuses a drifted account shape', () => {
    expect(decodeCashAccountList({ companyId: COMPANY, items: [item({ status: 'Open' })] }, COMPANY)).toBeNull()
    expect(decodeCashAccountList({ companyId: COMPANY, items: [item({ pendingChange: 'Rename' })] }, COMPANY)).toBeNull()
    expect(decodeCashAccountList({ companyId: COMPANY, items: [item({ companyId: OTHER })] }, COMPANY)).toBeNull()
  })
})


describe('Q7 actual recorded document number', () => {
  it('accepts additive number proof and preserves a legacy missing proof without a fallback', () => {
    expect(decodeExpensePaymentView(recorded(), COMPANY).ok).toBe(true)
    const number = { number: 'DEK-2026-000001', formatVersion: '9007199254740993' }
    const decoded = decodeExpensePaymentView(recorded({ documentNumber: number }), COMPANY)
    expect(decoded.ok).toBe(true)
    if (decoded.ok) expect(decoded.value.documentNumber).toEqual(number)
    for (const bad of [null, { ...number, formatVersion: 1 }, { ...number, extra: true }])
      expect(decodeExpensePaymentView(recorded({ documentNumber: bad }), COMPANY).ok).toBe(false)
    expect(decodeExpensePaymentView(recorded({ companyId: '99999999-9999-4999-8999-999999999999', documentNumber: number }), COMPANY).ok).toBe(false)
  })
})
