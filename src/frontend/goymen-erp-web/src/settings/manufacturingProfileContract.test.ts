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

  it('K3 P2: accepts only the exact key sets of the receipt, the list, each profile, each value and each key definition', () => {
    const without = (value: object, key: string) => Object.fromEntries(Object.entries(value).filter(([name]) => name !== key))
    const receipt = { profileId: MANUFACTURING_PROFILE_ID, profileVersion: '2', stateVersion: '2', status: 'Published' }
    expect(decodeManufacturingProfileReceipt(receipt)).toEqual(receipt)
    expect(decodeManufacturingProfileReceipt({ ...receipt, extra: 'SENTETIK' })).toBeNull()
    for (const key of Object.keys(receipt)) expect(decodeManufacturingProfileReceipt(without(receipt, key))).toBeNull()

    for (const kind of ['parameters', 'tolerances'] as const) {
      const list: Record<string, unknown> = manufacturingProfileFixture(kind)
      const [profile] = list.profiles as Record<string, unknown>[]
      expect(decodeManufacturingProfiles(list, COMPANY_A, kind)).not.toBeNull()
      expect(decodeManufacturingProfiles({ ...list, extra: 'SENTETIK' }, COMPANY_A, kind)).toBeNull()
      for (const key of Object.keys(list)) expect(decodeManufacturingProfiles(without(list, key), COMPANY_A, kind)).toBeNull()
      expect(decodeManufacturingProfiles({ ...list, profiles: [{ ...profile, extra: 'SENTETIK' }] }, COMPANY_A, kind)).toBeNull()
      for (const key of Object.keys(profile)) expect(decodeManufacturingProfiles({ ...list, profiles: [without(profile, key)] }, COMPANY_A, kind)).toBeNull()
    }

    // Each list carries only its own schema's keys: the other kind's fields are unknown keys here.
    const parameters = manufacturingProfileFixture('parameters') as Record<string, unknown>
    const tolerances = manufacturingProfileFixture('tolerances') as Record<string, unknown>
    const [parameterProfile] = parameters.profiles as Record<string, unknown>[]
    const [toleranceProfile] = tolerances.profiles as Record<string, unknown>[]
    expect(decodeManufacturingProfiles({ ...parameters, startingValues: [] }, COMPANY_A, 'parameters')).toBeNull()
    expect(decodeManufacturingProfiles({ ...tolerances, keys: [] }, COMPANY_A, 'tolerances')).toBeNull()
    expect(decodeManufacturingProfiles({ ...parameters, profiles: [{ ...parameterProfile, maximumFirePercent: '3' }] }, COMPANY_A, 'parameters')).toBeNull()
    expect(decodeManufacturingProfiles({ ...tolerances, profiles: [{ ...toleranceProfile, values: [] }] }, COMPANY_A, 'tolerances')).toBeNull()
    expect(decodeManufacturingProfiles({ ...tolerances, startingValues: [{ ...toleranceProfile, extra: 'SENTETIK' }] }, COMPANY_A, 'tolerances')).toBeNull()
    expect(decodeManufacturingProfiles({ ...tolerances, profiles: [], startingValues: [toleranceProfile] }, COMPANY_A, 'tolerances')?.startingValues).toHaveLength(1)

    const [value] = parameterProfile.values as Record<string, unknown>[]
    const [definition] = parameters.keys as Record<string, unknown>[]
    expect(decodeManufacturingProfiles({ ...parameters, profiles: [{ ...parameterProfile, values: [{ ...value, extra: 'SENTETIK' }] }] }, COMPANY_A, 'parameters')).toBeNull()
    for (const key of Object.keys(value)) expect(decodeManufacturingProfiles({ ...parameters, profiles: [{ ...parameterProfile, values: [without(value, key)] }] }, COMPANY_A, 'parameters')).toBeNull()
    expect(decodeManufacturingProfiles({ ...parameters, keys: [{ ...definition, extra: 'SENTETIK' }] }, COMPANY_A, 'parameters')).toBeNull()
    for (const key of Object.keys(definition)) expect(decodeManufacturingProfiles({ ...parameters, keys: [without(definition, key)] }, COMPANY_A, 'parameters')).toBeNull()
  })
})
