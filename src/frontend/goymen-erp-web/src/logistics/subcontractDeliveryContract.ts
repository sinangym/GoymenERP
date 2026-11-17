import { isBoundUuid as guid } from '../product/wireGuards'

export const SUBCONTRACT_DELIVERY_PATH = '/logistics/subcontract-deliveries'
export const SUBCONTRACT_DELIVERY_ROUTE = '/lojistik/fason-teslimler'
export const SUBCONTRACT_CAPABILITY = { create: 'logistics.subcontract-deliveries.create', detail: 'logistics.subcontract-deliveries.detail',
  list: 'logistics.subcontract-deliveries.list', issue: 'logistics.subcontract-deliveries.issue', load: 'logistics.subcontract-deliveries.load',
  weigh: 'logistics.subcontract-deliveries.weigh', ship: 'logistics.subcontract-deliveries.ship', correct: 'logistics.subcontract-deliveries.weighing-correct' } as const
export interface SubcontractSalesLine { readonly salesOrderId: string; readonly salesOrderLineId: string; readonly documentNo: string; readonly serviceCode: string; readonly serviceName: string }
export interface SubcontractSalesLines { readonly companyId: string; readonly items: readonly SubcontractSalesLine[]; readonly nextCursor: string | null }
export interface SubcontractList { readonly companyId: string; readonly items: readonly { readonly instructionId: string; readonly version: string; readonly status: 'Draft' | 'Issued' | 'Cancelled'; readonly createdAtUtc: string; readonly labelCodes: readonly string[] }[]; readonly nextCursor: string | null }
export interface SubcontractCandidate {
  readonly salesOrderId: string; readonly salesOrderLineId: string; readonly documentNo: string; readonly serviceCode: string; readonly serviceName: string
  readonly manufacturingOrderId: string; readonly manufacturingOrderVersion: string; readonly planVersion: string; readonly salesLinkRevision: string
  readonly salesLineRevision: number; readonly salesOrderVersion: string; readonly customerPartyId: string; readonly customerAccountId: string
  readonly outputFactId: string; readonly labelId: string; readonly labelCode: string; readonly labelVersion: string; readonly productId: string
  readonly productVersion: string; readonly declaredKg: string; readonly physicalKg: string; readonly reservedKg: string; readonly availableKg: string; readonly isReleasable: boolean
}
export interface SubcontractCandidates { readonly companyId: string; readonly items: readonly SubcontractCandidate[]; readonly pageIndex: number; readonly hasNext: boolean }
export interface SubcontractSource {
  readonly companyId: string; readonly customerPartyId: string; readonly customerAccountId: string; readonly salesOrderId: string; readonly salesOrderLineId: string
  readonly salesOrderVersion: string; readonly salesLineRevision: number; readonly productId: string; readonly productVersion: string; readonly salesServiceProductId: string
  readonly manufacturingOrderId: string; readonly manufacturingOrderVersion: string; readonly manufacturingPlanId: string; readonly manufacturingPlanVersion: string
  readonly manufacturingSalesLinkRevision: string; readonly outputFactId: string; readonly labelId: string; readonly outputNetKg: string
  readonly manufacturingSourceVersion: string; readonly labelCode: string; readonly salesDocumentNo: string; readonly serviceCode: string; readonly serviceName: string
}
export interface SubcontractLine { readonly lineId: string; readonly source: SubcontractSource; readonly allocationId: string; readonly instructedKg: string
  readonly loadedKg: string; readonly shippedKg: string; readonly deliveredKg: string; readonly allocationVersion: string | null; readonly labelVersion: string | null }
export interface SubcontractWeighing { readonly weighingId: string; readonly revision: number; readonly direction: 'TareFirst' | 'GrossFirst'
  readonly firstKg: string; readonly secondKg: string; readonly netKg: string; readonly loadedKg: string; readonly differenceKg: string }
export interface SubcontractCorrection { readonly correctionId: string; readonly revision: number; readonly direction: 'TareFirst' | 'GrossFirst'; readonly firstKg: string
  readonly secondKg: string; readonly netKg: string; readonly previousNetKg: string; readonly differenceKg: string; readonly reason: string; readonly correctedBy: string; readonly correctedAtUtc: string }
