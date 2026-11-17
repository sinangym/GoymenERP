import { describe, expect, it } from 'vitest'
import { COMPANY_A, COMPANY_B } from './testHarness'
import { decodeManufacturingProfiles, decodeManufacturingProfileReceipt } from './manufacturingProfileContract'
import { MANUFACTURING_PROFILE_ID, manufacturingProfileFixture } from './manufacturingProfileFixtures'

describe('URN-D6 manufacturing profile contract', () => {
  it('requires an explicit baseline transfer state without assuming completion', () => {
    const fixture = manufacturingProfileFixture()
    expect(decodeManufacturingProfiles({ ...fixture, startingValuesTransferVersion: '1' }, COMPANY_A, 'parameters')?.startingValuesTransferVersion).toBe('1')
    expect(decodeManufacturingProfiles({ ...fixture, startingValuesTransferVersion: undefined }, COMPANY_A, 'parameters')).toBeNull()
    expect(decodeManufacturingProfiles({ ...fixture, startingValuesTransferVersion: '2' }, COMPANY_A, 'parameters')).toBeNull()
  })

  it('preserves exact value text, the state version and legacy approval history', () => {
    const fixture = manufacturingProfileFixture()
    const decoded = decodeManufacturingProfiles(fixture, COMPANY_A, 'parameters')!
    expect(decoded.profiles[0]).toMatchObject({ stateVersion: '1', profileVersion: '1', status: 'Draft', values: [{ value: '7850' }] })
    expect(decodeManufacturingProfiles(fixture, COMPANY_B, 'parameters')).toBeNull()
    expect(decodeManufacturingProfiles({ ...fixture, profiles: [{ ...fixture.profiles[0], profileVersion: '9223372036854775808' }] }, COMPANY_A, 'parameters')).toBeNull()
  })

  it('refuses unknown lifecycle states, broken numeric values and duplicate parameter keys', () => {
    const fixture = manufacturingProfileFixture()
    expect(decodeManufacturingProfiles({ ...fixture, profiles: [{ ...fixture.profiles[0], status: 'Activated' }] }, COMPANY_A, 'parameters')).toBeNull()
    const keys = 'keys' in fixture ? fixture.keys : []
    expect(decodeManufacturingProfiles({ ...fixture, keys: [...keys, ...keys] }, COMPANY_A, 'parameters')).toBeNull()
    const tolerance = manufacturingProfileFixture('tolerances')
    expect(decodeManufacturingProfiles(tolerance, COMPANY_A, 'tolerances')).not.toBeNull()
    expect(decodeManufacturingProfiles({ ...tolerance, profiles: [{ ...tolerance.profiles[0], maximumFirePercent: 3 }] }, COMPANY_A, 'tolerances')).toBeNull()
  })

  it('only accepts a typed command receipt as success', () => {
    expect(decodeManufacturingProfileReceipt({ profileId: MANUFACTURING_PROFILE_ID, profileVersion: '2', stateVersion: '2', status: 'Published' })).toMatchObject({ status: 'Published' })
    expect(decodeManufacturingProfileReceipt({ status: 'Published' })).toBeNull()
  })
})
