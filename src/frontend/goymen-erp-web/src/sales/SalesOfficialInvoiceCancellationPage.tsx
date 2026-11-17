import { useEffect, useId, useState } from 'react'
import { fetchOfficialCancellation, fetchOfficialCancellationCandidates, officialCancellationPath, sendOfficialCancellation,
  type OfficialCancellation, type OfficialCancellationCommand, type OfficialInvoiceKind, type CancellationCandidates } from './officialInvoiceCancellationApi'
import { OFFICIAL_CANCELLATION_TEXT as T } from './officialInvoiceCancellationText'
import { usePoolAttempt, poolNotice } from './poolAttempt'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import styles from './SalesPages.module.css'

/** Standalone purpose route: needs finalize only; no price or general invoice-read grant is added. */
export function SalesOfficialInvoiceCancellationPage() {
  const shell = useShellState(); const companyId = shell.activeContext?.companyId
  const binding = `${companyId}:${shell.sessionEpoch}:${shell.committedContextRevision}:${shell.requestFenceGeneration}:${shell.activeContext?.capabilityGeneration}:${shell.routeEntryGeneration}`
  return <section className={styles.section}><h2>{T.heading}</h2><p>{T.description}</p>
    {companyId == null ? <StateBlock state={{kind:'no-authorized-company'}} subject={T.subject} />
      : !hasCapability(shell,'sales.invoices.finalize') ? <p role="status">{T.noAuthority}</p>
        : <CandidateSelector key={binding} companyId={companyId} />}</section>
}
function CandidateSelector({companyId}:{readonly companyId:string}) {
  const boundary = useAuthBoundary(); const prefix = useId(); const [kind,setKind] = useState<OfficialInvoiceKind>('Normal')
  const [cursor,setCursor] = useState<string|null>(null); const [refresh,setRefresh] = useState(0); const [selected,setSelected] = useState('')
  const [outcome,setOutcome] = useState<ViewOutcome<CancellationCandidates>>({type:'loading'})
  useEffect(() => { const controller = new AbortController(); const security = boundary.capture();
    void fetchOfficialCancellationCandidates(kind,companyId,cursor,controller.signal).then(result => { if (controller.signal.aborted) return
      if (result.type === 'http') boundary.report(result.status,security); setOutcome(result) }); return () => controller.abort()
  },[kind,companyId,cursor,refresh,boundary])
  const page = outcome.type === 'ok-single' ? outcome.data : null; const item = page?.items.find(x => x.invoiceId === selected)
  const reload = (next: string|null = cursor) => {setOutcome({type:'loading'});setSelected('');setCursor(next);setRefresh(x=>x+1)}
  return <><label htmlFor={`${prefix}-kind`}>{T.kind}</label><select id={`${prefix}-kind`} value={kind} onChange={event=>{setKind(event.target.value as OfficialInvoiceKind);reload(null)}}>
    <option value="Normal">{T.normal}</option><option value="Subcontract">{T.subcontract}</option></select>
    <Button type="button" onClick={()=>reload(null)}>{T.refresh}</Button>
    {page === null ? <StateBlock state={classifyOutcome(outcome)} subject={T.subject} onRetry={()=>reload()} />
      : page.items.length === 0 ? <p role="status">{T.empty}</p> : <>
        <label htmlFor={`${prefix}-invoice`}>{T.invoice}</label><select id={`${prefix}-invoice`} value={selected} onChange={event=>setSelected(event.target.value)}>
          <option value="">{T.choose}</option>{page.items.map(row=><option key={row.invoiceId} value={row.invoiceId}>{row.documentNo} — {row.officialInvoiceNo}</option>)}</select>
        {page.nextCursor === null ? null : <Button type="button" onClick={()=>reload(page.nextCursor)}>{T.next}</Button>}
        {cursor === null ? null : <Button type="button" onClick={()=>reload(null)}>{T.first}</Button>}
      </>}
    {item == null ? null : <SalesOfficialInvoiceCancellationPanel key={`${kind}:${item.invoiceId}:${item.version}`}
      kind={kind} invoiceId={item.invoiceId} companyId={companyId} version={item.version} state={item.state} onRecorded={()=>reload(null)} />}
  </>
}
export function SalesOfficialInvoiceCancellationPanel({kind,invoiceId,companyId,version,state,onRecorded}:{readonly kind:OfficialInvoiceKind;readonly invoiceId:string;readonly companyId:string;readonly version:string;readonly state:string;readonly onRecorded?:()=>void}) {
  const shell = useShellState(); const boundary = useAuthBoundary(); const prefix = useId(); const [reason,setReason] = useState(''); const [release,setRelease] = useState(false)
  const [read,setRead] = useState<ViewOutcome<OfficialCancellation>>({type:'loading'}); const [refresh,setRefresh] = useState(0); const [specific,setSpecific] = useState('')
  const allowed = hasCapability(shell,'sales.invoices.finalize')
  useEffect(()=>{ if (!allowed || state !== 'OfficiallyCancelled') return; const controller = new AbortController(); const security = boundary.capture()
    void fetchOfficialCancellation(kind,invoiceId,companyId,controller.signal).then(result=>{if(controller.signal.aborted)return;if(result.type==='http')boundary.report(result.status,security);setRead(result)})
    return ()=>controller.abort()
  },[allowed,state,kind,invoiceId,companyId,refresh,boundary])
  const attempt = usePoolAttempt<OfficialCancellationCommand,OfficialCancellation>(`sales.official-cancellation:${kind}:${invoiceId}`,async snapshot=>{
    const security=boundary.capture(); const answer=await sendOfficialCancellation(snapshot.payload,{companyId:snapshot.companyId,idempotencyKey:snapshot.key})
    if(answer.type==='http'){boundary.report(answer.status,security);if(answer.status===503&&answer.problem?.code==='official_cancellation_evidence_unavailable')setSpecific(T.proofUnavailable)
      else if(answer.status===409&&answer.problem?.code==='official_cancellation_not_confirmed')setSpecific(T.notConfirmed)}
    return answer
  },next=>{if(next.phase==='completed'){setRead({type:'ok-single',data:next.result});setRelease(false);onRecorded?.()}}, {values:{reason}})
  const notice=poolNotice(attempt.state,{noun:T.subject,onRecord:true,blockedChange:attempt.blockedChange,completed:()=>T.recorded})
  const recorded=read.type==='ok-single'?read.data:null
  const held=attempt.frozen&&attempt.state.phase!=='idle'?attempt.state.snapshot.payload:null
  const shownReason=held?.action==='record'?held.reason:reason
  const shownRelease=held?.action==='finance-retry'?held.releaseActiveAllocations:release
  if(!allowed)return null
  if(state!=='Finalized'&&state!=='OfficiallyCancelled'&&recorded===null)return null
  function send(payload:OfficialCancellationCommand){if(attempt.frozen)return;if(attempt.state.phase==='completed'||attempt.state.phase==='failed')attempt.reset();setSpecific('');attempt.submit(payload.action==='record'?officialCancellationPath(kind,invoiceId):`/sales/official-cancellations/${payload.cancellationId}/finance-retry`,payload)}
  return <section className={styles.section} data-testid="sales-official-cancellation"><h3>{T.heading}</h3><p>{T.description}</p>
    {recorded!==null?<><p role="status">{T.recorded}</p><p role="status">{recorded.financeStatus==='Cancelled'?T.cancelled:recorded.financeStatus==='AllocationConfirmationRequired'?T.confirmation:T.pending}</p>
      {recorded.financeStatus==='Cancelled'?null:<fieldset disabled={attempt.frozen}>{(recorded.financeStatus==='AllocationConfirmationRequired'||held?.action==='finance-retry'&&held.releaseActiveAllocations)?<label><input type="checkbox" checked={shownRelease} onChange={event=>setRelease(event.target.checked)} />{T.release}</label>:null}
        <Button type="button" disabled={recorded.financeStatus==='AllocationConfirmationRequired'&&!release} onClick={()=>send({action:'finance-retry',cancellationId:recorded.cancellationId,expectedDeliveryVersion:recorded.deliveryVersion,releaseActiveAllocations:release})}>{T.retryFinance}</Button></fieldset>}
    </>:state==='OfficiallyCancelled'?<StateBlock state={classifyOutcome(read)} subject={T.subject} onRetry={()=>{setRead({type:'loading'});setRefresh(x=>x+1)}} />
      :<fieldset disabled={attempt.frozen}><label htmlFor={`${prefix}-reason`}>{T.reason}</label><textarea id={`${prefix}-reason`} value={shownReason} maxLength={1000} onChange={event=>setReason(event.target.value)} />
        <Button type="button" disabled={!reason.trim()} onClick={()=>send({action:'record',kind,invoiceId,expectedVersion:version,reason:reason.trim()})}>{T.record}</Button></fieldset>}
    {specific===''?null:<p role="alert">{specific}</p>}{notice===null?null:<p role={notice.role}>{notice.message}</p>}
    {notice?.offerRetry?<><Button type="button" onClick={attempt.retry}>{T.retrySame}</Button><Button type="button" onClick={attempt.leave}>{T.leave}</Button></>:null}
  </section>
}
