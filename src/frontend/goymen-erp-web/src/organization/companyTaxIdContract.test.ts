/**
 * ORG-05 (KIM-S23) — the strict decoders of the company VKN view. Every case is a synthetic fixture; nothing here is
 * evidence of the real endpoint.
 */

import { describe, expect, it } from 'vitest'
import {
  COMPANY_TAX_ID_PROBLEM,
  decodeCompanyTaxId,
  decodeCompanyTaxIdProblemCode,
  decodeTaxIdChange,
  decodeTaxIdVersionConflict,
  isValidVknInput,
} from './companyTaxIdContract'

const COMPANY = 'aaaaaaaa-0000-0000-0000-000000000001'
const OTHER_COMPANY = 'bbbbbbbb-0000-0000-0000-000000000002'
const REQUEST = 'cccccccc-0000-0000-0000-000000000003'

function pendingChange(overrides: Record<string, unknown> = {}) {
  return {
    id: REQUEST,
    currentVkn: '1234567890',
    proposedVkn: '0987654321',
    reason: 'Vergi dairesi yazısı',
    status: 'pending',
    requestedBy: 'Hazırlayan A',
    requestedAtUtc: '2026-11-14T08:00:00Z',
    decidedBy: null,
    decidedAtUtc: null,
    decisionNote: null,
    ...overrides,
  }
}

function view(overrides: Record<string, unknown> = {}) {
  return {
    companyId: COMPANY,
    code: 'GDC',
    legalName: 'Göymen Demir Çelik',
    vkn: '1234567890',
    isPlaceholder: false,
    version: 3,
    pending: null,
    lastDecided: null,
    viewer: { canRequest: true, canApprove: false, isRequesterOfPending: false },
    ...overrides,
  }
}

describe('decodeCompanyTaxId', () => {
  it('reads the exact server shape', () => {
    const decoded = decodeCompanyTaxId(view({ pending: pendingChange() }), COMPANY)
    expect(decoded?.vkn).toBe('1234567890')
    expect(decoded?.version).toBe(3)
    expect(decoded?.pending?.proposedVkn).toBe('0987654321')
  })

  it('accepts the company id in another letter case', () => {
    expect(decodeCompanyTaxId(view(), COMPANY.toUpperCase())).not.toBeNull()
  })

  it('refuses an answer about another company', () => {
    expect(decodeCompanyTaxId(view({ companyId: OTHER_COMPANY }), COMPANY)).toBeNull()
  })

  it('refuses a missing or extra key', () => {
    const { viewer: _viewer, ...missing } = view()
    expect(decodeCompanyTaxId(missing, COMPANY)).toBeNull()
    expect(decodeCompanyTaxId({ ...view(), nextVersion: 4 }, COMPANY)).toBeNull()
  })

  it('allows a null VKN only on a placeholder company (KIM-D28)', () => {
    expect(decodeCompanyTaxId(view({ vkn: null }), COMPANY)).toBeNull()
    expect(decodeCompanyTaxId(view({ vkn: null, isPlaceholder: true }), COMPANY)?.vkn).toBeNull()
  })

  it('refuses a VKN that is not ten digits', () => {
    for (const vkn of ['123456789', '12345678901', '12345 7890', '١٢٣٤٥٦٧٨٩٠', 1234567890]) {
      expect(decodeCompanyTaxId(view({ vkn }), COMPANY)).toBeNull()
    }
  })

  it('refuses a non-positive or fractional version', () => {
    for (const version of [0, -1, 1.5, '3']) {
      expect(decodeCompanyTaxId(view({ version }), COMPANY)).toBeNull()
    }
  })

  it('refuses a closed request in the pending slot and an open one in the last-decided slot', () => {
    const closed = pendingChange({
      status: 'approved',
      decidedBy: 'Onaylayan B',
      decidedAtUtc: '2026-11-14T09:00:00Z',
    })
    expect(decodeCompanyTaxId(view({ pending: closed }), COMPANY)).toBeNull()
    expect(decodeCompanyTaxId(view({ lastDecided: pendingChange() }), COMPANY)).toBeNull()
    expect(decodeCompanyTaxId(view({ lastDecided: closed }), COMPANY)?.lastDecided?.status).toBe('approved')
  })

  it('refuses a viewer that may do nothing — the server answers such a caller with 403', () => {
    expect(
      decodeCompanyTaxId(
        view({ viewer: { canRequest: false, canApprove: false, isRequesterOfPending: false } }),
        COMPANY,
      ),
    ).toBeNull()
  })

  it('refuses "I prepared the pending request" without a pending request', () => {
    expect(
      decodeCompanyTaxId(
        view({ viewer: { canRequest: true, canApprove: true, isRequesterOfPending: true } }),
        COMPANY,
      ),
    ).toBeNull()
  })
})

describe('decodeTaxIdChange', () => {
  it('refuses decision fields on a pending request', () => {
    expect(decodeTaxIdChange(pendingChange({ decidedBy: 'Onaylayan B' }))).toBeNull()
    expect(decodeTaxIdChange(pendingChange({ decisionNote: 'not' }))).toBeNull()
  })

  it('requires who and when on a closed request', () => {
    expect(decodeTaxIdChange(pendingChange({ status: 'rejected' }))).toBeNull()
    expect(
      decodeTaxIdChange(
        pendingChange({ status: 'rejected', decidedBy: 'Onaylayan B', decidedAtUtc: '2026-11-14T09:00:00Z' }),
      )?.status,
    ).toBe('rejected')
  })

  it('refuses a proposal equal to the VKN it was prepared against', () => {
    expect(decodeTaxIdChange(pendingChange({ proposedVkn: '1234567890' }))).toBeNull()
  })

  it('refuses an unknown status and a malformed id', () => {
    expect(decodeTaxIdChange(pendingChange({ status: 'withdrawn' }))).toBeNull()
    expect(decodeTaxIdChange(pendingChange({ id: 'not-a-uuid' }))).toBeNull()
  })

  it('accepts a first VKN request on a company without one', () => {
    expect(decodeTaxIdChange(pendingChange({ currentVkn: null }))?.currentVkn).toBeNull()
  })
})

describe('problem helpers', () => {
  it('reads only this surface’s codes', () => {
    expect(decodeCompanyTaxIdProblemCode({ code: COMPANY_TAX_ID_PROBLEM.selfDecision })).toBe(
      COMPANY_TAX_ID_PROBLEM.selfDecision,
    )
    expect(decodeCompanyTaxIdProblemCode({ code: 'organization_master_data_version_conflict' })).toBeNull()
    expect(decodeCompanyTaxIdProblemCode({ title: 'Çakışma' })).toBeNull()
    expect(decodeCompanyTaxIdProblemCode(null)).toBeNull()
  })

  it('reads a version conflict only with two positive versions', () => {
    expect(decodeTaxIdVersionConflict({ conflict: { expectedVersion: 2, currentVersion: 3 } })).toEqual({
      expectedVersion: 2,
      currentVersion: 3,
    })
    expect(decodeTaxIdVersionConflict({ conflict: { expectedVersion: 0, currentVersion: 3 } })).toBeNull()
    expect(decodeTaxIdVersionConflict({})).toBeNull()
  })

  it('checks the form input shape after trimming', () => {
    expect(isValidVknInput(' 1234567890 ')).toBe(true)
    expect(isValidVknInput('123456789')).toBe(false)
    expect(isValidVknInput('12345678a0')).toBe(false)
  })
})
