import { headers, readFailure, request } from './api'
import { decodeProblem } from './contract'
import { isReferenceId, isVersionString } from './wirePrimitives'
import type { ViewOutcome } from '../shell/uiState'
import type { OrderCommandOutcome } from './orderAttempt'
export type OfficialInvoiceKind = 'Normal' | 'Subcontract'
export interface CancellationCandidate { readonly invoiceId: string; readonly kind: OfficialInvoiceKind; readonly documentNo: string; readonly version: string; readonly state: 'Finalized' | 'OfficiallyCancelled'; readonly officialInvoiceNo: string; readonly ettn: string }
export interface CancellationCandidates { readonly items: readonly CancellationCandidate[]; readonly nextCursor: string | null }
export interface OfficialCancellationEvidence { readonly companyId: string; readonly invoiceId: string; readonly officialInvoiceNo: string; readonly ettn: string; readonly sourceKind: string; readonly sourceRecordId: string; readonly sourceVersion: string; readonly sourcePayloadSha256: string; readonly officiallyCancelledAtUtc: string }
export interface OfficialCancellation { readonly companyId: string; readonly cancellationId: string; readonly invoiceId: string; readonly kind: OfficialInvoiceKind; readonly invoiceVersion: string; readonly finalizedVersion: string; readonly admissionId: string; readonly officialInvoiceNo: string; readonly ettn: string; readonly recordedBy: string; readonly recordedAtUtc: string; readonly reason: string; readonly evidence: OfficialCancellationEvidence; readonly deliveryVersion: number; readonly financeStatus: 'Pending' | 'Cancelled' | 'AllocationConfirmationRequired'; readonly financeReason: string | null }
export type OfficialCancellationCommand = { readonly action: 'record'; readonly kind: OfficialInvoiceKind; readonly invoiceId: string; readonly expectedVersion: string; readonly reason: string } | { readonly action: 'finance-retry'; readonly cancellationId: string; readonly expectedDeliveryVersion: number; readonly releaseActiveAllocations: boolean }
const obj = (v: unknown): Record<string, unknown> | null => typeof v === 'object' && v !== null && !Array.isArray(v) ? v as Record<string, unknown> : null
const exact = (v: unknown, keys: readonly string[]) => { const o = obj(v); return o && Object.keys(o).length === keys.length && keys.every(k => Object.hasOwn(o, k)) ? o : null }
const text = (v: unknown, max = 128): v is string => typeof v === 'string' && v.length > 0 && v.length <= max && v === v.trim() && !/[\u0000-\u001f\u007f]/.test(v)
const instant = (v: unknown): v is string => typeof v === 'string' && /^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v) && !Number.isNaN(Date.parse(v))
const kindIs = (v: unknown): v is OfficialInvoiceKind => v === 'Normal' || v === 'Subcontract'
export function decodeOfficialCancellation(v: unknown, companyId: string, invoiceId?: string): OfficialCancellation | null {
  const o = exact(v, ['companyId','cancellationId','invoiceId','kind','invoiceVersion','finalizedVersion','admissionId','officialInvoiceNo','ettn','recordedBy','recordedAtUtc','reason','evidence','deliveryVersion','financeStatus','financeReason'])
  if (!o || o.companyId !== companyId || !isReferenceId(o.cancellationId) || !isReferenceId(o.invoiceId) || (invoiceId !== undefined && o.invoiceId !== invoiceId)
    || !kindIs(o.kind) || !isVersionString(o.invoiceVersion) || !isVersionString(o.finalizedVersion) || BigInt(o.invoiceVersion) !== BigInt(o.finalizedVersion) + 1n
    || !isReferenceId(o.admissionId) || !text(o.officialInvoiceNo,64) || !isReferenceId(o.ettn) || !isReferenceId(o.recordedBy) || !instant(o.recordedAtUtc)
    || !text(o.reason,1000) || !Number.isSafeInteger(o.deliveryVersion) || (o.deliveryVersion as number) < 0
    || !['Pending','Cancelled','AllocationConfirmationRequired'].includes(o.financeStatus as string) || !(o.financeReason === null || text(o.financeReason))) return null
  const e = exact(o.evidence,['companyId','invoiceId','officialInvoiceNo','ettn','sourceKind','sourceRecordId','sourceVersion','sourcePayloadSha256','officiallyCancelledAtUtc'])
  if (!e || e.companyId !== companyId || e.invoiceId !== o.invoiceId || e.officialInvoiceNo !== o.officialInvoiceNo || e.ettn !== o.ettn
    || !text(e.sourceKind) || !text(e.sourceRecordId,256) || !text(e.sourceVersion) || typeof e.sourcePayloadSha256 !== 'string' || !/^[0-9a-fA-F]{64}$/.test(e.sourcePayloadSha256)
    || !instant(e.officiallyCancelledAtUtc) || Date.parse(e.officiallyCancelledAtUtc) > Date.parse(o.recordedAtUtc)) return null
  return o as unknown as OfficialCancellation
}
export function decodeCancellationCandidates(value: unknown, kind: OfficialInvoiceKind): CancellationCandidates | null {
  const o = exact(value,['items','nextCursor']); if (!o || !Array.isArray(o.items) || o.items.length > 50 || !(o.nextCursor === null || isReferenceId(o.nextCursor))) return null
  const items: CancellationCandidate[] = []
  for (const item of o.items) { const row = exact(item,['invoiceId','kind','documentNo','version','state','officialInvoiceNo','ettn']); if (!row || row.kind !== kind || !isReferenceId(row.invoiceId) || !text(row.documentNo) || !isVersionString(row.version)
      || (row.state !== 'Finalized' && row.state !== 'OfficiallyCancelled') || !text(row.officialInvoiceNo,64) || !isReferenceId(row.ettn) || items.some(x => x.invoiceId === row.invoiceId)) return null
    items.push(row as unknown as CancellationCandidate) }
  if (o.nextCursor !== null && (items.length !== 50 || items.at(-1)?.invoiceId !== o.nextCursor)) return null
  return { items, nextCursor: o.nextCursor }
}
export const officialCancellationPath = (kind: OfficialInvoiceKind, id: string) => `/sales/${kind === 'Normal' ? 'invoices' : 'subcontract-service-invoices'}/${encodeURIComponent(id)}/official-cancellation`
export async function fetchOfficialCancellationCandidates(kind: OfficialInvoiceKind, companyId: string, cursor: string | null, signal?: AbortSignal): Promise<ViewOutcome<CancellationCandidates>> {
  const query = new URLSearchParams({kind, ...(cursor === null ? {} : {cursor})}); const raw = await request(`/sales/official-cancellations/candidates?${query}`,{headers:headers(companyId)},signal)
  if ('failure' in raw) return readFailure(raw.failure); if (raw.status !== 200) return {type:'http',status:raw.status,body:raw.body}
  const data = decodeCancellationCandidates(raw.body,kind); return data === null ? {type:'malformed'} : {type:'ok-single',data}
}
export async function fetchOfficialCancellation(kind: OfficialInvoiceKind, invoiceId: string, companyId: string, signal?: AbortSignal): Promise<ViewOutcome<OfficialCancellation>> {
  const raw = await request(officialCancellationPath(kind,invoiceId),{headers:headers(companyId)},signal)
  if ('failure' in raw) return readFailure(raw.failure); if (raw.status !== 200) return {type:'http',status:raw.status,body:raw.body}
  const data = decodeOfficialCancellation(raw.body,companyId,invoiceId); return data === null || data.kind !== kind ? {type:'malformed'} : {type:'ok-single',data}
}
export async function sendOfficialCancellation(payload: OfficialCancellationCommand, context: {readonly companyId:string;readonly idempotencyKey:string}): Promise<OrderCommandOutcome<OfficialCancellation>> {
  const path = payload.action === 'record' ? officialCancellationPath(payload.kind,payload.invoiceId) : `/sales/official-cancellations/${encodeURIComponent(payload.cancellationId)}/finance-retry`
  const body = payload.action === 'record' ? {expectedVersion:payload.expectedVersion,reason:payload.reason} : {expectedDeliveryVersion:payload.expectedDeliveryVersion,releaseActiveAllocations:payload.releaseActiveAllocations}
  const raw = await request(path,{method:'POST',headers:{...headers(context.companyId),'Content-Type':'application/json','Idempotency-Key':context.idempotencyKey},body:JSON.stringify(body)},undefined)
  if ('failure' in raw) return raw.failure === 'aborted' ? {type:'aborted'} : raw.failure === 'offline' ? {type:'offline'} : {type:'network-error'}
  if (raw.status !== 200) return raw.status >= 200 && raw.status < 300 ? {type:'malformed'} : {type:'http',status:raw.status,problem:decodeProblem(raw.body,raw.status)}
  const data = decodeOfficialCancellation(raw.body,context.companyId,payload.action === 'record' ? payload.invoiceId : undefined)
  if (!data || raw.replayed !== (raw.replayCorrelationId !== null) || (payload.action === 'record' ? data.kind !== payload.kind || data.reason !== payload.reason || data.finalizedVersion !== payload.expectedVersion
    : data.cancellationId !== payload.cancellationId || data.deliveryVersion !== payload.expectedDeliveryVersion + 1)) return {type:'malformed'}
  return {type:'succeeded',data}
}
