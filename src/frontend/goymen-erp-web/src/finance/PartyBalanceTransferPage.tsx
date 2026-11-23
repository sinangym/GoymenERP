import { useCallback, useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { fetchCounterparties } from '../cari/api'
import { fetchDocuments, documentsQueryKey } from '../documents/api'
import { OWNER_KIND } from '../documents/contract'
import { DocumentsPanel } from '../documents/DocumentsPanel'
import { useShellState } from '../shell/shellContext'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { useFinanceBinding } from './financePort'
import { currencyShown, WithCurrencyCodes } from './currencyCodes'
import { formatDecimalTextTr } from './decimalText'
import { isDateOnly } from './dateOnly'
import { fetchBalanceTransferPreview, recordBalanceTransfer } from './PartyBalanceTransferHttp'
import type { BalanceTransferSelection, BalanceTransferView } from './PartyBalanceTransferContract'
import { balanceTransferSlot, markBalanceTransferUnknown, recallBalanceTransfer, recoverBalanceTransfer,
  reserveBalanceTransfer, retryBalanceTransfer, settleBalanceTransfer, type BalanceTransferAttempt } from './PartyBalanceTransferAttempt'
import { BALANCE_TRANSFER_TEXT as T } from './PartyBalanceTransferText'
import styles from './Finance.module.css'

interface AccountChoice { readonly partyId: string; readonly accountId: string; readonly label: string }
function ReadStatus<T>({ outcome, loading }: { readonly outcome?: ViewOutcome<T>; readonly loading: boolean }) {
  if (loading) return <StateBlock subject={T.heading} state={{ kind: 'initial-loading' }} />
  if (outcome === undefined || outcome.type === 'ok' || outcome.type === 'ok-single') return null
  return <StateBlock subject={T.heading} state={classifyOutcome(outcome)} />
}
function PartyChoice({ companyId, namespace, predecessor, frozen, selected, onSelected }: {
  readonly companyId: string; readonly namespace: string; readonly predecessor: boolean; readonly frozen: boolean
  readonly selected: AccountChoice | null; readonly onSelected: (choice: AccountChoice | null) => void
}) {
  const boundary = useAuthBoundary()
  const [text, setText] = useState('')
  const [query, setQuery] = useState('')
  const [cursors, setCursors] = useState<readonly (string | null)[]>([null])
  const cursor = cursors[cursors.length - 1] ?? null
  const read = useQuery({ queryKey: [namespace, 'finance-balance-transfer-parties', predecessor, query, cursor],
    queryFn: async ({ signal }) => {
      const fence = boundary.capture()
      const answer = await fetchCounterparties({ mode: 'single', companyId }, {
        q: query, partyState: predecessor ? 'all' : 'active', accountState: predecessor ? 'all' : 'active',
        pageSize: 50, ...(cursor === null ? {} : { cursor }),
      }, signal)
      if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, fence)
      return answer
    },
  })
  const page = !read.isFetching && read.data?.type === 'ok-single' ? read.data.data : null
  const rows = page === null || Array.isArray(page) ? [] : page.items.flatMap((party) => party.companyAccounts
    .filter((account) => account.companyId === companyId && (predecessor ? !party.isActive || !account.isActive : party.isActive && account.isActive))
    .map((account) => ({ partyId: party.id, accountId: account.id, label: `${party.code} — ${party.legalName}` })))
  if (selected !== null && !rows.some((row) => row.accountId === selected.accountId)) rows.push(selected)
  return <fieldset disabled={frozen}>
    <legend>{predecessor ? T.predecessor : T.successor}</legend>
    <label>{T.search}<input value={text} onChange={(event) => setText(event.target.value)} /></label>
    <button type="button" onClick={() => { setQuery(text); setCursors([null]); onSelected(null) }}>{T.search}</button>
    <ReadStatus outcome={read.data} loading={read.isFetching} />
    {page !== null && rows.length === 0 ? <StateBlock subject={T.heading} state={{ kind: 'empty-filtered' }} /> : null}
    <select aria-label={predecessor ? T.predecessor : T.successor} value={selected?.accountId ?? ''}
      onChange={(event) => onSelected(rows.find((row) => row.accountId === event.target.value) ?? null)}>
      <option value="">{T.choose}</option>{rows.map((row) => <option value={row.accountId} key={row.accountId}>{row.label}</option>)}
    </select>
    <button type="button" disabled={cursors.length < 2 || read.isFetching} onClick={() => setCursors((values) => values.slice(0, -1))}>{T.previous}</button>
    <button type="button" disabled={page === null || Array.isArray(page) || page.nextCursor === null || read.isFetching}
      onClick={() => { if (page !== null && !Array.isArray(page) && page.nextCursor !== null) setCursors((values) => [...values, page.nextCursor]) }}>{T.next}</button>
  </fieldset>
}

