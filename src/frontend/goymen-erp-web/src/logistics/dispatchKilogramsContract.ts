export const KG_TEXT = /^(0|[1-9][0-9]{0,14})(\.[0-9]{1,3})?$/
const signed = /^-?(0|[1-9][0-9]{0,14})(\.[0-9]{1,3})?$/
const decimal = /^-?(0|[1-9][0-9]*)(\.[0-9]+)?$/
export const KG_GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const object = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v)
const exact = (v: Record<string, unknown>, keys: readonly string[]) => Object.keys(v).length === keys.length && keys.every(k => Object.hasOwn(v, k))
const RECORD_FIELDS = ['companyId', 'recordId', 'dispatchNoteId', 'instructionId', 'theoreticalKg', 'scaleKg', 'differenceKg', 'bandLevel',
  'warningBandPercent', 'approvalBandPercent', 'blockBandPercent', 'totalExcessKg', 'excessRatioPercent', 'approvalRatePercent', 'decision',
  'status', 'version', 'products', 'enteredBy', 'enteredByComplete', 'recordedBy', 'recordedAtUtc', 'approvedBy', 'approvedAtUtc', 'kilogramAllocation'] as const
const PRODUCT_FIELDS = ['productId', 'noteKg', 'scaleKg', 'differenceKg', 'remainingPhysicalKg', 'physicallyCoveredKg', 'excessKg', 'targetLineNo'] as const
const LINE_FIELDS = ['lineNo', 'shipmentId', 'productId', 'theoreticalKg', 'kilograms'] as const
const DOCUMENT_FIELDS = ['ettn', 'kind', 'documentId', 'documentVersion', 'sha256'] as const
const id = (v: unknown): v is string => typeof v === 'string' && KG_GUID.test(v) && v !== '00000000-0000-0000-0000-000000000000'
const kg = (v: unknown): v is string => typeof v === 'string' && KG_TEXT.test(v)
const optionalDecimal = (v: unknown) => v === null || typeof v === 'string' && decimal.test(v)
const instant = (v: unknown): v is string => typeof v === 'string' && v.length <= 64
  && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$/.test(v) && Number.isFinite(Date.parse(v))
