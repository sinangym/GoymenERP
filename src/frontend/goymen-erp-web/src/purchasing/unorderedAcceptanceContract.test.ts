import { describe, expect, it } from 'vitest'
import { decodeRecordUnorderedAcceptance, decodeUnorderedAcceptanceSource, decodeUnorderedAcceptanceWrite,
  type RecordUnorderedAcceptance, type UnorderedAcceptanceSource, type UnorderedAcceptanceWrite } from './unorderedAcceptanceContract'

const id = (n: number) => `${n.toString().padStart(8, '0')}-abcd-4abc-8abc-abcdefabcdef`
function request(): RecordUnorderedAcceptance {
  return { occurrence: { acceptanceId: id(1), acceptanceLineId: id(2), expectedVersion: '0', supplierPartyId: id(3), companyAccountId: id(4),
    expectedPartyVersion: 'party-2', expectedAccountVersion: 'account-3', subject: { kind: 'Goods', productId: id(5), productVersion: 'product-7' },
    unitId: id(6), unitProfile: { profileId: id(7), version: 8 }, acceptedQuantity: '1.25', occurredAtUtc: '2026-11-20T01:02:03.0000001Z',
    evidence: { documentId: id(8), documentVersion: 9, sha256: 'a'.repeat(64), sourceLineId: 'satır-1' }, controlEvidence: [] },
  businessDate: '2026-10-09', reason: '  Fiilî kabul\nkontrol sonra  ' }
}
function source(): UnorderedAcceptanceSource {
  const r = request(), x = r.occurrence
  return { companyId: id(10), acceptanceId: x.acceptanceId, acceptanceLineId: x.acceptanceLineId, version: 1,
    supplierPartyId: x.supplierPartyId, companyAccountId: x.companyAccountId, subject: x.subject, unitId: x.unitId,
    acceptedQuantity: x.acceptedQuantity, uninvoicedQuantity: x.acceptedQuantity, occurredAtUtc: '2026-11-20T01:02:03.0000001+00:00',
    evidence: x.evidence, controlEvidence: x.controlEvidence, declaration: { businessDate: r.businessDate, reason: r.reason ?? null } }
}
function answer(): UnorderedAcceptanceWrite {
  const r = request().occurrence
  return { record: { source: source(), partyVersion: r.expectedPartyVersion, accountVersion: r.expectedAccountVersion,
    unitProfile: r.unitProfile, recordedBy: 'SENTETIK-actor', recordedAtUtc: '2026-11-28T04:05:06.1234567Z' },
  isReplay: false, originalCorrelationId: 'SENTETIK-original' }
}
const decodeSource = (v: unknown) => decodeUnorderedAcceptanceSource(v, id(10), id(1), id(2))
const decodeAnswer = (v: unknown, r = request(), replay = false, correlation: string | null = null) =>
  decodeUnorderedAcceptanceWrite(v, id(10), 'SENTETIK-actor', r, replay, correlation)

