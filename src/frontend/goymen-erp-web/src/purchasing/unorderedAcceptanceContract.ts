import { isDateOnly } from '../sales/wirePrimitives'

/** Native Purchasing occurrence facts. These are not a stock receipt, commercial match or approval. */
export interface UnorderedDocumentEvidence {
  readonly documentId: string; readonly documentVersion: number; readonly sha256: string; readonly sourceLineId: string
}
export type UnorderedSubject = { readonly kind: 'Goods'; readonly productId: string; readonly productVersion: string }
  | { readonly kind: 'Service'; readonly serviceTypeId: string; readonly serviceTypeVersion: string }
export interface UnorderedUnitProfile { readonly profileId: string; readonly version: number }
export interface UnorderedDeclaration { readonly businessDate: string; readonly reason: string | null }
export interface UnorderedOccurrence {
  readonly acceptanceId: string; readonly acceptanceLineId: string; readonly expectedVersion: '0'
  readonly supplierPartyId: string; readonly companyAccountId: string; readonly expectedPartyVersion: string; readonly expectedAccountVersion: string
  readonly subject: UnorderedSubject; readonly unitId: string; readonly unitProfile: UnorderedUnitProfile
  readonly acceptedQuantity: string; readonly occurredAtUtc: string; readonly evidence: UnorderedDocumentEvidence
  readonly controlEvidence: readonly UnorderedDocumentEvidence[]
}
export interface RecordUnorderedAcceptance {
  readonly occurrence: UnorderedOccurrence; readonly businessDate: string; readonly reason?: string | null
}
export interface UnorderedAcceptanceSource {
  readonly companyId: string; readonly acceptanceId: string; readonly acceptanceLineId: string; readonly version: number
  readonly supplierPartyId: string; readonly companyAccountId: string; readonly subject: UnorderedSubject; readonly unitId: string
  readonly acceptedQuantity: string; readonly uninvoicedQuantity: string; readonly occurredAtUtc: string
  readonly evidence: UnorderedDocumentEvidence; readonly controlEvidence: readonly UnorderedDocumentEvidence[]
  /** Absent on genuine v1 records. Never infer from a file, occurrence timestamp or current form. */
  readonly declaration?: UnorderedDeclaration
}
export interface UnorderedAcceptanceRecord {
  readonly source: UnorderedAcceptanceSource; readonly partyVersion: string; readonly accountVersion: string
  readonly unitProfile: UnorderedUnitProfile; readonly recordedBy: string; readonly recordedAtUtc: string
}
export interface UnorderedAcceptanceWrite {
  readonly record: UnorderedAcceptanceRecord; readonly isReplay: boolean; readonly originalCorrelationId: string
}

const object = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const exact = (v: Record<string, unknown>, required: readonly string[], optional: readonly string[] = []) =>
  required.every(k => Object.hasOwn(v, k)) && Object.keys(v).every(k => required.includes(k) || optional.includes(k))
export const unorderedGuid = (v: unknown): v is string => typeof v === 'string' && v.length === 36
  && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(v) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(v)
