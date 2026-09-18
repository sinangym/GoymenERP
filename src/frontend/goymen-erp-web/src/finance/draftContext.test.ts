/**
 * Draft read context decoder against the VERBATIM #261 example (SENTETIK/NOT_ENV).
 * Parsing evidence only — not provider, profile, permission or HTTP runtime readiness.
 */

import { describe, expect, it } from 'vitest'
import { DRAFT_CONTEXT_EXAMPLE, EXAMPLE_COMPANY } from './contractExamples'
import { decodeDraftContext, draftContextRequestFor, partyLabelText, type DraftContextRequest } from './draftContext'

const REQUEST: DraftContextRequest = DRAFT_CONTEXT_EXAMPLE.request
const clone = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T

// eslint-disable-next-line @typescript-eslint/no-explicit-any -- mutation table edits arbitrary JSON
type Body = Record<string, any>

function mutated(edit: (body: Body) => void): unknown {
  const body = clone(DRAFT_CONTEXT_EXAMPLE.response) as Body
  edit(body)
  return body
}

describe('decodeDraftContext — #261 example', () => {
  it('decodes the example and keeps profile version and scale exactly', () => {
    const decoded = decodeDraftContext(DRAFT_CONTEXT_EXAMPLE.response, EXAMPLE_COMPANY, REQUEST)
    expect(decoded.ok).toBe(true)
    if (!decoded.ok) return
    expect(decoded.value.currency.profile.reference).toEqual({ profileId: '77777777-7777-7777-7777-777777777777', version: '9007199254740993' })
    expect(decoded.value.currency.profile.scale).toBe(0)
    expect(decoded.value.currency.displayCode).toBe('SENTETIK')
    expect(partyLabelText(decoded.value.party)).toBe('SENTETIK-code — SENTETIK legal name')
  })

  it('shows the trade name in the label when the owner supplies one', () => {
    const decoded = decodeDraftContext(mutated((b) => (b.party.tradeName = 'SENTETIK trade')), EXAMPLE_COMPANY, REQUEST)
    expect(decoded.ok && partyLabelText(decoded.value.party)).toBe('SENTETIK-code — SENTETIK legal name (SENTETIK trade)')
  })

  it('accepts a profile whose validity still covers the date', () => {
    const body = mutated((b) => {
      b.currency.profile.effectiveFrom = '2026-01-01'
      b.currency.profile.effectiveThrough = '2026-09-13'
    })
    expect(decodeDraftContext(body, EXAMPLE_COMPANY, REQUEST).ok).toBe(true)
  })
})

describe('decodeDraftContext — refuses a body that does not answer this request', () => {
  it.each([
    ['another company', (b: Body) => (b.companyId = 'bbbbbbbb-0000-4000-8000-000000000002')],
    ['another date', (b: Body) => (b.occurredOn = '2026-09-14')],
    ['another bank account', (b: Body) => (b.bankAccount.id = 'cccccccc-0000-4000-8000-000000000003')],
    ['bank of another company', (b: Body) => (b.bankAccount.companyId = 'bbbbbbbb-0000-4000-8000-000000000002')],
    ['inactive bank', (b: Body) => (b.bankAccount.isActive = false)],
    ['bank not usable for receipts', (b: Body) => (b.bankAccount.isUsableForReceipt = false)],
    ['currency not the bank currency', (b: Body) => (b.currency.currencyId = '99999999-0000-4000-8000-000000000009')],
    ['blank currency code', (b: Body) => (b.currency.displayCode = ' ')],
    ['another party', (b: Body) => (b.party.partyId = 'dddddddd-0000-4000-8000-000000000004')],
    ['another company account', (b: Body) => (b.party.companyAccountId = 'eeeeeeee-0000-4000-8000-000000000005')],
    ['blank legal name', (b: Body) => (b.party.legalName = '')],
    ['profile not yet valid', (b: Body) => (b.currency.profile.effectiveFrom = '2026-09-14')],
    ['profile expired', (b: Body) => (b.currency.profile.effectiveThrough = '2026-09-12')],
    ['negative scale', (b: Body) => (b.currency.profile.scale = -1)],
    ['fractional scale', (b: Body) => (b.currency.profile.scale = 1.5)],
    ['scale as text', (b: Body) => (b.currency.profile.scale = '2')],
    ['profile version past Int64', (b: Body) => (b.currency.profile.reference.version = '9223372036854775808')],
    ['numeric profile version', (b: Body) => (b.currency.profile.reference.version = 9007199254740993)],
    ['nil profile id', (b: Body) => (b.currency.profile.reference.profileId = '00000000-0000-0000-0000-000000000000')],
    ['instant as effective date', (b: Body) => (b.currency.profile.effectiveFrom = '2026-09-13T00:00:00Z')],
    ['an amount member', (b: Body) => (b.amount = '0')],
    ['a balance member on the bank', (b: Body) => (b.bankAccount.balance = '0')],
    ['missing nullable tradeName', (b: Body) => delete b.party.tradeName],
    ['missing nullable effectiveThrough', (b: Body) => delete b.currency.profile.effectiveThrough],
  ])('rejects %s', (_label, edit) => {
    expect(decodeDraftContext(mutated(edit), EXAMPLE_COMPANY, REQUEST).ok).toBe(false)
  })

  it('rejects a context decoded under another shell company', () => {
    expect(decodeDraftContext(DRAFT_CONTEXT_EXAMPLE.response, 'bbbbbbbb-0000-4000-8000-000000000002', REQUEST).ok).toBe(false)
  })
})

describe('draftContextRequestFor', () => {
  const party = { partyId: REQUEST.partyId, companyAccountId: REQUEST.companyAccountId }

  it('builds a request only for a complete, well-formed selection', () => {
    expect(draftContextRequestFor(REQUEST.treasuryAccountId, party, ' 2026-09-13 ')).toEqual(REQUEST)
    expect(draftContextRequestFor('', party, '2026-09-13')).toBeNull()
    expect(draftContextRequestFor(REQUEST.treasuryAccountId, party, '')).toBeNull()
    expect(draftContextRequestFor(REQUEST.treasuryAccountId, party, '2026-02-30')).toBeNull()
    expect(draftContextRequestFor(REQUEST.treasuryAccountId, { ...party, partyId: 'not-a-uuid' }, '2026-09-13')).toBeNull()
  })
})
