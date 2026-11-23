/**
 * F-029 S4b decoder cases — the strict runtime decoders of the Settings sales policy views (SAT-D5 delivery tolerance,
 * SAT-D6 approval amount thresholds). SENTETIK fixtures only; the real wire is the backend's contract tests. What these
 * cases establish: a valid view before and after the baseline transfer decodes to itself, and every off-contract answer
 * (missing, extra, mistyped or null field, another company, duplicate versions or currencies, an amount that disagrees
 * with the enabled flag, a recorder before the transfer, none after) fails CLOSED to null.
 */

import { describe, expect, it } from 'vitest'
import {
  approvalThresholdVersionsPath,
  decodeApprovalThresholdsView,
  decodeDeliveryToleranceView,
  inForceOf,
  isAmountText,
} from './salesPolicyContract'
import {
  CURRENCY_ONE,
  thresholdsAfterTransfer,
  thresholdsBeforeTransfer,
  toleranceAfterTransfer,
  toleranceBeforeTransfer,
} from './salesPolicyFixtures'
import { COMPANY_A, COMPANY_B } from './testHarness'

describe('decodeDeliveryToleranceView', () => {
  it('decodes the compiled catalog before the transfer and the stored timeline after it', () => {
    const before = decodeDeliveryToleranceView(toleranceBeforeTransfer(), COMPANY_A)
    expect(before).toEqual(toleranceBeforeTransfer())
    expect(before?.versions[0].recordedBy).toBeNull()

    const after = decodeDeliveryToleranceView(toleranceAfterTransfer(), COMPANY_A)
    expect(after).toEqual(toleranceAfterTransfer())
    expect(inForceOf(after?.versions ?? [])?.version).toBe('2')
  })

  it('accepts an empty timeline', () => {
    expect(decodeDeliveryToleranceView({ ...toleranceAfterTransfer(), version: '0', versions: [] }, COMPANY_A)).not.toBeNull()
  })

  it('rejects a view for another company and a view-level key that is missing, extra or mistyped', () => {
    expect(decodeDeliveryToleranceView(toleranceAfterTransfer(), COMPANY_B)).toBeNull()
    const { today: _today, ...missing } = toleranceAfterTransfer()
    expect(decodeDeliveryToleranceView(missing, COMPANY_A)).toBeNull()
    expect(decodeDeliveryToleranceView({ ...toleranceAfterTransfer(), extra: 1 }, COMPANY_A)).toBeNull()
    expect(decodeDeliveryToleranceView({ ...toleranceAfterTransfer(), version: 2 }, COMPANY_A)).toBeNull()
    expect(decodeDeliveryToleranceView({ ...toleranceAfterTransfer(), isBaselineTransferred: 'true' }, COMPANY_A)).toBeNull()
    expect(decodeDeliveryToleranceView({ ...toleranceAfterTransfer(), today: '2026-02-30' }, COMPANY_A)).toBeNull()
    expect(decodeDeliveryToleranceView({ ...toleranceAfterTransfer(), versions: null }, COMPANY_A)).toBeNull()
    expect(decodeDeliveryToleranceView(null, COMPANY_A)).toBeNull()
    expect(decodeDeliveryToleranceView([], COMPANY_A)).toBeNull()
  })

  it('rejects a non-zero view version before the transfer', () => {
    expect(decodeDeliveryToleranceView({ ...toleranceBeforeTransfer(), version: '1' }, COMPANY_A)).toBeNull()
  })

  it('rejects an entry whose fields are missing, extra, mistyped, null or off the enumerations', () => {
    const cases: Record<string, unknown>[] = [
      { warnPercent: undefined },
      { blockPercent: null },
      { approvalPercent: 3 },
      { warnPercent: '101' },
      { warnPercent: '1.23456' },
      { profileId: '00000000-0000-0000-0000-000000000000' },
      { profileId: 'not-a-guid' },
      { version: '0' },
      { version: 1 },
      { isActive: 'yes' },
      { effectiveFrom: '2026-13-01' },
      { effectiveThrough: '2025-12-31' },
      { state: 'Active' },
      { sourceKind: 'Imported' },
      { decisionReference: '' },
      { decisionReference: 'x'.repeat(401) },
      { extra: true },
    ]
    for (const change of cases) {
      const view = toleranceAfterTransfer()
      const entry: Record<string, unknown> = { ...view.versions[1], ...change }
      if ('warnPercent' in change && change.warnPercent === undefined) delete entry.warnPercent
      expect(decodeDeliveryToleranceView({ ...view, versions: [view.versions[0], entry] }, COMPANY_A)).toBeNull()
    }
  })

  it('rejects a recorder before the transfer, a missing recorder after it and a user entry before it', () => {
    const before = toleranceBeforeTransfer()
    expect(
      decodeDeliveryToleranceView({ ...before, versions: [{ ...before.versions[0], reason: 'SENTETIK' }] }, COMPANY_A),
    ).toBeNull()
    expect(
      decodeDeliveryToleranceView(
        { ...before, versions: [{ ...before.versions[0], sourceKind: 'UserMaintained' }] },
        COMPANY_A,
      ),
    ).toBeNull()

    const after = toleranceAfterTransfer()
    for (const change of [{ recordedBy: null }, { recordedAtUtc: '2026-11-10 09:00' }, { reason: '   ' }]) {
      expect(
        decodeDeliveryToleranceView({ ...after, versions: [after.versions[0], { ...after.versions[1], ...change }] }, COMPANY_A),
      ).toBeNull()
    }
  })

  it('rejects two entries with the same version number', () => {
    const view = toleranceAfterTransfer()
    expect(
      decodeDeliveryToleranceView({ ...view, versions: [view.versions[1], { ...view.versions[1] }] }, COMPANY_A),
    ).toBeNull()
  })
})

