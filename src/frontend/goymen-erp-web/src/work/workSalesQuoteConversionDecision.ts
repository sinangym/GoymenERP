import {decodeQuoteDetail,type SalesQuoteDetail} from '../sales/poolContract'
import type {ApprovalRequest} from '../sales/approvalRequests'
import {isBoundUuid} from '../finance/contract'
import {hasExactKeys} from '../finance/wireShape'
import {salesRequestVersion,salesRequestIncrementable,validSalesRequestReason} from './workSalesRequestDecision'
export const SALES_QUOTE_CONVERSION_WORK_KIND='sales.quote-conversion-request.v1'
export const SALES_QUOTE_CONVERSION_WORK_CAPABILITY='sales.quote-conversion-request.work-decision'
export interface WorkSalesQuoteConversionPreview {readonly requestId:string;readonly request:ApprovalRequest;readonly quote:SalesQuoteDetail;readonly approvalSupported:boolean;readonly rejectionSupported:boolean}
export interface SalesQuoteConversionSelection {readonly quoteId:string;readonly requestNo:number;readonly subjectRevision:number;readonly requestVersion:string}
export interface SalesQuoteConversionWorkCommand {readonly expectedVersion:string;readonly approve:boolean;readonly reason:string|null;readonly delegationId:string|null;readonly salesQuoteConversion:SalesQuoteConversionSelection}
const record=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v)
const guid=(v:unknown):v is string=>typeof v==='string'&&v.length===36&&isBoundUuid(v)
const same=(v:unknown,id:string)=>typeof v==='string'&&v.toLowerCase()===id.toLowerCase()
const int32=(v:unknown):v is number=>typeof v==='number'&&Number.isInteger(v)&&v>=0&&v<=2147483647
const text=(v:unknown):v is string=>typeof v==='string'&&v.trim()!==''
const optionalText=(v:unknown):v is string|null=>v===null||typeof v==='string'&&v.trim()!==''&&v.length<=4000
const revision=(v:unknown):v is string=>typeof v==='string'&&/^Rev\.(?:0|[1-9][0-9]*)$/.test(v)&&BigInt(v.slice(4))<=2147483647n
const utc=(v:unknown,date:(v:unknown)=>boolean):v is string=>typeof v==='string'&&v===v.trim()&&date(v.slice(0,10))
 &&/^\d{4}-\d{2}-\d{2}T(?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d{1,7})?(?:Z|[+-](?:(?:0\d|1[0-3]):[0-5]\d|14:00))$/.test(v)
const REQUEST_KEYS=['kind','subjectId','requestNo','subjectRevision','status','version','lineId','ordered','quantity','requestedTotal','loadingInstructionId','shipmentId','note','requestedBy','requestedAtUtc','decidedBy','decidedAtUtc','decisionReason'] as const
// Actual native Rev.N is retained; the older general approval-request decoder's numeric-revision default is unchanged.
function requestView(v:unknown,date:(v:unknown)=>boolean):ApprovalRequest|null {
 if(!record(v)||!hasExactKeys(v,REQUEST_KEYS)||v.kind!=='QuoteExpiredConversion'||!guid(v.subjectId)||!int32(v.requestNo)||v.requestNo===0||!revision(v.subjectRevision)
  ||!salesRequestVersion(v.version)||v.status!=='Pending'&&v.status!=='Approved'&&v.status!=='Rejected'&&v.status!=='Stale'
  ||v.lineId!==null||v.ordered!==null||v.quantity!==null||v.requestedTotal!==null||v.loadingInstructionId!==null||v.shipmentId!==null
  ||!optionalText(v.note)||!text(v.requestedBy)||v.requestedBy.length>128||!utc(v.requestedAtUtc,date)
  ||!optionalText(v.decidedBy)||v.decidedBy!==null&&v.decidedBy.length>128||v.decidedAtUtc!==null&&!utc(v.decidedAtUtc,date)||!optionalText(v.decisionReason)
  ||(v.status==='Rejected'?(v.decidedBy===null||v.decidedAtUtc===null||v.decisionReason===null):(v.decidedBy!==null||v.decidedAtUtc!==null||v.decisionReason!==null)))return null
 return {kind:v.kind,subjectId:v.subjectId,requestNo:v.requestNo,subjectRevision:v.subjectRevision,status:v.status,version:v.version,
  lineId:null,ordered:null,quantity:null,requestedTotal:null,loadingInstructionId:null,shipmentId:null,note:v.note,requestedBy:v.requestedBy,requestedAtUtc:v.requestedAtUtc,
  decidedBy:v.decidedBy,decidedAtUtc:v.decidedAtUtc,decisionReason:v.decisionReason}
}
export function decodeWorkSalesQuoteConversionPreview(v:unknown,company:string,id:string,date:(v:unknown)=>boolean):WorkSalesQuoteConversionPreview|null {
 if(!guid(company)||!guid(id)||!record(v)||!hasExactKeys(v,['requestId','request','quote','approvalSupported','rejectionSupported'])||!guid(v.requestId)||!same(v.requestId,id))return null
 const request=requestView(v.request,date),quote=decodeQuoteDetail(v.quote,null)
 if(request===null||quote===null)return null
 const q=quote.quote
 if(!same(q.companyId,company)||!same(request.subjectId,q.id)||!guid(q.partyId)||!guid(q.companyAccountId)||!guid(q.currencyId)||!salesRequestVersion(q.version)||!revision(q.revision)||BigInt(request.subjectRevision.slice(4))>BigInt(q.revision.slice(4))
  ||!date(q.businessDate)||q.validUntil!==null&&!date(q.validUntil)||!utc(q.createdAtUtc,date)||quote.requestedDeliveryDate!==null&&!date(quote.requestedDeliveryDate)
  ||quote.lines.some(l=>!salesRequestVersion(l.productVersion)||!guid(l.lineId)||!guid(l.productId)||!guid(l.orderUnitId)||l.priceUnitId!==null&&!guid(l.priceUnitId)
   ||l.requestedDeliveryDate!==null&&!date(l.requestedDeliveryDate)||l.priceDisclosure!==quote.dueTermsDisclosure)
  ||quote.revisions.some(r=>!utc(r.revisedAtUtc,date)||r.validUntil!==null&&!date(r.validUntil))
  ||quote.expiryApprovals.some(a=>!revision(a.revision)||!utc(a.approvedAtUtc,date)||!date(a.validUntil)||!date(a.businessDay))||!date(quote.expiry.businessDay)
  ||quote.loss!==null&&!utc(quote.loss.lostAtUtc,date)||quote.conversion!==null&&!utc(quote.conversion.convertedAtUtc,date))return null
 const status=request.status==='Rejected'?'Rejected':quote.expiryApprovals.some(a=>a.revision===request.subjectRevision)?'Approved':q.state==='Open'&&request.subjectRevision===q.revision?'Pending':'Stale'
 if(request.status!==status||v.approvalSupported!==(status==='Pending')||v.rejectionSupported!==(status!=='Rejected'))return null
 return {requestId:v.requestId,request,quote,approvalSupported:v.approvalSupported,rejectionSupported:v.rejectionSupported}
}
export const isSalesQuoteConversionWorkCommand=(v:unknown):v is SalesQuoteConversionWorkCommand=>record(v)&&hasExactKeys(v,['expectedVersion','approve','reason','delegationId','salesQuoteConversion'])
 &&salesRequestIncrementable(v.expectedVersion)&&typeof v.approve==='boolean'&&validSalesRequestReason(v.approve,v.reason)&&(v.delegationId===null||guid(v.delegationId))
 &&record(v.salesQuoteConversion)&&hasExactKeys(v.salesQuoteConversion,['quoteId','requestNo','subjectRevision','requestVersion'])&&guid(v.salesQuoteConversion.quoteId)
 &&int32(v.salesQuoteConversion.requestNo)&&v.salesQuoteConversion.requestNo>0&&int32(v.salesQuoteConversion.subjectRevision)&&salesRequestVersion(v.salesQuoteConversion.requestVersion)
 &&(v.approve||v.expectedVersion===v.salesQuoteConversion.requestVersion)