describe('NOPO V2 native contract, controlled source fixtures only', () => {
  it.each([undefined, null, '', '  ham\nnot  '])('retains optional raw Reason %s and an explicit independent business date', reason => {
    const r = request(), value = reason === undefined ? { occurrence: r.occurrence, businessDate: r.businessDate } : { ...r, reason }
    expect(decodeRecordUnorderedAcceptance(value)).toEqual(value)
    expect(Object.hasOwn(decodeRecordUnorderedAcceptance(value)!, 'reason')).toBe(reason !== undefined)
    expect(JSON.stringify(decodeRecordUnorderedAcceptance(value))).toBe(JSON.stringify(value))
  })
  it('keeps genuine v1 declaration absent and V2 declaration only in the Source, without reconstructing a day', () => {
    const { declaration: _declaration, ...legacy } = source()
    expect(decodeSource(legacy)).toEqual(legacy)
    expect(Object.hasOwn(decodeSource(legacy)!, 'declaration')).toBe(false)
    expect(decodeSource(source())).toEqual(source())
    expect(decodeSource({ ...legacy, declaration: null })).toBeNull()
    expect(decodeAnswer(answer())).toEqual(answer())
    expect(decodeAnswer({ ...answer(), record: { ...answer().record, declaration: source().declaration } })).toBeNull()
  })
  it('preserves all goods/service identities, raw quantities, 100ns timestamps and recorded actor separately', () => {
    const r = request(), a = answer(), service = { kind: 'Service' as const, serviceTypeId: id(20), serviceTypeVersion: 'service-7' }
    expect(decodeRecordUnorderedAcceptance({ ...r, occurrence: { ...r.occurrence, subject: service } })).not.toBeNull()
    expect(decodeAnswer({ ...a, record: { ...a.record, source: { ...a.record.source, subject: service } } },
      { ...r, occurrence: { ...r.occurrence, subject: service } })).not.toBeNull()
    expect(decodeAnswer(a)?.record.source.occurredAtUtc).toBe('2026-11-20T01:02:03.0000001+00:00')
    expect(decodeAnswer(a)?.record.recordedAtUtc).toBe('2026-11-28T04:05:06.1234567Z')
    expect(decodeUnorderedAcceptanceSource(source(), id(10).toUpperCase(), id(1).toUpperCase(), id(2).toUpperCase())).not.toBeNull()
  })
  it.each(['0001-01-01', '0099-02-28', '2000-02-29', '9999-12-31'])('accepts native DateOnly boundary %s without a new period policy', businessDate => {
    expect(decodeRecordUnorderedAcceptance({ ...request(), businessDate })).not.toBeNull()
  })
  it.each(['2026-02-30', '1900-02-29', '0000-01-01', '2026-01-01\n'])('refuses impossible or nonexact date %s', businessDate => {
    expect(decodeRecordUnorderedAcceptance({ ...request(), businessDate })).toBeNull()
  })
  it.each(['2026-02-30T08:00:00Z', '2026-01-01T24:00:00Z', '2026-01-01T01:60:00Z', '2026-01-01T01:00:60Z',
    '2026-01-01T01:00:00+03:00', '2026-01-01T01:00:00.12345678Z', '2026-01-01T01:00:00Z\n'])('refuses normalized/non-UTC native timestamp %s', occurredAtUtc => {
    const r = request()
    expect(decodeRecordUnorderedAcceptance({ ...r, occurrence: { ...r.occurrence, occurredAtUtc } })).toBeNull()
    expect(decodeSource({ ...source(), occurredAtUtc })).toBeNull()
    expect(decodeAnswer({ ...answer(), record: { ...answer().record, recordedAtUtc: occurredAtUtc } })).toBeNull()
  })
  it.each(['0.000001', '999999999999999999.999999'])('retains canonical native decimal(24,6) quantity %s without arithmetic', acceptedQuantity => {
    const r = request()
    expect(decodeRecordUnorderedAcceptance({ ...r, occurrence: { ...r.occurrence, acceptedQuantity } })?.occurrence.acceptedQuantity).toBe(acceptedQuantity)
  })
  it.each(['0', '01', '1.0', '1.0000001', '1000000000000000000', '-1', '1e2', '1\n'])('refuses native quantity boundary %s', acceptedQuantity => {
    const r = request()
    expect(decodeRecordUnorderedAcceptance({ ...r, occurrence: { ...r.occurrence, acceptedQuantity } })).toBeNull()
  })
  it.each([0, -1, 1.5, '1', 9007199254740992])('refuses unsafe/non-native numeric Int64 %s at every numeric version', version => {
    const r = request(), o = r.occurrence
    expect(decodeRecordUnorderedAcceptance({ ...r, occurrence: { ...o, unitProfile: { ...o.unitProfile, version } } })).toBeNull()
    expect(decodeRecordUnorderedAcceptance({ ...r, occurrence: { ...o, evidence: { ...o.evidence, documentVersion: version } } })).toBeNull()
    expect(decodeSource({ ...source(), version })).toBeNull()
  })
  it('preserves the largest lossless numeric native versions; it never stringifies already-rounded JSON', () => {
    const r = request(), o = r.occurrence
    const value = { ...r, occurrence: { ...o, unitProfile: { ...o.unitProfile, version: Number.MAX_SAFE_INTEGER },
      evidence: { ...o.evidence, documentVersion: Number.MAX_SAFE_INTEGER } } }
    expect(decodeRecordUnorderedAcceptance(value)).toEqual(value)
  })
  it('requires every exact request field and refuses foreign/private/PO/price fields and duplicate controls', () => {
    const r = request(), o = r.occurrence
    for (const key of Object.keys(o)) {
      const copy = { ...o }; Reflect.deleteProperty(copy, key)
      expect(decodeRecordUnorderedAcceptance({ ...r, occurrence: copy })).toBeNull()
    }
    const { businessDate: _day, ...withoutDay } = r
    expect(decodeRecordUnorderedAcceptance(withoutDay)).toBeNull()
    for (const key of ['purchaseOrderId', 'unitPrice', 'sourceProofJson', 'receipt', 'approved']) {
      expect(decodeRecordUnorderedAcceptance({ ...r, [key]: 'SENTETIK' })).toBeNull()
      expect(decodeRecordUnorderedAcceptance({ ...r, occurrence: { ...o, [key]: 'SENTETIK' } })).toBeNull()
      expect(decodeSource({ ...source(), [key]: 'SENTETIK' })).toBeNull()
    }
    expect(decodeRecordUnorderedAcceptance({ ...r, occurrence: { ...o, controlEvidence: [o.evidence, { ...o.evidence, documentId: o.evidence.documentId.toUpperCase() }] } })).toBeNull()
    expect(decodeRecordUnorderedAcceptance({ ...r, occurrence: { ...o, evidence: { ...o.evidence, sourceLineId: ' line ' } } })).toBeNull()
    expect(decodeRecordUnorderedAcceptance({ ...r, reason: 'x'.repeat(4000) })).not.toBeNull()
    expect(decodeRecordUnorderedAcceptance({ ...r, reason: 'x'.repeat(4001) })).toBeNull()
  })
  it.each(['company', 'acceptance', 'line', 'supplier', 'account', 'subject', 'unit', 'quantity', 'uninvoiced', 'occurred', 'document', 'control',
    'day', 'reason', 'party-version', 'account-version', 'profile', 'actor', 'recorded', 'version', 'correlation', 'replay'])('refuses a genuine-command binding mismatch: %s', fault => {
    const a = answer(), s = a.record.source, r = a.record
    const changed = fault === 'company' ? { ...s, companyId: id(99) } : fault === 'acceptance' ? { ...s, acceptanceId: id(99) }
      : fault === 'line' ? { ...s, acceptanceLineId: id(99) } : fault === 'supplier' ? { ...s, supplierPartyId: id(99) }
        : fault === 'account' ? { ...s, companyAccountId: id(99) } : fault === 'subject' ? { ...s, subject: { kind: 'Service', serviceTypeId: id(99), serviceTypeVersion: '7' } }
          : fault === 'unit' ? { ...s, unitId: id(99) } : fault === 'quantity' ? { ...s, acceptedQuantity: '2' }
            : fault === 'uninvoiced' ? { ...s, uninvoicedQuantity: '0' } : fault === 'occurred' ? { ...s, occurredAtUtc: '2026-11-20T01:02:03.0000002Z' }
              : fault === 'document' ? { ...s, evidence: { ...s.evidence, sha256: 'b'.repeat(64) } } : fault === 'control' ? { ...s, controlEvidence: [s.evidence] }
                : fault === 'day' ? { ...s, declaration: { ...s.declaration, businessDate: '2026-10-10' } }
                  : fault === 'reason' ? { ...s, declaration: { ...s.declaration, reason: request().reason!.trim() } } : fault === 'version' ? { ...s, version: 2 } : s
    const record = fault === 'party-version' ? { ...r, partyVersion: 'other' } : fault === 'account-version' ? { ...r, accountVersion: 'other' }
      : fault === 'profile' ? { ...r, unitProfile: { ...r.unitProfile, version: 9 } } : fault === 'actor' ? { ...r, recordedBy: 'other' }
        : fault === 'recorded' ? { ...r, recordedAtUtc: 'not-native' } : r
    const value = { ...a, record: { ...record, source: changed }, ...(fault === 'correlation' ? { originalCorrelationId: '' } : {}), ...(fault === 'replay' ? { isReplay: true } : {}) }
    expect(decodeAnswer(value)).toBeNull()
  })
  it('accepts only the original command replay correlation and keeps actual recorded facts', () => {
    const a = { ...answer(), isReplay: true }
    expect(decodeAnswer(a, request(), true, 'SENTETIK-original')).toEqual(a)
    expect(decodeAnswer(a, request(), true, 'SENTETIK-new')).toBeNull()
    expect(decodeAnswer(a, request(), true, null)).toBeNull()
  })
})
