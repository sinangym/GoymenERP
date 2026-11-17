import { useLayoutEffect, useRef, useState, useSyncExternalStore, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { beginTransferPlanAttempt, differentTransferPlanTarget, fetchTransferPlanDestinations, fetchTransferPlanLabels,
  finishTransferPlanAttempt, leaveTransferPlanAttempt, positiveTransferPlanKg, sameTransferPlanSource, sendTransferPlan,
  subscribeTransferPlanAttempt, transferPlanAttempt, transferPlanBinding, transferPlanFrozen, TRANSFER_PLAN_CAPABILITY,
  type TransferPlanDestination, type TransferPlanLabel, type TransferPlanSnapshot } from './transferPlanApi'
import { transferDispatchUtc } from './transferDispatchApi'
import { TRANSFER_PLAN_TEXT as T } from './transferPlanText'
import styles from './inventory.module.css'

interface Props { readonly onPlanned: (transferId: string) => void }
export function TransferPlanPanel(props: Props) {
  const shell = useShellState(); const key = contextKey(shell)
  if (key === null || shell.activeContext?.scopeMode !== 'single' || shell.activeContext.companyId === null)
    return <StateBlock subject={T.heading} state={{ kind: 'no-authorized-company' }} />
  if (!hasCapability(shell, TRANSFER_PLAN_CAPABILITY)) return null
  const namespace = contextKeyString(key)
  return <Workspace key={JSON.stringify([namespace, shell.sessionEpoch, shell.routeEntryGeneration])} {...props} shell={shell}
    namespace={namespace} companyId={shell.activeContext.companyId} actorId={shell.activeContext.userId} />
}
function Workspace({ onPlanned, shell, namespace, companyId, actorId }: Props & {
  readonly shell: ShellState; readonly namespace: string; readonly companyId: string; readonly actorId: string
}) {
  const boundary = useAuthBoundary(); const binding = transferPlanBinding(shell)
  const attempt = useSyncExternalStore((callback) => subscribeTransferPlanAttempt(binding, callback),
    () => transferPlanAttempt(binding), () => transferPlanAttempt(null))
  const frozen = transferPlanFrozen(attempt); const alive = useRef(false)
  useLayoutEffect(() => { alive.current = true; return () => { alive.current = false; leaveTransferPlanAttempt(binding) } }, [binding])
  const [labelDraft, setLabelDraft] = useState(''); const [labelSearch, setLabelSearch] = useState<string | null>(null)
  const [destinationDraft, setDestinationDraft] = useState(''); const [destinationSearch, setDestinationSearch] = useState<string | null>(null)
  const [selected, setSelected] = useState<readonly TransferPlanLabel[]>([])
  const [target, setTarget] = useState<TransferPlanDestination | null>(null)
  const [plannedAt, setPlannedAt] = useState(''); const [error, setError] = useState<string | null>(null)
  const labels = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'transfer-plan-labels', labelSearch], retry: false,
    queryFn: async ({ signal }) => {
      const security = boundary.capture(); const result = await fetchTransferPlanLabels(companyId, labelSearch, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, security)
      return result
    } })
  const destinations = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'transfer-plan-destinations', destinationSearch], retry: false,
    queryFn: async ({ signal }) => {
      const security = boundary.capture(); const result = await fetchTransferPlanDestinations(companyId, destinationSearch, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, security)
      return result
    } })
  const sourceRows = labels.data?.type === 'ok-single' ? labels.data.data : null
  const targetRows = destinations.data?.type === 'ok-single' ? destinations.data.data : null
  const currentSelection = selected.every((row) => sourceRows?.items.some((current) => JSON.stringify(current) === JSON.stringify(row)))
  const currentTarget = target === null || targetRows?.items.some((current) => JSON.stringify(current) === JSON.stringify(target)) === true
  const stale = !currentSelection || !currentTarget
  const ready = !frozen && !labels.isFetching && !destinations.isFetching && sourceRows !== null && targetRows !== null && !stale
  function search(event: FormEvent, side: 'labels' | 'destinations') {
    event.preventDefault(); if (frozen) return
    const value = (side === 'labels' ? labelDraft : destinationDraft).trim()
    if (value.length > (side === 'labels' ? 64 : 200) || /[\u0000-\u001f\u007f-\u009f]/.test(value)) { setError(T.queryInvalid); return }
    setError(null)
    if (side === 'labels') { setSelected([]); setLabelSearch(value === '' ? null : value); if ((value || null) === labelSearch) void labels.refetch() }
    else { setTarget(null); setDestinationSearch(value === '' ? null : value); if ((value || null) === destinationSearch) void destinations.refetch() }
  }
  function refresh() {
    if (frozen) return
    setSelected([]); setTarget(null); setError(null); void labels.refetch(); void destinations.refetch()
  }
  function select(row: TransferPlanLabel, checked: boolean) {
    if (frozen || labels.isFetching) return
    if (!checked) { setSelected((rows) => rows.filter((current) => current.labelId !== row.labelId)); setError(null); return }
    if (selected.length !== 0 && !sameTransferPlanSource(selected[0], row)) { setError(T.sourceMismatch); return }
    setSelected((rows) => rows.some((current) => current.labelId === row.labelId) ? rows : [...rows, row]); setError(null)
  }
  async function execute(snapshot: TransferPlanSnapshot) {
    const security = boundary.capture(); const outcome = await sendTransferPlan(snapshot)
    if (outcome.type === 'refused' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, security)
    finishTransferPlanAttempt(snapshot, outcome)
    if (!alive.current || outcome.type !== 'applied') return
    setError(null); setSelected([]); setTarget(null); setPlannedAt(''); void labels.refetch(); void destinations.refetch()
    onPlanned(outcome.transfer.transferId)
  }
  function submit(event: FormEvent) {
    event.preventDefault(); if (!ready) return
    const parsed = plannedAt === '' ? null : new Date(plannedAt)
    const at = parsed !== null && Number.isFinite(parsed.getTime()) ? parsed.toISOString() : null
    if (selected.length === 0 || target === null || !differentTransferPlanTarget(selected[0], target) || !transferDispatchUtc(at)) { setError(T.invalid); return }
    const snapshot = beginTransferPlanAttempt(binding, companyId, actorId, selected, target, at)
    if (snapshot === null) { setError(T.invalid); return }
    setError(null); void execute(snapshot)
  }
  function retry(snapshot: TransferPlanSnapshot) {
    if (snapshot.actorId !== actorId || snapshot.companyId.toLowerCase() !== companyId.toLowerCase() || snapshot.binding !== binding) return
    const retained = beginTransferPlanAttempt(binding, companyId, actorId, snapshot.labels, snapshot.destination, snapshot.payload.plannedAtUtc)
    if (retained !== null) void execute(retained)
  }
  return <section aria-label={T.heading} className={styles.lineCard}>
    <h2>{T.heading}</h2><p>{T.intro}</p>
    {attempt.phase === 'sending' ? <p role="status">{T.pending}</p> : null}
    {attempt.phase === 'completed' ? <div className={styles.successCard}><p role="status">{attempt.replayed ? T.replayed : T.saved}</p>
      <dl className={styles.facts}><div><dt>{T.planId}</dt><dd>{attempt.snapshot.payload.transferId}</dd></div>
        <div><dt>{T.selected}</dt><dd>{attempt.snapshot.labels.map((row) => row.labelCode).join(', ')}</dd></div>
        <div><dt>{T.destination}</dt><dd>{attempt.snapshot.destination.locationCode} — {attempt.snapshot.destination.locationName}</dd></div></dl></div> : null}
    {attempt.phase === 'unknown' || attempt.phase === 'failed' && attempt.unresolved ? <div className={styles.warningCard}>
      <p role="alert">{T.unknown}</p><dl className={styles.facts}><div><dt>{T.planId}</dt><dd>{attempt.snapshot.payload.transferId}</dd></div>
        <div><dt>{T.selected}</dt><dd>{attempt.snapshot.labels.map((row) => row.labelCode).join(', ')}</dd></div>
        <div><dt>{T.destination}</dt><dd>{attempt.snapshot.destination.locationCode} — {attempt.snapshot.destination.locationName}</dd></div></dl>
      <StateBlock subject={T.heading} state={attempt.phase === 'unknown' ? { kind: attempt.reason } : classifyOutcome({ type: 'http', status: attempt.status })} />
      <Button type="button" onClick={() => retry(attempt.snapshot)}>{T.retry}</Button></div> : null}
    {attempt.phase === 'failed' && !attempt.unresolved ? [400, 415, 422].includes(attempt.status) ? <p role="alert">{T.refused}</p>
      : <StateBlock subject={T.heading} state={classifyOutcome({ type: 'http', status: attempt.status })} /> : null}
    {error === null ? null : <p role="alert">{error}</p>}
    {!frozen && stale ? <p role="alert">{T.stale}</p> : null}
    <div className={styles.actions}><Button type="button" disabled={frozen || labels.isFetching || destinations.isFetching} onClick={refresh}>{T.refresh}</Button></div>
    <form className={styles.form} onSubmit={(event) => search(event, 'labels')}><fieldset disabled={frozen || labels.isFetching}>
      <legend>{T.labels}</legend><Field label={T.labelQuery}>{(control) => <input {...control} maxLength={64} type="search" value={labelDraft}
        onChange={(event) => setLabelDraft(event.currentTarget.value)} />}</Field><Button type="submit">{T.search}</Button></fieldset></form>
    {labels.isFetching ? <StateBlock subject={T.labels} state={{ kind: 'initial-loading' }} /> : sourceRows === null
      ? <StateBlock subject={T.labels} state={classifyOutcome(labels.data ?? { type: 'malformed' })} onRetry={frozen ? undefined : refresh} />
      : sourceRows.items.length === 0 ? <p role="status">{T.noLabels}</p> : <>
        {sourceRows.searchWindowFull ? <p role="status">{T.windowFull}</p> : null}
        <div className={styles.tableWrap}><table><caption>{T.labels}</caption><thead><tr><th>{T.select}</th><th>{T.label}</th><th>{T.physical}</th>
          <th>{T.blocked}</th><th>{T.allocated}</th><th>{T.version}</th><th>{T.source}</th></tr></thead><tbody>{sourceRows.items.map((row) => <tr key={row.labelId}>
            <td><input type="checkbox" aria-label={`${T.select}: ${row.labelCode}`} checked={selected.some((current) => current.labelId === row.labelId)}
              disabled={frozen || !positiveTransferPlanKg(row.physicalKg)} onChange={(event) => select(row, event.currentTarget.checked)} /></td>
            <td>{row.labelCode}<br />{row.documentStatus === 'Complete' ? T.complete : T.documentsPending}</td><td>{row.physicalKg}</td><td>{row.blockedPhysicalKg}</td>
            <td>{row.activeAllocationKg}</td><td>{row.labelVersion}</td><td>{row.sourceLocationId}<br />{row.sourceCustodianKind === 'Company' ? T.companyCustodian : T.partyCustodian}<br />{row.sourceCustodianId}</td>
          </tr>)}</tbody></table></div></>}
    <form className={styles.form} onSubmit={(event) => search(event, 'destinations')}><fieldset disabled={frozen || destinations.isFetching}>
      <legend>{T.destinations}</legend><Field label={T.destinationQuery}>{(control) => <input {...control} maxLength={200} type="search" value={destinationDraft}
        onChange={(event) => setDestinationDraft(event.currentTarget.value)} />}</Field><Button type="submit">{T.search}</Button></fieldset></form>
    {destinations.isFetching ? <StateBlock subject={T.destinations} state={{ kind: 'initial-loading' }} /> : targetRows === null
      ? <StateBlock subject={T.destinations} state={classifyOutcome(destinations.data ?? { type: 'malformed' })} onRetry={frozen ? undefined : refresh} />
      : targetRows.items.length === 0 ? <p role="status">{T.noDestinations}</p> : targetRows.searchWindowFull ? <p role="status">{T.windowFull}</p> : null}
    <form className={styles.form} onSubmit={submit}><fieldset disabled={!ready}><legend>{T.submit}</legend><p>{T.selection}</p>
      {selected.length > 0 ? <p>{T.selected}: {selected.map((row) => row.labelCode).join(', ')}</p> : null}
      <Field label={T.destination}>{(control) => <select {...control} value={target?.locationId ?? ''} onChange={(event) => {
        setTarget(targetRows?.items.find((row) => row.locationId === event.currentTarget.value) ?? null); setError(null)
      }}><option value="">{T.choose}</option>{targetRows?.items.map((row) => <option key={row.locationId} value={row.locationId}>
        {row.siteCode} / {row.locationCode} — {row.locationName} ({row.custodianKind === 'Company' ? T.companyCustodian : T.partyCustodian})
      </option>)}</select>}</Field>
      <Field label={T.plannedAt}>{(control) => <input {...control} type="datetime-local" step="1" value={plannedAt} onChange={(event) => setPlannedAt(event.currentTarget.value)} />}</Field>
      <Button type="submit" variant="primary">{T.submit}</Button></fieldset></form>
  </section>
}
