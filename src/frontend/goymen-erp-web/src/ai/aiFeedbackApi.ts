import { API_BASE, devHeaders } from '../finance/http'
import { readJson, type CommandOutcome } from '../settings/discountLimitApi'
import { replayHeadersAgree } from '../finance/wireShape'
import { aiFeedbackPath, decodeAiFeedbackView, decodeAiFeedbackReceipt, isAiFeedbackAnswerId, isAiFeedbackRequest, type AiFeedbackRequest, type AiFeedbackReceipt } from './aiFeedbackContract'
export const fetchAiFeedback = (company: string,id: string,signal?: AbortSignal) =>
 !isAiFeedbackAnswerId(id) ? Promise.resolve({type:'malformed'} as const) : readJson(aiFeedbackPath(id),company,raw => decodeAiFeedbackView(raw,id),signal)
export async function sendAiFeedback(company: string,id: string,body: AiFeedbackRequest,key: string,reportAmbiguousAuthBoundary?: (status: 403) => void): Promise<CommandOutcome<AiFeedbackReceipt>> {
 if (!isAiFeedbackAnswerId(id) || !isAiFeedbackRequest(body)) return {type:'unknown',reason:'unexpected'}
 let response: Response
 try { response=await fetch(API_BASE+aiFeedbackPath(id),{method:'POST',headers:{...devHeaders(),'X-Company-Id':company,'Content-Type':'application/json','Idempotency-Key':key},body:JSON.stringify(body)}) }
 catch { return {type:'unknown',reason:'network-error'} }
 const raw: unknown=await response.json().catch(()=>null)
 if(response.status===200){
  const receipt=decodeAiFeedbackReceipt(raw,id,body)
  return receipt!==null && replayHeadersAgree(response.headers,receipt.replayed,receipt.originalCorrelationId)
   ? {type:'applied',data:receipt,replayed:receipt.replayed} : {type:'unknown',reason:'unexpected'}
 }
 // Feedback can commit before its fresh response-release fence returns 403. Preserve the original attempt.
 if(response.status===403){reportAmbiguousAuthBoundary?.(403);return {type:'unknown',reason:'unexpected'}}
 const replayed=response.headers.get('Idempotency-Replayed'),original=response.headers.get('Idempotency-Original-Correlation-Id')
 if(replayed!==null && replayed!=='true' || replayed==='true' && (original===null || original.trim()==='') || replayed===null && original!==null) return {type:'unknown',reason:'unexpected'}
 if([400,401,403,404,409,415,422].includes(response.status)) return {type:'refused',status:response.status,code:null,companyId:null,groupCode:null,replayed:replayed==='true'}
 return {type:'unknown',reason:'server'}
}
