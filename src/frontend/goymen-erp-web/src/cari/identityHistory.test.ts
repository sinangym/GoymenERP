import { describe, expect, it } from 'vitest'
import {
  IDENTITY_HISTORY_TEXT as T,
  LEGAL_BASIS_MAX,
  checkSuccession,
  decodeIdentityHistoryView,
  identityHistoryPath,
  identityHistoryProblemCodeOf,
  isBusinessDate,
  successionPath,
} from './identityHistory'

const PARTY = '11111111-1111-1111-1111-111111111111'
const OTHER = '22222222-2222-2222-2222-222222222222'
const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const expected = { partyId: PARTY, companyId: COMPANY }

const revision = (over: Record<string, unknown> = {}) => ({
  attribute: 'TaxIdentityNumber',
  kind: 'Correction',
  sequence: '2',
  value: '1234567890',
  effectiveFrom: '2026-11-10',
  reason: 'Yanlış girilmiş VKN düzeltildi.',
  recordedInActiveCompany: true,
  recordedAtUtc: '2026-11-10T08:00:00+00:00',
  ...over,
})

const link = (over: Record<string, unknown> = {}) => ({
  predecessorPartyId: PARTY,
  successorPartyId: OTHER,
  otherPartyId: OTHER,
  otherPartyCode: 'C-000002',
  otherPartyLegalName: 'Yeni Örnek A.Ş.',
  kind: 'Conversion',
  legalBasis: 'Ticaret sicili ilanı',
  effectiveOn: '2026-11-01',
  recordedAtUtc: '2026-11-02T09:00:00+00:00',
  ...over,
})

const raw = (over: Record<string, unknown> = {}) => ({
  partyId: PARTY,
  companyId: COMPANY,
  code: 'C-000001',
  kind: 1,
  accountVersion: '5',
  accountIsActive: true,
  taxIdentityNumber: '1234567890',
  legalName: 'Örnek Ticaret A.Ş.',
  revisions: [],
  eDocumentStatusRevisions: [],
  successor: null,
  predecessors: [],
  viewer: { canRecordSuccession: false },
  ...over,
})

