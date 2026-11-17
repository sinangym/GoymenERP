import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { DocumentsPanel } from '../documents/DocumentsPanel'
import { documentsQueryKey, fetchDocuments, fetchDocumentVersions, downloadDocumentVersion, sha256Hex } from '../documents/api'
import { OWNER_KIND } from '../documents/contract'
import { formatDecimalTextTr, parseAmountInput } from './decimalText'
import { BOOK_VALUE_TEXT as T } from './cashExchangeBookValueText'
import { fetchBookValueCandidates, fetchBookValueWorkspace, sendBookValueAttempt, type BookValueAttempt, type BookValueEvidence } from './cashExchangeBookValue'

export interface CashExchangeBookValuePageProps { readonly companyId: string; readonly actorId: string; readonly canPrepare: boolean; readonly canApprove: boolean }
export function CashExchangeBookValuePage(props: CashExchangeBookValuePageProps) {
  const shell = useShellState(); const key = contextKey(shell)
  if (key === null) return <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
  const namespace = contextKeyString(key)
  return <section><h1>{T.heading}</h1><p>{T.boundary}</p><Workspace {...props} namespace={namespace} generation={shell.routeEntryGeneration}
    key={`${namespace}|${shell.sessionEpoch}|${shell.routeEntryGeneration}|${props.companyId}|${props.actorId}`} /></section>
}