export function PartyBalanceTransferPage({ canRecord }: { readonly canRecord: boolean }) {
  const bound = useFinanceBinding()
  const shell = useShellState()
  if (!canRecord) return <StateBlock subject={T.heading} state={{ kind: 'forbidden' }} />
  if (bound === null) return <StateBlock subject={T.heading} state={{ kind: 'no-authorized-company' }} />
  return <TransferScreen key={`${bound.namespace}:${shell.routeEntryGeneration}`} bound={bound} entry={shell.routeEntryGeneration} />
}
function TransferScreen({ bound, entry }: { readonly bound: NonNullable<ReturnType<typeof useFinanceBinding>>; readonly entry: number }) {
  const { companyId } = bound.binding
  const slot = balanceTransferSlot(bound.binding)
  const boundary = useAuthBoundary()
  const client = useQueryClient()
  const mounted = useRef(false)
  const [predecessor, setPredecessor] = useState<AccountChoice | null>(null)
  const [successor, setSuccessor] = useState<AccountChoice | null>(null)
  const [selection, setSelection] = useState<BalanceTransferSelection | null>(null)
  const [previewGeneration, setPreviewGeneration] = useState(0)
  const [documentId, setDocumentId] = useState('')
  const [occurredOn, setOccurredOn] = useState('')
  const [reason, setReason] = useState('')
  const [attempt, setAttempt] = useState<BalanceTransferAttempt | null>(() => recoverBalanceTransfer(slot))
  const [message, setMessage] = useState<string | null>(null)
  const [saved, setSaved] = useState<BalanceTransferView | null>(null)
  const frozen = attempt !== null
  useEffect(() => { mounted.current = true; return () => { mounted.current = false } }, [])
  const report = useCallback(async <TValue,>(action: () => Promise<ViewOutcome<TValue>>) => {
    const fence = boundary.capture()
    const answer = await action()
    if (answer.type === 'http' && (answer.status === 401 || answer.status === 403)) boundary.report(answer.status, fence)
    return answer
  }, [boundary])
  const preview = useQuery({ queryKey: [bound.namespace, 'finance', 'party-balance-transfer-preview', entry, selection, previewGeneration],
    enabled: selection !== null, gcTime: 0,
    queryFn: ({ signal }) => report(() => fetchBalanceTransferPreview(companyId, selection!, signal)),
  })
  const snapshot = selection !== null && !preview.isFetching && preview.data?.type === 'ok-single' ? preview.data.data : null
  const documents = useQuery({
    queryKey: documentsQueryKey(bound.namespace, entry, OWNER_KIND.counterpartyParty, predecessor?.partyId ?? ''),
    enabled: predecessor !== null,
    queryFn: ({ signal }) => report(() => fetchDocuments(OWNER_KIND.counterpartyParty, predecessor!.partyId, companyId, signal)),
  })
  const documentRows = !documents.isFetching && documents.data?.type === 'ok-single'
    ? documents.data.data.items.filter((item) => item.downloadable) : []
  const document = documentRows.find((item) => item.id === documentId) ?? null
  const reset = () => { setSelection(null); setMessage(null); setSaved(null); setDocumentId('') }
  /**
   * `final` (K3 P2-1): the user's explicit way out of an unknown attempt. The same key and body are sent once more;
   * any decided server answer closes the attempt (a replay shows the recorded transfer). Only no answer or a 401/403
   * — the session or authority, not this attempt — keeps it open, so leaving never mints a second key blindly.
   */
  const run = async (value: BalanceTransferAttempt, wasUnknown: boolean, final = false) => {
    const fence = boundary.capture()
    const reply = await recordBalanceTransfer(companyId, value.key, value.request)
    if (reply.kind === 'refused' && (reply.status === 401 || reply.status === 403)) boundary.report(reply.status, fence)
    if (!mounted.current || recallBalanceTransfer(slot)?.key !== value.key) return
    if (reply.kind === 'answered' && reply.result.outcome === 'Succeeded' && reply.result.resource !== null) {
      settleBalanceTransfer(slot, value.key); setAttempt(null); setSaved(reply.result.resource); setSelection(null); setMessage(T.success)
      void client.invalidateQueries({ queryKey: [bound.namespace, 'finance'] })
    } else if (final && (reply.kind === 'unknown' || (reply.kind === 'refused' && (reply.status === 401 || reply.status === 403)))) {
      setAttempt(markBalanceTransferUnknown(slot, value.key)); setMessage(T.finalOpen)
    } else if (final) {
      settleBalanceTransfer(slot, value.key); setAttempt(null); setSelection(null); setMessage(T.abandoned)
      void client.invalidateQueries({ queryKey: [bound.namespace, 'finance'] })
    } else if (reply.kind === 'unknown' || (wasUnknown && (reply.kind !== 'answered' || !reply.result.replayed))) {
      setAttempt(markBalanceTransferUnknown(slot, value.key)); setMessage(T.unknown)
    } else {
      settleBalanceTransfer(slot, value.key); setAttempt(null); setSelection(null)
      setMessage(reply.kind === 'answered' && reply.result.outcome === 'VersionConflict' ? T.conflict : T.refused)
    }
  }
  const submit = () => {
    if (snapshot === null || !snapshot.canRecord || selection === null || document === null || !isDateOnly(occurredOn) || reason.trim() === '' || frozen) return
    const value = reserveBalanceTransfer(slot, { transferId: crypto.randomUUID(), selection, expectedSnapshotVersion: snapshot.snapshotVersion,
      occurredOn, reason: reason.trim(), evidence: { documentId: document.id, versionNo: document.versionNo, sha256: document.sha256 } })
    if (value === null) { setAttempt(recoverBalanceTransfer(slot)); return }
    setAttempt(value); void run(value, false)
  }
  return <section className={styles.panel} aria-labelledby="party-balance-transfer-heading">
    <h2 id="party-balance-transfer-heading">{T.heading}</h2><p>{T.explanation}</p>
    <PartyChoice companyId={companyId} namespace={bound.namespace} predecessor frozen={frozen} selected={predecessor}
      onSelected={(value) => { setPredecessor(value); reset() }} />
    <PartyChoice companyId={companyId} namespace={bound.namespace} predecessor={false} frozen={frozen} selected={successor}
      onSelected={(value) => { setSuccessor(value); reset() }} />
    <button type="button" disabled={frozen || predecessor === null || successor === null || predecessor.accountId === successor.accountId}
      onClick={() => { if (predecessor && successor) { setSaved(null); setSelection({ predecessorPartyId: predecessor.partyId,
        predecessorAccountId: predecessor.accountId, successorPartyId: successor.partyId, successorAccountId: successor.accountId }); setPreviewGeneration((value) => value + 1) } }}>{T.preview}</button>
    {selection === null ? null : <ReadStatus outcome={preview.data} loading={preview.isFetching} />}
    {snapshot === null ? null : <>
      <p>{snapshot.ownerProof.predecessorCode} — {snapshot.ownerProof.predecessorName} → {snapshot.ownerProof.successorCode} — {snapshot.ownerProof.successorName}</p>
      <WithCurrencyCodes>{(codes) => <table><caption>{T.balance}</caption><thead><tr><th scope="col">{T.currency}</th><th scope="col">{T.balance}</th><th scope="col">{T.receivable}</th><th scope="col">{T.payable}</th></tr></thead>
        <tbody>{snapshot.currencies.map((item) => <tr key={item.currencyId}><td>{currencyShown(item.currencyId, codes)}</td>
          <td>{T.sides[item.side]} {formatDecimalTextTr(item.amount)}</td><td>{formatDecimalTextTr(item.openReceivable)}</td><td>{formatDecimalTextTr(item.openPayable)}</td></tr>)}</tbody></table>}</WithCurrencyCodes>
      {!snapshot.canRecord ? <p>{T.empty}</p> : null}
    </>}
    {predecessor === null ? null : <>
      <DocumentsPanel ownerKind={OWNER_KIND.counterpartyParty} ownerId={predecessor.partyId} companyId={companyId}
        namespace={bound.namespace} entryGeneration={entry} heading={T.documents} />
      <ReadStatus outcome={documents.data} loading={documents.isFetching} />
      <label>{T.evidence}<select value={documentId} disabled={frozen} onChange={(event) => setDocumentId(event.target.value)}>
        <option value="">{T.choose}</option>{documentRows.map((item) => <option key={item.id} value={item.id}>{item.fileName} — v{item.versionNo}</option>)}
      </select></label>{documentRows.length === 0 ? <p>{T.noDocument}</p> : null}
    </>}
    <label>{T.occurredOn}<input type="date" value={occurredOn} disabled={frozen} onChange={(event) => setOccurredOn(event.target.value)} /></label>
    <label>{T.reason}<textarea value={reason} maxLength={1000} disabled={frozen} onChange={(event) => setReason(event.target.value)} /></label>
    {attempt?.phase === 'unknown' ? <>
      <button type="button" onClick={() => { const next = retryBalanceTransfer(slot); if (next) { setAttempt(next); void run(next, true) } }}>{T.retry}</button>
      <button type="button" aria-describedby="party-balance-transfer-final-help"
        onClick={() => { const next = retryBalanceTransfer(slot); if (next) { setAttempt(next); void run(next, true, true) } }}>{T.finalCheck}</button>
      <p id="party-balance-transfer-final-help">{T.finalCheckHelp}</p>
    </>
      : <button type="button" disabled={frozen || snapshot === null || !snapshot.canRecord || document === null || !isDateOnly(occurredOn) || reason.trim() === ''} onClick={submit}>{T.submit}</button>}
    {attempt?.phase === 'unknown' && message === null ? <p role="status">{T.unknown}</p> : null}
    {message === null ? null : <p role="status">{message}</p>}
    {saved === null ? null : <p>{saved.ownerProof.predecessorName} → {saved.ownerProof.successorName} — {saved.occurredOn}</p>}
  </section>
}