export interface KgLine { readonly lineNo: number; readonly shipmentId: string; readonly productId: string; readonly theoreticalKg: string; readonly kilograms: string }
export interface KgDocument { readonly ettn: string; readonly kind: 'ElectronicDispatch' | 'ElectronicInvoice'; readonly documentId: string; readonly documentVersion: number; readonly sha256: string }
export interface KgProduct { readonly productId: string; readonly noteKg: string; readonly scaleKg: string; readonly differenceKg: string; readonly remainingPhysicalKg: string; readonly physicallyCoveredKg: string; readonly excessKg: string; readonly targetLineNo: number | null }
export interface DispatchKilograms {
  readonly companyId: string; readonly recordId: string; readonly dispatchNoteId: string; readonly instructionId: string
  readonly theoreticalKg: string; readonly scaleKg: string; readonly differenceKg: string; readonly totalExcessKg: string
  readonly bandLevel: 'NotEvaluated' | 'None' | 'Warning' | 'Approval' | 'Block'
  readonly warningBandPercent: string | null; readonly approvalBandPercent: string | null; readonly blockBandPercent: string | null
  readonly excessRatioPercent: string | null; readonly approvalRatePercent: string | null
  readonly decision: 'NoExcess' | 'WithinRate' | 'ApprovalRequired'; readonly status: 'Recorded' | 'PendingApproval' | 'Approved'
  readonly version: number; readonly products: readonly KgProduct[]; readonly enteredBy: readonly string[]; readonly enteredByComplete: boolean
  readonly recordedBy: string; readonly recordedAtUtc: string; readonly approvedBy: string | null; readonly approvedAtUtc: string | null
  readonly kilogramAllocation: { readonly lines: readonly KgLine[]; readonly finalizedDocument: KgDocument | null } | null
}
export interface RecordKilograms {
  readonly recordId: string; readonly dispatchNoteId: string
  readonly products: readonly { readonly productId: string; readonly scaleKg: string }[]
  readonly finalizedDocument: { readonly ettn: string; readonly kind: KgDocument['kind']; readonly documentId: string;
    readonly expectedDocumentVersion: number; readonly expectedDocumentSha256: string;
    readonly lines: readonly { readonly lineNo: number; readonly shipmentId: string; readonly productId: string; readonly kilograms: string }[] } | null
}
export type KgCommand = RecordKilograms | { readonly expectedVersion: number }
export interface KgResponse { readonly record: DispatchKilograms; readonly isReplay: boolean; readonly originalCorrelationId: string }
export function decodeDispatchKilograms(v: unknown, companyId: string, noteId: string): DispatchKilograms | null {
  if (!object(v) || !exact(v, RECORD_FIELDS) || v.companyId !== companyId || v.dispatchNoteId !== noteId || !id(v.recordId) || !id(v.instructionId)
    || !kg(v.theoreticalKg) || !kg(v.scaleKg) || !kg(v.totalExcessKg) || typeof v.differenceKg !== 'string' || !signed.test(v.differenceKg)
    || !['NotEvaluated', 'None', 'Warning', 'Approval', 'Block'].includes(String(v.bandLevel))
    || !['NoExcess', 'WithinRate', 'ApprovalRequired'].includes(String(v.decision)) || !['Recorded', 'PendingApproval', 'Approved'].includes(String(v.status))
    || !Number.isSafeInteger(v.version) || (v.version as number) < 1 || !Array.isArray(v.products) || v.products.length === 0 || v.products.length > 200
    || !['warningBandPercent', 'approvalBandPercent', 'blockBandPercent', 'excessRatioPercent', 'approvalRatePercent'].every(k => optionalDecimal(v[k]))
    || !Array.isArray(v.enteredBy) || !v.enteredBy.every(x => typeof x === 'string' && x.length > 0) || typeof v.enteredByComplete !== 'boolean'
    || typeof v.recordedBy !== 'string' || !v.recordedBy || !instant(v.recordedAtUtc)
    || !(v.approvedBy === null || typeof v.approvedBy === 'string' && v.approvedBy.length > 0)
    || !(v.approvedAtUtc === null || instant(v.approvedAtUtc))) return null
  if (new Set(v.products.map(x => object(x) ? x.productId : null)).size !== v.products.length || v.products.some(x => !object(x) || !exact(x, PRODUCT_FIELDS) || !id(x.productId)
    || !['noteKg', 'scaleKg', 'remainingPhysicalKg', 'physicallyCoveredKg', 'excessKg'].every(k => kg(x[k]))
    || typeof x.differenceKg !== 'string' || !signed.test(x.differenceKg) || !(x.targetLineNo === null || Number.isSafeInteger(x.targetLineNo) && (x.targetLineNo as number) > 0))) return null
  if (v.kilogramAllocation !== null) {
    const a = v.kilogramAllocation
    if (!object(a) || !exact(a, ['lines', 'finalizedDocument']) || !Array.isArray(a.lines) || !a.lines.length || a.lines.length > 200 || a.lines.some(x => !object(x) || !exact(x, LINE_FIELDS)
      || !Number.isSafeInteger(x.lineNo) || (x.lineNo as number) < 1 || !id(x.shipmentId) || !id(x.productId) || !kg(x.theoreticalKg) || !kg(x.kilograms))
      || new Set(a.lines.map(x => x.lineNo)).size !== a.lines.length || new Set(a.lines.map(x => x.shipmentId)).size !== a.lines.length) return null
    if (a.finalizedDocument !== null) {
      const d = a.finalizedDocument
      if (!object(d) || !exact(d, DOCUMENT_FIELDS) || !id(d.ettn) || !id(d.documentId) || !['ElectronicDispatch', 'ElectronicInvoice'].includes(String(d.kind))
        || !Number.isSafeInteger(d.documentVersion) || (d.documentVersion as number) < 1 || typeof d.sha256 !== 'string' || !/^[0-9a-f]{64}$/i.test(d.sha256)) return null
    }
  }
  if ((v.status === 'Approved') !== (v.approvedBy !== null && v.approvedAtUtc !== null)) return null
  return v as unknown as DispatchKilograms
}
export function decodeKgResponse(v: unknown, company: string, note: string): KgResponse | null {
  if (!object(v) || !exact(v, ['record', 'isReplay', 'originalCorrelationId']) || typeof v.isReplay !== 'boolean' || typeof v.originalCorrelationId !== 'string' || !v.originalCorrelationId) return null
  const record = decodeDispatchKilograms(v.record, company, note)
  return record === null ? null : { record, isReplay: v.isReplay, originalCorrelationId: v.originalCorrelationId }
}
