import { AI_PANEL_CAPABILITY } from './aiCapabilities'
import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { contextKey, contextKeyString, capabilityFailClosed, hasCapability } from '../shell/shellState'
import { useShellState } from '../shell/shellContext'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useScopedCommandAttempt } from '../settings/useCommandAttempt'
import { fetchAiFeedback,sendAiFeedback } from './aiFeedbackApi'
import { AI_FEEDBACK_KINDS,AI_FEEDBACK_REASONS,isAiFeedbackRequest,isAiFeedbackAnswerId,type AiFeedbackKind,type AiFeedbackReason,type AiFeedbackRequest,type AiFeedbackReceipt } from './aiFeedbackContract'
import { AI_FEEDBACK_TEXT as T } from './aiFeedbackText'
import styles from './AiPanel.module.css'
export function AiFeedbackPanel({companyId,answerId}:{readonly companyId:string;readonly answerId:string}) {
 const shell=useShellState(),context=contextKey(shell)
 if(context===null || shell.activeContext?.scopeMode!=='single' || shell.activeContext.companyId!==companyId || capabilityFailClosed(shell) || !hasCapability(shell, AI_PANEL_CAPABILITY) || !isAiFeedbackAnswerId(answerId)) return null
 return <Feedback key={`${contextKeyString(context)}:${shell.routeEntryGeneration}:${answerId}`} company={companyId} answerId={answerId} namespace={contextKeyString(context)}/>
}
function Feedback({company,answerId,namespace}:{readonly company:string;readonly answerId:string;readonly namespace:string}) {
 const boundary=useAuthBoundary()
 const [selected,setSelected]=useState<AiFeedbackKind|''>(''),[reason,setReason]=useState<AiFeedbackReason|''>('')
 const attempt=useScopedCommandAttempt<AiFeedbackRequest,AiFeedbackReceipt>(`ai-answer-feedback:${answerId}`,(body,key)=>sendAiFeedback(company,answerId,body,key),isAiFeedbackRequest)
 const read=useQuery({queryKey:[namespace,'ai-answer-feedback',answerId],retry:false,staleTime:0,gcTime:0,queryFn:async({signal})=>{
  const captured=boundary.capture(),result=await fetchAiFeedback(company,answerId,signal)
  if(result.type==='http')boundary.report(result.status,captured)
  return result
 }})
 const current=read.data?.type==='ok-single'?read.data.data:null
 const frozen=attempt.status.kind==='busy'||attempt.hasPendingRetry
 const shownKind=frozen?attempt.pendingPayload?.kind??'':selected,shownReason=frozen?attempt.pendingPayload?.reasonCode??'':reason
 const body=current===null||selected===''?null:{expectedVersion:current.version,kind:selected,reasonCode:reason===''?null:reason}
 const valid=body!==null&&isAiFeedbackRequest(body)&&(selected!=='Reverted'||current?.kind==='Accepted')
 const submit=async()=>{if(frozen||!valid||body===null)return;const result=await attempt.run(body);if(result.type==='applied')void read.refetch()}
 const retry=async()=>{const result=await attempt.retry();if(result.type==='applied')void read.refetch()}
 return <section aria-label={T.heading}><h4>{T.heading}</h4><p>{T.intro}</p>
  {current===null?<StateBlock state={read.data===undefined?{kind:'initial-loading'}:classifyOutcome(read.data)} subject={T.heading} onRetry={()=>void read.refetch()}/>:<>
   <p>{T.current}: {current.kind===null?T.none:T.kinds[current.kind]} · {T.version}: {current.version}</p>
   <fieldset disabled={frozen}><label>{T.kind}<select value={shownKind} onChange={event=>setSelected(event.target.value as AiFeedbackKind|'')}><option value="">{T.choose}</option>
    {AI_FEEDBACK_KINDS.map(kind=><option key={kind} value={kind} disabled={kind==='Reverted'&&current.kind!=='Accepted'}>{T.kinds[kind]}</option>)}</select></label>
    <label>{T.reason}<select value={shownReason} onChange={event=>setReason(event.target.value as AiFeedbackReason|'')}><option value="">{T.choose}</option>
     {AI_FEEDBACK_REASONS.map(reason=><option key={reason} value={reason}>{T.reasons[reason]}</option>)}</select></label>
    <p>{T.optionalReason}</p><p>{T.revertHelp}</p><button className={styles.submitButton} type="button" disabled={!valid} onClick={()=>void submit()}>{T.save}</button>
   </fieldset></>}
  {attempt.status.kind==='unknown'?<div role="alert"><p>{T.unknown}</p><p>{T.version}: {attempt.pendingPayload?.expectedVersion}</p>
   {attempt.pendingPayload?<p>{T.kind}: {T.kinds[attempt.pendingPayload.kind]} · {T.reason}: {attempt.pendingPayload.reasonCode===null?T.choose:T.reasons[attempt.pendingPayload.reasonCode]}</p>:null}<button type="button" onClick={()=>void retry()}>{T.retry}</button></div>:null}
  {attempt.status.kind==='refused'?<div role="alert"><p>{T.refused} ({attempt.status.status})</p>{attempt.status.status===409?<button type="button" onClick={()=>void read.refetch()}>{T.reload}</button>:null}</div>:null}
  {attempt.status.kind==='applied'?<p role="status">{T.done} {T.receipt}: {attempt.status.data.version}</p>:null}
 </section>
}
