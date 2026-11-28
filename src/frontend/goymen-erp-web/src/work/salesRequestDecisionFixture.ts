import {requestDetail,REQUEST_ID} from '../sales/poolFixtures'
import {decodeRequestDetail,type RequestState} from '../sales/poolContract'
import {SALES_REQUEST_WORK_KIND as kind,type WorkSalesRequestPreview} from './workSalesRequestDecision'
export const salesRequestId=REQUEST_ID
export function salesRequestHuman(company:string,state:RequestState='Submitted',version=state==='Submitted'?'9007199254740993':'2'):WorkSalesRequestPreview {
 const d=decodeRequestDetail(requestDetail({companyId:company,state,version}),null)!
 return {detail:{...d,note:'SENTETIK full request note',lines:d.lines.map(l=>({...l,productVersion:'9007199254740993',quantity:'0001.2500',note:'SENTETIK line note'}))},approvalSupported:state==='Submitted',rejectionSupported:state!=='Rejected'}
}
export function salesRequestFixture(company:string,state:RequestState='Submitted',version?:string){
 const human=salesRequestHuman(company,state,version),h=human.detail.request
 return {companyId:company,kind,itemId:h.id,documentLabel:h.documentNo,sourceVersion:h.version,pending:state==='Submitted',approvalReasonRequired:false,
  rejectionSupported:human.rejectionSupported,approvalSupported:human.approvalSupported,
  references:[{role:'sales-request',label:h.documentNo,recordId:h.id,link:`/satis/talepler/${h.id}`}],
  rules:[{ruleCode:'sales.request-approval-required',threshold:null,actual:null,deviation:null,unit:null}],salesRequest:human}
}
