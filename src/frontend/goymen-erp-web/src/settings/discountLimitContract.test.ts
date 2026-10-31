/**
 * SAT-D61…D70 decoders fail CLOSED and keep the server's lossless text; the screen never computes a limit.
 */

import { describe, expect, it } from 'vitest'
import {
  decodeBinding,
  decodeCreated,
  decodeGroupList,
  decodePeople,
  isPercentText,
  normalizeCode,
  problemSubjectOf,
} from './discountLimitContract'
import { COMPANY_A, COMPANY_B, PERSON, binding, groupList } from './testHarness'

describe('discount limit decoders', () => {
  it('accept the canonical answers as given', () => {
    const list = decodeGroupList(groupList(), COMPANY_A)
    expect(list?.groups.map((group) => [group.code, group.effectiveLimit.percent])).toEqual([
      ['MUDUR', '38'],
      ['SATIS-DESTEK', '35'],
      ['ESKI', null],
    ])
    expect(decodeBinding(binding('1', '0'), PERSON)?.companies.map((company) => company.version)).toEqual(['1', '0'])
    expect(decodeBinding(binding('1', '1', false), PERSON)?.companies.map((company) => company.isMember)).toEqual([true, false])
    const invalid = groupList()
    const [managerRow] = invalid.groups as Record<string, unknown>[]
    expect(decodeGroupList({ ...invalid, groups: [{ ...managerRow, effectiveLimit: { state: 'Invalid', percent: null } }] }, COMPANY_A)
      ?.groups[0].effectiveLimit.state).toBe('Invalid')
    expect(decodeCreated({ id: PERSON, version: '1' })).toEqual({ id: PERSON, version: '1' })
    expect(decodePeople({ items: [{ userId: PERSON, displayName: 'Ali' }], hasMore: true })?.hasMore).toBe(true)
  })

  it('refuse another company, a malformed row, an unknown kind and a contradictory limit shape', () => {
    expect(decodeGroupList(groupList(), COMPANY_B)).toBeNull()
    const list = groupList()
    const [manager, seller, old] = list.groups as Record<string, unknown>[]
    expect(decodeGroupList({ ...list, groups: [{ ...manager, limit: { ...(manager.limit as object), kind: 'Percent' } }] }, COMPANY_A)).toBeNull()
    expect(decodeGroupList({ ...list, groups: [{ ...seller, limit: { ...(seller.limit as object), linkedGroupId: PERSON } }] }, COMPANY_A)).toBeNull()
    expect(decodeGroupList({ ...list, groups: [{ ...seller, version: 2 }] }, COMPANY_A)).toBeNull()
    expect(decodeGroupList({ ...list, groups: [{ ...old, effectiveLimit: { state: 'None', percent: '0' } }] }, COMPANY_A)).toBeNull()
    expect(decodeGroupList({ ...list, groups: [seller, { ...seller, id: PERSON }] }, COMPANY_A)).toBeNull()
    expect(decodeGroupList({ ...list, groups: [{ ...seller, effectiveLimit: { state: 'Defined', percent: '100.5' } }] }, COMPANY_A)).toBeNull()
  })

  it('SAT-D78: keep the scheduled records only when they are later than the business date, one per day, earliest first', () => {
    const list = groupList()
    const [, seller] = list.groups as Record<string, unknown>[]
    const [entry] = seller.scheduled as Record<string, unknown>[]
    const at = (date: string) => ({ ...entry, limit: { ...(entry.limit as object), effectiveFrom: date } })
    const decoded = decodeGroupList(list, COMPANY_A)
    expect(decoded?.groups[1].scheduled.map((scheduled) => [scheduled.limit.effectiveFrom, scheduled.effectiveLimit.percent])).toEqual([
      ['2026-11-01', '40'],
    ])
    expect(decodeGroupList({ ...list, groups: [{ ...seller, scheduled: undefined }] }, COMPANY_A)).toBeNull()
    expect(decodeGroupList({ ...list, groups: [{ ...seller, scheduled: [at('2026-10-27')] }] }, COMPANY_A)).toBeNull()
    expect(decodeGroupList({ ...list, groups: [{ ...seller, scheduled: [at('2026-12-01'), at('2026-11-01')] }] }, COMPANY_A)).toBeNull()
    expect(decodeGroupList({ ...list, groups: [{ ...seller, scheduled: [at('2026-11-01'), at('2026-11-01')] }] }, COMPANY_A)).toBeNull()
    expect(decodeGroupList({ ...list, groups: [{ ...seller, limit: { ...(seller.limit as object), effectiveFrom: '2026-11-01' } }] }, COMPANY_A)).toBeNull()
    expect(decodeGroupList({ ...list, businessDate: '2026-02-30' }, COMPANY_A)).toBeNull()
  })

  it('refuse a binding of another person, a version-0 company with groups and duplicate companies', () => {
    expect(decodeBinding(binding(), COMPANY_A)).toBeNull()
    const value = binding('0', '1')
    expect(decodeBinding({ ...value, companies: [{ ...value.companies[1], version: '0' }] }, PERSON)).toBeNull()
    expect(decodeBinding({ ...value, companies: [value.companies[1], value.companies[1]] }, PERSON)).toBeNull()
    // SAT-D116: a skipped company carries no version and no groups; the flag is required.
    expect(decodeBinding({ ...value, companies: [{ ...value.companies[1], isMember: false }] }, PERSON)).toBeNull()
    expect(decodeBinding({ ...value, companies: [{ ...value.companies[1], isMember: undefined }] }, PERSON)).toBeNull()
    expect(decodeCreated({ id: PERSON, version: '2' })).toBeNull()
  })

  it('read percents and codes as the server writes them, and a refusal subject only when well formed', () => {
    expect(['0', '35', '37.5', '100', '0.0001'].every(isPercentText)).toBe(true)
    expect(['101', '-1', '1.23456', '1e2', '', ' 5', '5.'].some(isPercentText)).toBe(false)
    expect(normalizeCode('  satis-destek ')).toBe('SATIS-DESTEK')
    expect(problemSubjectOf({ companyId: COMPANY_B, groupCode: 'MUDUR' })).toEqual({ companyId: COMPANY_B, groupCode: 'MUDUR' })
    expect(problemSubjectOf({ companyId: 'x', groupCode: 'bad code' })).toEqual({ companyId: null, groupCode: null })
  })
})