function ReadState<T>({ outcome }: { readonly outcome: ViewOutcome<T> | undefined }) {
  return <StateBlock state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)} subject={T.subject} />
}
function Workspace({ companyId, actorId, canPrepare, canApprove, namespace, generation }: CashExchangeBookValuePageProps & { readonly namespace: string; readonly generation: number }) {
  const client = useQueryClient(); const boundary = useAuthBoundary(); const live = useRef(true)
  useEffect(() => { live.current = true; return () => { live.current = false } }, [])
  const [cursor, setCursor] = useState<string | null>(null); const [selected, setSelected] = useState<string | null>(null)
  const [amount, setAmount] = useState(''); const [reason, setReason] = useState(''); const [documentId, setDocumentId] = useState('')
  const [rejectReason, setRejectReason] = useState(''); const [attempt, setAttempt] = useState<BookValueAttempt | null>(null)
  const [sending, setSending] = useState(false); const [message, setMessage] = useState<string | null>(null)
  const [authorityStatus, setAuthorityStatus] = useState<401 | 403 | null>(null)
  async function bound<T>(read: () => Promise<ViewOutcome<T>>) {
    const captured = boundary.capture(); const value = await read()
    if (value.type === 'http' && (value.status === 401 || value.status === 403)) boundary.report(value.status, captured)
    return value
  }
  const candidates = useQuery({ queryKey: [namespace, generation, 'exchange-book-values', cursor], queryFn: ({ signal }) => bound(() => fetchBookValueCandidates(companyId, cursor, signal)), enabled: selected === null })
  const workspace = useQuery({ queryKey: [namespace, generation, 'exchange-book-value', selected], queryFn: ({ signal }) => bound(() => fetchBookValueWorkspace(companyId, selected!, signal)), enabled: selected !== null })
  const docs = useQuery({ queryKey: documentsQueryKey(namespace, generation, OWNER_KIND.financeCashExchange, selected ?? ''),
    queryFn: ({ signal }) => bound(() => fetchDocuments(OWNER_KIND.financeCashExchange, selected!, companyId, signal)), enabled: selected !== null && canPrepare })
  async function send(frozen: BookValueAttempt) {
    setAttempt(frozen); setSending(true); setMessage(null); const captured = boundary.capture()
    const answer = await sendBookValueAttempt(companyId, actorId, frozen)
    if (!live.current) return
    setSending(false)
    if (answer.kind === 'unknown') { setMessage(T.unknown); return }
    setAttempt(null)
    if (answer.kind === 'refused') {
      if (answer.status === 401 || answer.status === 403) { boundary.report(answer.status, captured); setAuthorityStatus(answer.status); return }
      setMessage(answer.status === 409 ? T.conflict : T.refused); return
    }
    setMessage(answer.result.isReplay ? T.replay : T.completed); setAmount(''); setReason(''); setDocumentId(''); setRejectReason('')
    await client.invalidateQueries({ queryKey: [namespace, generation, 'exchange-book-value', selected] })
  }
  function propose(e: FormEvent) {
    e.preventDefault(); const parsed = parseAmountInput(amount)
    const doc = docs.data?.type === 'ok-single' ? docs.data.data.items.find(x => x.id === documentId && x.downloadable) : undefined
    if (!parsed.ok || !doc || !selected || !reason.trim()) { setMessage(T.invalid); return }
    void send({ key: crypto.randomUUID(), decision: false, body: { transferId: selected, expectedTransferVersion: '1', sourceBookValueTl: parsed.wire,
      evidence: { documentId: doc.id, versionNo: doc.versionNo, sha256: doc.sha256 }, reason: reason.trim() } })
  }
  if (authorityStatus !== null) return <ReadState outcome={{ type: 'http', status: authorityStatus }} />
  if (selected === null) {
    if (candidates.data?.type !== 'ok-single') return <ReadState outcome={candidates.data} />
    const page = candidates.data.data
    return <><h2>{T.choose}</h2>{page.items.length === 0 ? <StateBlock state={{ kind: 'empty-no-data' }} subject={T.subject} /> : <ul>{page.items.map(row => <li key={row.id}>
      <Button type="button" onClick={() => { setSelected(row.id); setMessage(null) }}>{row.documentNumber?.number ?? row.preparation.reference ?? row.preparation.occurredOn}
        {' — '}{formatDecimalTextTr(row.preparation.sourceMoney.amount)} → {formatDecimalTextTr(row.preparation.targetMoney.amount)}</Button></li>)}</ul>}
      {page.nextCursor !== null && <Button type="button" onClick={() => setCursor(page.nextCursor)}>{T.next}</Button>}
      {cursor !== null && <Button type="button" onClick={() => setCursor(null)}>{T.first}</Button>}</>
  }
  if (workspace.data?.type !== 'ok-single') return <><Button type="button" onClick={() => setSelected(null)}>{T.back}</Button><ReadState outcome={workspace.data} /></>
  const view = workspace.data.data; const source = view.transfer; const locked = attempt !== null || sending
  const canPropose = canPrepare && !view.proposals.some(x => x.status === 'Pending' || x.status === 'Approved')
  return <><Button type="button" disabled={locked} onClick={() => { setSelected(null); setMessage(null); setDocumentId(''); setAmount(''); setReason('') }}>{T.back}</Button>
    <dl><dt>{T.source}</dt><dd>{formatDecimalTextTr(source.preparation.sourceMoney.amount)}</dd><dt>{T.target}</dt><dd>{formatDecimalTextTr(source.preparation.targetMoney.amount)}</dd>
      {source.exchange?.sourceReferenceTl && <><dt>{T.reference}</dt><dd>{formatDecimalTextTr(source.exchange.sourceReferenceTl.amount)}</dd></>}</dl>
    {message && <p role="status">{message}</p>}{attempt && !sending && <Button type="button" onClick={() => void send(attempt)}>{T.retry}</Button>}
    {(canPrepare || canApprove) && <DocumentsPanel ownerKind={OWNER_KIND.financeCashExchange} ownerId={selected} companyId={companyId} namespace={namespace} entryGeneration={generation} heading={T.uploadHeading} />}
    {canPropose && <>
      {docs.data?.type !== 'ok-single' ? <ReadState outcome={docs.data} /> : <form onSubmit={propose}><fieldset disabled={locked}>
        <label>{T.amount}<input inputMode="decimal" value={amount} onChange={e => setAmount(e.target.value)} required /></label>
        <label>{T.document}<select value={documentId} onChange={e => setDocumentId(e.target.value)} required><option value="">{T.choose}</option>
          {docs.data.data.items.filter(x => x.downloadable).map(x => <option key={x.id} value={x.id}>{x.fileName} (v{x.versionNo})</option>)}</select></label>
        {docs.data.data.items.every(x => !x.downloadable) && <p>{T.documentEmpty}</p>}
        <Button type="button" onClick={() => void docs.refetch()}>{T.refresh}</Button>
        <label>{T.reason}<textarea value={reason} maxLength={1000} onChange={e => setReason(e.target.value)} required /></label><Button type="submit">{T.prepare}</Button>
      </fieldset></form>}</>}
    <ul>{view.proposals.map(row => <li key={row.id}><p>{row.status === 'Pending' ? T.pending : row.status === 'Approved' ? T.approved : T.rejected}: {formatDecimalTextTr(row.sourceBookValueTl)} TL</p>
      <p>{row.reason}</p><PinnedEvidence companyId={companyId} transferId={selected} namespace={namespace} generation={generation} evidence={row.evidence} />
      {row.status === 'Pending' && canApprove && (row.preparedBy === actorId ? <p>{T.own}</p> : <fieldset disabled={locked}>
        <Button type="button" onClick={() => void send({ key: crypto.randomUUID(), decision: true, body: { proposalId: row.id, expectedVersion: '1', approve: true, reason: null } })}>{T.approve}</Button>
        <label>{T.rejection}<textarea value={rejectReason} maxLength={1000} onChange={e => setRejectReason(e.target.value)} /></label>
        <Button type="button" disabled={!rejectReason.trim()} onClick={() => void send({ key: crypto.randomUUID(), decision: true, body: { proposalId: row.id, expectedVersion: '1', approve: false, reason: rejectReason.trim() } })}>{T.reject}</Button>
      </fieldset>)}</li>)}</ul></>
}

