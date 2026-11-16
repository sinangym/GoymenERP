import { useState } from 'react'
import type { ProductionPlanView } from './contract'
import { newCommandId } from './commandId'
import { reapprovePlan, type PlanReapprovalSnapshot } from './planCommandApi'
import { PLAN_REAPPROVAL_TEXT as T } from './manufacturingText'
import { Button } from '../ui/Button'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { PlanInputAvailabilityPanel } from './PlanInputAvailabilityPanel'

// Like pendingFactStore, an unknown outcome keeps its exact key and selection across route remounts in this session.
const pending = new Map<string, PlanReapprovalSnapshot>()

export function PlanReapprovalSection({ plan, companyId, namespace, canApprove, onApplied }: {
  readonly plan: ProductionPlanView
  readonly companyId: string
  readonly namespace: string
  readonly canApprove: boolean
  readonly onApplied: (plan: ProductionPlanView) => Promise<void>
}) {
  const boundary = useAuthBoundary()
  const storeKey = `${namespace}:${companyId}:${plan.planId}`
  const [selection, setSelection] = useState<readonly string[]>(() => pending.get(storeKey)?.selectedLabelIds ?? [])
  const [attempt, setAttempt] = useState<PlanReapprovalSnapshot | null>(() => pending.get(storeKey) ?? null)
  const [sending, setSending] = useState(false)
  const [message, setMessage] = useState<string | null>(() => pending.has(storeKey) ? T.pending : null)
  const [refreshGeneration, setRefreshGeneration] = useState(0)
  const send = async (snapshot: PlanReapprovalSnapshot) => {
    const captured = boundary.capture()
    setSending(true); setAttempt(snapshot); pending.set(storeKey, snapshot)
    const result = await reapprovePlan(companyId, plan.planId, snapshot)
    setSending(false)
    if (result.type === 'unknown') { setMessage(T.pending); return }
    pending.delete(storeKey); setAttempt(null)
    if (result.type === 'applied') { setMessage(T.applied); await onApplied(result.resource); return }
    if (result.status === 401 || result.status === 403) boundary.report(result.status, captured)
    setRefreshGeneration((current) => current + 1)
    setMessage(result.code === 'manufacturing_plan_reapproval_settings_changed' ? T.changedSettings
      : result.code === 'manufacturing_plan_reapproval_label_mismatch' ? T.changedLabels : T.refused)
  }
  return <section aria-label={T.heading}>
    <PlanInputAvailabilityPanel key={`${companyId}:${plan.planId}:${plan.version}`}
      companyId={companyId} planId={plan.planId} planVersion={plan.version} refreshGeneration={refreshGeneration} />
    {(plan.reapprovals ?? []).length > 0 ? <>
      <h3>{T.history}</h3>
      <ul>{plan.reapprovals!.map((entry) => <li key={entry.number}>
        {T.historyLine(entry.number, entry.reapprovedAtUtc, entry.planVersion)}
      </li>)}</ul>
    </> : null}
    {plan.requiresReapproval && canApprove ? <>
      <h3>{T.heading}</h3><p>{T.help}</p>
      {plan.inputs.map((input) => <label key={input.parentRollId}>
        <input type="checkbox" checked={selection.includes(input.parentRollId)} disabled={sending || attempt !== null}
          onChange={(event) => setSelection((current) => event.target.checked
            ? [...current, input.parentRollId] : current.filter((id) => id !== input.parentRollId))} />
        {input.parentRollId} ({input.plannedKg} kg)
      </label>)}
      {attempt !== null ? <Button type="button" disabled={sending} onClick={() => void send(attempt)}>{T.resend}</Button>
        : <Button type="button" disabled={sending} onClick={() => {
          if (selection.length !== plan.inputs.length) { setMessage(T.chooseAll); return }
          void send({ idempotencyKey: newCommandId(), expectedVersion: plan.version, selectedLabelIds: [...selection] })
        }}>{T.submit}</Button>}
    </> : null}
    {message !== null ? <p role="status">{message}</p> : null}
  </section>
}
