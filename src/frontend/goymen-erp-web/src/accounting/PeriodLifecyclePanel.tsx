import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellDispatch, useShellState } from '../shell/shellContext'
import { hasCapability } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { ACCOUNTING_CAPABILITY } from './capabilities'
import type { PeriodView } from './contract'
import { fetchPeriodLifecycle, submitPeriodReopening, type PeriodLifecycle, type PeriodReopeningAttempt, type PeriodReopeningOutcome } from './periodLifecycleApi'
import { PERIOD_LIFECYCLE_TEXT as T } from './periodLifecycleText'
import styles from './Accounting.module.css'

const pending = new Map<string, PeriodReopeningAttempt>()
const pendingRows = new Map<string, PeriodView>()
export function pendingReopeningPeriod(namespace: string | null): PeriodView | null {
  return namespace === null ? null : pendingRows.get(namespace) ?? null
}
export function PeriodLifecyclePanel({ row, rows, namespace, disabled, onFrozenChange, onChanged }: {
  readonly row: PeriodView; readonly rows: readonly PeriodView[]; readonly namespace: string; readonly disabled: boolean
  readonly onFrozenChange: (frozen: boolean) => void; readonly onChanged: () => Promise<void>
}) {
  const shell = useShellState(); const dispatch = useShellDispatch(); const boundary = useAuthBoundary(); const company = shell.activeContext?.companyId ?? ''
  const actor = shell.activeContext?.userId ?? ''; const slot = `${namespace}:${row.periodId}`
  const held = pending.get(slot) ?? null
  const [attempt, setAttempt] = useState<PeriodReopeningAttempt | null>(held)
  const [outcome, setOutcome] = useState<PeriodReopeningOutcome | null>(held === null ? null : { type: 'unknown' })
  const [reason, setReason] = useState(''); const [invalid, setInvalid] = useState(false); const [sending, setSending] = useState(false)
  const inFlight = useRef(false); const mounted = useRef(true)
  const frozen = sending || outcome?.type === 'unknown'
  const canPropose = hasCapability(shell, ACCOUNTING_CAPABILITY.periodsPropose)
  const canApprove = hasCapability(shell, ACCOUNTING_CAPABILITY.periodsReopen) // ROL-D12: reopening has its own name
  useEffect(() => { onFrozenChange(frozen) }, [frozen, onFrozenChange])
  useEffect(() => { dispatch({ type: frozen ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope: 'accounting.period-reopening' }) }, [frozen, dispatch])
  useEffect(() => () => dispatch({ type: 'DIRTY_CLEAR', scope: 'accounting.period-reopening' }), [dispatch])
  useEffect(() => { mounted.current = true; return () => { mounted.current = false; onFrozenChange(false) } }, [onFrozenChange])
  const query = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'period-lifecycle', row.periodId, row.version],
    queryFn: async ({ signal }) => {
      const security = boundary.capture(); const answer = await fetchPeriodLifecycle(company, row.periodId, signal)
      if (answer.type === 'http' && [401, 403].includes(answer.status)) boundary.report(answer.status as 401 | 403, security)
      return answer
    } })
  const send = async (next: PeriodReopeningAttempt, retry = false) => {
    if (inFlight.current || disabled || outcome?.type === 'unknown' && !retry
      || next.operation === 'propose' && !canPropose || next.operation === 'approve' && !canApprove) return
    inFlight.current = true; pending.set(slot, next); pendingRows.set(namespace, row); setAttempt(next); setSending(true); setOutcome(null)
    try {
      const security = boundary.capture(); const answer = await submitPeriodReopening(company, next)
      if (answer.type === 'refused' && [401, 403].includes(answer.status)) boundary.report(answer.status as 401 | 403, security)
      const resolved = retry && answer.type === 'refused' && [401, 403].includes(answer.status) ? { type: 'unknown' as const } : answer
      if (resolved.type !== 'unknown') { pending.delete(slot); pendingRows.delete(namespace) }
      if (mounted.current) {
        setOutcome(resolved)
        if (resolved.type === 'applied' || resolved.type === 'refused' && resolved.status === 409) {
          await onChanged(); await query.refetch()
        }
      }
    } finally { inFlight.current = false; if (mounted.current) setSending(false) }
  }
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as RemoteViewState<PeriodLifecycle>
  const data = state.kind === 'ready' ? state.data : null
  const current = data?.closures.find(close => close.closedPeriodVersion === row.version && close.reopeningId === null)
  const proposal = current === undefined ? undefined : data?.reopenings.find(item => item.closureId === current.id)
  const submit = (event: FormEvent) => {
    event.preventDefault(); if (frozen || disabled || !canPropose || current === undefined || proposal !== undefined) return
    if (!reason.trim() || reason.trim().length > 1000) { setInvalid(true); return }
    setInvalid(false)
    void send({ operation: 'propose', periodId: row.periodId, key: crypto.randomUUID(),
      request: { expectedVersion: row.version, proposalId: crypto.randomUUID(), reason: reason.trim() } })
  }
  return <section aria-labelledby="period-lifecycle-title"><h2 id="period-lifecycle-title">{T.title}: {row.startsOn} – {row.endsOn}</h2><p>{T.intro}</p>
    {outcome?.type === 'unknown' ? <div role="alert"><p>{T.unknown}</p><button type="button" disabled={sending || disabled || attempt === null}
      onClick={() => attempt !== null && void send(attempt, true)}>{T.resend}</button></div> : null}
    {outcome?.type === 'refused' ? <p role="alert">{T.refused} ({outcome.status})</p> : null}
    {outcome?.type === 'applied' ? <p role="status">{T.applied}</p> : null}
    {state.kind !== 'ready' ? <StateBlock state={state} subject="dönem kapanış geçmişi" onRetry={() => void query.refetch()} />
      : <><ul>{state.data.closures.map(close => { const target = rows.find(period => period.periodId === close.targetPeriodId)
        return <li key={close.id}>{T.closed}: {close.closedAtUtc}; {T.count}: {close.lineCount}
          {target === undefined ? null : <>; {T.target}: {target.startsOn} – {target.endsOn}</>}
          {close.reopeningId === null ? null : <p>{T.reversed}: {close.reopenedAtUtc}</p>}</li> })}</ul>
        {state.data.closures.length === 0 ? <p>{T.empty}</p> : null}
        {row.status !== 'Closed' ? null : current === undefined ? <p role="note">{T.missing}</p>
          : proposal !== undefined ? <><p>{proposal.reason}</p><p>{T.pending}</p>
            {proposal.proposedBy.toLowerCase() === actor.toLowerCase() ? <p>{T.own}</p> : canApprove
              ? <button type="button" disabled={frozen || disabled} onClick={() => void send({ operation: 'approve', periodId: row.periodId,
                proposalId: proposal.id, key: crypto.randomUUID(), request: { expectedVersion: proposal.version, expectedPeriodVersion: row.version } })}>{T.approve}</button>
              : <p>{T.noAuthority}</p>}</>
          : canPropose ? <form onSubmit={submit} className={styles.form}><label>{T.reason}<textarea value={reason} maxLength={1000}
            disabled={frozen || disabled} onChange={event => setReason(event.target.value)} /></label>
            {invalid ? <p role="alert">{T.invalid}</p> : null}<button type="submit" disabled={frozen || disabled}>{T.propose}</button></form>
            : <p>{T.noAuthority}</p>}</>}
  </section>
}
