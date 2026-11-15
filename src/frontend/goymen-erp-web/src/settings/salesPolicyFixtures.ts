/**
 * Test-only SENTETIK fixtures of the F-029 S4b Settings sales policy views (delivery tolerance, approval amount thresholds).
 * The percents, amounts and references are synthetic: they are NOT the reviewed SAT-D5/SAT-D6 catalog values, and a
 * fixture is not a record. Shared by the decoder and the screen cases.
 */

import { COMPANY_A, IDENTITY } from './testHarness'

export const TODAY = '2026-11-15'
export const TOLERANCE_PROFILE = '71000000-0000-4000-8000-000000000001'
export const CURRENCY_ONE = '72000000-0000-4000-8000-0000000000c1'
export const CURRENCY_TWO = '72000000-0000-4000-8000-0000000000c2'
export const THRESHOLD_PROFILE_ONE = '73000000-0000-4000-8000-0000000000d1'
export const THRESHOLD_PROFILE_TWO = '73000000-0000-4000-8000-0000000000d2'

type Entry = Record<string, unknown>

/** A compiled catalog entry before the transfer: no recorder, no reason. */
function catalogMeta(profileId: string): Entry {
  return {
    profileId,
    version: '1',
    isActive: true,
    effectiveFrom: '2026-01-01',
    effectiveThrough: null,
    state: 'InForce',
    sourceKind: 'CompiledBaselineV0',
    decisionReference: 'SENTETIK-REF',
    recordedBy: null,
    recordedAtUtc: null,
    reason: null,
  }
}

/** A stored record after the transfer: who recorded it, when and why. */
function storedMeta(profileId: string, version: string, overrides: Entry = {}): Entry {
  return {
    ...catalogMeta(profileId),
    version,
    recordedBy: IDENTITY.userId,
    recordedAtUtc: '2026-11-10T09:00:00+00:00',
    reason: 'SENTETIK gerekçe',
    ...overrides,
  }
}

export function toleranceBeforeTransfer() {
  return {
    companyId: COMPANY_A,
    today: TODAY,
    isBaselineTransferred: false,
    version: '0',
    versions: [{ ...catalogMeta(TOLERANCE_PROFILE), warnPercent: '1.5', approvalPercent: '3', blockPercent: '7.25' }] as Entry[],
  }
}

export function toleranceAfterTransfer() {
  return {
    companyId: COMPANY_A,
    today: TODAY,
    isBaselineTransferred: true,
    version: '2',
    versions: [
      {
        ...storedMeta(TOLERANCE_PROFILE, '1', {
          effectiveThrough: '2026-11-09',
          state: 'Ended',
          reason: 'SENTETIK devir',
        }),
        warnPercent: '1.5',
        approvalPercent: '3',
        blockPercent: '7.25',
      },
      {
        ...storedMeta(TOLERANCE_PROFILE, '2', {
          effectiveFrom: '2026-11-10',
          sourceKind: 'UserMaintained',
          decisionReference: null,
        }),
        warnPercent: '2',
        approvalPercent: '4.5',
        blockPercent: '9',
      },
    ] as Entry[],
  }
}

export function thresholdsBeforeTransfer() {
  return {
    companyId: COMPANY_A,
    today: TODAY,
    isBaselineTransferred: false,
    currencies: [
      {
        currencyId: CURRENCY_ONE,
        version: '0',
        versions: [{ ...catalogMeta(THRESHOLD_PROFILE_ONE), isEnabled: true, amount: '250000.5' }] as Entry[],
      },
      {
        currencyId: CURRENCY_TWO,
        version: '0',
        versions: [{ ...catalogMeta(THRESHOLD_PROFILE_TWO), isEnabled: false, amount: null }] as Entry[],
      },
    ],
  }
}

export function thresholdsAfterTransfer() {
  return {
    companyId: COMPANY_A,
    today: TODAY,
    isBaselineTransferred: true,
    currencies: [
      {
        currencyId: CURRENCY_ONE,
        version: '3',
        versions: [
          { ...storedMeta(THRESHOLD_PROFILE_ONE, '1', { state: 'Superseded' }), isEnabled: true, amount: '250000.5' },
          {
            ...storedMeta(THRESHOLD_PROFILE_ONE, '3', { sourceKind: 'UserMaintained', decisionReference: null }),
            isEnabled: true,
            amount: '275000',
          },
        ] as Entry[],
      },
      {
        currencyId: CURRENCY_TWO,
        version: '1',
        versions: [{ ...storedMeta(THRESHOLD_PROFILE_TWO, '1'), isEnabled: false, amount: null }] as Entry[],
      },
    ],
  }
}