describe('F-027 / KLN-D38 identity history contract', () => {
  it('decodes the exact shape, with dated revisions, status revisions and links', () => {
    expect(decodeIdentityHistoryView(raw(), expected)).toEqual(raw())
    const full = raw({
      accountIsActive: false,
      revisions: [
        revision({ kind: 'Baseline', sequence: '1', value: '1234567891', effectiveFrom: null, reason: null,
          recordedInActiveCompany: false }),
        revision(),
      ],
      eDocumentStatusRevisions: [
        { sequence: '1', status: 'EInvoice', effectiveFrom: '2026-11-03', recordedInActiveCompany: true,
          recordedAtUtc: '2026-11-03T10:00:00+00:00' },
      ],
      successor: link(),
      viewer: { canRecordSuccession: true },
    })
    expect(decodeIdentityHistoryView(full, expected)).toEqual(full)
  })

  it('refuses an unknown or missing key, a numeric version and a record for another party or company', () => {
    expect(decodeIdentityHistoryView({ ...raw(), version: '5' }, expected)).toBeNull()
    const { revisions: _omitted, ...missing } = raw()
    expect(decodeIdentityHistoryView(missing, expected)).toBeNull()
    expect(decodeIdentityHistoryView(raw({ accountVersion: 5 }), expected)).toBeNull()
    expect(decodeIdentityHistoryView(raw({ partyId: OTHER }), expected)).toBeNull()
    expect(decodeIdentityHistoryView(raw({ companyId: 'bbbbbbbb-0000-0000-0000-000000000002' }), expected)).toBeNull()
    expect(decodeIdentityHistoryView(raw({ kind: 'LegalEntity' }), expected)).toBeNull()
    expect(decodeIdentityHistoryView(raw({ viewer: { canRecordSuccession: true, canApprove: true } }), expected))
      .toBeNull()
  })

  it('refuses a revision whose date does not fit its kind or whose reason leaks from another company', () => {
    expect(decodeIdentityHistoryView(raw({ revisions: [revision({ effectiveFrom: null })] }), expected)).toBeNull()
    expect(
      decodeIdentityHistoryView(raw({ revisions: [revision({ kind: 'Baseline', reason: null })] }), expected),
    ).toBeNull()
    expect(
      decodeIdentityHistoryView(raw({ revisions: [revision({ recordedInActiveCompany: false })] }), expected),
    ).toBeNull()
    expect(decodeIdentityHistoryView(raw({ revisions: [revision({ value: '12AB' })] }), expected)).toBeNull()
    expect(decodeIdentityHistoryView(raw({ revisions: [revision({ effectiveFrom: '2026-02-30' })] }), expected))
      .toBeNull()
  })

  it('refuses a link on the wrong side, a self-link and an unknown succession kind', () => {
    expect(decodeIdentityHistoryView(raw({ successor: link({ predecessorPartyId: OTHER }) }), expected)).toBeNull()
    expect(decodeIdentityHistoryView(raw({ successor: link({ otherPartyId: PARTY }) }), expected)).toBeNull()
    expect(decodeIdentityHistoryView(raw({ successor: link({ kind: 'Sale' }) }), expected)).toBeNull()
    expect(decodeIdentityHistoryView(raw({ predecessors: [link()] }), expected)).toBeNull()
    expect(
      decodeIdentityHistoryView(
        raw({ predecessors: [link({ predecessorPartyId: OTHER, successorPartyId: PARTY, otherPartyId: OTHER })] }),
        expected,
      )?.predecessors,
    ).toHaveLength(1)
  })

  it('mirrors the server checks that need no data', () => {
    const draft = { successorPartyId: ` ${OTHER} `, kind: 'Merger' as const, legalBasis: '  İlan  ',
      effectiveOn: '2026-11-01' }
    expect(checkSuccession(draft, PARTY)).toEqual({
      ok: true,
      value: { successorPartyId: OTHER, kind: 'Merger', legalBasis: 'İlan', effectiveOn: '2026-11-01' },
    })
    expect(checkSuccession({ ...draft, successorPartyId: 'x' }, PARTY)).toMatchObject({ ok: false,
      field: 'successorPartyId', message: T.successorRequired })
    expect(checkSuccession({ ...draft, successorPartyId: PARTY }, PARTY)).toMatchObject({ ok: false,
      message: T.successorSelf })
    expect(checkSuccession({ ...draft, kind: '' }, PARTY)).toMatchObject({ ok: false, field: 'kind' })
    expect(checkSuccession({ ...draft, legalBasis: '   ' }, PARTY)).toMatchObject({ ok: false,
      message: T.legalBasisRequired })
    expect(checkSuccession({ ...draft, legalBasis: 'x'.repeat(LEGAL_BASIS_MAX + 1) }, PARTY)).toMatchObject({
      ok: false, message: T.legalBasisTooLong })
    expect(checkSuccession({ ...draft, effectiveOn: '' }, PARTY)).toMatchObject({ ok: false, field: 'effectiveOn' })
  })

  it('reads only the endpoint codes, checks real dates and escapes the path segment', () => {
    expect(identityHistoryProblemCodeOf({ code: 'succession_cycle' })).toBe('succession_cycle')
    expect(identityHistoryProblemCodeOf({ code: 'customer_group_exists' })).toBeNull()
    expect(identityHistoryProblemCodeOf(null)).toBeNull()
    expect(isBusinessDate('2028-02-29')).toBe(true)
    expect(isBusinessDate('2026-02-29')).toBe(false)
    expect(identityHistoryPath('a/b')).toBe('/counterparties/a%2Fb/identity-history')
    expect(successionPath('a/b')).toBe('/counterparties/a%2Fb/succession')
  })
})
