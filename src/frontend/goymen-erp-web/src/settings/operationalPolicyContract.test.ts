import { describe, expect, it } from 'vitest'
import { COMPANY_A, COMPANY_B } from './testHarness'
import { decodeOperationalPolicy } from './operationalPolicyContract'
const initial = { companyId: COMPANY_A, activeDays: 90, version: '0', versions: [] }
describe('KIM-S19 operational policy decoder', () => {
  it('accepts only decided empty-store values and rejects foreign or malformed responses', () => {
    expect(decodeOperationalPolicy(initial, COMPANY_A, 'notification-retention')?.value).toBe(90)
    for (const raw of [{ ...initial, companyId: COMPANY_B }, { ...initial, activeDays: 89 }, { ...initial, activeDays: -1 }, { ...initial, activeDays: '90' }, { ...initial, version: 0 }, { ...initial, extra: true }]) expect(decodeOperationalPolicy(raw, COMPANY_A, 'notification-retention')).toBeNull()
    expect(decodeOperationalPolicy({ companyId: COMPANY_A, includeScrapCost: true, version: '0', versions: [] }, COMPANY_A, 'scrap-cost')?.value).toBe(true)
    expect(decodeOperationalPolicy({ companyId: COMPANY_A, includeScrapCost: false, version: '0', versions: [] }, COMPANY_A, 'scrap-cost')).toBeNull()
  })
  it('requires descending distinct typed history and the exact current value', () => {
    const entry = { version: '2', value: 30, recordedBy: 'SENTETIK', recordedAtUtc: '2026-11-17T09:00:00Z', reason: 'SENTETIK gerekçe' }
    const view = { companyId: COMPANY_A, activeDays: 30, version: '2', versions: [entry, { ...entry, version: '1', value: 90 }] }
    expect(decodeOperationalPolicy(view, COMPANY_A, 'notification-retention')?.versions).toHaveLength(2)
    for (const raw of [{ ...view, activeDays: 90 }, { ...view, versions: [entry, entry] }, { ...view, versions: [...view.versions].reverse() }, { ...view, versions: [{ ...entry, value: true }] }, { ...view, versions: [{ ...entry, recordedAtUtc: '2026-11-17' }] }, { ...view, version: '9223372036854775808' }]) expect(decodeOperationalPolicy(raw, COMPANY_A, 'notification-retention')).toBeNull()
  })
})
