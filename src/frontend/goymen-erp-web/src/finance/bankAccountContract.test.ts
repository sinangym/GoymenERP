/**
 * F-04 wire decoders and the bank-account command rules. Unit evidence; SENTETIK/NOT_ENV.
 */

import { describe, expect, it } from 'vitest'
import { decodeBankAccountCommandResult, decodeBankAccountMasterPage, decodeCurrencyCandidates } from './bankAccountContract'
import {
  BANK_IDLE,
  accountAnswers,
  bankAttemptReducer,
  classifyBankReply,
  type BankAccountSnapshot,
  type BankAccountRequest,
} from './bankAccountCommand'
import { classifyReply } from './receiptCommand'
import { COMPANY_A, COMPANY_B, bankAccount } from './testHarness'

const ACCOUNT = 'aaaaaaaa-0000-4000-8000-0000000000b1'
const TRY = 'cccccccc-0000-4000-8000-0000000000c1'
const USD = 'cccccccc-0000-4000-8000-0000000000c2'

const pending = (overrides: Record<string, unknown> = {}) =>
  bankAccount(COMPANY_A, { id: ACCOUNT, currencyId: TRY, displayName: 'SENTETIK Ana', isActive: false, isUsableForReceipt: false, version: '1', ...overrides })

describe('decodeBankAccountMasterPage', () => {
  it('accepts the exact page with the caller flag', () => {
    const decoded = decodeBankAccountMasterPage({ companyId: COMPANY_A, items: [{ account: pending(), isOwnProposal: true }] }, COMPANY_A)
    expect(decoded).toEqual({
      ok: true,
      value: { companyId: COMPANY_A, items: [{ account: expect.objectContaining({ id: ACCOUNT, isActive: false }), isOwnProposal: true }] },
    })
  })

  it.each([
    ['another company', { companyId: COMPANY_B, items: [] }],
    ['an extra member', { companyId: COMPANY_A, items: [], nextCursor: null }],
    ['a missing flag', { companyId: COMPANY_A, items: [{ account: pending() }] }],
    ['a non-boolean flag', { companyId: COMPANY_A, items: [{ account: pending(), isOwnProposal: 'yes' }] }],
    ['an account of another company', { companyId: COMPANY_A, items: [{ account: pending({ companyId: COMPANY_B }), isOwnProposal: false }] }],
    ['the same account twice', { companyId: COMPANY_A, items: [{ account: pending(), isOwnProposal: false }, { account: pending(), isOwnProposal: false }] }],
  ])('refuses %s', (_label, value) => {
    expect(decodeBankAccountMasterPage(value, COMPANY_A).ok).toBe(false)
  })
})

describe('decodeCurrencyCandidates', () => {
  it('keeps the owner order and adds nothing', () => {
    const decoded = decodeCurrencyCandidates(
      { companyId: COMPANY_A, items: [
        { currencyId: USD, code: 'USD', displayLabel: 'SENTETIK Dolar' },
        { currencyId: TRY, code: 'TRY', displayLabel: 'SENTETIK Lira' },
      ] },
      COMPANY_A,
    )
    expect(decoded.ok && decoded.value.items.map((item) => item.code)).toEqual(['USD', 'TRY'])
  })

  it('accepts an honest empty list', () => {
    expect(decodeCurrencyCandidates({ companyId: COMPANY_A, items: [] }, COMPANY_A)).toEqual({ ok: true, value: { companyId: COMPANY_A, items: [] } })
  })

  it.each([
    ['a repeated currency', [{ currencyId: TRY, code: 'TRY', displayLabel: 'A' }, { currencyId: TRY, code: 'TRY', displayLabel: 'B' }]],
    ['a blank code', [{ currencyId: TRY, code: ' ', displayLabel: 'A' }]],
    ['a nil identity', [{ currencyId: '00000000-0000-0000-0000-000000000000', code: 'TRY', displayLabel: 'A' }]],
    ['an extra member', [{ currencyId: TRY, code: 'TRY', displayLabel: 'A', scale: 2 }]],
  ])('refuses %s', (_label, items) => {
    expect(decodeCurrencyCandidates({ companyId: COMPANY_A, items }, COMPANY_A).ok).toBe(false)
  })
})

describe('decodeBankAccountCommandResult', () => {
  const envelope = (overrides: Record<string, unknown>) => ({
    outcome: 'Succeeded', resource: pending(), isReplay: false, originalCorrelationId: 'SENTETIK-1', issues: [], ...overrides,
  })

  it('decodes a success and a refusal', () => {
    expect(decodeBankAccountCommandResult(envelope({}), COMPANY_A).ok).toBe(true)
    expect(decodeBankAccountCommandResult(envelope({ outcome: 'VersionConflict', resource: null, issues: [{ field: 'expectedVersion', code: 'version_conflict' }] }), COMPANY_A).ok).toBe(true)
  })

  it('accepts a status-only answer without a correlation, but never a replay without one', () => {
    expect(decodeBankAccountCommandResult(envelope({ outcome: 'Denied', resource: null, originalCorrelationId: '' }), COMPANY_A).ok).toBe(true)
    expect(decodeBankAccountCommandResult(envelope({ isReplay: true, originalCorrelationId: '' }), COMPANY_A).ok).toBe(false)
  })

  it('refuses a refusal that carries a resource, or a success without one', () => {
    expect(decodeBankAccountCommandResult(envelope({ outcome: 'Denied' }), COMPANY_A).ok).toBe(false)
    expect(decodeBankAccountCommandResult(envelope({ resource: null }), COMPANY_A).ok).toBe(false)
  })
})

