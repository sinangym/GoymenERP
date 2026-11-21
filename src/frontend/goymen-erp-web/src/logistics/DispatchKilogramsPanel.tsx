import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { ShellState } from '../shell/shellState'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { DocumentsPanel } from '../documents/DocumentsPanel'
import { documentsQueryKey, fetchDocuments } from '../documents/api'
import { OWNER_KIND } from '../documents/contract'
import { DISPATCH_NOTE_STATUS, type DispatchNote } from './contract'
import { attemptBinding, applyOutcome, beginSend, decideSubmit, generateAttemptKey, generateClientId, isOutcomeStillUnknown, liveSnapshot, type AttemptSnapshot, type AttemptState } from './instructionAttempt'
import { kgApprovePath, kgRecordPath, readDispatchKilograms, sendDispatchKilograms } from './dispatchKilogramsApi'
import { KG_GUID, KG_TEXT, type DispatchKilograms, type KgCommand, type KgDocument, type KgResponse, type RecordKilograms } from './dispatchKilogramsContract'
import { recallKgAttempt, rememberKgAttempt } from './pendingDispatchKilograms'
import { DISPATCH_KG_TEXT as T } from './dispatchKilogramsText'
import styles from './LogisticsPages.module.css'

function readMessage<TData>(result: ViewOutcome<TData>): string | null {
  switch (result.type) {
    case 'loading': return T.loading
    case 'offline': return T.offline
    case 'network-error': return T.network
    case 'malformed': return T.malformed
    case 'http': return result.status === 404 ? T.empty : result.status === 401 ? T.unauthenticated : result.status === 403 ? T.forbidden : result.status === 409 ? T.conflict : result.status >= 500 ? T.unavailable : T.malformed
    default: return null
  }
}
export function DispatchKilogramsPanel(props: { readonly shell: ShellState; readonly note: DispatchNote; readonly companyId: string }) {
  const binding = attemptBinding(props.shell, `dispatch-kilograms:${props.note.dispatchNoteId}`)
  if (binding === null) return null
  return <KilogramPanel key={`${binding}|${props.shell.requestFenceGeneration}|${props.shell.routeEntryGeneration}`} {...props} binding={binding} />
}
function KilogramPanel({ shell, note, companyId, binding }: { readonly shell: ShellState; readonly note: DispatchNote; readonly companyId: string; readonly binding: string }) {
  const boundary = useAuthBoundary()
  const [read, setRead] = useState<ViewOutcome<DispatchKilograms>>({ type: 'loading' })
  const [refresh, setRefresh] = useState(0)
  const [external, setExternal] = useState(false)
  const [ettn, setEttn] = useState('')
  const [kind, setKind] = useState<KgDocument['kind']>('ElectronicDispatch')
  const [documentId, setDocumentId] = useState('')
  const [values, setValues] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const [recordId] = useState(generateClientId)
  const [attempt, setAttempt] = useState<AttemptState<KgCommand, KgResponse>>(() => recallKgAttempt(binding))
  const current = useRef(attempt)
  const alive = useRef(true)
  useEffect(() => { alive.current = true; return () => { alive.current = false } }, [])
  useEffect(() => {
    const controller = new AbortController()
    const captured = boundary.capture()
    setRead({ type: 'loading' })
    void readDispatchKilograms(companyId, note.dispatchNoteId, controller.signal).then(result => {
      if (result.type === 'http') boundary.report(result.status, captured)
      if (!controller.signal.aborted) setRead(result)
    })
    return () => controller.abort()
  }, [companyId, note.dispatchNoteId, refresh, boundary])
  const documents = useQuery({ queryKey: documentsQueryKey(binding, shell.routeEntryGeneration, OWNER_KIND.logisticsDispatchNote, note.dispatchNoteId),
    queryFn: ({ signal }) => fetchDocuments(OWNER_KIND.logisticsDispatchNote, note.dispatchNoteId, companyId, signal), enabled: external })
  const choices = documents.data?.type === 'ok-single' ? documents.data.data.items.filter(d => d.downloadable && d.contentType === 'application/pdf') : []
  const productIds = [...new Set(note.lines.map(line => line.productId))]
  const busy = attempt.phase === 'submitting'
  const unresolved = isOutcomeStillUnknown(attempt)
  const view = read.type === 'ok-single' ? read.data : null
  const empty = read.type === 'http' && read.status === 404
  async function send(snapshot: AttemptSnapshot<KgCommand>) {
    if (current.current.phase === 'submitting') return
    const previous = current.current
    const captured = boundary.capture()
    const flight = beginSend<KgCommand, KgResponse>(snapshot)
    current.current = flight; setAttempt(flight); rememberKgAttempt(binding, flight)
    const outcome = await sendDispatchKilograms(snapshot, note.dispatchNoteId)
    if (outcome.type === 'http') boundary.report(outcome.status, captured)
    const next = applyOutcome(flight, snapshot, binding, outcome, previous)
    rememberKgAttempt(binding, next)
    if (!alive.current) return
    current.current = next; setAttempt(next)
    if (next.phase === 'completed') setRead({ type: 'ok-single', data: next.result.record })
  }
  function submit(path: string, payload: KgCommand) {
    const state = current.current.phase === 'completed' ? { phase: 'idle' as const } : current.current
    const choice = decideSubmit(state, { path, payload, binding, companyId }, generateAttemptKey)
    if (choice.type === 'send') void send(choice.snapshot)
  }
  function record(event: FormEvent) {
    event.preventDefault(); setError(null)
    const inputs = external ? note.lines.map(line => values[String(line.lineNo)] ?? '') : productIds.map(id => values[id] ?? '')
    if (inputs.some(value => !KG_TEXT.test(value)) || external && (!KG_GUID.test(ettn) || !choices.some(d => d.id === documentId))) { setError(T.invalid); return }
    const document = choices.find(d => d.id === documentId)
    const payload: RecordKilograms = { recordId, dispatchNoteId: note.dispatchNoteId,
      products: external ? [] : productIds.map(id => ({ productId: id, scaleKg: values[id] })),
      finalizedDocument: !external || !document ? null : { ettn: ettn.toLowerCase(), kind, documentId, expectedDocumentVersion: document.versionNo, expectedDocumentSha256: document.sha256,
        lines: note.lines.map(line => ({ lineNo: line.lineNo, shipmentId: line.shipmentId,
        productId: line.productId, kilograms: values[String(line.lineNo)] })) } }
    submit(kgRecordPath, payload)
  }
  const message = readMessage(read)
  const notice = attempt.phase === 'submitting' ? T.sending : unresolved ? T.unknown
    : attempt.phase === 'completed' ? attempt.result.isReplay ? T.replayed : T.completed
    : attempt.phase === 'failed' ? attempt.failure === 'forbidden' ? T.forbidden : attempt.failure === 'unauthenticated' ? T.unauthenticated : attempt.failure === 'conflict' ? T.conflict : T.refused : null
  return <section className={styles.section} aria-label={T.title}>
    <h3>{T.title}</h3><p>{T.explanation}</p>
    {message && <p role="status">{message}</p>}
    {read.type !== 'ok-single' && <StateBlock state={classifyOutcome(read)} subject={T.title} onRetry={() => setRefresh(n => n + 1)} />}
    {(view || empty) && <Button type="button" disabled={busy} onClick={() => setRefresh(n => n + 1)}>{T.retryRead}</Button>}
    {view && <>
      <p>{view.status === 'Approved' ? T.approved : view.status === 'PendingApproval' ? T.pending : T.recorded}</p>
      <dl><dt>{T.total}</dt><dd>{view.scaleKg}</dd><dt>{T.difference}</dt><dd>{view.differenceKg}</dd></dl>
      {view.kilogramAllocation === null ? <p>{T.legacy}</p> : <table><caption>{T.title}</caption><thead><tr><th scope="col">{T.line}</th><th scope="col">{T.sourceKg}</th><th scope="col">{T.assignedKg}</th></tr></thead>
        <tbody>{view.kilogramAllocation.lines.map(line => <tr key={line.shipmentId}><td>{line.lineNo}</td><td>{line.theoreticalKg}</td><td>{line.kilograms}</td></tr>)}</tbody></table>}
      {view.status === 'PendingApproval' && <Button type="button" disabled={busy || unresolved} onClick={() => submit(kgApprovePath(view.recordId), { expectedVersion: view.version })}>{T.approve}</Button>}
    </>}
    {empty && note.status === DISPATCH_NOTE_STATUS.draft && <form onSubmit={record}>
      <fieldset disabled={busy || unresolved}><legend>{T.source}</legend>
        <label><input type="radio" checked={!external} onChange={() => { setExternal(false); setValues({}) }} />{T.theoretical}</label>
        <label><input type="radio" checked={external} onChange={() => { setExternal(true); setValues({}) }} />{T.official}</label>
        {external ? <>
          <label>{T.ettn}<input value={ettn} onChange={e => setEttn(e.target.value.trim())} maxLength={36} /></label>
          <label>{T.kind}<select value={kind} onChange={e => setKind(e.target.value as KgDocument['kind'])}><option value="ElectronicDispatch">{T.dispatch}</option><option value="ElectronicInvoice">{T.invoice}</option></select></label>
          <label>{T.documents}<select value={documentId} onChange={e => setDocumentId(e.target.value)}><option value="">{T.chooseDocument}</option>{choices.map(d => <option key={d.id} value={d.id}>{d.fileName}</option>)}</select></label>
          {choices.length === 0 && <p role="status">{documents.isFetching ? T.loading : documents.data?.type === 'ok-single' ? T.noDocument
            : documents.data ? readMessage(documents.data) : T.unavailable}</p>}
          <Button type="button" onClick={() => { void documents.refetch() }}>{T.refreshDocuments}</Button>
          {note.lines.map(line => <label key={line.shipmentId}>{T.line} {line.lineNo} — {T.assignedKg}<input inputMode="decimal" value={values[String(line.lineNo)] ?? ''} onChange={e => setValues(v => ({ ...v, [String(line.lineNo)]: e.target.value }))} /></label>)}
        </> : productIds.map(id => <label key={id}>{T.scale} — {note.lines.filter(line => line.productId === id).map(line => line.lineNo).join(', ')}<input inputMode="decimal" value={values[id] ?? ''} onChange={e => setValues(v => ({ ...v, [id]: e.target.value }))} /></label>)}
        <Button type="submit">{T.save}</Button>
      </fieldset>
    </form>}
    {external && empty && <DocumentsPanel ownerKind={OWNER_KIND.logisticsDispatchNote} ownerId={note.dispatchNoteId} companyId={companyId} namespace={binding} entryGeneration={shell.routeEntryGeneration} heading={T.documents} />}
    {error && <p role="alert">{error}</p>}{notice && <p role="status">{notice}</p>}
    {unresolved && !busy && <Button type="button" onClick={() => { const live = liveSnapshot(current.current); if (live) void send(live) }}>{T.retryCommand}</Button>}
  </section>
}
