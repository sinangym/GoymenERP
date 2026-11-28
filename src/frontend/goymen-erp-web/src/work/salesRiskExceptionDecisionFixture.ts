import type {WorkDecisionPreview} from './workDecisionApi'
import {SALES_RISK_EXCEPTION_WORK_KIND as kind,type WorkSalesRiskExceptionPreview} from './workSalesRiskExceptionDecision'
import {salesRequestIncrementable} from './workSalesRequestDecision'
export const salesRiskExceptionId='f5050000-0000-4000-8000-000000000001',salesRiskOrderId='f5050000-0000-4000-8000-000000000002'
export const salesRiskMaker='f5050000-0000-4000-8000-000000000003',salesRiskChecker='f5050000-0000-4000-8000-000000000004'
export function salesRiskExceptionHuman(company:string,version='9007199254740993',state:WorkSalesRiskExceptionPreview['exception']['state']='Requested',closed=false):WorkSalesRiskExceptionPreview {
 const deciding=state==='Approved'||state==='Rejected',pending=state==='Requested'&&salesRequestIncrementable(version)
 return {companyId:company,exceptionId:salesRiskExceptionId,orderDocumentNo:'SENTETIK-SO-RISK-5',orderVersion:'9007199254740995',orderState:closed?'Closed':'Draft',orderApprovalState:'Approved',hasApprovedRevision:false,
  exception:{orderId:salesRiskOrderId,exceptionNo:3,loadingInstructionId:'f5050000-0000-4000-8000-000000000005',state,version,reason:'  SENTETIK özgün risk gerekçesi  ',requestedBy:salesRiskMaker,requestedAtUtc:'2026-11-28T09:00:00.0000001Z',decidedBy:deciding?salesRiskChecker:null,decidedAtUtc:deciding?'2026-11-28T10:00:00.0000001Z':null,rejectionReason:state==='Rejected'?'  SENTETIK ret  ':null,validOn:state==='Approved'?'2026-11-28':null,isValidToday:state==='Approved'},approvalSupported:pending&&!closed,rejectionSupported:pending}
}
export function salesRiskExceptionFixture(company:string,version='9007199254740993',state:WorkSalesRiskExceptionPreview['exception']['state']='Requested',closed=false):WorkDecisionPreview {
 const h=salesRiskExceptionHuman(company,version,state,closed)
 return {companyId:company,kind,itemId:h.exceptionId,documentLabel:`${h.orderDocumentNo} #${h.exception.exceptionNo}`,sourceVersion:h.exception.version,pending:state==='Requested',approvalReasonRequired:false,
  approvalSupported:h.approvalSupported,rejectionSupported:h.rejectionSupported,salesRiskException:h,rules:[{ruleCode:'sales.shipment-risk-exception',threshold:null,actual:null,deviation:null,unit:null}],references:[{role:'sales-order-draft',label:h.orderDocumentNo,recordId:h.exception.orderId,link:`/satis/siparisler/${h.exception.orderId}`}]}
}
