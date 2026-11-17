import { useLayoutEffect, useRef, useState, useSyncExternalStore, type FormEvent, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { beginTransferDepartureAttempt, fetchTransferDepartureCandidates, fetchTransferDepartureFacts, finishTransferDepartureAttempt,
  leaveTransferDepartureAttempt, recordTransferDeparture, subscribeTransferDepartureAttempt, transferDepartureAttempt,
  transferDepartureBinding, transferDepartureFrozen, transferDepartureKilograms, transferDepartureUtc,
  TRANSFER_DEPARTURE_CAPABILITY, type RecordTransferDeparture, type TransferDepartureFacts } from './transferDepartureApi'
import { TRANSFER_DEPARTURE_STATUS_TEXT as STATUS, TRANSFER_DEPARTURE_TEXT as T } from './transferDepartureMessages.tr'
import { formatInstant } from './format'
import styles from './LogisticsPages.module.css'

function Frame({ children }: { readonly children: ReactNode }) {
  return <section className={styles.page} aria-labelledby="transfer-departure-heading">
    <header><h1 id="transfer-departure-heading" className={styles.title}>{T.title}</h1><p>{T.intro}</p></header>{children}
  </section>
}
export function TransferDeparturePage() {
  const shell = useShellState(); const key = contextKey(shell)
  const company = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  if (key === null || company === null) return <Frame><StateBlock subject={T.subject} state={{ kind: 'no-authorized-company' }} /></Frame>
  if (!hasCapability(shell, TRANSFER_DEPARTURE_CAPABILITY)) return <Frame><StateBlock subject={T.subject} state={{ kind: 'forbidden' }} /></Frame>
  const namespace = contextKeyString(key)
  return <Screen key={JSON.stringify([namespace, shell.sessionEpoch, shell.routeEntryGeneration])} company={company} namespace={namespace} shell={shell} />
}
function Screen({ company, namespace, shell }: { readonly company: string; readonly namespace: string; readonly shell: ShellState }) {
  const boundary = useAuthBoundary(); const binding = transferDepartureBinding(shell)
  const attempt = useSyncExternalStore((callback) => subscribeTransferDepartureAttempt(binding, callback),
    () => transferDepartureAttempt(binding), () => transferDepartureAttempt(null))
  const frozen = transferDepartureFrozen(attempt); const alive = useRef(false)
  useLayoutEffect(() => { alive.current = true; return () => { alive.current = false; leaveTransferDepartureAttempt(binding) } }, [binding])
  const [transfer, setTransfer] = useState(''); const [kind, setKind] = useState(''); const [occurred, setOccurred] = useState('')
  const [reason, setReason] = useState(''); const [direction, setDirection] = useState(''); const [firstKg, setFirstKg] = useState(''); const [secondKg, setSecondKg] = useState('')
  const [localError, setLocalError] = useState<string | null>(null)
  function resetForm() { setKind(''); setOccurred(''); setReason(''); setDirection(''); setFirstKg(''); setSecondKg(''); setLocalError(null) }
  const candidates = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'transfer-departure', 'candidates'], retry: false,
    queryFn: async ({ signal }) => {
      const security = boundary.capture(); const result = await fetchTransferDepartureCandidates(company, signal)
      if (result.type === 'http') boundary.report(result.status, security)
      return result
    },
  })
  const choices = candidates.data?.type === 'ok-single' ? candidates.data.data : null
  const selected = choices?.items.find((item) => item.transferId === transfer) ?? null
  const facts = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'transfer-departure', transfer, selected?.version, selected?.sourceLocationId], retry: false,
    enabled: selected !== null && !candidates.isFetching,
    queryFn: async ({ signal }) => {
      if (selected === null) return { type: 'malformed' as const }
      const security = boundary.capture(); const result = await fetchTransferDepartureFacts(company, selected, signal)
      if (result.type === 'http') boundary.report(result.status, security)
      return result
    },
  })
  const view = selected !== null && facts.data?.type === 'ok-single' ? facts.data.data : null
  const canEdit = view !== null && view.association !== 0 && view.version !== null && !view.noFurtherExit
    && !facts.isFetching && !candidates.isFetching && !frozen
  const canRecord = canEdit && (kind === '1' ? view?.exitWeighingAtUtc === null : kind === '2' ? view?.vehicleExitAtUtc === null : true)
  async function send(transferId: string, sourceLocationId: string, payload: RecordTransferDeparture) {
    const snapshot = beginTransferDepartureAttempt(binding, company, transferId, sourceLocationId, payload); if (snapshot === null) return
    const security = boundary.capture(); const outcome = await recordTransferDeparture(snapshot)
    if (outcome.type === 'refused') boundary.report(outcome.status, security)
    finishTransferDepartureAttempt(snapshot, outcome)
    if (!alive.current) return
    if (outcome.type === 'applied' || outcome.type === 'refused' && (outcome.status === 409 || outcome.status === 404) && !transferDepartureFrozen(transferDepartureAttempt(binding))) {
      resetForm(); if (selected !== null) void facts.refetch()
    }
  }
  function submit(event: FormEvent) {
    event.preventDefault()
    if (!canRecord || view === null || view.version === null) return
    const trimmed = reason.trim(); const date = occurred === '' ? null : new Date(occurred)
    const at = date !== null && Number.isFinite(date.getTime()) ? date.toISOString() : null
    if ((kind !== '1' && kind !== '2') || !transferDepartureUtc(at) || trimmed === '' || trimmed.length > 4000 || /[\u0000-\u001f\u007f-\u009f]/.test(trimmed)
      || kind === '1' && ((direction !== '1' && direction !== '2') || !transferDepartureKilograms(firstKg) || !transferDepartureKilograms(secondKg))) {
      setLocalError(T.invalid); return
    }
    void send(view.transferId, view.sourceLocationId, { expectedVersion: String(view.version), kind: kind === '1' ? 1 : 2,
      occurredAtUtc: at, reason: trimmed, weighing: kind === '1' ? { direction: direction === '1' ? 1 : 2, firstKg, secondKg } : null })
  }
  return <Frame>
    {attempt.phase === 'sending' ? <p role="status">{T.pending}</p> : null}
    {attempt.phase === 'completed' ? <p role="status">{attempt.replayed ? T.replayed : T.saved}</p> : null}
    {attempt.phase === 'unknown' || attempt.phase === 'failed' && attempt.unresolved ? <div className={styles.notice} data-tone="unknown">
      <p role="alert">{T.unknown}</p>
      <StateBlock subject={T.subject} state={attempt.phase === 'unknown' ? { kind: attempt.reason } : classifyOutcome({ type: 'http', status: attempt.status })} />
      <Button type="button" onClick={() => void send(attempt.snapshot.transferId, attempt.snapshot.sourceLocationId, attempt.snapshot.payload)}>{T.retry}</Button>
    </div> : null}
    {attempt.phase === 'failed' && !attempt.unresolved ? attempt.status === 400 || attempt.status === 422
      ? <p role="alert">{T.refused}</p> : <StateBlock subject={T.subject} state={classifyOutcome({ type: 'http', status: attempt.status })} /> : null}
    {localError === null ? null : <p role="alert">{localError}</p>}
    <Button type="button" disabled={frozen || candidates.isFetching || facts.isFetching} onClick={() => {
      resetForm(); void candidates.refetch(); if (selected !== null) void facts.refetch()
    }}>{T.refresh}</Button>
    {candidates.isFetching || choices === null ? <StateBlock subject={T.candidates} state={candidates.isFetching ? { kind: 'initial-loading' }
      : classifyOutcome(candidates.data ?? { type: 'malformed' })} onRetry={() => void candidates.refetch()} /> : <>
      {choices.windowFull ? <p role="status">{T.windowFull}</p> : null}
      {choices.items.length === 0 ? <p role="status">{T.empty}</p> : <Field label={T.transfer}>{(control) => <select {...control}
        value={transfer} disabled={frozen} onChange={(event) => { setTransfer(event.currentTarget.value); resetForm() }}>
        <option value="">{T.choose}</option>{choices.items.map((item) => <option key={item.transferId} value={item.transferId}>
          {item.sourceLocationCode} — {item.sourceLocationName} → {item.destinationLocationCode} — {item.destinationLocationName} · {STATUS[item.status]} · {item.transferId}
        </option>)}
      </select>}</Field>}
      {selected === null ? choices.items.length > 0 ? <p>{T.selection}</p> : null : facts.isFetching || view === null ? <StateBlock subject={T.subject}
        state={facts.isFetching ? { kind: 'initial-loading' } : classifyOutcome(facts.data ?? { type: 'malformed' })} onRetry={() => void facts.refetch()} /> : <>
        <Facts view={view} />
        {view.association === 0 ? <p role="alert">{T.legacy}</p> : view.noFurtherExit ? <p role="alert">{T.closed}</p>
          : view.exitWeighingAtUtc !== null && view.vehicleExitAtUtc !== null ? <p role="status">{T.bothRecorded}</p> : <form onSubmit={submit}>
            <fieldset className={styles.fieldset} disabled={!canEdit}>
              <legend>{T.subject}</legend>
              <Field label={T.kind}>{(control) => <select {...control} value={kind} onChange={(event) => { setKind(event.currentTarget.value); setLocalError(null) }}>
                <option value="">{T.chooseKind}</option><option value="1" disabled={view.exitWeighingAtUtc !== null}>{T.weighing}</option>
                <option value="2" disabled={view.vehicleExitAtUtc !== null}>{T.vehicle}</option>
              </select>}</Field>
              <Field label={T.occurred}>{(control) => <input {...control} type="datetime-local" step="1" value={occurred} onChange={(event) => setOccurred(event.currentTarget.value)} />}</Field>
              {kind === '1' ? <>
                <Field label={T.direction}>{(control) => <select {...control} value={direction} onChange={(event) => setDirection(event.currentTarget.value)}>
                  <option value="">{T.chooseDirection}</option><option value="1">{T.tareFirst}</option><option value="2">{T.grossFirst}</option>
                </select>}</Field>
                <Field label={T.firstKg} help={T.weighingHelp}>{(control) => <input {...control} type="text" inputMode="decimal" maxLength={19} value={firstKg} onChange={(event) => setFirstKg(event.currentTarget.value)} />}</Field>
                <Field label={T.secondKg}>{(control) => <input {...control} type="text" inputMode="decimal" maxLength={19} value={secondKg} onChange={(event) => setSecondKg(event.currentTarget.value)} />}</Field>
              </> : null}
              <Field label={T.reason}>{(control) => <input {...control} maxLength={4000} value={reason} onChange={(event) => setReason(event.currentTarget.value)} />}</Field>
            </fieldset><Button type="submit" variant="primary" disabled={!canRecord}>{T.save}</Button>
          </form>}
      </>}
    </>}
  </Frame>
}
function Facts({ view }: { readonly view: TransferDepartureFacts }) {
  return <dl className={styles.facts}>
    <div><dt>{T.version}</dt><dd>{view.version ?? '—'}</dd></div>
    <div><dt>{T.association}</dt><dd>{view.association === 0 ? '—' : view.association === 1 ? T.notLinked : T.linked}</dd></div>
    <div><dt>{T.weighing}</dt><dd>{view.exitWeighingAtUtc === null ? T.notRecorded : formatInstant(view.exitWeighingAtUtc)}</dd></div>
    <div><dt>{T.vehicle}</dt><dd>{view.vehicleExitAtUtc === null ? T.notRecorded : formatInstant(view.vehicleExitAtUtc)}</dd></div>
    {view.officialDocument === null ? null : <div><dt>{T.official}</dt><dd>{view.officialDocument.officialDocumentId} — {view.officialDocument.isSentToGib ? T.gibSent : T.gibNotSent}</dd></div>}
  </dl>
}
