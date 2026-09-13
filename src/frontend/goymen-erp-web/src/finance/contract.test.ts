/**
 * Decoder/encoder tests against the VERBATIM #258 examples (SENTETIK/NOT_ENV).
 * Contract evidence for this client's parsing only — not API, SQL or provider readiness.
 */

import { describe, expect, it } from 'vitest'
import {
  decodeBankAccountPage,
  decodeCommandResult,
  decodeReadResult,
  decodeSourceDetail,
  decodeSourcePage,
} from './contract'
import {
  EXAMPLE_ACCOUNT,
  EXAMPLE_BANK,
  EXAMPLE_COMPANY,
  EXAMPLE_CURRENCY,
  EXAMPLE_PARTY,
  EXAMPLE_PROFILE,
  NEGATIVE_EXAMPLE,
  NORMAL_EXAMPLE,
} from './contractExamples'
import { buildDraftSaveRequest } from './manualReceiptForm'
import { bankAccount, bankPage, sourceDetail, sourceSummary } from './testHarness'

const A = EXAMPLE_COMPANY
const clone = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T

describe('normal example', () => {
  it('decodes the Succeeded draft result and keeps amount and version as exact text', () => {
    const decoded = decodeCommandResult(NORMAL_EXAMPLE.draftSaveResult, A)
    expect(decoded.ok).toBe(true)
    if (!decoded.ok || decoded.value.outcome !== 'Succeeded') throw new Error('expected success')
    const { source, unallocated } = decoded.value.resource
    expect(source.stage).toBe('Draft')
    expect(source.version).toBe('9007199254740993') // past 2^53 — a number would round it
    expect(source.amount).toEqual({ disclosure: 'Visible', money: { amount: '1000', currencyId: EXAMPLE_CURRENCY } })
    expect(unallocated).toEqual({ knowledge: 'Unavailable', amount: { disclosure: 'Unavailable', money: null } })
  })

  it('the form builds exactly the example DraftSave create request', () => {
    const built = buildDraftSaveRequest(
      { treasuryAccountId: EXAMPLE_BANK, amount: '1000', occurredOn: '2026-09-13', valueOn: '', bankReference: '', description: 'SENTETIK/NOT_ENV' },
      {
        party: { partyId: EXAMPLE_PARTY, companyAccountId: EXAMPLE_ACCOUNT },
        identity: { draftId: null, expectedVersion: null },
        profile: EXAMPLE_PROFILE,
        bankAccounts: [bankAccount(A) as never],
      },
    )
    expect(built).toEqual({ ok: true, request: NORMAL_EXAMPLE.draftSave })
    if (built.ok) expect(JSON.stringify(built.request)).toBe(JSON.stringify(NORMAL_EXAMPLE.draftSave))
  })

  it('reads wrap resources in ReadResult with the outcome in the body', () => {
    expect(decodeReadResult({ outcome: 'Found', resource: bankPage(A) }, (r) => decodeBankAccountPage(r, A)).ok).toBe(true)
    expect(decodeReadResult({ outcome: 'Denied', resource: null }, (r) => decodeBankAccountPage(r, A))).toEqual({
      ok: true,
      value: { outcome: 'Denied', resource: null },
    })
    // A non-Found outcome carrying a resource, or Found without one, is not trusted.
    expect(decodeReadResult({ outcome: 'Denied', resource: bankPage(A) }, (r) => decodeBankAccountPage(r, A)).ok).toBe(false)
    expect(decodeReadResult({ outcome: 'Found', resource: null }, (r) => decodeBankAccountPage(r, A)).ok).toBe(false)
  })

  it('a list page decodes and carries the cursor verbatim', () => {
    const page = decodeSourcePage({ companyId: A, items: [sourceSummary(A)], nextCursor: 'opaque==' }, A)
    expect(page.ok && page.value.nextCursor).toBe('opaque==')
  })
})

describe('negative example results', () => {
  it.each([
    ['unavailableResult', 'Unavailable'],
    ['conflictResult', 'VersionConflict'],
    ['idempotencyConflictResult', 'IdempotencyConflict'],
  ] as const)('%s decodes as %s with no resource', (name, outcome) => {
    const decoded = decodeCommandResult(NEGATIVE_EXAMPLE[name], A)
    expect(decoded.ok && decoded.value.outcome).toBe(outcome)
    expect(decoded.ok && decoded.value.resource).toBeNull()
  })

  it('a redacted amount and an unavailable allocation decode without any digits', () => {
    const detail = decodeSourceDetail(
      sourceDetail(A, { source: { stage: 'Recorded', amount: NEGATIVE_EXAMPLE.redactedAmount }, unallocated: NEGATIVE_EXAMPLE.unavailableAllocation }),
      A,
    )
    expect(detail.ok).toBe(true)
    if (detail.ok) {
      expect(detail.value.source.amount).toEqual({ disclosure: 'Redacted', money: null })
      expect(JSON.stringify(detail.value)).not.toContain('"amount":"0"')
    }
  })
})

