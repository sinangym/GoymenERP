import { useLayoutEffect, useRef, useState, useSyncExternalStore, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability, type ShellState } from '../shell/shellState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { Frame } from './SalesPolicyParts'
import { beginWarehouseScaleAttempt, fetchTransferWarehouses, fetchWarehouseScale, finishWarehouseScaleAttempt,
  leaveWarehouseScaleAttempt, recordWarehouseScale, subscribeWarehouseScaleAttempt, warehouseScaleAttempt,
  warehouseScaleBinding, warehouseScaleFrozen, WAREHOUSE_TRANSFER_SCALE_CAPABILITY,
  type RecordWarehouseScale, type WarehouseScaleView } from './warehouseTransferScaleApi'
import { WAREHOUSE_TRANSFER_SCALE_TEXT as T } from './warehouseTransferScaleMessages.tr'
import styles from './settings.module.css'

export function WarehouseTransferScalePage() {
  const shell = useShellState(); const key = contextKey(shell)
  const company = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  if (key === null || company === null) return <Frame title={T.title} intro={T.intro}><StateBlock subject={T.subject} state={{ kind: 'no-authorized-company' }} /></Frame>
  if (!hasCapability(shell, WAREHOUSE_TRANSFER_SCALE_CAPABILITY)) return <Frame title={T.title} intro={T.intro}><StateBlock subject={T.subject} state={{ kind: 'forbidden' }} /></Frame>
  const namespace = contextKeyString(key)
  return <Screen key={JSON.stringify([namespace, shell.sessionEpoch, shell.routeEntryGeneration])} company={company} namespace={namespace} shell={shell} />
}
function Screen({ company, namespace, shell }: { readonly company: string; readonly namespace: string; readonly shell: ShellState }) {
  const boundary = useAuthBoundary(); const binding = warehouseScaleBinding(shell)
  const attempt = useSyncExternalStore((callback) => subscribeWarehouseScaleAttempt(binding, callback),
    () => warehouseScaleAttempt(binding), () => warehouseScaleAttempt(null))
  const frozen = warehouseScaleFrozen(attempt); const alive = useRef(false)
  useLayoutEffect(() => { alive.current = true; return () => { alive.current = false; leaveWarehouseScaleAttempt(binding) } }, [binding])
  const [input, setInput] = useState(''); const [search, setSearch] = useState<{ q: string | null; revision: number }>({ q: null, revision: 0 })
  const [location, setLocation] = useState(''); const [value, setValue] = useState(''); const [reason, setReason] = useState('')
  const [localError, setLocalError] = useState<string | null>(null)
  const candidates = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'warehouse-transfer-scale', 'choices', search], retry: false,
    queryFn: async ({ signal }) => {
      const security = boundary.capture(); const result = await fetchTransferWarehouses(company, search.q, signal)
      if (result.type === 'http') boundary.report(result.status, security)
      return result
    },
  })
  const choices = candidates.data?.type === 'ok-single' ? candidates.data.data : null
  const selected = choices?.items.find((item) => item.locationId === location) ?? null
  const policy = useQuery({ queryKey: [namespace, shell.routeEntryGeneration, 'warehouse-transfer-scale', location], retry: false,
    enabled: selected !== null && !candidates.isFetching,
    queryFn: async ({ signal }) => {
      const security = boundary.capture(); const result = await fetchWarehouseScale(company, location, signal)
      if (result.type === 'http') boundary.report(result.status, security)
      return result
    },
  })
  const view = selected !== null && policy.data?.type === 'ok-single' ? policy.data.data : null
  const canEdit = view !== null && view.policy.status !== 3 && !policy.isFetching && !candidates.isFetching && !frozen
  async function send(locationId: string, payload: RecordWarehouseScale) {
    const snapshot = beginWarehouseScaleAttempt(binding, company, locationId, payload); if (snapshot === null) return
    const security = boundary.capture(); const outcome = await recordWarehouseScale(snapshot)
    if (outcome.type === 'refused') boundary.report(outcome.status, security)
    finishWarehouseScaleAttempt(snapshot, outcome)
    if (!alive.current) return
    if (outcome.type === 'applied' || outcome.type === 'refused' && (outcome.status === 409 || outcome.status === 404) && !warehouseScaleFrozen(warehouseScaleAttempt(binding))) {
      setReason(''); setValue(''); setLocalError(null)
      if (selected !== null) void policy.refetch()
    }
  }
  function submit(event: FormEvent) {
    event.preventDefault()
    if (!canEdit || view === null) return
    const trimmed = reason.trim()
    if ((value !== 'on' && value !== 'off') || trimmed === '' || trimmed.length > 4000 || /[\u0000-\u001f\u007f-\u009f]/.test(trimmed)) {
      setLocalError(T.required); return
    }
    void send(view.locationId, { expectedVersion: view.versions[0]?.version ?? '0', requireExitWeighing: value === 'on', reason: trimmed })
  }
  return <Frame title={T.title} intro={T.intro}>
    {attempt.phase === 'sending' ? <p role="status">{T.pending}</p> : null}
    {attempt.phase === 'completed' ? <p role="status">{attempt.replayed ? T.replayed : T.saved}</p> : null}
    {attempt.phase === 'unknown' || attempt.phase === 'failed' && attempt.unresolved ? <div>
      <p role="alert">{T.unknown}</p>
      <StateBlock subject={T.subject} state={attempt.phase === 'unknown' ? { kind: attempt.reason } : classifyOutcome({ type: 'http', status: attempt.status })} />
      <Button type="button" onClick={() => void send(attempt.snapshot.locationId, attempt.snapshot.payload)}>{T.retry}</Button>
    </div> : null}
    {attempt.phase === 'failed' && !attempt.unresolved ? attempt.status === 400
      ? <p role="alert">{T.refused}</p> : <StateBlock subject={T.subject} state={classifyOutcome({ type: 'http', status: attempt.status })} /> : null}
    {localError === null ? null : <p role="alert">{localError}</p>}
    <form onSubmit={(event) => {
      event.preventDefault(); if (frozen) return
      if (input.trim().length > 200 || /[\u0000-\u001f\u007f-\u009f]/.test(input.trim())) { setLocalError(T.invalidQuery); return }
      setLocation(''); setValue(''); setReason(''); setLocalError(null); setSearch({ q: input.trim() || null, revision: search.revision + 1 })
    }}>
      <Field label={T.query}>{(control) => <input {...control} type="search" maxLength={200} value={input} disabled={frozen}
        onChange={(event) => setInput(event.currentTarget.value)} />}</Field>
      <Button type="submit" disabled={frozen || candidates.isFetching}>{T.search}</Button>
    </form>
    {candidates.isFetching || choices === null ? <StateBlock subject={T.warehouses} state={candidates.isFetching ? { kind: 'initial-loading' }
      : classifyOutcome(candidates.data ?? { type: 'malformed' })} onRetry={() => void candidates.refetch()} /> : <>
      {choices.searchWindowFull ? <p role="status">{T.windowFull}</p> : null}
      {choices.items.length === 0 ? <p role="status">{T.empty}</p> : <Field label={T.warehouse}>{(control) => <select {...control}
        value={location} disabled={frozen} onChange={(event) => { setLocation(event.currentTarget.value); setValue(''); setReason(''); setLocalError(null) }}>
        <option value="">{T.choose}</option>{choices.items.map((item) => <option key={item.locationId} value={item.locationId}>
          {item.locationCode} — {item.locationName} ({item.siteCode} — {item.siteName})</option>)}
      </select>}</Field>}
      {selected === null ? <p>{T.selection}</p> : policy.isFetching || view === null ? <StateBlock subject={T.subject}
        state={policy.isFetching ? { kind: 'initial-loading' } : classifyOutcome(policy.data ?? { type: 'malformed' })} onRetry={() => void policy.refetch()} />
        : view.policy.status === 3 ? <StateBlock subject={T.subject} state={{ kind: 'service-unavailable' }} /> : <>
          <p role="status">{view.policy.status === 1 ? T.notConfigured : T.configured}</p>
          <p>{T.value}: {view.policy.requireExitWeighing ? T.enabled : T.disabled}</p>
          <Button type="button" disabled={frozen} onClick={() => void policy.refetch()}>{T.refresh}</Button>
          <form className={styles.form} onSubmit={submit}><fieldset disabled={!canEdit}>
            <Field label={T.value}>{(control) => <select {...control} value={value} onChange={(event) => setValue(event.currentTarget.value)}>
              <option value="">{T.chooseValue}</option><option value="on">{T.enabled}</option><option value="off">{T.disabled}</option>
            </select>}</Field>
            <Field label={T.reason}>{(control) => <input {...control} value={reason} maxLength={4000} onChange={(event) => setReason(event.currentTarget.value)} />}</Field>
          </fieldset><Button type="submit" variant="primary" disabled={!canEdit}>{T.save}</Button></form>
          <History view={view} />
        </>}
    </>}
  </Frame>
}
function History({ view }: { readonly view: WarehouseScaleView }) {
  return view.versions.length === 0 ? <p>{T.noHistory}</p> : <table className={styles.table}><caption>{T.history}</caption>
    <thead><tr><th scope="col">{T.version}</th><th scope="col">{T.value}</th><th scope="col">{T.who}</th><th scope="col">{T.when}</th><th scope="col">{T.reason}</th></tr></thead>
    <tbody>{view.versions.map((item) => <tr key={item.version}><td>{item.version}</td><td>{item.requireExitWeighing ? T.enabled : T.disabled}</td>
      <td>{item.recordedBy}</td><td>{item.recordedAtUtc}</td><td>{item.reason}</td></tr>)}</tbody></table>
}
