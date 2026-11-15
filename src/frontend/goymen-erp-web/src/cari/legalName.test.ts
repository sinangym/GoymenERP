import { describe, expect, it } from 'vitest'
import {
  LEGAL_NAME_MAX,
  LEGAL_NAME_TEXT,
  checkLegalName,
  decodeLegalNameView,
  legalNamePath,
  legalNameProblemCodeOf,
} from './legalName'

const PARTY = '11111111-1111-1111-1111-111111111111'
const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const expected = { partyId: PARTY, companyId: COMPANY }

const raw = (over: Record<string, unknown> = {}) => ({
  partyId: PARTY,
  companyId: COMPANY,
  code: 'C-000001',
  version: '7',
  legalName: 'Örnek Ticaret A.Ş.',
  viewer: { canUpdateLegalName: false },
  ...over,
})

describe('F-027 / ALS-D95 legal name contract', () => {
  it('decodes the exact shape', () => {
    expect(decodeLegalNameView(raw(), expected)).toEqual(raw())
    expect(decodeLegalNameView(raw({ viewer: { canUpdateLegalName: true } }), expected)?.viewer.canUpdateLegalName).toBe(
      true,
    )
  })

  it('refuses an unknown or missing key, a numeric version and a record for another party or company', () => {
    expect(decodeLegalNameView({ ...raw(), tradeName: null }, expected)).toBeNull()
    const { legalName: _omitted, ...missing } = raw()
    expect(decodeLegalNameView(missing, expected)).toBeNull()
    expect(decodeLegalNameView(raw({ viewer: { canUpdateLegalName: true, canApprove: true } }), expected)).toBeNull()
    expect(decodeLegalNameView(raw({ viewer: { canUpdateTradeName: true } }), expected)).toBeNull()
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

  it('mirrors the server normalization: trim, required, at most 250', () => {
    expect(checkLegalName('  Örnek A.Ş.  ')).toEqual({ ok: true, value: 'Örnek A.Ş.' })
    expect(checkLegalName('   ')).toEqual({ ok: false, message: LEGAL_NAME_TEXT.required })
    expect(checkLegalName('x'.repeat(LEGAL_NAME_MAX))).toEqual({ ok: true, value: 'x'.repeat(LEGAL_NAME_MAX) })
    expect(checkLegalName('x'.repeat(LEGAL_NAME_MAX + 1))).toEqual({ ok: false, message: LEGAL_NAME_TEXT.tooLong })
  })

  it('reads only the endpoint codes and escapes the path segment', () => {
    expect(legalNameProblemCodeOf({ code: 'version_conflict' })).toBe('version_conflict')
    expect(legalNameProblemCodeOf({ code: 'customer_group_exists' })).toBeNull()
    expect(legalNameProblemCodeOf(null)).toBeNull()
    expect(legalNamePath('a/b')).toBe('/counterparties/a%2Fb/legal-name')
  })
})
