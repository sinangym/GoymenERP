import type {ApprovalRequest} from '../sales/approvalRequests'
import {isBoundUuid} from '../finance/contract'
import {hasExactKeys} from '../finance/wireShape'
import {salesRequestVersion,salesRequestIncrementable,validSalesRequestReason} from './workSalesRequestDecision'
export const SALES_OVER_DELIVERY_REQUEST_WORK_KIND='sales.over-delivery-request.v1'
export const SALES_OVER_DELIVERY_REQUEST_WORK_CAPABILITY='sales.over-delivery-request.work-decision'
export interface WorkSalesOverDeliveryRequestLine {readonly id:string;readonly lineNo:number;readonly productId:string;readonly productCode:string;readonly productName:string;readonly orderUnitId:string;readonly orderUnitCode:string;readonly currentQuantity:string}
export interface WorkSalesOverDeliveryRequestOrder {readonly id:string;readonly documentNo:string;readonly version:string;readonly revision:number;readonly state:'Draft'|'Closed'|'Cancelled';readonly approvalState:'Preparing'|'PendingApproval'|'Approved'|'Rejected'|'Withdrawn';readonly partyId:string;readonly companyAccountId:string;readonly currencyId:string;readonly businessDate:string;readonly line:WorkSalesOverDeliveryRequestLine|null}
export interface WorkSalesOverDeliveryRequestView extends ApprovalRequest {readonly kind:'OverDelivery';readonly status:'Pending'|'Approved'|'Rejected';readonly lineId:string;readonly ordered:string;readonly quantity:string;readonly requestedTotal:string;readonly loadingInstructionId:string;readonly shipmentId:string;readonly note:null}
export interface WorkSalesOverDeliveryRequestPreview {readonly requestId:string;readonly request:WorkSalesOverDeliveryRequestView;readonly order:WorkSalesOverDeliveryRequestOrder;readonly approvalSupported:boolean;readonly rejectionSupported:boolean}
export interface SalesOverDeliveryRequestTarget {readonly requestId:string;readonly orderId:string;readonly requestNo:number;readonly subjectRevision:number;readonly lineId:string;readonly requestVersion:string}
export interface SalesOverDeliveryRequestWorkCommand {readonly expectedVersion:string;readonly approve:boolean;readonly reason:string|null;readonly delegationId:string|null}
const record=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(v:unknown,id:string)=>typeof v==='string'&&v.toLowerCase()===id.toLowerCase()
const text=(v:unknown):v is string=>typeof v==='string'&&v.trim()!==''
const actor=(v:unknown):v is string=>text(v)&&v===v.trim()&&v.length<=128
const int32=(v:unknown,min=1):v is number=>typeof v==='number'&&Number.isInteger(v)&&v>=min&&v<=2147483647
const revision=(v:unknown):v is string=>typeof v==='string'&&v===v.trim()&&/^Rev\.(?:0|[1-9][0-9]*)$/.test(v)&&BigInt(v.slice(4))<=2147483647n
const utc=(v:unknown,date:(v:unknown)=>boolean):v is string=>typeof v==='string'&&v===v.trim()&&date(v.slice(0,10))&&/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(v)
const requestKeys=['kind','subjectId','requestNo','subjectRevision','status','version','lineId','ordered','quantity','requestedTotal','loadingInstructionId','shipmentId','note','requestedBy','requestedAtUtc','decidedBy','decidedAtUtc','decisionReason'] as const
type Amount=(v:unknown,positive:boolean)=>boolean
export function decodeWorkSalesOverDeliveryRequestPreview(v:unknown,company:string,id:string,date:(v:unknown)=>boolean,amount:Amount):WorkSalesOverDeliveryRequestPreview|null {
 if(!guid(company)||!guid(id)||!record(v)||!hasExactKeys(v,['requestId','request','order','approvalSupported','rejectionSupported'])||!same(v.requestId,id)||!record(v.request)||!record(v.order))return null
 const r=v.request,o=v.order
 if(!hasExactKeys(r,requestKeys)||r.kind!=='OverDelivery'||!guid(r.subjectId)||!int32(r.requestNo)||!revision(r.subjectRevision)||!salesRequestVersion(r.version)
  ||r.status!=='Pending'&&r.status!=='Approved'&&r.status!=='Rejected'||![r.lineId,r.loadingInstructionId,r.shipmentId].every(guid)||r.note!==null
  ||![r.ordered,r.quantity,r.requestedTotal].every(q=>typeof q==='string'&&q.length<=32&&amount(q,true))||!actor(r.requestedBy)||!utc(r.requestedAtUtc,date)
  ||(r.status==='Pending'?(r.decidedBy!==null||r.decidedAtUtc!==null||r.decisionReason!==null):(!actor(r.decidedBy)||r.decidedBy===r.requestedBy||!utc(r.decidedAtUtc,date)||r.version==='1'
   ||(r.status==='Approved'?r.decisionReason!==null:!validSalesRequestReason(false,r.decisionReason))))
  ||!hasExactKeys(o,['id','documentNo','version','revision','state','approvalState','partyId','companyAccountId','currencyId','businessDate','line'])
  ||!same(o.id,r.subjectId)||!text(o.documentNo)||o.documentNo!==o.documentNo.trim()||o.documentNo.length>200||!salesRequestVersion(o.version)||!int32(o.revision,0)
  ||o.state!=='Draft'&&o.state!=='Closed'&&o.state!=='Cancelled'||!['Preparing','PendingApproval','Approved','Rejected','Withdrawn'].includes(o.approvalState as string)
  ||![o.partyId,o.companyAccountId,o.currencyId].every(guid)||!date(o.businessDate)||v.approvalSupported!==(r.status==='Pending')||v.rejectionSupported!==(r.status==='Pending'))return null
 let line:WorkSalesOverDeliveryRequestLine|null=null
 if(o.line!==null){const l=o.line;if(!record(l)||!hasExactKeys(l,['id','lineNo','productId','productCode','productName','orderUnitId','orderUnitCode','currentQuantity'])
  ||!same(l.id,r.lineId as string)||!int32(l.lineNo)||![l.productId,l.orderUnitId].every(guid)||![l.productCode,l.productName,l.orderUnitCode].every(text)||typeof l.currentQuantity!=='string'||!amount(l.currentQuantity,true))return null
  line={id:l.id as string,lineNo:l.lineNo,productId:l.productId as string,productCode:l.productCode as string,productName:l.productName as string,orderUnitId:l.orderUnitId as string,orderUnitCode:l.orderUnitCode as string,currentQuantity:l.currentQuantity}}
 return {requestId:v.requestId as string,request:{kind:'OverDelivery',subjectId:r.subjectId,requestNo:r.requestNo,subjectRevision:r.subjectRevision,status:r.status,version:r.version,lineId:r.lineId as string,
  ordered:r.ordered as string,quantity:r.quantity as string,requestedTotal:r.requestedTotal as string,loadingInstructionId:r.loadingInstructionId as string,shipmentId:r.shipmentId as string,note:null,
  requestedBy:r.requestedBy,requestedAtUtc:r.requestedAtUtc,decidedBy:r.decidedBy as string|null,decidedAtUtc:r.decidedAtUtc as string|null,decisionReason:r.decisionReason as string|null},
  order:{id:o.id as string,documentNo:o.documentNo,version:o.version,revision:o.revision,state:o.state,approvalState:o.approvalState as WorkSalesOverDeliveryRequestOrder['approvalState'],partyId:o.partyId as string,companyAccountId:o.companyAccountId as string,currencyId:o.currencyId as string,businessDate:o.businessDate as string,line},approvalSupported:v.approvalSupported as boolean,rejectionSupported:v.rejectionSupported as boolean}
}
export const isSalesOverDeliveryRequestWorkCommand=(v:unknown):v is SalesOverDeliveryRequestWorkCommand=>record(v)&&hasExactKeys(v,['expectedVersion','approve','reason','delegationId'])
 &&salesRequestIncrementable(v.expectedVersion)&&typeof v.approve==='boolean'&&validSalesRequestReason(v.approve,v.reason)&&(v.delegationId===null||guid(v.delegationId))
