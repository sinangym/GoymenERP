import { describe, expect, it } from 'vitest'
import { COMPANY_A, COMPANY_B } from './testHarness'
import { decodeNumberFormat, decodeNumberGaps } from './documentNumberSeriesContract'
describe('KIM-S19 number series contract', () => {
  it('keeps a missing format empty and refuses foreign, extra or inconsistent history', () => {
    const empty = { companyId: COMPANY_A, series: 4, versions: [] }
    expect(decodeNumberFormat(empty, COMPANY_A, 4)?.versions).toEqual([])
    expect(decodeNumberFormat(empty, COMPANY_B, 4)).toBeNull()
    expect(decodeNumberFormat({ ...empty, prefix: 'ALI' }, COMPANY_A, 4)).toBeNull()
    const entry = { version: '1', prefix: 'ALI', sequenceDigits: 6, recordedBy: 'SENTETIK', recordedAtUtc: '2026-11-17T09:00:00Z', reason: 'SENTETIK' }
    expect(decodeNumberFormat({ ...empty, versions: [entry] }, COMPANY_A, 4)?.versions[0].prefix).toBe('ALI')
    for (const value of [{ ...entry, version: '0' }, { ...entry, prefix: 'a' }, { ...entry, sequenceDigits: 0 }, { ...entry, sequenceDigits: 1.5 }]) expect(decodeNumberFormat({ ...empty, versions: [value] }, COMPANY_A, 4)).toBeNull()
    expect(decodeNumberFormat({ ...empty, versions: [entry, entry] }, COMPANY_A, 4)).toBeNull()
  })
  it('retains exact Int64 sequence text and binds gaps to the selected series and year', () => {
    const gap = { series: 4, year: 2026, sequence: '9223372036854775807', number: 'ALI-2026-9223372036854775807', documentId: COMPANY_A, cancelledAtUtc: '2026-11-17T09:00:00Z', reason: 'SENTETIK' }
    expect(decodeNumberGaps([gap], 4, 2026)?.[0].sequence).toBe(gap.sequence)
    for (const value of [{ ...gap, sequence: 1 }, { ...gap, sequence: '9223372036854775808' }, { ...gap, series: 1 }, { ...gap, year: 2027 }]) expect(decodeNumberGaps([value], 4, 2026)).toBeNull()
  })
})
