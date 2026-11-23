import { describe, expect, it } from 'vitest'
import {
  LEGAL_NAME_MAX,
  LEGAL_NAME_NOTE_MAX,
  LEGAL_NAME_REASON_MAX,
  LEGAL_NAME_TEXT,
  checkLegalName,
  checkLegalNameNote,
  checkLegalNameReason,
  decodeLegalNameView,
  legalNamePath,
  legalNameProblemCodeOf,
} from './legalName'

const PARTY = '11111111-1111-1111-1111-111111111111'
const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const REQUEST = 'cccccccc-0000-0000-0000-000000000001'
const expected = { partyId: PARTY, companyId: COMPANY }

const change = (over: Record<string, unknown> = {}) => ({
  id: REQUEST,
  status: 'pending',
  currentLegalName: 'Örnek Ticaret A.Ş.',
  proposedLegalName: 'Yeni Örnek Ticaret A.Ş.',
  reason: 'Ticaret sicili değişikliği',
  requestedBy: 'maker-user',
  requestedAtUtc: '2026-11-16T09:00:00+00:00',
  decidedBy: null,
  decidedAtUtc: null,
  decisionNote: null,
  ...over,
})

const decided = (over: Record<string, unknown> = {}) =>
  change({ status: 'approved', decidedBy: 'checker-user', decidedAtUtc: '2026-11-16T10:00:00+00:00', ...over })

const raw = (over: Record<string, unknown> = {}) => ({
  partyId: PARTY,
  companyId: COMPANY,
  code: 'C-000001',
  version: '7',
  legalName: 'Örnek Ticaret A.Ş.',
  pendingChange: null,
  lastDecidedChange: null,
  viewer: { canPropose: false, canApprove: false, isRequesterOfPending: false },
  ...over,
})

describe('ALS-D111 + ALS-D113 legal name maker-checker contract', () => {
  it('decodes the exact shape with and without a pending and a decided change', () => {
    expect(decodeLegalNameView(raw(), expected)).toEqual(raw())
    const full = raw({
      pendingChange: change(),
      lastDecidedChange: decided({ status: 'rejected', decisionNote: 'Belge eksik' }),
      viewer: { canPropose: true, canApprove: true, isRequesterOfPending: true },
    })
    expect(decodeLegalNameView(full, expected)).toEqual(full)
  })

  it('refuses an unknown or missing key, a numeric version and a record for another party or company', () => {
    expect(decodeLegalNameView({ ...raw(), tradeName: null }, expected)).toBeNull()
    const { pendingChange: _omitted, ...missing } = raw()
    expect(decodeLegalNameView(missing, expected)).toBeNull()
    expect(decodeLegalNameView(raw({ viewer: { canUpdateLegalName: true } }), expected)).toBeNull()
    expect(
      decodeLegalNameView(
        raw({ viewer: { canPropose: true, canApprove: true, isRequesterOfPending: false, canReject: true } }),
        expected,
      ),
    ).toBeNull()
    expect(decodeLegalNameView(raw({ version: 7 }), expected)).toBeNull()
    expect(decodeLegalNameView(raw({ version: '9223372036854775808' }), expected)).toBeNull()
    expect(decodeLegalNameView(raw({ partyId: '22222222-2222-2222-2222-222222222222' }), expected)).toBeNull()
    expect(decodeLegalNameView(raw({ companyId: 'bbbbbbbb-0000-0000-0000-000000000002' }), expected)).toBeNull()
  })

  it('refuses a blank legal name or one past the server limit', () => {
    expect(decodeLegalNameView(raw({ legalName: '' }), expected)).toBeNull()
    expect(decodeLegalNameView(raw({ legalName: '   ' }), expected)).toBeNull()
    expect(decodeLegalNameView(raw({ legalName: 'x'.repeat(LEGAL_NAME_MAX + 1) }), expected)).toBeNull()
  })

  it('refuses a pending slot that is not pending, a decided slot that is pending and inconsistent decisions', () => {
    expect(decodeLegalNameView(raw({ pendingChange: decided() }), expected)).toBeNull()
    expect(decodeLegalNameView(raw({ lastDecidedChange: change() }), expected)).toBeNull()
    expect(decodeLegalNameView(raw({ pendingChange: change({ decidedBy: 'x' }) }), expected)).toBeNull()
    expect(decodeLegalNameView(raw({ lastDecidedChange: decided({ decidedAtUtc: null }) }), expected)).toBeNull()
    expect(decodeLegalNameView(raw({ pendingChange: change({ status: 'cancelled' }) }), expected)).toBeNull()
    expect(decodeLegalNameView(raw({ pendingChange: change({ requestedAtUtc: 'dün' }) }), expected)).toBeNull()
    expect(
      decodeLegalNameView(raw({ pendingChange: change({ proposedLegalName: 'Örnek Ticaret A.Ş.' }) }), expected),
    ).toBeNull()
    expect(decodeLegalNameView(raw({ pendingChange: change({ reason: '' }) }), expected)).toBeNull()
    expect(
      decodeLegalNameView(raw({ pendingChange: { ...change(), extra: true } }), expected),
    ).toBeNull()
  })

  it('refuses "requester of the pending request" when nothing is pending', () => {
    expect(
      decodeLegalNameView(
        raw({ viewer: { canPropose: true, canApprove: false, isRequesterOfPending: true } }),
        expected,
      ),
    ).toBeNull()
  })

  it('mirrors the server normalization of the name, the reason and the note', () => {
    expect(checkLegalName('  Örnek A.Ş.  ')).toEqual({ ok: true, value: 'Örnek A.Ş.' })
    expect(checkLegalName('   ')).toEqual({ ok: false, message: LEGAL_NAME_TEXT.required })
    expect(checkLegalName('x'.repeat(LEGAL_NAME_MAX))).toEqual({ ok: true, value: 'x'.repeat(LEGAL_NAME_MAX) })
    expect(checkLegalName('x'.repeat(LEGAL_NAME_MAX + 1))).toEqual({ ok: false, message: LEGAL_NAME_TEXT.tooLong })
    expect(checkLegalNameReason('  Sicil  ')).toEqual({ ok: true, value: 'Sicil' })
    expect(checkLegalNameReason(' ')).toEqual({ ok: false, message: LEGAL_NAME_TEXT.reasonRequired })
    expect(checkLegalNameReason('x'.repeat(LEGAL_NAME_REASON_MAX + 1))).toEqual({
      ok: false,
      message: LEGAL_NAME_TEXT.reasonTooLong,
    })
    expect(checkLegalNameNote('   ')).toEqual({ ok: true, value: null })
    expect(checkLegalNameNote(' Tamam ')).toEqual({ ok: true, value: 'Tamam' })
    expect(checkLegalNameNote('x'.repeat(LEGAL_NAME_NOTE_MAX + 1))).toEqual({
      ok: false,
      message: LEGAL_NAME_TEXT.noteTooLong,
    })
  })

  it('reads only the endpoint codes and escapes the path segment', () => {
    expect(legalNameProblemCodeOf({ code: 'version_conflict' })).toBe('version_conflict')
    expect(legalNameProblemCodeOf({ code: 'legal_name_self_decision' })).toBe('legal_name_self_decision')
    expect(legalNameProblemCodeOf({ code: 'legal_name_change_stale' })).toBe('legal_name_change_stale')
    expect(legalNameProblemCodeOf({ code: 'customer_group_exists' })).toBeNull()
    expect(legalNameProblemCodeOf(null)).toBeNull()
    expect(legalNamePath('a/b')).toBe('/counterparties/a%2Fb/legal-name')
  })
})
