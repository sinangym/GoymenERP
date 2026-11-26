import { useState } from 'react'
import { Link } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, capabilityFailClosed, hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useScopedCommandAttempt, type AttemptStatus } from '../settings/useCommandAttempt'
import { WorkApprovalRulesSection } from '../settings/WorkApprovalRulesSection'
import { WorkApprovalRuleEditor } from './WorkApprovalRuleEditor'
import { delegationPersonLabel } from './delegationPersonLabel'
import { Button } from '../ui/Button'
import { ApprovalFollowUpPage } from './ApprovalFollowUpPage'
import { WORK_INBOX_KINDS, WORK_INBOX_CAPABILITIES } from './workInboxKinds'
import { inboxKindText } from './workInboxText'
import { fetchDelegations, fetchDelegationPeople, createDelegation, revokeDelegation, isCreateDelegation, isRevokeDelegation, type Delegation } from './workDelegationApi'
import { WORK_MANAGEMENT_TEXT as T } from './workManagementText'
import styles from './Work.module.css'
export const WORK_MANAGEMENT_ROUTE = '/calisma-yonetimi'
export const WORK_MANAGEMENT_CAPABILITIES = ['work.delegations.manage','work.approvals.follow-up','settings.work-approval-duration.manage',...WORK_INBOX_CAPABILITIES]
export function WorkManagementPage() {
  const shell=useShellState(); const context=contextKey(shell)
  return <Management key={`${context === null ? 'none' : contextKeyString(context)}:${shell.routeEntryGeneration}`} />
}
function Management() {
  const shell=useShellState(); const context=contextKey(shell)
  const company=shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const rules=hasCapability(shell,'settings.work-approval-duration.manage')
  const delegates=hasCapability(shell,'work.delegations.manage') || WORK_INBOX_CAPABILITIES.some(cap => hasCapability(shell,cap))
  const followUp=hasCapability(shell,'work.approvals.follow-up')
  const [tab,setTab]=useState(rules ? 'rules' : delegates ? 'delegations' : 'followUp')
  if (company === null || context === null || capabilityFailClosed(shell)) return <StateBlock state={{kind:'no-authorized-company'}} subject={T.title} />
  return <section className={styles.page}><h1>{T.title}</h1>
    <div role="group" aria-label={T.title}>{([[rules,'rules',T.rules],[delegates,'delegations',T.delegations],[followUp,'followUp',T.followUp]] as const)
      .filter(([allowed]) => allowed).map(([,id,label]) => <Button key={id} type="button" variant={tab===id?'primary':'secondary'} aria-pressed={tab===id} onClick={()=>setTab(id)}>{label}</Button>)}</div>
    {rules ? <div hidden={tab!=='rules'}><WorkApprovalRulesSection companyId={company} namespace={contextKeyString(context)} routeEntryGeneration={shell.routeEntryGeneration} /><WorkApprovalRuleEditor /><Link to="/ayarlar/onay-sureleri">{T.policy}</Link></div> : null}
    {delegates ? <div hidden={tab!=='delegations'}><Delegations company={company} namespace={contextKeyString(context)} /></div> : null}
    {followUp ? <div hidden={tab!=='followUp'}><ApprovalFollowUpPage /></div> : null}
  </section>
}
function Delegations({company,namespace}:{readonly company:string;readonly namespace:string}) {
  const shell=useShellState(); const boundary=useAuthBoundary(); const manage=hasCapability(shell,'work.delegations.manage')
  const [all,setAll]=useState(false); const [page,setPage]=useState(0); const effectiveAll=manage&&all
  const query=useQuery({queryKey:[namespace,shell.routeEntryGeneration,'delegations',effectiveAll,page],queryFn:async({signal})=>{
    const captured=boundary.capture(); const result=await fetchDelegations(company,effectiveAll,page,signal)
    if(result.type==='http') boundary.report(result.status,captured); return result
  }})
  const rows=query.data?.type==='ok-single'?query.data.data:null
  return <section><h2>{T.delegations}</h2>{manage?<label><input type="checkbox" checked={all} onChange={e=>{setAll(e.target.checked);setPage(0)}} />{T.all}</label>:<p>{T.own}</p>}
    <Create company={company} namespace={namespace} manage={manage} refresh={()=>void query.refetch()} />
    {rows===null?<StateBlock state={query.data===undefined?{kind:'initial-loading'}:classifyOutcome(query.data) as RemoteViewState<unknown>} subject={T.delegations} onRetry={()=>void query.refetch()}/>
      : rows.items.length===0?<p>{T.empty}</p>:<ul>{rows.items.map(row=><li key={row.id}><p>{inboxKindText(row.kind)} · <time dateTime={row.startsAtUtc}>{new Date(row.startsAtUtc).toLocaleString('tr-TR')}</time> – <time dateTime={row.endsAtUtc}>{new Date(row.endsAtUtc).toLocaleString('tr-TR')}</time></p>
        <p>{T.ownDelegator}: <span title={row.delegatorUserId}>{delegationPersonLabel(rows.people,row.delegatorUserId)}</span> · {T.person}: <span title={row.delegateUserId}>{delegationPersonLabel(rows.people,row.delegateUserId)}</span></p><p>{row.reason}</p>
        {(manage||row.delegatorUserId===shell.activeContext?.userId)?<Revoke row={row} refresh={()=>void query.refetch()}/>:row.revokedAtUtc!==null?<p>{T.inactive}</p>:null}</li>)}</ul>}
    <Button type="button" disabled={page===0} onClick={()=>setPage(p=>p-1)}>{T.previous}</Button><Button type="button" disabled={!rows?.hasMore} onClick={()=>setPage(p=>p+1)}>{T.more}</Button>
  </section>
}
function Create({company,namespace,manage,refresh}:{readonly company:string;readonly namespace:string;readonly manage:boolean;readonly refresh:()=>void}) {
  const shell=useShellState(); const boundary=useAuthBoundary()
  const kinds=WORK_INBOX_KINDS.filter(row=>manage||row.capability!==null&&hasCapability(shell,row.capability))
  const [kind,setKind]=useState(kinds[0]?.kind??''); const [search,setSearch]=useState(''); const [person,setPerson]=useState(''); const [delegator,setDelegator]=useState('')
  const [personName,setPersonName]=useState(''); const [delegatorName,setDelegatorName]=useState('')
  const [start,setStart]=useState(''); const [end,setEnd]=useState(''); const [reason,setReason]=useState('')
  const query=useQuery({queryKey:[namespace,shell.routeEntryGeneration,'delegation-people',kind,search],enabled:kind!==''&&search.trim().length>=2,
    queryFn:async({signal})=>{const captured=boundary.capture(); const result=await fetchDelegationPeople(company,kind,search,signal);if(result.type==='http')boundary.report(result.status,captured);return result}})
  const people=query.data?.type==='ok-single'?query.data.data.items:[]
  const choices=[...people]
  for(const [userId,displayName] of [[person,personName],[delegator,delegatorName]]) if(userId&&displayName&&!choices.some(row=>row.userId===userId)) choices.push({userId,displayName})
  const attempt=useScopedCommandAttempt('work-delegation:create',(payload,key)=>createDelegation(company,payload,key),isCreateDelegation)
  const frozen=attempt.status.kind==='busy'||attempt.hasPendingRetry
  const datesValid=Number.isFinite(Date.parse(start))&&Number.isFinite(Date.parse(end))&&Date.parse(end)>Date.parse(start)
  const submit=async()=>{const from=new Date(start),to=new Date(end);if(!Number.isFinite(from.getTime())||!Number.isFinite(to.getTime())||to<=from)return
    const answer=await attempt.run({kind,delegateUserId:person,delegatorUserId:manage?delegator||null:null,startsAtUtc:from.toISOString(),endsAtUtc:to.toISOString(),reason:reason.trim()});if(answer.type==='applied')refresh()}
  return <section><h3>{T.create}</h3><p>{T.picker}</p><fieldset disabled={frozen}>
    <label>{T.kind}<select value={kind} onChange={e=>{setKind(e.target.value);setPerson('');setDelegator('');setPersonName('');setDelegatorName('')}}>{kinds.map(row=><option key={row.kind} value={row.kind}>{inboxKindText(row.kind)}</option>)}</select></label>
    <label>{T.search}<input value={search} maxLength={100} onChange={e=>setSearch(e.target.value)} /></label>
    {search.trim().length>=2&&query.data?.type!=='ok-single'?<StateBlock state={query.data===undefined?{kind:'initial-loading'}:classifyOutcome(query.data) as RemoteViewState<unknown>} subject={T.person} onRetry={()=>void query.refetch()}/>:null}
    {query.data?.type==='ok-single'&&people.length===0?<p>{T.noPeople}</p>:null}
    {query.data?.type==='ok-single'&&query.data.data.hasMore?<p>{T.narrowSearch}</p>:null}
    <label>{T.person}<select value={person} onChange={e=>{setPerson(e.target.value);setPersonName(choices.find(row=>row.userId===e.target.value)?.displayName??'')}}><option value="">{T.choose}</option>{choices.map(row=><option key={row.userId} value={row.userId}>{row.displayName}</option>)}</select></label>
    {manage?<label>{T.delegator}<select value={delegator} onChange={e=>{setDelegator(e.target.value);setDelegatorName(choices.find(row=>row.userId===e.target.value)?.displayName??'')}}><option value="">{T.self}</option>{choices.map(row=><option key={row.userId} value={row.userId}>{row.displayName}</option>)}</select></label>:null}
    <label>{T.start}<input type="datetime-local" value={start} onChange={e=>setStart(e.target.value)}/></label><label>{T.end}<input type="datetime-local" value={end} onChange={e=>setEnd(e.target.value)}/></label>
    <label>{T.reason}<textarea maxLength={500} value={reason} onChange={e=>setReason(e.target.value)}/></label>
    <Button type="button" disabled={!person||!reason.trim()||!datesValid} onClick={()=>void submit()}>{T.create}</Button></fieldset><CommandState status={attempt.status} retry={()=>void attempt.retry()}/></section>
}
function Revoke({row,refresh}:{readonly row:Delegation;readonly refresh:()=>void}) {
  const [reason,setReason]=useState(''); const attempt=useScopedCommandAttempt(`work-delegation:revoke:${row.id}`,(payload,key)=>revokeDelegation(row.companyId,row.id,payload,key),isRevokeDelegation)
  const frozen=attempt.status.kind==='busy'||attempt.hasPendingRetry
  if (row.revokedAtUtc!==null&&!attempt.hasPendingRetry) return <p>{T.inactive}</p>
  return <><fieldset disabled={frozen}><label>{T.reason}<input value={reason} maxLength={500} onChange={e=>setReason(e.target.value)}/></label><Button type="button" disabled={!reason.trim()} onClick={()=>void attempt.run({expectedVersion:row.version,reason:reason.trim()}).then(answer=>{if(answer.type==='applied')refresh()})}>{T.revoke}</Button></fieldset><CommandState status={attempt.status} retry={()=>void attempt.retry()}/></>
}
function CommandState({status,retry}:{readonly status:AttemptStatus<Delegation>;readonly retry:()=>void}) {
  return status.kind==='unknown'?<div role="alert"><p>{T.unknown}</p><Button type="button" onClick={retry}>{T.retry}</Button></div>
    :status.kind==='refused'?<p role="alert">{T.refused} ({status.status})</p>:status.kind==='applied'?<p role="status">{T.done}</p>:null
}