export const salesOverDeliveryRequestTarget=(h:WorkSalesOverDeliveryRequestPreview):SalesOverDeliveryRequestTarget=>({requestId:h.requestId,orderId:h.order.id,requestNo:h.request.requestNo,subjectRevision:parseInt(h.request.subjectRevision.slice(4),10),lineId:h.request.lineId,requestVersion:h.request.version})
export function salesOverDeliveryRequestWorkCommand(h:WorkSalesOverDeliveryRequestPreview,approve:boolean,reason:string,delegationId:string|null):SalesOverDeliveryRequestWorkCommand|null {
 if(!(approve?h.approvalSupported:h.rejectionSupported))return null
 const body={expectedVersion:h.request.version,approve,reason:approve?null:reason,delegationId}
 return isSalesOverDeliveryRequestWorkCommand(body)?body:null
}
// Representational normalization only, for the owner's canonical rule text; no quantity arithmetic or eligibility calculation.
const identity=(v:string)=>{const [w,f='']=v.split('.');const whole=w!.replace(/^0+(?=\d)/,'');const fraction=f.replace(/0+$/,'');return fraction?`${whole}.${fraction}`:whole}
export function validSalesOverDeliveryRequestPreview(v:Record<string,unknown>,company:string,id:string,date:(v:unknown)=>boolean,amount:Amount):boolean {
 const h=decodeWorkSalesOverDeliveryRequestPreview(v.salesOverDeliveryRequest,company,id,date,amount)
 if(h===null||!hasExactKeys(v,['companyId','kind','itemId','documentLabel','sourceVersion','pending','approvalReasonRequired','references','rules','approvalSupported','rejectionSupported','salesOverDeliveryRequest'])
  ||!same(v.companyId,company)||!same(v.itemId,id)||v.kind!==SALES_OVER_DELIVERY_REQUEST_WORK_KIND||v.documentLabel!==`${h.order.documentNo} #${h.request.requestNo}`||v.sourceVersion!==h.request.version
  ||v.pending!==(h.request.status==='Pending')||v.approvalReasonRequired!==false||v.approvalSupported!==(h.approvalSupported&&salesRequestIncrementable(h.request.version))
  ||v.rejectionSupported!==(h.rejectionSupported&&salesRequestIncrementable(h.request.version))||!Array.isArray(v.rules)||v.rules.length!==1||!Array.isArray(v.references)||v.references.length!==1)return false
 const rule=v.rules[0],ref=v.references[0]
 return record(rule)&&hasExactKeys(rule,['ruleCode','threshold','actual','deviation','unit'])&&rule.ruleCode==='sales.over-delivery-requested'&&rule.threshold===identity(h.request.ordered!)&&rule.actual===identity(h.request.requestedTotal!)
  &&amount(rule.deviation,true)&&rule.unit===null&&record(ref)&&hasExactKeys(ref,['role','label','recordId','link'])&&ref.role==='sales-order'&&ref.label===h.order.documentNo
  &&same(ref.recordId,h.order.id)&&typeof ref.link==='string'&&ref.link.toLowerCase()===`/satis/siparisler/${h.order.id.toLowerCase()}`
}
