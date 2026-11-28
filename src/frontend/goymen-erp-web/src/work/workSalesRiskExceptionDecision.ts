import type {RiskException} from '../sales/riskException'
import {isBoundUuid} from '../finance/contract'
import {hasExactKeys} from '../finance/wireShape'
import {salesRequestVersion,salesRequestIncrementable,validSalesRequestReason} from './workSalesRequestDecision'
export const SALES_RISK_EXCEPTION_WORK_KIND='sales.risk-exception.v1'
export const SALES_RISK_EXCEPTION_WORK_CAPABILITY='sales.risk-exception.work-decision'
export interface WorkSalesRiskExceptionPreview {readonly companyId:string;readonly exceptionId:string;readonly orderDocumentNo:string;readonly orderVersion:string;readonly orderState:'Draft'|'Closed'|'Cancelled';readonly orderApprovalState:'Preparing'|'PendingApproval'|'Approved'|'Rejected'|'Withdrawn';readonly hasApprovedRevision:boolean;readonly exception:RiskException;readonly approvalSupported:boolean;readonly rejectionSupported:boolean}
export interface SalesRiskExceptionWorkCommand {readonly expectedVersion:string;readonly approve:boolean;readonly reason:string|null;readonly delegationId:string|null}
const record=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(v:unknown,id:string)=>typeof v==='string'&&v.toLowerCase()===id.toLowerCase()
const text=(v:unknown):v is string=>typeof v==='string'&&v.trim()!==''
const actor=(v:unknown):v is string=>text(v)&&v.length<=128
const utc=(v:unknown,date:(v:unknown)=>boolean):v is string=>typeof v==='string'&&v===v.trim()&&date(v.slice(0,10))&&/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)
const exceptionKeys=['orderId','exceptionNo','loadingInstructionId','state','version','reason','requestedBy','requestedAtUtc','decidedBy','decidedAtUtc','rejectionReason','validOn','isValidToday'] as const
// The native decoder's fixed1/2 profile stays unchanged. This Work projection accepts the owner's retained Int64 versions.
export function decodeWorkSalesRiskExceptionPreview(v:unknown,company:string,id:string,date:(v:unknown)=>boolean):WorkSalesRiskExceptionPreview|null {
 if(!guid(company)||!guid(id)||!record(v)||!hasExactKeys(v,['companyId','exceptionId','orderDocumentNo','orderVersion','orderState','orderApprovalState','hasApprovedRevision','exception','approvalSupported','rejectionSupported'])
  ||!same(v.companyId,company)||!same(v.exceptionId,id)||!text(v.orderDocumentNo)||v.orderDocumentNo.length>200||!salesRequestVersion(v.orderVersion)
  ||v.orderState!=='Draft'&&v.orderState!=='Closed'&&v.orderState!=='Cancelled'||!['Preparing','PendingApproval','Approved','Rejected','Withdrawn'].includes(v.orderApprovalState as string)
  ||typeof v.hasApprovedRevision!=='boolean'||!record(v.exception))return null
 const r=v.exception
 if(!hasExactKeys(r,exceptionKeys)||!guid(r.orderId)||!guid(r.loadingInstructionId)||typeof r.exceptionNo!=='number'||!Number.isInteger(r.exceptionNo)||r.exceptionNo<1||r.exceptionNo>2147483647
  ||!salesRequestVersion(r.version)||!['Requested','Approved','Rejected','Withdrawn'].includes(r.state as string)||!text(r.reason)||r.reason.length>4000||!actor(r.requestedBy)||!utc(r.requestedAtUtc,date)||typeof r.isValidToday!=='boolean')return null
 if(r.state==='Requested'||r.state==='Withdrawn') {
  if(r.decidedBy!==null||r.decidedAtUtc!==null||r.rejectionReason!==null||r.validOn!==null||r.isValidToday||r.state==='Withdrawn'&&r.version==='1')return null
 }else if(r.version==='1'||!actor(r.decidedBy)||r.decidedBy===r.requestedBy||!utc(r.decidedAtUtc,date)
  ||(r.state==='Approved'?r.rejectionReason!==null||!date(r.validOn):typeof r.rejectionReason!=='string'||r.rejectionReason.length<1||r.rejectionReason.length>4000||r.validOn!==null||r.isValidToday))return null
 const rejecting=r.state==='Requested'&&salesRequestIncrementable(r.version)
 if(v.rejectionSupported!==rejecting||v.approvalSupported!==(rejecting&&v.orderState==='Draft'&&(v.orderApprovalState==='Approved'||v.hasApprovedRevision)))return null
 return {companyId:v.companyId as string,exceptionId:v.exceptionId as string,orderDocumentNo:v.orderDocumentNo,orderVersion:v.orderVersion,orderState:v.orderState,orderApprovalState:v.orderApprovalState as WorkSalesRiskExceptionPreview['orderApprovalState'],hasApprovedRevision:v.hasApprovedRevision,
  exception:{orderId:r.orderId,exceptionNo:r.exceptionNo,loadingInstructionId:r.loadingInstructionId,state:r.state as RiskException['state'],version:r.version,reason:r.reason,requestedBy:r.requestedBy,requestedAtUtc:r.requestedAtUtc,decidedBy:r.decidedBy as string|null,decidedAtUtc:r.decidedAtUtc as string|null,rejectionReason:r.rejectionReason as string|null,validOn:r.validOn as string|null,isValidToday:r.isValidToday},approvalSupported:v.approvalSupported as boolean,rejectionSupported:v.rejectionSupported as boolean}
}
export const isSalesRiskExceptionWorkCommand=(v:unknown):v is SalesRiskExceptionWorkCommand=>record(v)&&hasExactKeys(v,['expectedVersion','approve','reason','delegationId'])
 &&salesRequestIncrementable(v.expectedVersion)&&typeof v.approve==='boolean'&&validSalesRequestReason(v.approve,v.reason)&&(v.delegationId===null||guid(v.delegationId))