const sameGuid = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()
const text = (v: unknown, maximum: number): v is string => typeof v === 'string' && v.trim() !== '' && v.trim() === v && v.length <= maximum
const day = (v: unknown): v is string => typeof v === 'string' && v.length === 10 && isDateOnly(v)
// Native Int64 is numeric here. Refuse a value JSON has already rounded; never project it through Number/string coercion.
const nativeLong = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v > 0
const utc = (v: unknown): v is string => {
  if (typeof v !== 'string') return false
  const m = /^(\d{4}-\d{2}-\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(v)
  return m !== null && m[0] === v && day(m[1]) && Number(m[2]) < 24 && Number(m[3]) < 60 && Number(m[4]) < 60
}
const instantKey = (v: string) => v.replace(/(?:Z|\+00:00)$/, '').split('.').map((s, i) => i === 0 ? s : s.padEnd(7, '0'))
const sameInstant = (a: string, b: string) => { const x = instantKey(a), y = instantKey(b); return x[0] === y[0] && (x[1] ?? '0000000') === (y[1] ?? '0000000') }
// Native TryQuantity: canonical decimal(24,6), not a new unit/profile or a UI calculation.
const quantity = (v: unknown, allowZero = false): v is string => typeof v === 'string'
  && /^(0|[1-9][0-9]{0,17})(?:\.[0-9]{0,5}[1-9])?$/.exec(v)?.[0] === v && (allowZero || v !== '0')
const profile = (v: unknown): v is UnorderedUnitProfile => object(v) && exact(v, ['profileId', 'version']) && unorderedGuid(v.profileId) && nativeLong(v.version)
const subject = (v: unknown): v is UnorderedSubject => object(v) && (v.kind === 'Goods'
  ? exact(v, ['kind', 'productId', 'productVersion']) && unorderedGuid(v.productId) && text(v.productVersion, 64)
  : v.kind === 'Service' && exact(v, ['kind', 'serviceTypeId', 'serviceTypeVersion']) && unorderedGuid(v.serviceTypeId) && text(v.serviceTypeVersion, 64))
const evidence = (v: unknown): v is UnorderedDocumentEvidence => object(v) && exact(v, ['documentId', 'documentVersion', 'sha256', 'sourceLineId'])
  && unorderedGuid(v.documentId) && nativeLong(v.documentVersion) && typeof v.sha256 === 'string' && v.sha256.length === 64
  && /^[0-9a-f]{64}$/i.test(v.sha256) && typeof v.sourceLineId === 'string' && v.sourceLineId.trim() !== '' && v.sourceLineId.trim() === v.sourceLineId
const controls = (v: unknown): v is readonly UnorderedDocumentEvidence[] => Array.isArray(v) && v.every(evidence)
  && new Set(v.map(e => JSON.stringify([e.documentId.toLowerCase(), e.documentVersion, e.sourceLineId]))).size === v.length
const declaration = (v: unknown): v is UnorderedDeclaration => object(v) && exact(v, ['businessDate', 'reason']) && day(v.businessDate)
  && (v.reason === null || typeof v.reason === 'string' && v.reason.length <= 4000)
const occurrence = (v: unknown): v is UnorderedOccurrence => object(v) && exact(v, ['acceptanceId', 'acceptanceLineId', 'expectedVersion', 'supplierPartyId',
  'companyAccountId', 'expectedPartyVersion', 'expectedAccountVersion', 'subject', 'unitId', 'unitProfile', 'acceptedQuantity', 'occurredAtUtc', 'evidence', 'controlEvidence'])
  && unorderedGuid(v.acceptanceId) && unorderedGuid(v.acceptanceLineId) && v.expectedVersion === '0' && unorderedGuid(v.supplierPartyId)
  && unorderedGuid(v.companyAccountId) && text(v.expectedPartyVersion, 64) && text(v.expectedAccountVersion, 64) && subject(v.subject)
  && unorderedGuid(v.unitId) && profile(v.unitProfile) && quantity(v.acceptedQuantity) && utc(v.occurredAtUtc) && evidence(v.evidence) && controls(v.controlEvidence)

export function decodeRecordUnorderedAcceptance(v: unknown): RecordUnorderedAcceptance | null {
  return object(v) && exact(v, ['occurrence', 'businessDate'], ['reason']) && occurrence(v.occurrence) && day(v.businessDate)
    && (!Object.hasOwn(v, 'reason') || v.reason === null || typeof v.reason === 'string' && v.reason.length <= 4000)
    ? v as unknown as RecordUnorderedAcceptance : null
}
export function decodeUnorderedAcceptanceSource(v: unknown, companyId: string, acceptanceId: string, lineId: string): UnorderedAcceptanceSource | null {
  if (!object(v) || !exact(v, ['companyId', 'acceptanceId', 'acceptanceLineId', 'version', 'supplierPartyId', 'companyAccountId', 'subject', 'unitId',
    'acceptedQuantity', 'uninvoicedQuantity', 'occurredAtUtc', 'evidence', 'controlEvidence'], ['declaration'])
    || !unorderedGuid(companyId) || !unorderedGuid(acceptanceId) || !unorderedGuid(lineId)
    || !unorderedGuid(v.companyId) || !sameGuid(v.companyId, companyId) || !unorderedGuid(v.acceptanceId) || !sameGuid(v.acceptanceId, acceptanceId)
    || !unorderedGuid(v.acceptanceLineId) || !sameGuid(v.acceptanceLineId, lineId) || !nativeLong(v.version) || !unorderedGuid(v.supplierPartyId)
    || !unorderedGuid(v.companyAccountId) || !subject(v.subject) || !unorderedGuid(v.unitId) || !quantity(v.acceptedQuantity)
    || !quantity(v.uninvoicedQuantity, true) || !utc(v.occurredAtUtc) || !evidence(v.evidence) || !controls(v.controlEvidence)
    || Object.hasOwn(v, 'declaration') && !declaration(v.declaration)) return null
  return v as unknown as UnorderedAcceptanceSource
}

const sameSubject = (a: UnorderedSubject, b: UnorderedSubject) => a.kind === 'Goods' && b.kind === 'Goods'
  ? sameGuid(a.productId, b.productId) && a.productVersion === b.productVersion : a.kind === 'Service' && b.kind === 'Service'
    && sameGuid(a.serviceTypeId, b.serviceTypeId) && a.serviceTypeVersion === b.serviceTypeVersion
const sameEvidence = (a: UnorderedDocumentEvidence, b: UnorderedDocumentEvidence) => sameGuid(a.documentId, b.documentId)
  && a.documentVersion === b.documentVersion && a.sha256 === b.sha256 && a.sourceLineId === b.sourceLineId

export function decodeUnorderedAcceptanceWrite(v: unknown, companyId: string, actorId: string, request: RecordUnorderedAcceptance,
  replayed: boolean, originalCorrelation: string | null): UnorderedAcceptanceWrite | null {
  if (decodeRecordUnorderedAcceptance(request) === null || !object(v) || !exact(v, ['record', 'isReplay', 'originalCorrelationId'])
    || v.isReplay !== replayed || !text(v.originalCorrelationId, 128) || replayed && v.originalCorrelationId !== originalCorrelation
    || !object(v.record) || !exact(v.record, ['source', 'partyVersion', 'accountVersion', 'unitProfile', 'recordedBy', 'recordedAtUtc'])) return null
  const r = request.occurrence, record = v.record, source = decodeUnorderedAcceptanceSource(record.source, companyId, r.acceptanceId, r.acceptanceLineId)
  if (source === null || source.version !== 1 || !sameGuid(source.supplierPartyId, r.supplierPartyId) || !sameGuid(source.companyAccountId, r.companyAccountId)
    || !sameSubject(source.subject, r.subject) || !sameGuid(source.unitId, r.unitId) || source.acceptedQuantity !== r.acceptedQuantity
    || source.uninvoicedQuantity !== r.acceptedQuantity || !sameInstant(source.occurredAtUtc, r.occurredAtUtc)
    || !sameEvidence(source.evidence, r.evidence) || source.controlEvidence.length !== r.controlEvidence.length
    || !source.controlEvidence.every((e, i) => sameEvidence(e, r.controlEvidence[i]))
    || source.declaration === undefined || source.declaration.businessDate !== request.businessDate || source.declaration.reason !== (request.reason ?? null)
    || record.partyVersion !== r.expectedPartyVersion || record.accountVersion !== r.expectedAccountVersion || !profile(record.unitProfile)
    || !sameGuid(record.unitProfile.profileId, r.unitProfile.profileId) || record.unitProfile.version !== r.unitProfile.version
    || record.recordedBy !== actorId || !text(record.recordedBy, 128) || !utc(record.recordedAtUtc)) return null
  return v as unknown as UnorderedAcceptanceWrite
}