describe('the draft save regression: a status-only 403 is a refusal, not an unknown', () => {
  it('classifies the HTTP port\'s restated Denied (empty correlation) as denied', () => {
    const restated = { kind: 'body' as const, body: { outcome: 'Denied', resource: null, isReplay: false, originalCorrelationId: '', issues: [] } }
    expect(
      classifyReply(restated, COMPANY_A, { operation: 'draft-save', body: { draftId: null, expectedVersion: null, preparation: null as never } }),
    ).toEqual({ type: 'refused', reason: 'denied', issues: [], replayed: false })
  })
})

describe('bank-account command rules', () => {
  const propose: BankAccountRequest = { operation: 'bank-account-propose', body: { displayName: 'SENTETIK Ana', currencyId: TRY } }
  const approve: BankAccountRequest = { operation: 'bank-account-approve', accountId: ACCOUNT, body: { expectedVersion: '1' } }
  const snapshot = (request: BankAccountRequest): BankAccountSnapshot => ({
    key: 'k-1',
    binding: { sessionEpoch: 1, actorId: 'SENTETIK-actor', companyId: COMPANY_A },
    request,
    identity: 'id-1',
  })
  const reply = (body: Record<string, unknown>) => ({ kind: 'body' as const, body })

  it('a success settles only the command it answers', () => {
    expect(accountAnswers(propose, pending() as never)).toBe(true)
    expect(accountAnswers(propose, pending({ currencyId: USD }) as never)).toBe(false)
    expect(accountAnswers(approve, pending() as never)).toBe(false)
    expect(accountAnswers(approve, pending({ isActive: true, isUsableForReceipt: true, version: '2' }) as never)).toBe(true)
    expect(
      classifyBankReply(reply({ outcome: 'Succeeded', resource: pending({ id: 'aaaaaaaa-0000-4000-8000-0000000000b9', isActive: true }), isReplay: false, originalCorrelationId: 'c', issues: [] }), COMPANY_A, approve),
    ).toEqual({ type: 'unknown', reason: 'malformed' })
  })

  it('no reply and Unavailable are unknown; the form freezes', () => {
    const submitted = bankAttemptReducer(BANK_IDLE, { type: 'SUBMIT', snapshot: snapshot(approve) })
    const unknown = bankAttemptReducer(submitted, { type: 'SETTLE', key: 'k-1', settlement: classifyBankReply({ kind: 'no-reply' }, COMPANY_A, approve) })
    expect(unknown).toMatchObject({ phase: 'unknown', reason: 'no-reply' })
    expect(classifyBankReply(reply({ outcome: 'Unavailable', resource: null, isReplay: false, originalCorrelationId: '', issues: [] }), COMPANY_A, approve)).toEqual({ type: 'unknown', reason: 'unavailable' })
    // Frozen: a fresh submit is refused.
    expect(bankAttemptReducer(unknown, { type: 'SUBMIT', snapshot: { ...snapshot(approve), key: 'k-2' } })).toBe(unknown)
  })

  it('after an unknown, a non-replayed refusal of the resend stays unknown; a replayed refusal settles', () => {
    const unknown = bankAttemptReducer(
      bankAttemptReducer(BANK_IDLE, { type: 'SUBMIT', snapshot: snapshot(approve) }),
      { type: 'SETTLE', key: 'k-1', settlement: { type: 'unknown', reason: 'no-reply' } },
    )
    const resent = bankAttemptReducer(unknown, { type: 'RESEND' })
    expect(resent).toMatchObject({ phase: 'submitting', unresolvedSince: 'no-reply' })
    const gate = bankAttemptReducer(resent, { type: 'SETTLE', key: 'k-1', settlement: { type: 'refused', reason: 'denied', issues: [], replayed: false } })
    expect(gate).toMatchObject({ phase: 'unknown', reason: 'no-reply', lastRefusal: 'denied' })
    const replayed = bankAttemptReducer(bankAttemptReducer(gate, { type: 'RESEND' }), {
      type: 'SETTLE', key: 'k-1', settlement: { type: 'refused', reason: 'version-conflict', issues: [], replayed: true },
    })
    expect(replayed).toMatchObject({ phase: 'refused', reason: 'version-conflict' })
  })

  it('a late answer for another key settles nothing', () => {
    const submitted = bankAttemptReducer(BANK_IDLE, { type: 'SUBMIT', snapshot: snapshot(propose) })
    expect(bankAttemptReducer(submitted, { type: 'SETTLE', key: 'k-other', settlement: { type: 'unknown', reason: 'no-reply' } })).toBe(submitted)
  })
})