describe('fail-closed mutations of the normal resource', () => {
  const resource = NORMAL_EXAMPLE.draftSaveResult.resource

  function mutated(edit: (detail: Record<string, any>) => void): unknown {
    const detail = clone(resource) as Record<string, any>
    edit(detail)
    return detail
  }

  it.each([
    ['numeric amount', (d: Record<string, any>) => (d.source.amount.money.amount = 1000)],
    ['exponent amount', (d: Record<string, any>) => (d.source.amount.money.amount = '1e3')],
    ['zero source amount', (d: Record<string, any>) => (d.source.amount.money.amount = '0')],
    ['instant as occurredOn', (d: Record<string, any>) => (d.source.occurredOn = '2026-09-13T00:00:00Z')],
    ['numeric version', (d: Record<string, any>) => (d.source.version = 9007199254740993)],
    ['blank version', (d: Record<string, any>) => (d.source.version = '')],
    ['unknown member', (d: Record<string, any>) => (d.balance = '1000')],
    ['missing nullable member', (d: Record<string, any>) => delete d.source.valueOn],
    ['foreign company', (d: Record<string, any>) => (d.source.companyId = 'bbbbbbbb-0000-4000-8000-000000000002')],
    ['money in another currency', (d: Record<string, any>) => (d.source.amount.money.currencyId = EXAMPLE_ACCOUNT)],
    ['redacted amount with digits', (d: Record<string, any>) => (d.source.amount = { disclosure: 'Redacted', money: { amount: '1000', currencyId: EXAMPLE_CURRENCY } })],
    ['unknown stage token', (d: Record<string, any>) => (d.source.stage = 'Posted')],
    ['unknown disclosure token', (d: Record<string, any>) => (d.source.amount.disclosure = 'Hidden')],
    ['integer enum', (d: Record<string, any>) => (d.source.stage = 0)],
    ['unallocated unavailable but amount visible', (d: Record<string, any>) => (d.unallocated.amount = { disclosure: 'Visible', money: { amount: '0', currencyId: EXAMPLE_CURRENCY } })],
    ['known allocation with unavailable amount', (d: Record<string, any>) => (d.unallocated.knowledge = 'Known')],
    ['nil party id', (d: Record<string, any>) => (d.source.partyId = '00000000-0000-0000-0000-000000000000')],
    ['profile version with leading zero', (d: Record<string, any>) => (d.profile.version = '09')],
  ])('rejects %s', (_label, edit) => {
    expect(decodeSourceDetail(mutated(edit), A).ok).toBe(false)
  })

  it('source and bank versions are opaque strings: etag-v1 is valid, blank is not', () => {
    expect(decodeSourceDetail(mutated((d) => (d.source.version = 'etag-v1')), A).ok).toBe(true)
    expect(decodeSourceDetail(mutated((d) => (d.source.version = '   ')), A).ok).toBe(false)
    expect(decodeBankAccountPage(bankPage(A, [bankAccount(A, { version: 'etag-v1' })]), A).ok).toBe(true)
    expect(decodeBankAccountPage(bankPage(A, [bankAccount(A, { version: '' })]), A).ok).toBe(false)
  })

  it('a profile version is a positive Int64: MaxValue passes, MaxValue + 1 does not', () => {
    expect(decodeSourceDetail(mutated((d) => (d.profile.version = '9223372036854775807')), A).ok).toBe(true)
    expect(decodeSourceDetail(mutated((d) => (d.profile.version = '9223372036854775808')), A).ok).toBe(false)
    expect(decodeSourceDetail(mutated((d) => (d.profile.version = '10000000000000000000')), A).ok).toBe(false)
    expect(decodeSourceDetail(mutated((d) => (d.profile.version = 'etag-v1')), A).ok).toBe(false)
  })

  it('accepts a known zero unallocated amount — nothing left is a real answer', () => {
    const detail = mutated((d) => {
      d.source.stage = 'Reversed'
      d.unallocated = { knowledge: 'Known', amount: { disclosure: 'Visible', money: { amount: '0', currencyId: EXAMPLE_CURRENCY } } }
    })
    expect(decodeSourceDetail(detail, A).ok).toBe(true)
  })

  it('a Succeeded result without a resource, or a failure with one, is not trusted', () => {
    expect(decodeCommandResult({ ...NORMAL_EXAMPLE.draftSaveResult, resource: null }, A).ok).toBe(false)
    expect(decodeCommandResult({ ...NEGATIVE_EXAMPLE.conflictResult, resource }, A).ok).toBe(false)
    expect(decodeCommandResult({ ...NEGATIVE_EXAMPLE.conflictResult, outcome: 3 }, A).ok).toBe(false)
  })

  it('a bank page item from another company is refused', () => {
    expect(decodeBankAccountPage(bankPage(A, [bankAccount('bbbbbbbb-0000-4000-8000-000000000002')]), A).ok).toBe(false)
    expect(decodeBankAccountPage(bankPage(A, [bankAccount(A, { isUsableForReceipt: 'yes' })]), A).ok).toBe(false)
  })
})
