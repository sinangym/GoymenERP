import {decodeRequestDetail,type SalesRequestDetail} from '../sales/poolContract'
import {isBoundUuid} from '../finance/contract'
import {hasExactKeys} from '../finance/wireShape'
import {catalogVersion,catalogIncrementable} from './workApprovedSourceCatalogDecision'
export const SALES_REQUEST_WORK_KIND='sales.request.v1'
export const SALES_REQUEST_WORK_CAPABILITY='sales.request.work-decision'
export interface WorkSalesRequestPreview {readonly detail:SalesRequestDetail;readonly approvalSupported:boolean;readonly rejectionSupported:boolean}
export interface SalesRequestWorkCommand {readonly expectedVersion:string;readonly approve:boolean;readonly reason:string|null;readonly delegationId:string|null}
const record=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(v:unknown,id:string)=>typeof v==='string'&&v.toLowerCase()===id.toLowerCase()
export const salesRequestVersion=(v:unknown):v is string=>catalogVersion(v)&&v!=='0'
export const salesRequestIncrementable=(v:unknown)=>salesRequestVersion(v)&&catalogIncrementable(v)
// Native rejection is required raw text, not a trimmed/canonical note. SAT-D22 adds no maker exclusion.
export const validSalesRequestReason=(approve:boolean,v:unknown)=>approve?v===null:typeof v==='string'&&v.trim()!==''&&v.length<=4000
const timestamp=(v:string,date:(v:unknown)=>boolean)=>date(v.slice(0,10))&&v===v.trim()&&/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|[+-](?:(?:0\d|1[0-3]):[0-5]\d|14:00))$/.test(v)
export function decodeWorkSalesRequestPreview(value:unknown,company:string,id:string,date:(v:unknown)=>boolean):WorkSalesRequestPreview|null {
 if(!guid(company)||!guid(id)||!record(value)||!hasExactKeys(value,['detail','approvalSupported','rejectionSupported']))return null
 // Native strict5/10/11-field decoder stays unchanged. GUID context matching follows the Work boundary, case insensitive.
 const detail=decodeRequestDetail(value.detail,null)
 if(detail===null)return null
 const h=detail.request
 if(!guid(h.id)||!guid(h.companyId)||!guid(h.partyId)||!guid(h.companyAccountId)||!same(h.id,id)||!same(h.companyId,company)
  ||!salesRequestVersion(h.version)||h.state!=='Submitted'&&h.version==='1'||!date(h.businessDate)||!timestamp(h.createdAtUtc,date)
  ||detail.requestedDeliveryDate!==null&&!date(detail.requestedDeliveryDate)
  ||detail.lines.some(l=>!guid(l.lineId)||!guid(l.productId)||!guid(l.orderUnitId)||l.lineNo>2147483647||l.requestedDeliveryDate!==null&&!date(l.requestedDeliveryDate))
  ||detail.decision!==null&&!timestamp(detail.decision.decidedAtUtc,date)
  ||value.approvalSupported!==(h.state==='Submitted')||value.rejectionSupported!==(h.state==='Submitted'||h.state==='Approved'))return null
 return {detail,approvalSupported:value.approvalSupported,rejectionSupported:value.rejectionSupported}
}
export const isSalesRequestWorkCommand=(v:unknown):v is SalesRequestWorkCommand=>record(v)&&hasExactKeys(v,['expectedVersion','approve','reason','delegationId'])
 &&salesRequestIncrementable(v.expectedVersion)&&typeof v.approve==='boolean'&&validSalesRequestReason(v.approve,v.reason)&&(v.delegationId===null||guid(v.delegationId))
export function salesRequestWorkCommand(h:WorkSalesRequestPreview,approve:boolean,reason:string,delegationId:string|null):SalesRequestWorkCommand|null {
 if(!(approve?h.approvalSupported:h.rejectionSupported))return null
 const body={expectedVersion:h.detail.request.version,approve,reason:approve?null:reason,delegationId}
 return isSalesRequestWorkCommand(body)?body:null
}
export function validSalesRequestPreview(value:Record<string,unknown>,company:string,id:string,date:(v:unknown)=>boolean):boolean {
 const h=decodeWorkSalesRequestPreview(value.salesRequest,company,id,date)
 if(h===null||!hasExactKeys(value,['companyId','kind','itemId','documentLabel','sourceVersion','pending','approvalReasonRequired','references','rules','approvalSupported','rejectionSupported','salesRequest'])
  ||!same(value.companyId,company)||!same(value.itemId,id)||value.kind!==SALES_REQUEST_WORK_KIND||value.documentLabel!==h.detail.request.documentNo
  ||value.sourceVersion!==h.detail.request.version||value.pending!==(h.detail.request.state==='Submitted')||value.approvalReasonRequired!==false
  ||value.approvalSupported!==h.approvalSupported||value.rejectionSupported!==h.rejectionSupported||!Array.isArray(value.rules)||value.rules.length!==1
  ||!Array.isArray(value.references)||value.references.length!==1)return false
 const rule=value.rules[0],ref=value.references[0]
 return record(rule)&&hasExactKeys(rule,['ruleCode','threshold','actual','deviation','unit'])&&rule.ruleCode==='sales.request-approval-required'
  &&rule.threshold===null&&rule.actual===null&&rule.deviation===null&&rule.unit===null
  &&record(ref)&&hasExactKeys(ref,['role','label','recordId','link'])&&ref.role==='sales-request'&&ref.label===h.detail.request.documentNo
  &&same(ref.recordId,id)&&typeof ref.link==='string'&&ref.link.toLowerCase()===`/satis/talepler/${id.toLowerCase()}`
}