describe('decodeApprovalThresholdsView', () => {
  it('decodes the compiled catalog before the transfer and the stored thresholds after it', () => {
    expect(decodeApprovalThresholdsView(thresholdsBeforeTransfer(), COMPANY_A)).toEqual(thresholdsBeforeTransfer())
    expect(decodeApprovalThresholdsView(thresholdsAfterTransfer(), COMPANY_A)).toEqual(thresholdsAfterTransfer())
    expect(decodeApprovalThresholdsView({ ...thresholdsAfterTransfer(), currencies: [] }, COMPANY_A)).not.toBeNull()
  })

  it('rejects another company, a missing or extra key and a mistyped list', () => {
    expect(decodeApprovalThresholdsView(thresholdsAfterTransfer(), COMPANY_B)).toBeNull()
    const { currencies: _currencies, ...missing } = thresholdsAfterTransfer()
    expect(decodeApprovalThresholdsView(missing, COMPANY_A)).toBeNull()
    expect(decodeApprovalThresholdsView({ ...thresholdsAfterTransfer(), version: '1' }, COMPANY_A)).toBeNull()
    expect(decodeApprovalThresholdsView({ ...thresholdsAfterTransfer(), currencies: {} }, COMPANY_A)).toBeNull()
  })

  it('rejects a duplicate currency, a currency key that is extra or mistyped and a non-zero version before the transfer', () => {
    const view = thresholdsAfterTransfer()
    expect(
      decodeApprovalThresholdsView(
        { ...view, currencies: [view.currencies[0], { ...view.currencies[1], currencyId: CURRENCY_ONE.toUpperCase() }] },
        COMPANY_A,
      ),
    ).toBeNull()
    expect(
      decodeApprovalThresholdsView({ ...view, currencies: [{ ...view.currencies[0], code: 'TRY' }] }, COMPANY_A),
    ).toBeNull()
    expect(
      decodeApprovalThresholdsView({ ...view, currencies: [{ ...view.currencies[0], currencyId: 'TRY' }] }, COMPANY_A),
    ).toBeNull()
    const before = thresholdsBeforeTransfer()
    expect(
      decodeApprovalThresholdsView({ ...before, currencies: [{ ...before.currencies[0], version: '1' }] }, COMPANY_A),
    ).toBeNull()
  })

  it('rejects an amount that disagrees with the enabled flag or is not canonical text', () => {
    const cases: Record<string, unknown>[] = [
      { isEnabled: true, amount: null },
      { isEnabled: false, amount: '100' },
      { isEnabled: true, amount: 100 },
      { isEnabled: true, amount: '0' },
      { isEnabled: true, amount: '-5' },
      { isEnabled: true, amount: '01' },
      { isEnabled: true, amount: '1e5' },
      { isEnabled: true, amount: '1.123456789' },
      { isEnabled: 'true', amount: '100' },
    ]
    for (const change of cases) {
      const view = thresholdsAfterTransfer()
      const currency = view.currencies[0]
      const versions = [currency.versions[0], { ...currency.versions[1], ...change }]
      expect(
        decodeApprovalThresholdsView({ ...view, currencies: [{ ...currency, versions }, view.currencies[1]] }, COMPANY_A),
      ).toBeNull()
    }
  })

  it('rejects two versions with the same number inside one currency', () => {
    const view = thresholdsAfterTransfer()
    const currency = view.currencies[0]
    const versions = [currency.versions[1], { ...currency.versions[1] }]
    expect(decodeApprovalThresholdsView({ ...view, currencies: [{ ...currency, versions }] }, COMPANY_A)).toBeNull()
  })
})

describe('amount text and paths', () => {
  it('accepts the server canonical amount text only', () => {
    expect(isAmountText('250000.5')).toBe(true)
    expect(isAmountText('0.00000001')).toBe(true)
    expect(isAmountText('9'.repeat(20))).toBe(true)
    expect(isAmountText('9'.repeat(21))).toBe(false)
    expect(isAmountText('0.0')).toBe(false)
    expect(isAmountText('1,5')).toBe(false)
    expect(isAmountText(' 1')).toBe(false)
  })

  it('puts the currency id into the version path as one encoded segment', () => {
    expect(approvalThresholdVersionsPath(CURRENCY_ONE)).toBe(
      `/settings/sales-policies/approval-thresholds/${CURRENCY_ONE}/versions`,
    )
  })
})
