import type {WorkDecisionPreview} from './workDecisionApi'
import {SALES_OVER_DELIVERY_REQUEST_WORK_KIND as kind,type WorkSalesOverDeliveryRequestPreview} from './workSalesOverDeliveryRequestDecision'
import {salesRequestIncrementable} from './workSalesRequestDecision'
export const salesOverDeliveryRequestId='f4040000-0000-4000-8000-000000000001',salesOverDeliveryOrderId='f4040000-0000-4000-8000-000000000002',salesOverDeliveryLineId='f4040000-0000-4000-8000-000000000003'
export const salesOverDeliveryMaker='f4040000-0000-4000-8000-000000000004',salesOverDeliveryDecider='f4040000-0000-4000-8000-000000000005'
export function salesOverDeliveryRequestHuman(version='9007199254740993',status:'Pending'|'Approved'|'Rejected'='Pending'):WorkSalesOverDeliveryRequestPreview {
 return {requestId:salesOverDeliveryRequestId,request:{kind:'OverDelivery',subjectId:salesOverDeliveryOrderId,requestNo:3,subjectRevision:'Rev.2',status,version,lineId:salesOverDeliveryLineId,
  ordered:'1.25',quantity:'0.03',requestedTotal:'1.28',loadingInstructionId:'f4040000-0000-4000-8000-000000000006',shipmentId:'f4040000-0000-4000-8000-000000000007',note:null,
  requestedBy:salesOverDeliveryMaker,requestedAtUtc:'2026-11-28T09:00:00.0000001Z',decidedBy:status==='Pending'?null:salesOverDeliveryDecider,decidedAtUtc:status==='Pending'?null:'2026-11-28T10:00:00.0000001Z',decisionReason:status==='Rejected'?'  SENTETIK ret  ':null},
  order:{id:salesOverDeliveryOrderId,documentNo:'SENTETIK-SO-4',version:'9007199254740995',revision:0,state:'Draft',approvalState:'Approved',partyId:'f4040000-0000-4000-8000-000000000008',companyAccountId:'f4040000-0000-4000-8000-000000000009',currencyId:'f4040000-0000-4000-8000-000000000010',businessDate:'2026-11-28',
   line:{id:salesOverDeliveryLineId,lineNo:2147483647,productId:'f4040000-0000-4000-8000-000000000011',productCode:'SNT-OD',productName:'SENTETIK Fazla Teslim Ürünü',orderUnitId:'f4040000-0000-4000-8000-000000000012',orderUnitCode:'KG',currentQuantity:'0001.2500'}},approvalSupported:status==='Pending',rejectionSupported:status==='Pending'}
}
export function salesOverDeliveryRequestFixture(company:string,version='9007199254740993',status:'Pending'|'Approved'|'Rejected'='Pending'):WorkDecisionPreview {
 const h=salesOverDeliveryRequestHuman(version,status)
 return {companyId:company,kind,itemId:h.requestId,documentLabel:`${h.order.documentNo} #${h.request.requestNo}`,sourceVersion:h.request.version,pending:status==='Pending',approvalReasonRequired:false,
  approvalSupported:h.approvalSupported&&salesRequestIncrementable(version),rejectionSupported:h.rejectionSupported&&salesRequestIncrementable(version),salesOverDeliveryRequest:h,
  rules:[{ruleCode:'sales.over-delivery-requested',threshold:'1.25',actual:'1.28',deviation:'0.03',unit:null}],references:[{role:'sales-order',label:h.order.documentNo,recordId:h.order.id,link:`/satis/siparisler/${h.order.id}`}]}
}