function PinnedEvidence({ companyId, transferId, namespace, generation, evidence }: { readonly companyId: string; readonly transferId: string;
  readonly namespace: string; readonly generation: number; readonly evidence: BookValueEvidence }) {
  const boundary = useAuthBoundary(); const alive = useRef(true); const downloadController = useRef<AbortController | null>(null)
  const [problem, setProblem] = useState(false); const [busy, setBusy] = useState(false)
  useEffect(() => { alive.current = true; return () => { alive.current = false; downloadController.current?.abort() } }, [])
  const binding = { companyId, ownerKind: OWNER_KIND.financeCashExchange, ownerId: transferId, documentId: evidence.documentId }
  const query = useQuery({ queryKey: [namespace, generation, 'exchange-book-evidence', transferId, evidence.documentId, evidence.versionNo, evidence.sha256],
    queryFn: ({ signal }) => fetchDocumentVersions(binding, String(evidence.versionNo + 1), signal) })
  if (query.isLoading || query.data === undefined) return <p role="status">{T.pinnedDocument}: …</p>
  const version = query.data.type === 'ok' ? query.data.page.versions.find(row => row.versionNo === evidence.versionNo) ?? null : null
  if (version === null || version.id !== evidence.documentId || version.versionNo !== evidence.versionNo || !version.downloadable
    || version.sha256.toLowerCase() !== evidence.sha256.toLowerCase()) return <p role="alert">{T.documentUnavailable}</p>
  const pinned = version
  async function download() {
    const captured = boundary.capture(); const controller = new AbortController(); downloadController.current = controller
    setBusy(true); setProblem(false)
    const answer = await downloadDocumentVersion(binding, pinned.versionNo, pinned.sizeBytes, controller.signal)
    if (!alive.current || controller.signal.aborted) return
    if (answer.type !== 'ok') {
      if (answer.type === 'refused' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, captured)
      setProblem(true); setBusy(false); return
    }
    const hash = await sha256Hex(answer.blob)
    if (!alive.current || controller.signal.aborted) return
    setBusy(false)
    if (hash === null || hash.toLowerCase() !== evidence.sha256.toLowerCase()) { setProblem(true); return }
    const url = URL.createObjectURL(answer.blob)
    const link = document.createElement('a'); link.href = url; link.download = pinned.fileName; link.rel = 'noopener'
    document.body.append(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 0)
  }
  return <div><p>{T.pinnedDocument}: {version.fileName} (v{version.versionNo})</p>
    <Button type="button" disabled={busy} onClick={() => void download()}>{T.download}</Button>{problem && <p role="alert">{T.documentUnavailable}</p>}</div>
}
