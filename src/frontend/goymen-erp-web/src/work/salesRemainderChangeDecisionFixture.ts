import type {WorkDecisionPreview} from './workDecisionApi'
import {SALES_REMAINDER_CHANGE_WORK_KIND as kind,type WorkSalesRemainderChangePreview} from './workSalesRemainderChangeDecision'
import {salesRequestIncrementable} from './workSalesRequestDecision'
export const salesRemainderChangeId='f6060000-0000-4000-8000-000000000001',salesRemainderLineId='f6060000-0000-4000-8000-000000000002',salesRemainderMaker='f6060000-0000-4000-8000-000000000003'
export function salesRemainderChangeHuman(version='9007199254740993',state:'Pending'|'Approved'|'Rejected'='Pending'):WorkSalesRemainderChangePreview {
 return {draftId:salesRemainderChangeId,documentNo:'SENTETIK-SO-6',version,revision:0,state:'Draft',approvalState:'Approved',
 change:{changeNo:3,revision:'Rev.2',lineId:salesRemainderLineId,lineNo:2147483647,kind:'Revise',state,orderedQuantity:'2.0000',requestedQuantity:'0001.2500',reason:'SENTETIK kalan gerekçesi',requestedBy:salesRemainderMaker,requestedAtUtc:'2026-11-28T09:00:00.0000001Z',loadedAtRequest:'1.00',shippedAtRequest:'0.50',deliveredAtRequest:'0.25',
 decidedBy:state==='Pending'?null:salesRemainderMaker,decidedAtUtc:state==='Pending'?null:'2026-11-28T10:00:00.0000001Z',decisionReason:state==='Rejected'?'  SENTETIK ret  ':null,resultQuantity:state==='Approved'?'1.2500':null,loadedAtDecision:state==='Approved'?'1.00':null},
 line:{id:salesRemainderLineId,lineNo:2147483647,productId:'f6060000-0000-4000-8000-000000000004',productCode:'SNT-R',productName:'SENTETIK Kalan Ürünü',orderUnitId:'f6060000-0000-4000-8000-000000000005',orderUnitCode:'KG',currentQuantity:'2.0000',remainder:'Open'},approvalSupported:state==='Pending',rejectionSupported:state==='Pending'}
}
export function salesRemainderChangeFixture(company:string,version='9007199254740993',state:'Pending'|'Approved'|'Rejected'='Pending'):WorkDecisionPreview {
 const h=salesRemainderChangeHuman(version,state),link=`/satis/siparisler/${h.draftId}`
 return {companyId:company,kind,itemId:h.draftId,documentLabel:`${h.documentNo} #${h.change.changeNo}`,sourceVersion:h.version,pending:state==='Pending',approvalReasonRequired:false,
 approvalSupported:h.approvalSupported&&salesRequestIncrementable(version),rejectionSupported:h.rejectionSupported&&salesRequestIncrementable(version),salesRemainderChange:h,
 rules:[{ruleCode:'sales.remainder-change-requested',threshold:null,actual:null,deviation:null,unit:null}],references:[{role:'sales-order-draft',label:h.documentNo,recordId:h.draftId,link},{role:'sales-order-line',label:'Sipariş satırı',recordId:h.change.lineId,link}]}
}
