/** SENTETIK component/decoder fixtures; not acceptance evidence. */
import { COMPANY_A, IDENTITY } from './testHarness'
import type { ManufacturingProfileKind } from './manufacturingProfileContract'

export const MANUFACTURING_PROFILE_ID = '11111111-1111-4111-8111-111111111115'
export function manufacturingProfileFixture(kind: ManufacturingProfileKind = 'parameters') {
  const profile = { profileId: MANUFACTURING_PROFILE_ID, source: 'Settings', process: 'Slitting', workCenterId: null,
    profileVersion: '1', stateVersion: '1', status: 'Draft', effectiveFrom: '2026-11-16', effectiveThrough: null,
    preparedBy: IDENTITY.userId, preparedAtUtc: '2026-11-16T10:00:00Z', preparationReason: 'SENTETIK',
    approvedBy: null, approvedAtUtc: null, approvalReason: null, publishedBy: null, publishedAtUtc: null, publicationReason: null }
  return { companyId: COMPANY_A, startingValuesTransferVersion: '0', businessDate: '2026-11-16', profiles: [{ ...profile, ...(kind === 'parameters'
    ? { values: [{ key: 'densityKgPerCubicMetre', unit: 'kg/m3', value: '7850' }] }
    : { maximumFirePercent: '3', massBalanceDifferenceTolerancePercent: '1' }) }],
    ...(kind === 'parameters' ? { keys: [{ key: 'densityKgPerCubicMetre', unit: 'kg/m3', kind: 'Positive', maximumScale: 6, processes: ['Slitting'], choices: [] }] } : { startingValues: [] }) }
}