export function salesRiskExceptionWorkCommand(h:WorkSalesRiskExceptionPreview,approve:boolean,reason:string,delegationId:string|null):SalesRiskExceptionWorkCommand|null {
 if(!(approve?h.approvalSupported:h.rejectionSupported))return null
 const body={expectedVersion:h.exception.version,approve,reason:approve?null:reason,delegationId}
 return isSalesRiskExceptionWorkCommand(body)?body:null
}
export function validSalesRiskExceptionPreview(v:Record<string,unknown>,company:string,id:string,date:(v:unknown)=>boolean):boolean {
 const h=decodeWorkSalesRiskExceptionPreview(v.salesRiskException,company,id,date)
 if(h===null||!hasExactKeys(v,['companyId','kind','itemId','documentLabel','sourceVersion','pending','approvalReasonRequired','references','rules','approvalSupported','rejectionSupported','salesRiskException'])
  ||!same(v.companyId,company)||!same(v.itemId,id)||v.kind!==SALES_RISK_EXCEPTION_WORK_KIND||v.documentLabel!==`${h.orderDocumentNo} #${h.exception.exceptionNo}`||v.sourceVersion!==h.exception.version
  ||v.pending!==(h.exception.state==='Requested')||v.approvalReasonRequired!==false||v.approvalSupported!==h.approvalSupported||v.rejectionSupported!==h.rejectionSupported
  ||!Array.isArray(v.rules)||v.rules.length!==1||!Array.isArray(v.references)||v.references.length!==1)return false
 const rule=v.rules[0],ref=v.references[0]
 return record(rule)&&hasExactKeys(rule,['ruleCode','threshold','actual','deviation','unit'])&&rule.ruleCode==='sales.shipment-risk-exception'&&rule.threshold===null&&rule.actual===null&&rule.deviation===null&&rule.unit===null
  &&record(ref)&&hasExactKeys(ref,['role','label','recordId','link'])&&ref.role==='sales-order-draft'&&ref.label===h.orderDocumentNo&&same(ref.recordId,h.exception.orderId)&&ref.link===`/satis/siparisler/${h.exception.orderId.toLowerCase()}`
}