export function salesQuoteConversionWorkCommand(h:WorkSalesQuoteConversionPreview,approve:boolean,reason:string,delegationId:string|null):SalesQuoteConversionWorkCommand|null {
 if(!(approve?h.approvalSupported:h.rejectionSupported))return null
 const body={expectedVersion:approve?h.quote.quote.version:h.request.version,approve,reason:approve?null:reason,delegationId,
  salesQuoteConversion:{quoteId:h.quote.quote.id,requestNo:h.request.requestNo,subjectRevision:parseInt(h.request.subjectRevision.slice(4),10),requestVersion:h.request.version}}
 return isSalesQuoteConversionWorkCommand(body)?body:null
}
export function validSalesQuoteConversionPreview(v:Record<string,unknown>,company:string,id:string,date:(v:unknown)=>boolean):boolean {
 const h=decodeWorkSalesQuoteConversionPreview(v.salesQuoteConversion,company,id,date)
 if(h===null||!hasExactKeys(v,['companyId','kind','itemId','documentLabel','sourceVersion','pending','approvalReasonRequired','references','rules','approvalSupported','rejectionSupported','salesQuoteConversion'])
  ||!same(v.companyId,company)||!same(v.itemId,id)||v.kind!==SALES_QUOTE_CONVERSION_WORK_KIND||v.documentLabel!==`${h.quote.quote.documentNo} #${h.request.requestNo}`
  ||v.sourceVersion!==h.quote.quote.version||v.pending!==(h.request.status==='Pending')||v.approvalReasonRequired!==false
  ||v.approvalSupported!==(h.approvalSupported&&salesRequestIncrementable(h.quote.quote.version))||v.rejectionSupported!==(h.rejectionSupported&&salesRequestIncrementable(h.request.version))
  ||!Array.isArray(v.rules)||v.rules.length!==1||!Array.isArray(v.references)||v.references.length!==1)return false
 const rule=v.rules[0],ref=v.references[0]
 return record(rule)&&hasExactKeys(rule,['ruleCode','threshold','actual','deviation','unit'])&&rule.ruleCode==='sales.quote-expired-conversion-requested'
  &&rule.threshold===null&&rule.actual===null&&rule.deviation===null&&rule.unit===null&&record(ref)&&hasExactKeys(ref,['role','label','recordId','link'])
  &&ref.role==='sales-quote'&&ref.label===h.quote.quote.documentNo&&same(ref.recordId,h.quote.quote.id)&&typeof ref.link==='string'&&ref.link.toLowerCase()===`/satis/teklifler/${h.quote.quote.id.toLowerCase()}`
}
