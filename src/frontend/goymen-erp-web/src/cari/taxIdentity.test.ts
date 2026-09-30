import { describe, expect, it } from 'vitest'
import {
  checkTaxIdentity,
  decodeTaxDuplicateCheck,
  decodeTaxIdentityView,
  isValidTckn,
  isValidVkn,
  taxProblemCodeOf,
} from './taxIdentity'

const PARTY = '11111111-1111-1111-1111-111111111111'
const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const OTHER = '22222222-2222-2222-2222-222222222222'

function view(over: Record<string, unknown> = {}) {
  return {
    partyId: PARTY,
    companyId: COMPANY,
    version: '2',
    taxIdentityNumber: '1234567890',
    kind: 'vkn',
    duplicates: [{ partyId: OTHER, code: 'CAR-000002', legalName: 'Şube', accountIsActive: true }],
    pendingChange: null,
    lastDecidedChange: null,
    viewer: { canUpdate: true, canApprove: false, isRequesterOfPending: false },
    ...over,
  }
}

function change(over: Record<string, unknown> = {}) {
  return {
    id: '33333333-3333-3333-3333-333333333333',
    status: 'pending',
    currentTaxIdentityNumber: '1234567890',
    proposedTaxIdentityNumber: '10000000146',
    proposedKind: 'tckn',
    reason: 'Levha',
    requestedBy: 'maker',
    requestedAtUtc: '2026-09-30T10:00:00+00:00',
    decidedBy: null,
    decidedAtUtc: null,
    decisionNote: null,
    proposedDuplicates: [],
    ...over,
  }
}

describe('AT-009 VKN/TCKN check digits (the server rule, mirrored)', () => {
  it('accepts valid VKN and TCKN, trimming only surrounding whitespace', () => {
    for (const vkn of ['1234567890', '9876543217', '4567890128', '1111111114']) expect(isValidVkn(vkn)).toBe(true)
    for (const tckn of ['10000000146', '12345678950', '98765432150']) expect(isValidTckn(tckn)).toBe(true)
    expect(checkTaxIdentity(' 1234567890 ')).toEqual({ ok: true, value: '1234567890', kind: 'vkn' })
    expect(checkTaxIdentity('10000000146')).toEqual({ ok: true, value: '10000000146', kind: 'tckn' })
  })

  it('refuses wrong check digits, wrong lengths and anything it would have to clean', () => {
    for (const bad of [
      '1234567891',
      '10000000145',
      '10000000156',
      '01234567890',
      '',
      '   ',
      '123456789',
      '123456789012',
      '123 4567890',
      '123-456-7890',
      '12345678A0',
      '１２３４５６７８９０',
    ]) {
      const result = checkTaxIdentity(bad)
      expect(result.ok, bad).toBe(false)
    }
  })

  it('has exactly one valid check digit per VKN prefix and one valid pair per TCKN prefix', () => {
    for (const prefix of ['000000000', '123456789', '314159265']) {
      expect([...'0123456789'].filter((digit) => isValidVkn(prefix + digit))).toHaveLength(1)
    }
    for (const prefix of ['100000001', '987654321']) {
      const valid = Array.from({ length: 100 }, (_, pair) => prefix + String(pair).padStart(2, '0')).filter(isValidTckn)
      expect(valid).toHaveLength(1)
    }
  })
})

describe('AT-009 decoders are strict and bound to the request', () => {
  it('decodes the view and binds it to the party and company asked for', () => {
    const decoded = decodeTaxIdentityView(view(), { partyId: PARTY, companyId: COMPANY })
    expect(decoded?.taxIdentityNumber).toBe('1234567890')
    expect(decoded?.duplicates).toHaveLength(1)
    expect(decodeTaxIdentityView(view(), { partyId: OTHER, companyId: COMPANY })).toBeNull()
    expect(decodeTaxIdentityView(view(), { partyId: PARTY, companyId: OTHER })).toBeNull()
  })

  it('refuses a stored number that fails its check digits, a kind without a number and a lossy version', () => {
    const bound = { partyId: PARTY, companyId: COMPANY }
    expect(decodeTaxIdentityView(view({ taxIdentityNumber: '1234567891' }), bound)).toBeNull()
    expect(decodeTaxIdentityView(view({ taxIdentityNumber: null }), bound)).toBeNull()
    expect(decodeTaxIdentityView(view({ version: 2 }), bound)).toBeNull()
    expect(decodeTaxIdentityView(view({ viewer: { canUpdate: true } }), bound)).toBeNull()
    expect(decodeTaxIdentityView(view({ kind: 'tckn' }), bound)).toBeNull()
    expect(decodeTaxIdentityView(view({ taxIdentityNumber: null, kind: null, duplicates: [] }), bound)).not.toBeNull()
  })

  it('accepts a pending change only as pending and a decided one only as decided', () => {
    const bound = { partyId: PARTY, companyId: COMPANY }
    expect(decodeTaxIdentityView(view({ pendingChange: change() }), bound)?.pendingChange?.id).toBe(
      '33333333-3333-3333-3333-333333333333',
    )
    expect(decodeTaxIdentityView(view({ pendingChange: change({ status: 'approved' }) }), bound)).toBeNull()
    expect(decodeTaxIdentityView(view({ pendingChange: change({ decidedBy: 'checker' }) }), bound)).toBeNull()
    expect(decodeTaxIdentityView(view({ pendingChange: change({ proposedKind: 'vkn' }) }), bound)).toBeNull()
    expect(decodeTaxIdentityView(view({ lastDecidedChange: change() }), bound)).toBeNull()
    expect(decodeTaxIdentityView(view({ lastDecidedChange: change({ status: 'approved' }) }), bound)).toBeNull()
    expect(
      decodeTaxIdentityView(
        view({ lastDecidedChange: change({ status: 'rejected', decidedBy: 'checker', decidedAtUtc: 'x' }) }),
        bound,
      ),
    ).not.toBeNull()
  })

  it('binds the duplicate check to the number asked for and reads any stable code', () => {
    const check = { taxIdentityNumber: '1234567890', kind: 'vkn', duplicates: [] }
    expect(decodeTaxDuplicateCheck(check, '1234567890')).not.toBeNull()
    expect(decodeTaxDuplicateCheck(check, '9876543217')).toBeNull()
    expect(decodeTaxDuplicateCheck({ ...check, kind: 'tckn' }, '1234567890')).toBeNull()
    expect(taxProblemCodeOf({ code: 'tax_identity_self_decision' })).toBe('tax_identity_self_decision')
    expect(taxProblemCodeOf(null)).toBeNull()
  })
})
