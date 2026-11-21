import { useEffect, useId, useState } from 'react'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { usePoolAttempt, poolNotice } from '../sales/poolAttempt'
import { Button } from '../ui/Button'
import { SUBCONTRACT_RELEASE_TEXT as T } from './subcontractAllocationReleaseText'
import { fetchSubcontractReleaseCandidates, sendSubcontractRelease, subcontractReleasePath, SUBCONTRACT_RELEASE_CAPABILITY as CAP,
  type SubcontractReleaseCandidates, type SubcontractReleaseCommand, type SubcontractRelease } from './subcontractAllocationReleaseApi'

export function SubcontractAllocationReleasePage() {
  const shell=useShellState();const companyId=shell.activeContext?.companyId
  const binding=`${companyId}:${shell.sessionEpoch}:${shell.committedContextRevision}:${shell.requestFenceGeneration}:${shell.activeContext?.capabilityGeneration}:${shell.routeEntryGeneration}`
  return <section><h1>{T.heading}</h1><p>{T.explanation}</p>{companyId==null?<StateBlock state={{kind:'no-authorized-company'}} subject={T.subject}/>
    :!hasCapability(shell,CAP)?<p role="status">{T.forbidden}</p>:<ReleaseWork key={binding} companyId={companyId}/>}</section>
}
function ReleaseWork({companyId}:{readonly companyId:string}) {
  const boundary=useAuthBoundary();const id=useId();const [cursor,setCursor]=useState<string|null>(null);const [refresh,setRefresh]=useState(0)
  const [selected,setSelected]=useState('');const [reason,setReason]=useState('')
  const [outcome,setOutcome]=useState<ViewOutcome<SubcontractReleaseCandidates>>({type:'loading'})
  useEffect(()=>{const controller=new AbortController();const security=boundary.capture()
    void fetchSubcontractReleaseCandidates(companyId,cursor,controller.signal).then(value=>{if(controller.signal.aborted)return;if(value.type==='http')boundary.report(value.status,security);setOutcome(value)})
    return()=>controller.abort()
  },[companyId,cursor,refresh,boundary])
  const attempt=usePoolAttempt<SubcontractReleaseCommand,SubcontractRelease>('inventory.subcontract-release',async snapshot=>{
    const security=boundary.capture();const result=await sendSubcontractRelease(snapshot.payload,{companyId:snapshot.companyId,idempotencyKey:snapshot.key})
    if(result.type==='http')boundary.report(result.status,security);return result
  },undefined,{values:{reason}})
  const page=outcome.type==='ok-single'?outcome.data:null;const item=page?.items.find(x=>x.allocation.allocationId===selected)
  const held=attempt.frozen&&attempt.state.phase!=='idle'?attempt.state.snapshot.payload:null
  const notice=poolNotice(attempt.state,{noun:T.subject,onRecord:true,blockedChange:attempt.blockedChange,completed:()=>T.saved})
  const reload=(next:string|null=null)=>{setOutcome({type:'loading'});setSelected('');setCursor(next);setRefresh(x=>x+1)}
  function submit(){if(attempt.frozen||item===undefined||reason.trim()==='')return;if(attempt.state.phase==='completed'||attempt.state.phase==='failed')attempt.reset()
    const payload={allocationId:item.allocation.allocationId,expectedAllocationVersion:item.allocation.version,reason:reason.trim()};attempt.submit(subcontractReleasePath(payload.allocationId),payload)}
  return <><Button type="button" disabled={attempt.frozen} onClick={()=>reload()}>{T.refresh}</Button>
    {page===null?<StateBlock state={classifyOutcome(outcome)} subject={T.subject} onRetry={()=>reload(cursor)}/>:page.items.length===0?<p role="status">{T.empty}</p>:<>
      <label htmlFor={`${id}-select`}>{T.allocation}</label><select id={`${id}-select`} value={held?.allocationId??selected} disabled={attempt.frozen} onChange={e=>setSelected(e.target.value)}>
        <option value="">{T.choose}</option>{page.items.map(row=><option key={row.allocation.allocationId} value={row.allocation.allocationId}>{row.labelCode} · {row.allocation.remainingKg} kg · {row.allocation.createdAtUtc}</option>)}</select>
      <Button type="button" disabled={attempt.frozen||cursor===null} onClick={()=>reload()}>{T.first}</Button>{page.nextCursor===null?null:<Button type="button" disabled={attempt.frozen} onClick={()=>reload(page.nextCursor)}>{T.next}</Button>}
    </>}
    {(item!==undefined||held!==null)&&<fieldset disabled={attempt.frozen}><legend>{T.subject}</legend>{item===undefined?null:<p>{T.remaining}: {item.allocation.remainingKg}</p>}
      <label htmlFor={`${id}-reason`}>{T.reason}</label><textarea id={`${id}-reason`} maxLength={1000} value={held?.reason??reason} onChange={e=>setReason(e.target.value)}/>
      <Button type="button" disabled={item===undefined||reason.trim()===''} onClick={submit}>{T.release}</Button></fieldset>}
    {notice===null?null:<p role={notice.role}>{notice.message}</p>}
    {notice?.offerRetry&&<><Button type="button" onClick={attempt.retry}>{T.retry}</Button><Button type="button" onClick={attempt.leave}>{T.leave}</Button></>}
    {attempt.state.phase==='completed'?<dl><dt>{T.released}</dt><dd>{attempt.state.result.releasedKg}</dd><dt>{T.consumed}</dt><dd>{attempt.state.result.previouslyConsumedKg}</dd></dl>:null}
  </>
}
