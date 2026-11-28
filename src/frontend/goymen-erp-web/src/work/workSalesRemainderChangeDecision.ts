import type {RemainderChange} from '../sales/remainderChange'
import {isBoundUuid} from '../finance/contract'
import {hasExactKeys} from '../finance/wireShape'
import {salesRequestVersion,salesRequestIncrementable} from './workSalesRequestDecision'
export const SALES_REMAINDER_CHANGE_WORK_KIND='sales.remainder-change.v1'
export const SALES_REMAINDER_CHANGE_WORK_CAPABILITY='sales.remainder-change.work-decision'
export interface WorkSalesRemainderChangeLine {readonly id:string;readonly lineNo:number;readonly productId:string;readonly productCode:string;readonly productName:string;readonly orderUnitId:string;readonly orderUnitCode:string;readonly currentQuantity:string;readonly remainder:'Open'|'Revised'|'Cancelled'}
export interface WorkSalesRemainderChangePreview {readonly draftId:string;readonly documentNo:string;readonly version:string;readonly revision:number;readonly state:'Draft'|'Closed'|'Cancelled';readonly approvalState:'Preparing'|'PendingApproval'|'Approved'|'Rejected'|'Withdrawn';readonly change:RemainderChange;readonly line:WorkSalesRemainderChangeLine;readonly approvalSupported:boolean;readonly rejectionSupported:boolean}
export interface SalesRemainderChangeWorkCommand {readonly expectedVersion:string;readonly approve:boolean;readonly reason:string|null;readonly delegationId:string|null}
const record=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(v:unknown,id:string)=>typeof v==='string'&&v.toLowerCase()===id.toLowerCase()
const text=(v:unknown):v is string=>typeof v==='string'&&v.trim()!==''
const actor=(v:unknown):v is string=>text(v)&&v===v.trim()&&v.length<=128
const int32=(v:unknown,min=1):v is number=>typeof v==='number'&&Number.isInteger(v)&&v>=min&&v<=2147483647
const revision=(v:unknown):v is string=>typeof v==='string'&&v===v.trim()&&/^Rev\.(?:0|[1-9][0-9]*)$/.test(v)&&BigInt(v.slice(4))<=2147483647n
const utc=(v:unknown,date:(v:unknown)=>boolean):v is string=>typeof v==='string'&&v===v.trim()&&date(v.slice(0,10))&&/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)
type Amount=(v:unknown,positive:boolean)=>boolean
export const validSalesRemainderReason=(approve:boolean,v:unknown):v is string|null=>approve?v===null:v===null||typeof v==='string'&&v.length<=4000
// Shape and native exact-text checks only. The server owns floor, allocation, persona and quantity eligibility.
export function decodeWorkSalesRemainderChangePreview(v:unknown,company:string,id:string,date:(v:unknown)=>boolean,amount:Amount):WorkSalesRemainderChangePreview|null {
 if(!guid(company)||!guid(id)||!record(v)||!hasExactKeys(v,['draftId','documentNo','version','revision','state','approvalState','change','line','approvalSupported','rejectionSupported'])
  ||!same(v.draftId,id)||!text(v.documentNo)||v.documentNo!==v.documentNo.trim()||v.documentNo.length>200||!salesRequestVersion(v.version)||!int32(v.revision,0)
  ||!['Draft','Closed','Cancelled'].includes(v.state as string)||!['Preparing','PendingApproval','Approved','Rejected','Withdrawn'].includes(v.approvalState as string)||!record(v.change)||!record(v.line))return null
 const r=v.change,l=v.line
 if(!hasExactKeys(r,['changeNo','revision','lineId','lineNo','kind','state','orderedQuantity','requestedQuantity','reason','requestedBy','requestedAtUtc','loadedAtRequest','shippedAtRequest','deliveredAtRequest','decidedBy','decidedAtUtc','decisionReason','resultQuantity','loadedAtDecision'])
  ||!int32(r.changeNo)||!revision(r.revision)||!guid(r.lineId)||!int32(r.lineNo)||r.kind!=='Cancel'&&r.kind!=='Revise'||!['Pending','Approved','Rejected'].includes(r.state as string)
  ||!amount(r.orderedQuantity,true)||(r.kind==='Cancel'?r.requestedQuantity!==null:!amount(r.requestedQuantity,true))||!text(r.reason)||r.reason.length>4000||!actor(r.requestedBy)||!utc(r.requestedAtUtc,date)
  ||![r.loadedAtRequest,r.shippedAtRequest,r.deliveredAtRequest].every(x=>amount(x,false))
  ||(r.state==='Pending'?[r.decidedBy,r.decidedAtUtc,r.decisionReason,r.resultQuantity,r.loadedAtDecision].some(x=>x!==null)
   :!actor(r.decidedBy)||!utc(r.decidedAtUtc,date)||(r.state==='Approved'?r.decisionReason!==null||!amount(r.resultQuantity,false)||!amount(r.loadedAtDecision,false)
    :!validSalesRemainderReason(false,r.decisionReason)||r.resultQuantity!==null||r.loadedAtDecision!==null))
  ||!hasExactKeys(l,['id','lineNo','productId','productCode','productName','orderUnitId','orderUnitCode','currentQuantity','remainder'])||!same(l.id,r.lineId)||l.lineNo!==r.lineNo||!int32(l.lineNo)
  ||![l.productId,l.orderUnitId].every(guid)||![l.productCode,l.productName,l.orderUnitCode].every(text)||!amount(l.currentQuantity,true)||!['Open','Revised','Cancelled'].includes(l.remainder as string)
  ||v.approvalSupported!==(r.state==='Pending')||v.rejectionSupported!==(r.state==='Pending'))return null
 return {draftId:v.draftId as string,documentNo:v.documentNo,version:v.version,revision:v.revision,state:v.state as WorkSalesRemainderChangePreview['state'],approvalState:v.approvalState as WorkSalesRemainderChangePreview['approvalState'],
  change:{changeNo:r.changeNo,revision:r.revision,lineId:r.lineId,lineNo:r.lineNo,kind:r.kind,state:r.state as RemainderChange['state'],orderedQuantity:r.orderedQuantity as string,requestedQuantity:r.requestedQuantity as string|null,reason:r.reason,requestedBy:r.requestedBy,requestedAtUtc:r.requestedAtUtc,
   loadedAtRequest:r.loadedAtRequest as string,shippedAtRequest:r.shippedAtRequest as string,deliveredAtRequest:r.deliveredAtRequest as string,decidedBy:r.decidedBy as string|null,decidedAtUtc:r.decidedAtUtc as string|null,decisionReason:r.decisionReason as string|null,resultQuantity:r.resultQuantity as string|null,loadedAtDecision:r.loadedAtDecision as string|null},
  line:{id:l.id as string,lineNo:l.lineNo,productId:l.productId as string,productCode:l.productCode as string,productName:l.productName as string,orderUnitId:l.orderUnitId as string,orderUnitCode:l.orderUnitCode as string,currentQuantity:l.currentQuantity as string,remainder:l.remainder as WorkSalesRemainderChangeLine['remainder']},approvalSupported:v.approvalSupported as boolean,rejectionSupported:v.rejectionSupported as boolean}
}
export const isSalesRemainderChangeWorkCommand=(v:unknown):v is SalesRemainderChangeWorkCommand=>record(v)&&hasExactKeys(v,['expectedVersion','approve','reason','delegationId'])
 &&salesRequestIncrementable(v.expectedVersion)&&typeof v.approve==='boolean'&&validSalesRemainderReason(v.approve,v.reason)&&(v.delegationId===null||guid(v.delegationId))
