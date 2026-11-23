import { useLayoutEffect, useRef, useState, useSyncExternalStore, type FormEvent } from 'react'
import { useInfiniteQuery, useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { isTransferGuid } from './transferContract'
import { subscribeTransferDispatchAttempt, transferDispatchAttempt, transferDispatchBinding, transferDispatchFrozen, transferDispatchUtc } from './transferDispatchApi'
import { TransferDispatchPanel } from './TransferDispatchPanel'
import { TransferCancelPanel } from './TransferCancelPanel'
import { recallTransferCancel, transferCancelBinding } from './transferCancelAttempt'
import { INVENTORY_TRANSFER_CANCEL_CAPABILITY } from './capabilities'
import { beginReceiptAttempt, fetchReceiptCandidates, fetchReceiptDocuments, fetchReceiptWork, finishReceiptAttempt, leaveReceiptAttempt,
  mergeReceiptCandidatePages, mergeReceiptDocumentPages, receiptAttempt, receiptBinding, receiptCommandPurpose, receiptFrozen, receiptPositiveKg, receiptReason,
  recallReceiptSelection, rememberReceiptSelection, ReceiptReadError, sendReceiptCommand, subscribeReceiptAttempt, TRANSFER_RECEIPT_CAPABILITIES as CAP,
  type ReceiptCommand, type ReceiptPurpose, type ReceiptSnapshot, type ReceiptWork } from './transferReceiptApi'
import { TRANSFER_RECEIPT_TEXT as T } from './transferReceiptText'
import { parsePieceCount } from './stockCountContract'
import styles from './inventory.module.css'

interface Props { readonly transferId: string | null; readonly onChanged: (transferId: string) => void }
const purposes = Object.keys(CAP) as ReceiptPurpose[]
function unwrap<T>(outcome: ViewOutcome<T>): T { if (outcome.type === 'ok-single') return outcome.data; throw new ReceiptReadError(outcome.type === 'ok' ? { type: 'malformed' } : outcome) }
function readError(error: unknown) { return classifyOutcome(error instanceof ReceiptReadError ? error.outcome : { type: 'malformed' }) }
const localUtc = (value: string) => { const date = value === '' ? null : new Date(value); const result = date !== null && Number.isFinite(date.getTime()) ? date.toISOString() : null; return transferDispatchUtc(result) ? result : null }
const statusLabel = (value: string) => (T.statuses as Readonly<Record<string, string>>)[value] ?? value
function latestOverage(work: ReceiptWork, lineId: string) {
  const receipt = work.facts.receipts.find((row) => row.lineId === lineId)
  const renewal = work.facts.overageRenewals.filter((row) => row.lineId === lineId).reduce<(typeof work.facts.overageRenewals)[number] | null>((last, row) => last === null || row.requestNumber > last.requestNumber ? row : last, null)
  return renewal ? { lineId, requestNumber: renewal.requestNumber, status: renewal.status } : receipt && receipt.overageStatus !== 'None'
    ? { lineId, requestNumber: 1, status: receipt.overageStatus } : null
}
export function TransferReceiptPanel(props: Props) {
  const shell = useShellState(); const key = contextKey(shell)
  if (key === null || shell.activeContext?.scopeMode !== 'single' || shell.activeContext.companyId === null)
    return <StateBlock subject={T.heading} state={{ kind: 'no-authorized-company' }} />
  const allowed = purposes.filter((purpose) => hasCapability(shell, CAP[purpose]))
  if (allowed.length === 0) return null
  if (props.transferId !== null && !isTransferGuid(props.transferId)) return <StateBlock subject={T.heading} state={{ kind: 'malformed-response' }} />
  const namespace = contextKeyString(key)
  return <Workspace key={JSON.stringify([namespace, shell.sessionEpoch, shell.routeEntryGeneration, props.transferId])} {...props} shell={shell} namespace={namespace}
    allowed={allowed} companyId={shell.activeContext.companyId} actorId={shell.activeContext.userId} />
}
function Workspace({ transferId: seed, onChanged, shell, namespace, allowed, companyId, actorId }: Props & {
  readonly shell: ShellState; readonly namespace: string; readonly allowed: readonly ReceiptPurpose[]; readonly companyId: string; readonly actorId: string
}) {
  const boundary = useAuthBoundary(); const binding = receiptBinding(shell)
  const attempt = useSyncExternalStore((callback) => subscribeReceiptAttempt(binding, callback), () => receiptAttempt(binding), () => receiptAttempt(null))
  const dispatchBinding = transferDispatchBinding(shell)
  const dispatchAttempt = useSyncExternalStore((callback) => subscribeTransferDispatchAttempt(dispatchBinding, callback), () => transferDispatchAttempt(dispatchBinding), () => transferDispatchAttempt(null))
  const dispatchFrozen = transferDispatchFrozen(dispatchAttempt)
  const frozen = receiptFrozen(attempt) || dispatchFrozen; const alive = useRef(false)
  useLayoutEffect(() => { alive.current = true; return () => { alive.current = false; leaveReceiptAttempt(binding) } }, [binding])
  const retained = receiptFrozen(attempt) && attempt.phase !== 'idle' ? attempt.snapshot : null
  const remembered = recallReceiptSelection(binding)
  const [purpose, setPurpose] = useState<ReceiptPurpose | null>(() => retained && allowed.includes(retained.work.purpose) ? retained.work.purpose
    : dispatchFrozen && allowed.includes('dispatch') ? 'dispatch' : remembered && allowed.includes(remembered.purpose) ? remembered.purpose : null)
  const [transferId, setTransferId] = useState<string | null>(() => seed ?? retained?.work.transfer.transferId
    ?? (dispatchFrozen && dispatchAttempt.phase !== 'idle' ? dispatchAttempt.snapshot.transfer.transferId : remembered?.transferId) ?? null)
  const [queryDraft, setQueryDraft] = useState(''); const [query, setQuery] = useState<string | null>(null)
  const [revision, setRevision] = useState(0); const [documentRevision, setDocumentRevision] = useState(0)
  const [documentId, setDocumentId] = useState(''); const [documentLineId, setDocumentLineId] = useState(''); const [lineId, setLineId] = useState('')
  const [requestKey, setRequestKey] = useState(''); const [when, setWhen] = useState(''); const [reason, setReason] = useState(''); const [quantity, setQuantity] = useState('')
  // KLN-D119 (AK-2b): the received / re-requested pieces of a line dispatched with pieces.
  const [pieces, setPieces] = useState('')
  const [choice, setChoice] = useState<'' | 'Approve' | 'Reject'>('')
  const [draftLines, setDraftLines] = useState<Readonly<Record<string, { readonly selected: boolean; readonly kg: string; readonly reason: string }>>>({})
  const [error, setError] = useState<string | null>(null)
  function cancellationPending() {
    if (transferId === null) return false
    const cancellationBinding = transferCancelBinding(shell, transferId)
    return cancellationBinding !== null && recallTransferCancel(cancellationBinding, companyId, transferId).type === 'pending'
  }
  function physicalPending() { return transferDispatchFrozen(transferDispatchAttempt(dispatchBinding)) || cancellationPending() }
  function report<T>(outcome: ViewOutcome<T>, security: ReturnType<typeof boundary.capture>) {
    if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    return outcome
  }
  const candidates = useInfiniteQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'transfer-receipt-candidates', purpose, query, revision], enabled: purpose !== null,
    initialPageParam: null as string | null, retry: false, staleTime: 0, queryFn: async ({ signal, pageParam }) => {
      const security = boundary.capture(); return unwrap(report(await fetchReceiptCandidates(companyId, purpose!, query, pageParam, signal), security))
    }, getNextPageParam: (last) => last.nextCursor ?? undefined })
  const candidateItems = mergeReceiptCandidatePages(candidates.data?.pages ?? [])
  const workQuery = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'transfer-receipt-work', transferId, purpose, revision], enabled: transferId !== null && purpose !== null, retry: false, staleTime: 0,
    queryFn: async ({ signal }) => { const security = boundary.capture(); return report(await fetchReceiptWork(companyId, transferId!, purpose!, signal), security) } })
  const work = workQuery.data?.type === 'ok-single' ? workQuery.data.data : null
  const documents = useInfiniteQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'transfer-receipt-documents', transferId, work?.transfer.version, documentRevision],
    enabled: purpose === 'receive' && work !== null, initialPageParam: null as string | null, retry: false, staleTime: 0,
    queryFn: async ({ signal, pageParam }) => { const security = boundary.capture(); return unwrap(report(await fetchReceiptDocuments(work!.transfer, pageParam, signal), security)) },
    getNextPageParam: (last) => last.nextCursor ?? undefined })
  const documentItems = mergeReceiptDocumentPages(documents.data?.pages ?? [])
  const selectedDocument = documentItems?.find((row) => row.documentId === documentId) ?? null
  const selectedDocumentLine = selectedDocument?.lines.find((row) => row.documentLineId === documentLineId) ?? null
  const dispatchedPiecesOf = (transferLineId: string) => work?.transfer.lines.find((line) => line.lineId === transferLineId)?.dispatchedPieceCount
  const receivePieceLine = selectedDocumentLine !== null && dispatchedPiecesOf(selectedDocumentLine.transferLineId) !== undefined
  const renewalPieceLine = lineId !== '' && dispatchedPiecesOf(lineId) !== undefined
  const sameTarget = attempt.phase === 'idle' || transferId !== null && attempt.snapshot.work.transfer.transferId.toLowerCase() === transferId.toLowerCase()
  const ready = !frozen && work !== null && !workQuery.isFetching && purpose !== null && allowed.includes(purpose)
  const docsReady = ready && !documents.isFetching && !documents.isError && documentItems !== null
  function clear() { setDocumentId(''); setDocumentLineId(''); setLineId(''); setRequestKey(''); setWhen(''); setReason(''); setQuantity(''); setChoice(''); setDraftLines({}); setError(null) }
  function refresh() { if (frozen || physicalPending()) { setError(T.physicalPending); return }; clear(); setRevision((value) => value + 1); setDocumentRevision((value) => value + 1) }
  function selectTransfer(value: string) { if (frozen || physicalPending()) { setError(T.physicalPending); return }; clear(); setTransferId(value); if (purpose) rememberReceiptSelection(binding, purpose, value) }
  async function execute(snapshot: ReceiptSnapshot) {
    const security = boundary.capture(); const outcome = await sendReceiptCommand(snapshot)
    if (outcome.type === 'refused' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    finishReceiptAttempt(snapshot, outcome)
    if (!alive.current || outcome.type !== 'applied') return
    rememberReceiptSelection(binding, snapshot.work.purpose, snapshot.work.transfer.transferId)
    clear(); setRevision((value) => value + 1); setDocumentRevision((value) => value + 1); onChanged(snapshot.work.transfer.transferId)
  }
  function start(command: ReceiptCommand) {
    if (!ready || work === null || !hasCapability(shell, CAP[receiptCommandPurpose(command.kind)])) return
    if (physicalPending()) { setError(T.physicalPending); return }
    const snapshot = beginReceiptAttempt(binding, actorId, work, command)
    if (snapshot === null) { setError(T.invalid); return }
    setError(null); void execute(snapshot)
  }
  function retry(snapshot: ReceiptSnapshot) {
    if (snapshot.binding !== binding || snapshot.actorId !== actorId || !hasCapability(shell, CAP[receiptCommandPurpose(snapshot.command.kind)]) || !sameTarget) return
    const request = beginReceiptAttempt(binding, actorId, snapshot.work, snapshot.command)
    if (request !== null) void execute(request)
  }
  function create(event: FormEvent) {
    event.preventDefault(); if (!docsReady || work === null) return
    const at = localUtc(when); const chosen = work.transfer.lines.filter((line) => draftLines[line.lineId]?.selected)
    if (at === null || chosen.length === 0 || chosen.some((line) => !receiptPositiveKg(draftLines[line.lineId].kg.trim())
      || draftLines[line.lineId].reason.trim() !== '' && !receiptReason(draftLines[line.lineId].reason.trim()))) { setError(T.invalid); return }
    start({ kind: 'create', payload: { documentId: crypto.randomUUID(), transferId: work.transfer.transferId, expectedTransferVersion: work.transfer.version, receivedAtUtc: at,
      lines: chosen.map((line) => ({ documentLineId: crypto.randomUUID(), transferLineId: line.lineId, receivedKg: draftLines[line.lineId].kg.trim(), overageReason: draftLines[line.lineId].reason.trim() || null })) } })
  }
  function finalize(event: FormEvent) {
    event.preventDefault(); if (!docsReady || work === null || selectedDocument?.status !== 'Draft' || !receiptReason(reason.trim())) { setError(T.invalid); return }
    start({ kind: 'finalize', document: selectedDocument, payload: { expectedDocumentVersion: selectedDocument.version, expectedTransferVersion: work.transfer.version, reason: reason.trim() } })
  }
  function receive() {
    if (!docsReady || work === null || selectedDocument?.status !== 'Effective' || selectedDocumentLine === null) { setError(T.invalid); return }
    const receivedPieces = receivePieceLine ? parsePieceCount(pieces) : undefined
    if (receivedPieces === null || receivedPieces === 0) { setError(T.invalidPieces); return }
    start({ kind: 'receive', lineId: selectedDocumentLine.transferLineId, document: selectedDocument, payload: { expectedTransferVersion: work.transfer.version,
      receivedAtUtc: selectedDocument.receivedAtUtc, incomingDocument: { kind: 'inventory.transfer.incoming', id: selectedDocument.documentId, lineId: selectedDocumentLine.documentLineId },
      receivedKg: selectedDocumentLine.receivedKg, overageReason: selectedDocumentLine.overageReason, expectedIncomingDocumentVersion: selectedDocument.version,
      ...(receivedPieces === undefined ? {} : { receivedPieceCount: receivedPieces }) } })
  }
  function action(event: FormEvent, kind: 'overage-decision' | 'overage-renewal' | 'loss-request' | 'loss-decision' | 'loss-withdrawal') {
    event.preventDefault(); if (!ready || work === null) return
    const at = localUtc(when); const why = reason.trim()
    if (at === null || !receiptReason(why)) { setError(T.invalid); return }
    if (kind === 'loss-request') { start({ kind, lineId, payload: { expectedTransferVersion: work.transfer.version, requestedAtUtc: at, explanation: why } }); return }
    if (kind === 'overage-renewal') {
      const overagePieces = renewalPieceLine ? parsePieceCount(pieces) : undefined
      if (overagePieces === null) { setError(T.invalidPieces); return }
      start({ kind, lineId, payload: { expectedTransferVersion: work.transfer.version, requestedAtUtc: at, overageKg: quantity.trim(), reason: why,
        ...(overagePieces === undefined ? {} : { overagePieceCount: overagePieces }) } }); return
    }
    const pending = kind === 'overage-decision' ? work.facts.receipts.map((row) => latestOverage(work, row.lineId)).filter((row) => row?.status === 'Pending')
      : work.facts.losses.filter((row) => row.status === 'Pending')
    const selectedRequest = pending.find((row) => row !== null && `${row.lineId}:${row.requestNumber}` === requestKey)
    if (!selectedRequest || kind !== 'loss-withdrawal' && choice === '') { setError(T.invalid); return }
    if (kind === 'overage-decision') start({ kind, lineId: selectedRequest.lineId, payload: { expectedTransferVersion: work.transfer.version, decision: choice as 'Approve' | 'Reject', decidedAtUtc: at, reason: why, requestNumber: selectedRequest.requestNumber } })
    else if (kind === 'loss-decision') start({ kind, lineId: selectedRequest.lineId, payload: { expectedTransferVersion: work.transfer.version, decision: choice as 'Approve' | 'Reject', decidedAtUtc: at, reason: why, requestNumber: selectedRequest.requestNumber } })
    else start({ kind, lineId: selectedRequest.lineId, payload: { expectedTransferVersion: work.transfer.version, withdrawnAtUtc: at, reason: why, requestNumber: selectedRequest.requestNumber } })
  }
  const inTransit = work?.transfer.lines.filter((line) => line.status === 'InTransit' && !work.facts.losses.some((loss) => loss.lineId === line.lineId && loss.status === 'Pending')) ?? []
  const rejectedOverages = work?.facts.receipts.filter((row) => latestOverage(work, row.lineId)?.status === 'Rejected') ?? []
  const pendingOverages = work?.facts.receipts.map((row) => latestOverage(work, row.lineId)).filter((row) => row?.status === 'Pending') ?? []
  const pendingLosses = work?.facts.losses.filter((row) => row.status === 'Pending') ?? []
  const reasonField = <Field label={T.reason}>{(control) => <textarea {...control} maxLength={500} value={reason} onChange={(event) => setReason(event.currentTarget.value)} />}</Field>
  const timeField = <Field label={T.when}>{(control) => <input {...control} type="datetime-local" step="1" value={when} onChange={(event) => setWhen(event.currentTarget.value)} />}</Field>
  const decisionField = <Field label={T.decision}>{(control) => <select {...control} value={choice} onChange={(event) => setChoice(event.currentTarget.value as typeof choice)}><option value="">{T.chooseDecision}</option><option value="Approve">{T.approve}</option><option value="Reject">{T.reject}</option></select>}</Field>
  return <section aria-label={T.heading} className={styles.lineCard}><h2>{T.heading}</h2><p>{T.intro}</p>
    <Field label={T.purpose}>{(control) => <select {...control} value={purpose ?? ''} disabled={frozen} onChange={(event) => {
      const next = event.currentTarget.value as ReceiptPurpose
      if (physicalPending() && next !== 'dispatch') { setError(T.physicalPending); return }
      const nextTransfer = seed ?? (next === 'dispatch' ? transferId : null)
      clear(); setPurpose(allowed.includes(next) ? next : null); setTransferId(nextTransfer); setQuery(null); setQueryDraft('')
      if (allowed.includes(next)) rememberReceiptSelection(binding, next, nextTransfer)
    }}><option value="">{T.choosePurpose}</option>{allowed.map((item) => <option key={item} value={item}>{T.purposes[item]}</option>)}</select>}</Field>
    {sameTarget && attempt.phase === 'sending' ? <p role="status">{T.pending}</p> : null}
    {sameTarget && attempt.phase === 'completed' ? <div className={styles.successCard}><p role="status">{attempt.replayed ? T.replayed : T.saved}</p>
      <p>{T.commands[attempt.snapshot.command.kind]} — {attempt.snapshot.work.transfer.transferId}</p>
      {attempt.result.document ? <p>{T.document}: {attempt.result.document.documentId} — {attempt.result.document.status === 'Draft' ? T.draft : T.effective} — {T.documentVersion}: {attempt.result.document.version}</p>
        : attempt.result.work ? <p>{T.transferVersion}: {attempt.result.work.transfer.version} — {statusLabel(attempt.result.work.transfer.status)}</p> : null}</div> : null}
    {!sameTarget && retained ? <div><p role="alert">{T.otherPending}</p><Button type="button" disabled={!allowed.includes(retained.work.purpose)} onClick={() => { setPurpose(retained.work.purpose); setTransferId(retained.work.transfer.transferId); clear() }}>{T.returnPending}</Button></div> : null}
    {sameTarget && (attempt.phase === 'unknown' || attempt.phase === 'failed' && attempt.unresolved) ? <div className={styles.warningCard}><p role="alert">{T.unknown}</p>
      <p>{T.commands[attempt.snapshot.command.kind]} — {attempt.snapshot.work.transfer.transferId}</p><StateBlock subject={T.work} state={attempt.phase === 'unknown' ? { kind: attempt.reason } : classifyOutcome({ type: 'http', status: attempt.status })} />
      <Button type="button" disabled={!hasCapability(shell, CAP[receiptCommandPurpose(attempt.snapshot.command.kind)])} onClick={() => retry(attempt.snapshot)}>{T.retry}</Button></div> : null}
    {sameTarget && attempt.phase === 'failed' && !attempt.unresolved ? [400, 415, 422].includes(attempt.status) ? <p role="alert">{T.refused}</p>
      : <StateBlock subject={T.work} state={classifyOutcome({ type: 'http', status: attempt.status })} /> : null}
    {error === null ? null : <p role="alert">{error}</p>}
    {purpose === null ? null : <><Button type="button" disabled={frozen || workQuery.isFetching || candidates.isFetching || documents.isFetching} onClick={refresh}>{T.refresh}</Button>
      <form onSubmit={(event) => { event.preventDefault(); if (frozen) return; if (physicalPending()) { setError(T.physicalPending); return }; const value = queryDraft.trim(); if (value.length > 64 || /[\u0000-\u001f\u007f-\u009f]/.test(value)) { setError(T.invalid); return } clear(); setTransferId(null); setQuery(value || null); setRevision((value) => value + 1) }}>
        <fieldset disabled={frozen || candidates.isFetching}><legend>{T.search}</legend><Field label={T.search}>{(control) => <input {...control} type="search" maxLength={64} value={queryDraft} onChange={(event) => setQueryDraft(event.currentTarget.value)} />}</Field><Button type="submit">{T.searchSubmit}</Button></fieldset></form>
      {candidates.isPending ? <StateBlock subject={T.candidates} state={{ kind: 'initial-loading' }} /> : candidates.isError ? <StateBlock subject={T.candidates} state={readError(candidates.error)} onRetry={frozen ? undefined : () => { if (candidates.isFetchNextPageError) void candidates.fetchNextPage(); else void candidates.refetch() }} /> : candidateItems === null
        ? <StateBlock subject={T.candidates} state={{ kind: 'malformed-response' }} /> : candidateItems.length === 0 ? <p role="status">{T.noCandidates}</p>
          : <div className={styles.tableWrap}><table><caption>{T.candidates}</caption><thead><tr><th scope="col">{T.label}</th><th scope="col">{T.transfer}</th><th scope="col">{T.status}</th><th scope="col">{T.select}</th></tr></thead>
            <tbody>{candidateItems.map((row) => <tr key={row.transfer.transferId}><td>{row.labelCodes.join(', ')}</td><td>{row.sourceLocationCode} — {row.destinationLocationCode}<br />{row.sourceLocationName} — {row.destinationLocationName}</td>
              <td>{statusLabel(row.transfer.status)} — {T.transferVersion}: {row.transfer.version}</td><td><Button type="button" disabled={frozen || candidates.isFetching} onClick={() => selectTransfer(row.transfer.transferId)}>{T.open}: {row.labelCodes.join(', ')}</Button></td></tr>)}</tbody></table></div>}
      {candidates.hasNextPage ? <Button type="button" disabled={frozen || candidates.isFetching} onClick={() => void candidates.fetchNextPage()}>{candidates.isFetchNextPageError ? T.moreRetry : T.candidateMore}</Button> : null}
      {transferId === null ? null : workQuery.isFetching ? <StateBlock subject={T.work} state={{ kind: 'initial-loading' }} /> : work === null
        ? <StateBlock subject={T.work} state={classifyOutcome(workQuery.data ?? { type: 'malformed' })} onRetry={frozen ? undefined : () => void workQuery.refetch()} /> : <>
          <dl className={styles.facts}><div><dt>{T.transfer}</dt><dd>{work.transfer.transferId}</dd></div><div><dt>{T.transferVersion}</dt><dd>{work.transfer.version} — {statusLabel(work.transfer.status)}</dd></div></dl>
          {purpose === 'dispatch' ? <>
            <div onClickCapture={(event) => { if (cancellationPending()) { event.preventDefault(); event.stopPropagation(); setError(T.physicalPending) } }}
              onSubmitCapture={(event) => { if (cancellationPending()) { event.preventDefault(); event.stopPropagation(); setError(T.physicalPending) } }}>
              <TransferDispatchPanel transfer={work.transfer} onChanged={() => { setRevision((value) => value + 1); onChanged(work.transfer.transferId) }} /></div>
            <fieldset disabled={dispatchFrozen}><TransferCancelPanel transfer={work.transfer} binding={transferCancelBinding(shell, work.transfer.transferId)} canCancel={hasCapability(shell, INVENTORY_TRANSFER_CANCEL_CAPABILITY)}
              onChanged={() => { setRevision((value) => value + 1); onChanged(work.transfer.transferId) }} /></fieldset>
          </> : purpose === 'receive' ? <><p>{T.documentNote}</p>{documents.isPending ? <StateBlock subject={T.documents} state={{ kind: 'initial-loading' }} /> : documents.isError
            ? <StateBlock subject={T.documents} state={readError(documents.error)} onRetry={frozen ? undefined : () => { if (documents.isFetchNextPageError) void documents.fetchNextPage(); else void documents.refetch() }} /> : null}
            {documentItems === null ? <StateBlock subject={T.documents} state={{ kind: 'malformed-response' }} /> : documentItems.length === 0 && !documents.isPending && !documents.isError ? <p role="status">{T.emptyDocuments}</p> : null}
            {documents.isFetchingNextPage ? <p role="status">{T.moreLoading}</p> : null}
            {documents.hasNextPage ? <Button type="button" disabled={frozen || documents.isFetching} onClick={() => void documents.fetchNextPage()}>{documents.isFetchNextPageError ? T.moreRetry : T.more}</Button> : null}
            <fieldset disabled={!docsReady}><legend>{T.documents}</legend><Field label={T.document}>{(control) => <select {...control} value={documentId} onChange={(event) => { setDocumentId(event.currentTarget.value); setDocumentLineId(''); setReason(''); setError(null) }}>
              <option value="">{T.choose}</option>{documentItems?.map((row) => <option key={row.documentId} value={row.documentId}>{row.documentId} — {row.status === 'Draft' ? T.draft : T.effective} — {row.version}</option>)}</select>}</Field>
              {selectedDocument ? <><p>{T.sourceReceivedAt}: {selectedDocument.receivedAtUtc}</p>{selectedDocument.status === 'Draft' ? <form onSubmit={finalize}>{reasonField}<Button type="submit">{T.finalize}</Button></form>
                : <><Field label={T.documentLine}>{(control) => <select {...control} value={documentLineId} onChange={(event) => setDocumentLineId(event.currentTarget.value)}><option value="">{T.choose}</option>
                  {selectedDocument.lines.filter((row) => work.transfer.lines.some((line) => line.lineId === row.transferLineId && line.status === 'InTransit') && !work.facts.losses.some((loss) => loss.lineId === row.transferLineId && loss.status === 'Pending')).map((row) => <option key={row.documentLineId} value={row.documentLineId}>{row.labelId} — {T.received}: {row.receivedKg}</option>)}</select>}</Field>
                  {selectedDocumentLine ? <p>{T.received}: {selectedDocumentLine.receivedKg} — {selectedDocumentLine.overageReason ?? '—'}</p> : null}
                  {receivePieceLine ? <Field label={T.receivedPieces} help={T.dispatchedPieces(dispatchedPiecesOf(selectedDocumentLine!.transferLineId)!)}>{(control) =>
                    <input {...control} inputMode="numeric" required aria-required="true" value={pieces} maxLength={10} onChange={(event) => setPieces(event.currentTarget.value)} />}</Field> : null}<Button type="button" disabled={selectedDocumentLine === null} onClick={receive}>{T.receive}</Button></>}</> : null}
            </fieldset>
            {inTransit.length === 0 ? <p>{T.noTransit}</p> : <form className={styles.form} onSubmit={create}><fieldset disabled={!docsReady}><legend>{T.create}</legend>
              {inTransit.map((line, index) => { const draft = draftLines[line.lineId] ?? { selected: false, kg: '', reason: '' }; const update = (change: Partial<typeof draft>) => setDraftLines((rows) => ({ ...rows, [line.lineId]: { ...draft, ...change } })); return <div className={styles.lineCard} key={line.lineId}>
                <label><input type="checkbox" checked={draft.selected} onChange={(event) => update({ selected: event.currentTarget.checked })} />{T.select}: {T.line} {index + 1} — {line.labelId}</label><p>{T.dispatched}: {line.actualShippedKg}</p>
                <Field label={`${T.received} — ${index + 1}`} help={T.kgHelp}>{(control) => <input {...control} inputMode="decimal" value={draft.kg} maxLength={19} onChange={(event) => update({ kg: event.currentTarget.value })} />}</Field>
                <Field label={`${T.overageReason} — ${index + 1}`} help={T.overageHelp}>{(control) => <textarea {...control} value={draft.reason} maxLength={500} onChange={(event) => update({ reason: event.currentTarget.value })} />}</Field></div> })}
              <Field label={T.receivedAt}>{(control) => <input {...control} type="datetime-local" step="1" value={when} onChange={(event) => setWhen(event.currentTarget.value)} />}</Field><Button type="submit">{T.create}</Button></fieldset></form>}
            {rejectedOverages.length === 0 ? <p>{T.noRenewal}</p> : <form className={styles.form} onSubmit={(event) => action(event, 'overage-renewal')}><fieldset disabled={!ready}><legend>{T.renewOverage}</legend>
              <Field label={T.line}>{(control) => <select {...control} value={lineId} onChange={(event) => setLineId(event.currentTarget.value)}><option value="">{T.choose}</option>{rejectedOverages.map((row) => <option key={row.lineId} value={row.lineId}>{row.lineId} — {T.declaredOverage}: {row.overageKg}</option>)}</select>}</Field>
              <Field label={T.renewalKg} help={T.kgHelp}>{(control) => <input {...control} maxLength={19} inputMode="decimal" value={quantity} onChange={(event) => setQuantity(event.currentTarget.value)} />}</Field>
              {renewalPieceLine ? <Field label={T.renewalPieces}>{(control) => <input {...control} inputMode="numeric" required aria-required="true" value={pieces} maxLength={10}
                onChange={(event) => setPieces(event.currentTarget.value)} />}</Field> : null}{reasonField}{timeField}<Button type="submit">{T.renewOverage}</Button></fieldset></form>}
          </> : purpose === 'overage-decision' ? pendingOverages.length === 0 ? <p>{T.noPending}</p> : <form className={styles.form} onSubmit={(event) => action(event, 'overage-decision')}><fieldset disabled={!ready}><legend>{T.decideOverage}</legend><p>{T.makerChecker}</p>
            <Field label={T.pendingOverage}>{(control) => <select {...control} value={requestKey} onChange={(event) => setRequestKey(event.currentTarget.value)}><option value="">{T.choose}</option>{pendingOverages.map((row) => row && <option key={`${row.lineId}:${row.requestNumber}`} value={`${row.lineId}:${row.requestNumber}`}>{row.lineId} — {T.requestNumber} {row.requestNumber}</option>)}</select>}</Field>{decisionField}{reasonField}{timeField}<Button type="submit">{T.decideOverage}</Button></fieldset></form>
            : purpose === 'loss-request' ? <>{inTransit.length === 0 ? <p>{T.noTransit}</p> : <form className={styles.form} onSubmit={(event) => action(event, 'loss-request')}><fieldset disabled={!ready}><legend>{T.lossRequest}</legend>
              <Field label={T.line}>{(control) => <select {...control} value={lineId} onChange={(event) => setLineId(event.currentTarget.value)}><option value="">{T.choose}</option>{inTransit.map((line) => <option key={line.lineId} value={line.lineId}>{line.labelId} — {T.dispatched}: {line.actualShippedKg}</option>)}</select>}</Field>{reasonField}{timeField}<Button type="submit">{T.lossRequest}</Button></fieldset></form>}
              {pendingLosses.length === 0 ? <p>{T.noPending}</p> : <form className={styles.form} onSubmit={(event) => action(event, 'loss-withdrawal')}><fieldset disabled={!ready}><legend>{T.lossWithdraw}</legend>
                <Field label={T.pendingLoss}>{(control) => <select {...control} value={requestKey} onChange={(event) => setRequestKey(event.currentTarget.value)}><option value="">{T.choose}</option>{pendingLosses.map((row) => <option key={`${row.lineId}:${row.requestNumber}`} value={`${row.lineId}:${row.requestNumber}`}>{row.lineId} — {T.requestNumber} {row.requestNumber}</option>)}</select>}</Field>{reasonField}{timeField}<Button type="submit">{T.lossWithdraw}</Button></fieldset></form>}</>
              : pendingLosses.length === 0 ? <p>{T.noPending}</p> : <form className={styles.form} onSubmit={(event) => action(event, 'loss-decision')}><fieldset disabled={!ready}><legend>{T.lossDecision}</legend><p>{T.makerChecker}</p>
                <Field label={T.pendingLoss}>{(control) => <select {...control} value={requestKey} onChange={(event) => setRequestKey(event.currentTarget.value)}><option value="">{T.choose}</option>{pendingLosses.map((row) => <option key={`${row.lineId}:${row.requestNumber}`} value={`${row.lineId}:${row.requestNumber}`}>{row.lineId} — {T.requestNumber} {row.requestNumber}</option>)}</select>}</Field>{decisionField}{reasonField}{timeField}<Button type="submit">{T.lossDecision}</Button></fieldset></form>}
          <ReceiptHistory work={work} />
        </>}
    </>}
  </section>
}
function ReceiptHistory({ work }: { readonly work: ReceiptWork }) {
  return <><div className={styles.tableWrap}><table><caption>{T.receipts}</caption><thead><tr><th scope="col">{T.line}</th><th scope="col">{T.dispatched}</th><th scope="col">{T.received}</th><th scope="col">{T.shortage}</th><th scope="col">{T.overage}</th><th scope="col">{T.status}</th><th scope="col">{T.actor}</th></tr></thead>
    <tbody>{work.facts.receipts.map((row) => <tr key={row.lineId}><td>{row.lineId}</td><td>{row.dispatchedKg}{T.pieces(row.dispatchedPieceCount)}</td><td>{row.receivedKg}{T.pieces(row.receivedPieceCount)}</td><td>{row.shortageKg}{T.pieces(row.shortagePieceCount)}</td><td>{row.overageKg}{T.pieces(row.overagePieceCount)}</td><td>{statusLabel(row.overageStatus)}<br />{row.overageDecisionReason ?? row.overageReason ?? '—'}</td><td>{row.receivedBy}<br />{row.receivedAtUtc}<br />{row.overageDecidedBy ?? '—'}</td></tr>)}</tbody></table></div>
    <div className={styles.tableWrap}><table><caption>{T.losses}</caption><thead><tr><th scope="col">{T.line}</th><th scope="col">{T.requestNumber}</th><th scope="col">{T.explanation}</th><th scope="col">{T.status}</th><th scope="col">{T.actor}</th><th scope="col">{T.decisionActor}</th></tr></thead>
      <tbody>{work.facts.losses.map((row) => <tr key={`${row.lineId}:${row.requestNumber}`}><td>{row.lineId}</td><td>{row.requestNumber}</td><td>{row.explanation}</td><td>{statusLabel(row.status)}<br />{row.decisionReason ?? '—'}</td><td>{row.requestedBy}<br />{row.requestedAtUtc}</td><td>{row.decidedBy ?? '—'}<br />{row.decidedAtUtc ?? '—'}</td></tr>)}</tbody></table></div>
    <div className={styles.tableWrap}><table><caption>{T.renewals}</caption><thead><tr><th scope="col">{T.line}</th><th scope="col">{T.requestNumber}</th><th scope="col">{T.overage}</th><th scope="col">{T.reason}</th><th scope="col">{T.status}</th><th scope="col">{T.actor}</th></tr></thead>
      <tbody>{work.facts.overageRenewals.map((row) => <tr key={`${row.lineId}:${row.requestNumber}`}><td>{row.lineId}</td><td>{row.requestNumber}</td><td>{row.overageKg}</td><td>{row.reason}</td><td>{statusLabel(row.status)}<br />{row.decisionReason ?? '—'}</td><td>{row.requestedBy}<br />{row.requestedAtUtc}<br />{row.decidedBy ?? '—'}</td></tr>)}</tbody></table></div></>
}
