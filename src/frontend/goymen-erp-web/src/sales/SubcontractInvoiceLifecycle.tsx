import { SalesOfficialInvoiceCancellationPanel } from './SalesOfficialInvoiceCancellationPage'
import { useEffect, useId, useState } from 'react'
import { commandSubcontractInvoice, fetchSubcontractInvoice, fetchSubcontractInvoiceList, type SubcontractAction, type SubcontractActionPayload } from './SubcontractInvoiceApi'
import { subcontractServiceInvoicePath, type SubcontractInvoiceDetail, type SubcontractInvoiceList } from './SubcontractInvoiceContract'
import { SUBCONTRACT_LIFECYCLE_TEXT as L } from './SubcontractLifecycleText'
import { INVOICE_FINALIZATION_TEXT as T, INVOICE_STATE_TEXT } from './invoiceText'
import { usePoolAttempt, poolNotice } from './poolAttempt'
import { isReferenceId } from './wirePrimitives'
import { formatBusinessDate } from './format'
import { InvoiceDecisionFacts } from './SalesInvoiceDraftActions'
import { LIMITS } from './contract'
import { useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import styles from './SalesPool.module.css'

export function SubcontractInvoiceLifecycle({ salesOrderId, initialInvoiceId = '' }: {
  readonly salesOrderId: string
  readonly initialInvoiceId?: string
}) {
  const shell = useShellState()
  const companyId = shell.activeContext?.companyId
  return <section className={styles.section}>
    <h3>{L.heading}</h3>
    {companyId == null ? <StateBlock state={{ kind: 'no-authorized-company' }} subject={L.subject} />
      : <InvoiceSelector key={`${companyId}:${shell.sessionEpoch}:${shell.committedContextRevision}:${shell.requestFenceGeneration}:${shell.activeContext?.capabilityGeneration}:${shell.routeEntryGeneration}:${salesOrderId}`}
          companyId={companyId} salesOrderId={salesOrderId} initialInvoiceId={initialInvoiceId} />}
  </section>
}

function InvoiceSelector({ companyId, salesOrderId, initialInvoiceId }: {
  readonly companyId: string; readonly salesOrderId: string; readonly initialInvoiceId: string
}) {
  const boundary = useAuthBoundary()
  const inputId = useId()
  const [selected, setSelected] = useState('')
  const [opened, setOpened] = useState('')
  const [cursor, setCursor] = useState<string | null>(null)
  const [refresh, setRefresh] = useState(0)
  const [outcome, setOutcome] = useState<ViewOutcome<SubcontractInvoiceList>>({ type: 'loading' })
  useEffect(() => {
    const controller = new AbortController()
    const security = boundary.capture()
    void fetchSubcontractInvoiceList(salesOrderId, companyId, cursor, controller.signal).then(result => {
      if (controller.signal.aborted) return
      if (result.type === 'http') boundary.report(result.status, security)
      setOutcome(result)
    })
    return () => controller.abort()
  }, [salesOrderId, companyId, cursor, refresh, boundary])
  const page = outcome.type === 'ok-single' ? outcome.data : null
  const choice = page?.items.find(item => item.id === selected) ?? page?.items.find(item => item.id === initialInvoiceId) ?? page?.items[0]
  const load = (next: string | null) => { setOutcome({ type: 'loading' }); setSelected(''); setCursor(next); setRefresh(value => value + 1) }
  return <>
    <Button type="button" onClick={() => load(null)}>{L.refresh}</Button>
    {page === null ? <StateBlock state={classifyOutcome(outcome)} subject={L.subject} onRetry={() => load(cursor)} />
      : page.items.length === 0 ? <p role="status">{L.emptyList}</p> : <>
        <label htmlFor={inputId}>{L.invoice}</label>
        <select id={inputId} value={choice?.id ?? ''} onChange={event => setSelected(event.target.value)} className={styles.control}>
          {page.items.map(item => <option key={item.id} value={item.id}>{item.documentNo} — {INVOICE_STATE_TEXT[item.state]} — {formatBusinessDate(item.businessDate)}</option>)}
        </select>
        <Button type="button" disabled={choice === undefined} onClick={() => { if (choice) setOpened(choice.id) }}>{L.open}</Button>
        {page.nextCursor === null ? null : <Button type="button" onClick={() => load(page.nextCursor)}>{L.nextPage}</Button>}
        {cursor === null ? null : <Button type="button" onClick={() => load(null)}>{L.firstPage}</Button>}
      </>}
    {opened === '' ? <p role="status">{L.empty}</p>
      : <LifecyclePanel key={opened} invoiceId={opened} salesOrderId={salesOrderId} companyId={companyId} />}
  </>
}

function LifecyclePanel({ invoiceId, salesOrderId, companyId }: {
  readonly invoiceId: string; readonly salesOrderId: string; readonly companyId: string
}) {
  const shell = useShellState()
  const boundary = useAuthBoundary()
  const prefix = useId()
  const [outcome, setOutcome] = useState<ViewOutcome<SubcontractInvoiceDetail>>({ type: 'loading' })
  const [refresh, setRefresh] = useState(0)
  const [reason, setReason] = useState('')
  const [officialNo, setOfficialNo] = useState('')
  const [ettn, setEttn] = useState('')
  const [approveDifference, setApproveDifference] = useState(false)
  useEffect(() => {
    const controller = new AbortController()
    const security = boundary.capture()
    void fetchSubcontractInvoice(invoiceId, companyId, controller.signal).then(result => {
      if (controller.signal.aborted) return
      if (result.type === 'http') boundary.report(result.status, security)
      setOutcome(result.type === 'ok-single' && result.data.salesOrderId.toLowerCase() !== salesOrderId.toLowerCase()
        ? { type: 'http', status: 404 } : result)
    })
    return () => controller.abort()
  }, [invoiceId, companyId, salesOrderId, refresh, boundary])
  const reload = () => { setOutcome({ type: 'loading' }); setRefresh(value => value + 1) }
  const attempt = usePoolAttempt<SubcontractActionPayload, SubcontractInvoiceDetail>(`sales.subcontract-lifecycle:${invoiceId}`,
    async snapshot => {
      const security = boundary.capture()
      const result = await commandSubcontractInvoice(snapshot.payload, { idempotencyKey: snapshot.key, companyId: snapshot.companyId })
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, security)
      return result
    }, next => {
      if (next.phase === 'completed') setOutcome({ type: 'ok-single', data: next.result })
      if (next.phase === 'failed' && next.failure === 'conflict') reload()
    }, { values: null })
  const notice = poolNotice(attempt.state, { noun: L.subject, onRecord: true, blockedChange: attempt.blockedChange })
  const detail = outcome.type === 'ok-single' ? outcome.data : null
  const can = (name: string) => hasCapability(shell, `sales.invoices.${name}`)
  const offered: SubcontractAction[] = detail?.state === 'Draft' ? ['submit', 'cancel']
    : detail?.state === 'PendingApproval' ? ['approve', 'reject', 'cancel']
      : detail?.state === 'Approved' ? detail.lifecycle?.match == null ? ['match'] : ['finalize']
        : detail?.state === 'Finalized' && detail.lifecycle?.finalization?.financeStatus === 'waiting' ? ['resend-finance'] : []
  const permission = (action: SubcontractAction) => action === 'approve' || action === 'reject' ? 'approve'
    : action === 'match' ? 'match' : action === 'finalize' || action === 'resend-finance' ? 'finalize' : 'write'
  function send(action: SubcontractAction) {
    if (detail === null || attempt.frozen || !can(permission(action))) return
    if (attempt.state.phase === 'completed' || attempt.state.phase === 'failed') attempt.reset()
    const payload: SubcontractActionPayload = {
      invoiceId, action, expectedVersion: detail.version,
      ...(action === 'match' ? { officialInvoiceNo: officialNo.trim(), ettn: ettn.trim(), approveDifference,
        ...(approveDifference ? { differenceReason: reason.trim() } : {}) } : {}),
      ...(action === 'reject' || action === 'cancel' ? { reason: reason.trim() } : {}),
    }
    attempt.submit(`${subcontractServiceInvoicePath(invoiceId)}/${action}`, payload)
  }
  return <div data-testid="sales-subcontract-lifecycle">
    <Button type="button" disabled={attempt.frozen} onClick={reload}>{L.refresh}</Button>
    {detail === null ? <StateBlock state={classifyOutcome(outcome)} subject={L.subject} onRetry={reload} /> : <>
      <h4>{detail.documentNo} — {INVOICE_STATE_TEXT[detail.state]}</h4>
      <InvoiceDecisionFacts decision={detail.lifecycle?.decision ?? null} />
      <SalesOfficialInvoiceCancellationPanel kind="Subcontract" invoiceId={invoiceId} companyId={companyId} version={detail.version} state={detail.state} onRecorded={reload} />
      {detail.lifecycle?.instalments.length ? <table><caption>{L.due}</caption><thead><tr><th scope="col">{L.due}</th><th scope="col">{L.amount}</th></tr></thead>
        <tbody>{detail.lifecycle.instalments.map(item => <tr key={item.sequenceNo}><td>{formatBusinessDate(item.dueDate)}</td><td>{item.amount}</td></tr>)}</tbody></table>
        : <p role="status">{L.noPlan}</p>}
      {detail.lifecycle?.exchangeRate == null ? null : <dl>
        <dt>{L.rateDay}</dt><dd>{formatBusinessDate(detail.lifecycle.exchangeRate.rateDay)}</dd>
        <dt>{L.rate}</dt><dd>{detail.lifecycle.exchangeRate.rate}</dd>
        <dt>{L.functional}</dt><dd>{detail.lifecycle.functionalTotals?.payableAmount}</dd>
      </dl>}
      {detail.lifecycle?.match == null ? null : <p>{detail.lifecycle.match.officialInvoiceNo} — {detail.lifecycle.match.ettn}</p>}
      {detail.lifecycle?.finalization == null ? null : <p role="status">{detail.lifecycle.finalization.financeStatus === 'admitted' ? L.admitted : L.waiting}</p>}
      <fieldset disabled={attempt.frozen}>
        {offered.includes('match') && can('match') ? <>
          <label htmlFor={`${prefix}-no`}>{L.officialNo}</label><input id={`${prefix}-no`} value={officialNo} maxLength={64} onChange={event => setOfficialNo(event.target.value)} />
          <label htmlFor={`${prefix}-ettn`}>{L.ettn}</label><input id={`${prefix}-ettn`} value={ettn} onChange={event => setEttn(event.target.value)} />
          {can('approve') ? <label><input type="checkbox" checked={approveDifference} onChange={event => setApproveDifference(event.target.checked)} />{L.approveDifference}</label> : null}
        </> : null}
        <label htmlFor={`${prefix}-reason`}>{L.reason}</label><input id={`${prefix}-reason`} value={reason} maxLength={LIMITS.note} onChange={event => setReason(event.target.value)} />
        <div className={styles.actions}>{offered.filter(action => can(permission(action))).map(action =>
          <Button key={action} type="button" onClick={() => send(action)} disabled={action === 'match' && (!officialNo.trim() || !isReferenceId(ettn.trim()))}>{L.actions[action]}</Button>)}</div>
        {offered.some(action => !can(permission(action))) ? <p>{L.noControls}</p> : null}
      </fieldset>
    </>}
    {notice == null ? null : <p role={notice.role}>{notice.message}</p>}
    {notice?.offerRetry ? <><Button type="button" onClick={attempt.retry}>{T.retry}</Button><Button type="button" onClick={attempt.leave}>{T.abandon}</Button></> : null}
  </div>
}
