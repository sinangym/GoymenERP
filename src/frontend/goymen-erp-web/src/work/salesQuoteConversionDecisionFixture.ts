import {quoteDetail,QUOTE_ID} from '../sales/poolFixtures'
import {decodeQuoteDetail} from '../sales/poolContract'
import type {ApprovalRequestStatus} from '../sales/approvalRequests'
import {SALES_QUOTE_CONVERSION_WORK_KIND as kind,type WorkSalesQuoteConversionPreview} from './workSalesQuoteConversionDecision'
import {salesRequestIncrementable} from './workSalesRequestDecision'
export const salesQuoteConversionId='5a180000-0000-4000-8000-000000000002'
export function salesQuoteConversionHuman(company:string,status:ApprovalRequestStatus='Pending',quoteVersion='9007199254740993',requestVersion=status==='Rejected'?'2':'1',visible=false):WorkSalesQuoteConversionPreview {
 const day='2026-11-28',at='2026-11-28T09:00:00.0000001Z'
 const native=decodeQuoteDetail(quoteDetail({companyId:company,version:quoteVersion,state:status==='Stale'?'Lost':'Open'},
  {expiry:{businessDay:day,isExpired:true,isApproved:status==='Approved'},expiryApprovals:status==='Approved'?[{revision:'Rev.0',validUntil:'2026-11-22',businessDay:day,approvedBy:'SENTETIK checker',approvedAtUtc:at,note:null}]:[]}),null)!
 const quote={...native,note:'SENTETIK full quote note',dueTermsDisclosure:visible?'Visible' as const:'Withheld' as const,
  lines:native.lines.map(l=>({...l,productVersion:'9007199254740993',quantity:'0001.2500',note:'SENTETIK full quote line',priceDisclosure:visible?'Visible' as const:'Withheld' as const,unitPrice:visible?'00042.5000':null,priceUnitId:visible?l.orderUnitId:null,discountPercent:visible?'2.500':null}))}
 return {requestId:salesQuoteConversionId,request:{kind:'QuoteExpiredConversion',subjectId:QUOTE_ID,requestNo:3,subjectRevision:'Rev.0',status,version:requestVersion,
 lineId:null,ordered:null,quantity:null,requestedTotal:null,loadingInstructionId:null,shipmentId:null,note:'SENTETIK full selected request note',requestedBy:'SENTETIK maker',requestedAtUtc:at,
 decidedBy:status==='Rejected'?'SENTETIK checker':null,decidedAtUtc:status==='Rejected'?at:null,decisionReason:status==='Rejected'?'  SENTETIK raw rejection  ':null},quote,approvalSupported:status==='Pending',rejectionSupported:status!=='Rejected'}
}
export function salesQuoteConversionFixture(company:string,status:ApprovalRequestStatus='Pending',quoteVersion?:string,requestVersion?:string,visible=false){
 const h=salesQuoteConversionHuman(company,status,quoteVersion,requestVersion,visible),q=h.quote.quote
 return {companyId:company,kind,itemId:h.requestId,documentLabel:`${q.documentNo} #${h.request.requestNo}`,sourceVersion:q.version,pending:status==='Pending',approvalReasonRequired:false,
 approvalSupported:h.approvalSupported&&salesRequestIncrementable(q.version),rejectionSupported:h.rejectionSupported&&salesRequestIncrementable(h.request.version),
 references:[{role:'sales-quote',label:q.documentNo,recordId:q.id,link:`/satis/teklifler/${q.id}`}],rules:[{ruleCode:'sales.quote-expired-conversion-requested',threshold:null,actual:null,deviation:null,unit:null}],salesQuoteConversion:h}
}