export interface SubcontractInstruction { readonly companyId: string; readonly instructionId: string; readonly version: string; readonly status: 'Draft' | 'Issued' | 'Cancelled'
  readonly customerPartyId: string; readonly customerAccountId: string; readonly lines: readonly SubcontractLine[]; readonly weighing: SubcontractWeighing | null
  readonly corrections: readonly SubcontractCorrection[]; readonly isReplay: boolean; readonly originalCorrelationId: string; readonly originalWeighing: SubcontractWeighing | null; readonly recordedLoadId: string | null }
export interface SubcontractCreateLine { readonly lineId: string; readonly salesOrderId: string; readonly salesOrderLineId: string; readonly expectedSalesOrderVersion: string
  readonly salesLineRevision: number; readonly manufacturingOrderId: string; readonly expectedManufacturingOrderVersion: string; readonly expectedPlanVersion: string
  readonly expectedSalesLinkRevision: string; readonly outputFactId: string; readonly labelId: string; readonly expectedLabelVersion: string; readonly allocationId: string; readonly quantityKg: string; readonly reason: string }
export interface SubcontractCreate { readonly instructionId: string; readonly effectiveOn: string; readonly lines: readonly SubcontractCreateLine[] }
const obj = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const exact = (v: Record<string, unknown>, fields: readonly string[]) => Object.keys(v).length === fields.length && fields.every(f => Object.hasOwn(v, f))
const text = (v: unknown): v is string => typeof v === 'string' && v.trim() !== '' && v.length <= 4000 && !/[\u0000-\u001f\u007f]/.test(v)
export const subcontractVersion = (v: unknown): v is string => typeof v === 'string' && /^[1-9]\d{0,18}$/.test(v) && BigInt(v) <= 9223372036854775807n
const integer = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0
export const subcontractKg = (v: unknown): v is string => typeof v === 'string' && /^(?:0|[1-9]\d{0,14})(?:\.\d{0,2}[1-9])?$/.test(v)
const signedKg = (v: unknown) => typeof v === 'string' && subcontractKg(v.startsWith('-') ? v.slice(1) : v) && v !== '-0'
const same = (v: unknown, expected: string) => guid(v) && v.toLowerCase() === expected.toLowerCase()
const fields = (v: Record<string, unknown>, names: readonly string[], guard: (x: unknown) => boolean) => names.every(n => guard(v[n]))
export function decodeSubcontractList(v: unknown, company: string): SubcontractList | null {
  if (!obj(v) || !exact(v, ['companyId', 'items', 'nextCursor']) || !same(v.companyId, company) || !Array.isArray(v.items) || v.items.length > 25
    || !(v.nextCursor === null || text(v.nextCursor)) || v.items.some(x => !obj(x) || !exact(x, ['instructionId', 'version', 'status', 'createdAtUtc', 'labelCodes'])
      || !guid(x.instructionId) || !subcontractVersion(x.version) || !['Draft', 'Issued', 'Cancelled'].includes(String(x.status))
      || typeof x.createdAtUtc !== 'string' || !Number.isFinite(Date.parse(x.createdAtUtc)) || !Array.isArray(x.labelCodes)
      || x.labelCodes.length < 1 || x.labelCodes.length > 3 || x.labelCodes.some(code => !text(code)))
    || new Set(v.items.map(x => (x as SubcontractList['items'][number]).instructionId)).size !== v.items.length) return null
  return v as unknown as SubcontractList
}
function source(v: unknown, company: string): v is SubcontractSource {
  return obj(v) && exact(v, ['companyId', 'customerPartyId', 'customerAccountId', 'salesOrderId', 'salesOrderLineId', 'salesOrderVersion', 'salesLineRevision',
    'productId', 'productVersion', 'salesServiceProductId', 'manufacturingOrderId', 'manufacturingOrderVersion', 'manufacturingPlanId', 'manufacturingPlanVersion',
    'manufacturingSalesLinkRevision', 'outputFactId', 'labelId', 'outputNetKg', 'manufacturingSourceVersion', 'labelCode', 'salesDocumentNo', 'serviceCode', 'serviceName'])
    && same(v.companyId, company) && fields(v, ['customerPartyId', 'customerAccountId', 'salesOrderId', 'salesOrderLineId', 'productId', 'salesServiceProductId',
      'manufacturingOrderId', 'manufacturingPlanId', 'outputFactId', 'labelId'], guid)
    && fields(v, ['salesOrderVersion', 'manufacturingOrderVersion', 'manufacturingPlanVersion', 'manufacturingSalesLinkRevision'], subcontractVersion)
    && integer(v.salesLineRevision) && subcontractKg(v.outputNetKg) && v.outputNetKg !== '0'
    && fields(v, ['productVersion', 'labelCode', 'salesDocumentNo', 'serviceCode', 'serviceName'], text)
    && typeof v.manufacturingSourceVersion === 'string' && /^[0-9A-F]{64}$/.test(v.manufacturingSourceVersion)
}
export function decodeSubcontractSalesLines(v: unknown, company: string): SubcontractSalesLines | null {
  if (!obj(v) || !exact(v, ['companyId', 'items', 'nextCursor']) || !same(v.companyId, company) || !Array.isArray(v.items)
    || v.items.length > 25 || !(v.nextCursor === null || text(v.nextCursor))) return null
  if (v.items.some(x => !obj(x) || !exact(x, ['salesOrderId', 'salesOrderLineId', 'documentNo', 'serviceCode', 'serviceName'])
    || !fields(x, ['salesOrderId', 'salesOrderLineId'], guid) || !fields(x, ['documentNo', 'serviceCode', 'serviceName'], text))) return null
  return v as unknown as SubcontractSalesLines
}
export function decodeSubcontractCandidates(v: unknown, company: string, order: string, line: string, page: number): SubcontractCandidates | null {
  if (!obj(v) || !exact(v, ['companyId', 'items', 'pageIndex', 'hasNext']) || !same(v.companyId, company) || v.pageIndex !== page
    || typeof v.hasNext !== 'boolean' || !Array.isArray(v.items)) return null
  for (const x of v.items) {
    if (!obj(x) || !exact(x, ['salesOrderId', 'salesOrderLineId', 'documentNo', 'serviceCode', 'serviceName', 'manufacturingOrderId', 'manufacturingOrderVersion',
      'planVersion', 'salesLinkRevision', 'salesLineRevision', 'salesOrderVersion', 'customerPartyId', 'customerAccountId', 'outputFactId', 'labelId', 'labelCode',
      'labelVersion', 'productId', 'productVersion', 'declaredKg', 'physicalKg', 'reservedKg', 'availableKg', 'isReleasable'])
      || !same(x.salesOrderId, order) || !same(x.salesOrderLineId, line)
      || !fields(x, ['manufacturingOrderId', 'customerPartyId', 'customerAccountId', 'outputFactId', 'labelId', 'productId'], guid)
      || !fields(x, ['manufacturingOrderVersion', 'planVersion', 'salesLinkRevision', 'salesOrderVersion', 'labelVersion'], subcontractVersion)
      || !fields(x, ['documentNo', 'serviceCode', 'serviceName', 'labelCode', 'productVersion'], text)
      || !fields(x, ['declaredKg', 'physicalKg', 'reservedKg', 'availableKg'], subcontractKg) || !integer(x.salesLineRevision) || typeof x.isReleasable !== 'boolean') return null
  }
  if (new Set(v.items.map(x => `${(x as SubcontractCandidate).manufacturingOrderId}:${(x as SubcontractCandidate).outputFactId}`)).size !== v.items.length) return null
  return v as unknown as SubcontractCandidates
}
function weighing(v: unknown): v is SubcontractWeighing {
  return obj(v) && exact(v, ['weighingId', 'revision', 'direction', 'firstKg', 'secondKg', 'netKg', 'loadedKg', 'differenceKg'])
    && guid(v.weighingId) && integer(v.revision) && v.revision >= 1 && ['TareFirst', 'GrossFirst'].includes(String(v.direction))
    && fields(v, ['firstKg', 'secondKg', 'netKg', 'loadedKg'], subcontractKg) && v.firstKg !== '0' && v.secondKg !== '0' && v.netKg !== '0' && signedKg(v.differenceKg)
}
function sameWeighing(a: SubcontractWeighing, b: SubcontractWeighing) {
  return a.weighingId === b.weighingId && a.revision === b.revision && a.direction === b.direction && a.firstKg === b.firstKg && a.secondKg === b.secondKg
    && a.netKg === b.netKg && a.loadedKg === b.loadedKg && a.differenceKg === b.differenceKg
}
export function decodeSubcontractInstruction(v: unknown, company: string, id?: string): SubcontractInstruction | null {
  if (!obj(v) || !exact(v, ['companyId', 'instructionId', 'version', 'status', 'customerPartyId', 'customerAccountId', 'lines', 'weighing', 'corrections', 'isReplay', 'originalCorrelationId', 'originalWeighing', 'recordedLoadId'])
    || !same(v.companyId, company) || !guid(v.instructionId) || id !== undefined && !same(v.instructionId, id) || !subcontractVersion(v.version)
    || !['Draft', 'Issued', 'Cancelled'].includes(String(v.status)) || !guid(v.customerPartyId) || !guid(v.customerAccountId)
    || !Array.isArray(v.lines) || v.lines.length < 1 || v.lines.length > 500 || !Array.isArray(v.corrections) || typeof v.isReplay !== 'boolean'
    || typeof v.originalCorrelationId !== 'string' || v.isReplay && !text(v.originalCorrelationId) || !(v.recordedLoadId === null || guid(v.recordedLoadId))) return null
  for (const x of v.lines) {
    if (!obj(x) || !exact(x, ['lineId', 'source', 'allocationId', 'instructedKg', 'loadedKg', 'shippedKg', 'deliveredKg', 'allocationVersion', 'labelVersion'])
      || !guid(x.lineId) || !guid(x.allocationId) || !source(x.source, company) || x.source.customerPartyId !== v.customerPartyId || x.source.customerAccountId !== v.customerAccountId
      || !fields(x, ['instructedKg', 'loadedKg', 'shippedKg', 'deliveredKg'], subcontractKg)
      || !(x.allocationVersion === null || subcontractVersion(x.allocationVersion)) || !(x.labelVersion === null || subcontractVersion(x.labelVersion))) return null
  }
  if (new Set(v.lines.map(x => (x as SubcontractLine).lineId)).size !== v.lines.length) return null
  if (v.weighing === null ? v.originalWeighing !== null || v.corrections.length !== 0
    : !weighing(v.weighing) || !weighing(v.originalWeighing) || v.originalWeighing.revision !== 1 || v.originalWeighing.weighingId !== v.weighing.weighingId
      || v.originalWeighing.loadedKg !== v.weighing.loadedKg) return null
  for (const [index, x] of v.corrections.entries()) {
    if (!obj(x) || !exact(x, ['correctionId', 'revision', 'direction', 'firstKg', 'secondKg', 'netKg', 'previousNetKg', 'differenceKg', 'reason', 'correctedBy', 'correctedAtUtc'])
      || !guid(x.correctionId) || x.revision !== index + 2 || !['TareFirst', 'GrossFirst'].includes(String(x.direction))
      || !fields(x, ['firstKg', 'secondKg', 'netKg', 'previousNetKg'], subcontractKg) || !signedKg(x.differenceKg)
      || !text(x.reason) || !text(x.correctedBy) || typeof x.correctedAtUtc !== 'string' || !Number.isFinite(Date.parse(x.correctedAtUtc))) return null
  }
  if (v.corrections.length > 0 && (!obj(v.weighing) || v.weighing.revision !== v.corrections.length + 1 || v.weighing.netKg !== (v.corrections.at(-1) as SubcontractCorrection).netKg)) return null
  if (v.corrections.length > 0 && (!weighing(v.originalWeighing) || (v.corrections[0] as SubcontractCorrection).previousNetKg !== v.originalWeighing.netKg)) return null
  if (v.weighing !== null && v.corrections.length === 0 && (!weighing(v.weighing) || !weighing(v.originalWeighing) || !sameWeighing(v.weighing, v.originalWeighing))) return null
  return v as unknown as SubcontractInstruction
}
