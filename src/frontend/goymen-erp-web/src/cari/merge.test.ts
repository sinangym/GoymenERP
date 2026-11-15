import { describe, expect, it } from 'vitest'
import {
  MERGE_CANDIDATE_LIMIT,
  MERGE_REASON_MAX,
  MERGE_TEXT,
  checkMergeReason,
  decodeMergeView,
  mergePath,
  mergeProblemCodeOf,
} from './merge'

const PARTY = '11111111-1111-1111-1111-111111111111'
const DUPLICATE = '22222222-2222-2222-2222-222222222222'
const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const expected = { partyId: PARTY, companyId: COMPANY }

const candidate = (over: Record<string, unknown> = {}) => ({
  partyId: DUPLICATE,
  code: 'C-000002',
  legalName: 'Örnek Ticaret A.Ş. (mükerrer)',
  version: '4',
  ...over,
})

const raw = (over: Record<string, unknown> = {}) => ({
  partyId: PARTY,
  companyId: COMPANY,
  code: 'C-000001',
  version: '7',
  legalName: 'Örnek Ticaret A.Ş.',
  mergedInto: null,
  candidates: [candidate()],
  viewer: { canApproveMerge: true },
  ...over,
})

describe('F-027 / EK-18 #76 party merge contract', () => {
  it('decodes the exact shape with candidates, and a merged card with its target', () => {
    expect(decodeMergeView(raw(), expected)).toEqual(raw())
    const merged = raw({ mergedInto: { partyId: DUPLICATE, code: 'C-000002' }, candidates: [], viewer: { canApproveMerge: false } })
    expect(decodeMergeView(merged, expected)).toEqual(merged)
    const hidden = raw({ mergedInto: { partyId: DUPLICATE, code: null }, candidates: [], viewer: { canApproveMerge: false } })
    expect(decodeMergeView(hidden, expected)?.mergedInto).toEqual({ partyId: DUPLICATE, code: null })
  })

  it('refuses an unknown or missing key, a numeric version and a record for another party or company', () => {
    expect(decodeMergeView({ ...raw(), unmerge: true }, expected)).toBeNull()
    const { candidates: _omitted, ...missing } = raw()
    expect(decodeMergeView(missing, expected)).toBeNull()
    expect(decodeMergeView(raw({ viewer: { canApproveMerge: true, canUnmerge: true } }), expected)).toBeNull()
    expect(decodeMergeView(raw({ version: 7 }), expected)).toBeNull()
    expect(decodeMergeView(raw({ version: '9223372036854775808' }), expected)).toBeNull()
    expect(decodeMergeView(raw({ partyId: DUPLICATE }), expected)).toBeNull()
    expect(decodeMergeView(raw({ companyId: 'bbbbbbbb-0000-0000-0000-000000000002' }), expected)).toBeNull()
  })

  it('refuses a malformed candidate, the card itself as a candidate, duplicates and more than the server limit', () => {
    expect(decodeMergeView(raw({ candidates: [candidate({ version: 4 })] }), expected)).toBeNull()
    expect(decodeMergeView(raw({ candidates: [candidate({ taxIdentityNumber: '1234567890' })] }), expected)).toBeNull()
    expect(decodeMergeView(raw({ candidates: [candidate({ partyId: PARTY })] }), expected)).toBeNull()
    expect(decodeMergeView(raw({ candidates: [candidate(), candidate()] }), expected)).toBeNull()
    expect(decodeMergeView(raw({ candidates: [candidate({ legalName: '  ' })] }), expected)).toBeNull()
    const many = Array.from({ length: MERGE_CANDIDATE_LIMIT + 1 }, (_, index) =>
      candidate({ partyId: `33333333-3333-3333-3333-${String(index).padStart(12, '0')}`, code: `C-${index}` }),
    )
    expect(decodeMergeView(raw({ candidates: many }), expected)).toBeNull()
  })

  it('refuses a merged card that still offers candidates or approval, or that points at itself', () => {
    const target = { partyId: DUPLICATE, code: 'C-000002' }
    expect(decodeMergeView(raw({ mergedInto: target, viewer: { canApproveMerge: false } }), expected)).toBeNull()
    expect(decodeMergeView(raw({ mergedInto: target, candidates: [] }), expected)).toBeNull()
    expect(
      decodeMergeView(
        raw({ mergedInto: { partyId: PARTY, code: null }, candidates: [], viewer: { canApproveMerge: false } }),
        expected,
      ),
    ).toBeNull()
    expect(
      decodeMergeView(raw({ mergedInto: { partyId: DUPLICATE }, candidates: [], viewer: { canApproveMerge: false } }), expected),
    ).toBeNull()
  })

  it('mirrors the server reason normalization: trim, required, at most 1000', () => {
    expect(checkMergeReason('  Aynı firma iki kez açılmış.  ')).toEqual({ ok: true, value: 'Aynı firma iki kez açılmış.' })
    expect(checkMergeReason('   ')).toEqual({ ok: false, message: MERGE_TEXT.reasonRequired })
    expect(checkMergeReason('x'.repeat(MERGE_REASON_MAX))).toEqual({ ok: true, value: 'x'.repeat(MERGE_REASON_MAX) })
    expect(checkMergeReason('x'.repeat(MERGE_REASON_MAX + 1))).toEqual({ ok: false, message: MERGE_TEXT.reasonTooLong })
  })

  it('reads only the endpoint codes and escapes the path segment', () => {
    expect(mergeProblemCodeOf({ code: 'party_already_merged' })).toBe('party_already_merged')
    expect(mergeProblemCodeOf({ code: 'party_merge_tax_identity_mismatch' })).toBe('party_merge_tax_identity_mismatch')
    expect(mergeProblemCodeOf({ code: 'customer_group_exists' })).toBeNull()
    expect(mergeProblemCodeOf(null)).toBeNull()
    expect(mergePath('a/b')).toBe('/counterparties/a%2Fb/merge')
  })
})