export function salesRemainderChangeWorkCommand(h:WorkSalesRemainderChangePreview,approve:boolean,reason:string,delegationId:string|null):SalesRemainderChangeWorkCommand|null {
 if(!(approve?h.approvalSupported:h.rejectionSupported))return null
 const body={expectedVersion:h.version,approve,reason:approve?null:reason,delegationId};return isSalesRemainderChangeWorkCommand(body)?body:null
}
export function validSalesRemainderChangePreview(v:Record<string,unknown>,company:string,id:string,date:(v:unknown)=>boolean,amount:Amount):boolean {
 const h=decodeWorkSalesRemainderChangePreview(v.salesRemainderChange,company,id,date,amount)
 if(h===null||!hasExactKeys(v,['companyId','kind','itemId','documentLabel','sourceVersion','pending','approvalReasonRequired','references','rules','approvalSupported','rejectionSupported','salesRemainderChange'])
  ||!same(v.companyId,company)||!same(v.itemId,id)||v.kind!==SALES_REMAINDER_CHANGE_WORK_KIND||v.documentLabel!==`${h.documentNo} #${h.change.changeNo}`||v.sourceVersion!==h.version
  ||v.pending!==(h.change.state==='Pending')||v.approvalReasonRequired!==false||v.approvalSupported!==(h.approvalSupported&&salesRequestIncrementable(h.version))||v.rejectionSupported!==(h.rejectionSupported&&salesRequestIncrementable(h.version))
  ||!Array.isArray(v.rules)||v.rules.length!==1||!Array.isArray(v.references)||v.references.length!==2)return false
 const rule=v.rules[0],order=v.references[0],line=v.references[1],link=`/satis/siparisler/${h.draftId.toLowerCase()}`
 const ref=(r:unknown,role:string,label:string,recordId:string)=>record(r)&&hasExactKeys(r,['role','label','recordId','link'])&&r.role===role&&r.label===label&&same(r.recordId,recordId)&&typeof r.link==='string'&&r.link.toLowerCase()===link
 return record(rule)&&hasExactKeys(rule,['ruleCode','threshold','actual','deviation','unit'])&&rule.ruleCode==='sales.remainder-change-requested'&&[rule.threshold,rule.actual,rule.deviation,rule.unit].every(x=>x===null)
  &&ref(order,'sales-order-draft',h.documentNo,h.draftId)&&ref(line,'sales-order-line','Sipariş satırı',h.change.lineId)
}
