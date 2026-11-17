import { headers, readFailure, request } from '../sales/api'
import { decodeProblem } from '../sales/contract'
import { isReferenceId, isVersionString } from '../sales/wirePrimitives'
import type { ViewOutcome } from '../shell/uiState'
import type { OrderCommandOutcome } from '../sales/orderAttempt'

export const SUBCONTRACT_RELEASE_CAPABILITY = 'inventory.subcontract-allocations.release-unused'
export const SUBCONTRACT_RELEASE_ROUTE = '/stok/fason-ayirma-birak'
const path = '/inventory/subcontract-allocations'
export interface SubcontractAllocation {
  readonly allocationId: string; readonly companyId: string; readonly version: string; readonly status: 'Active'|'Released'
  readonly labelId: string; readonly productId: string; readonly demandKind: 'sales.subcontract-service-order-line'
  readonly demandId: string; readonly demandLineId: string; readonly demandProductVersion: string; readonly allocatedKg: string
  readonly remainingKg: string; readonly reason: string; readonly reservedUntilUtc: string|null
  readonly createdAtUtc: string; readonly updatedAtUtc: string; readonly isSubstitute: false
}
export interface SubcontractReleaseCandidate { readonly allocation: SubcontractAllocation; readonly labelCode: string; readonly labelVersion: string; readonly customerPartyId: string; readonly customerAccountId: string; readonly manufacturingOrderId: string; readonly outputFactId: string }
export interface SubcontractReleaseCandidates { readonly items: readonly SubcontractReleaseCandidate[]; readonly nextCursor: string|null }
export interface SubcontractRelease { readonly allocation: SubcontractAllocation; readonly labelVersion: string; readonly releasedKg: string; readonly previouslyConsumedKg: string; readonly releaseReason: string; readonly releasedAtUtc: string }
export interface SubcontractReleaseCommand { readonly allocationId: string; readonly expectedAllocationVersion: string; readonly reason: string }
const obj = (v: unknown): Record<string,unknown>|null => typeof v === 'object' && v !== null && !Array.isArray(v) ? v as Record<string,unknown> : null
const exact = (v: unknown, keys: readonly string[]) => {const o=obj(v);return o!==null&&Object.keys(o).length===keys.length&&keys.every(k=>Object.hasOwn(o,k))?o:null}
const text = (v: unknown, max=128): v is string => typeof v==='string'&&v.length>0&&v.length<=max&&v===v.trim()&&!/[\u0000-\u001f\u007f]/.test(v)
const instant = (v: unknown): v is string => typeof v==='string'&&/^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)&&!Number.isNaN(Date.parse(v))
const kg = (v: unknown): v is string => typeof v==='string'&&/^(?:0|[1-9]\d{0,14})(?:\.\d{0,2}[1-9])?$/.test(v)
function allocation(v: unknown, companyId: string, status: 'Active'|'Released'): SubcontractAllocation|null {
  const o=exact(v,['allocationId','companyId','version','status','labelId','productId','demandKind','demandId','demandLineId','demandProductVersion','allocatedKg','remainingKg','reason','reservedUntilUtc','createdAtUtc','updatedAtUtc','isSubstitute'])
  if(!o||o.companyId!==companyId||o.status!==status||o.demandKind!=='sales.subcontract-service-order-line'||o.isSubstitute!==false
    ||!['allocationId','labelId','productId','demandId','demandLineId'].every(k=>isReferenceId(o[k]))||!isVersionString(o.version)
    ||!text(o.demandProductVersion)||!text(o.reason,1000)||!kg(o.allocatedKg)||o.allocatedKg==='0'||!kg(o.remainingKg)
    ||(status==='Active'?o.remainingKg==='0':o.remainingKg!=='0')||!(o.reservedUntilUtc===null||instant(o.reservedUntilUtc))
    ||!instant(o.createdAtUtc)||!instant(o.updatedAtUtc)||Date.parse(o.updatedAtUtc)<Date.parse(o.createdAtUtc))return null
  return o as unknown as SubcontractAllocation
}
export function decodeSubcontractReleaseCandidates(v: unknown, companyId: string): SubcontractReleaseCandidates|null {
  const o=exact(v,['items','nextCursor']);if(!o||!Array.isArray(o.items)||o.items.length>50||!(o.nextCursor===null||isReferenceId(o.nextCursor)))return null
  const items:SubcontractReleaseCandidate[]=[]
  for(const item of o.items){const row=exact(item,['allocation','labelCode','labelVersion','customerPartyId','customerAccountId','manufacturingOrderId','outputFactId'])
    const a=row===null?null:allocation(row.allocation,companyId,'Active')
    if(row===null||a===null||!text(row.labelCode,200)||!isVersionString(row.labelVersion)||!['customerPartyId','customerAccountId','manufacturingOrderId','outputFactId'].every(k=>isReferenceId(row[k]))||items.some(x=>x.allocation.allocationId===a.allocationId))return null
    items.push({...row,allocation:a} as unknown as SubcontractReleaseCandidate)}
  if(o.nextCursor!==null&&(items.length!==50||items.at(-1)?.allocation.allocationId!==o.nextCursor))return null
  return {items,nextCursor:o.nextCursor}
}
export function decodeSubcontractRelease(v: unknown, companyId: string): SubcontractRelease|null {
  const o=exact(v,['allocation','labelVersion','releasedKg','previouslyConsumedKg','releaseReason','releasedAtUtc'])
  const a=o===null?null:allocation(o.allocation,companyId,'Released')
  if(o===null||a===null||!isVersionString(o.labelVersion)||!kg(o.releasedKg)||o.releasedKg==='0'||!kg(o.previouslyConsumedKg)
    ||!text(o.releaseReason,1000)||!instant(o.releasedAtUtc)||o.releasedAtUtc!==a.updatedAtUtc)return null
  return {...o,allocation:a} as unknown as SubcontractRelease
}
export const subcontractReleasePath = (id: string) => `${path}/${encodeURIComponent(id)}/release-unused`
export async function fetchSubcontractReleaseCandidates(companyId: string, cursor: string|null, signal?: AbortSignal): Promise<ViewOutcome<SubcontractReleaseCandidates>> {
  const raw=await request(`${path}/release-candidates${cursor===null?'':`?cursor=${encodeURIComponent(cursor)}`}`,{headers:headers(companyId)},signal)
  if('failure'in raw)return readFailure(raw.failure);if(raw.status!==200)return {type:'http',status:raw.status,body:raw.body}
  const data=decodeSubcontractReleaseCandidates(raw.body,companyId);return data===null?{type:'malformed'}:{type:'ok-single',data}
}
export async function sendSubcontractRelease(payload: SubcontractReleaseCommand, context: {readonly companyId:string;readonly idempotencyKey:string}): Promise<OrderCommandOutcome<SubcontractRelease>> {
  const raw=await request(subcontractReleasePath(payload.allocationId),{method:'POST',headers:{...headers(context.companyId),'Content-Type':'application/json','Idempotency-Key':context.idempotencyKey},body:JSON.stringify({expectedAllocationVersion:payload.expectedAllocationVersion,reason:payload.reason})},undefined)
  if('failure'in raw)return raw.failure==='aborted'?{type:'aborted'}:raw.failure==='offline'?{type:'offline'}:{type:'network-error'}
  if(raw.status!==200)return raw.status>=200&&raw.status<300?{type:'malformed'}:{type:'http',status:raw.status,problem:decodeProblem(raw.body,raw.status)}
  const data=decodeSubcontractRelease(raw.body,context.companyId)
  if(data===null||data.allocation.allocationId!==payload.allocationId||BigInt(data.allocation.version)!==BigInt(payload.expectedAllocationVersion)+1n||data.releaseReason!==payload.reason||raw.replayed!==(raw.replayCorrelationId!==null))return {type:'malformed'}
  return {type:'succeeded',data}
}
