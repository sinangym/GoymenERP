import { useEffect, useRef, useState } from 'react'
import { fetchPlanInputAvailability, type PlanInputAvailability } from './planInputAvailabilityApi'
import { PLAN_INPUT_AVAILABILITY_TEXT as T } from './manufacturingText'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './OrderDetailPage.module.css'

export function PlanInputAvailabilityPanel({ companyId, planId, planVersion, refreshGeneration = 0 }: {
  readonly companyId: string
  readonly planId: string
  readonly planVersion: number
  readonly refreshGeneration?: number
}) {
  const boundary = useAuthBoundary()
  const boundaryRef = useRef(boundary); boundaryRef.current = boundary
  const [outcome, setOutcome] = useState<ViewOutcome<PlanInputAvailability>>({ type: 'loading' })
  const [retry, setRetry] = useState(0)
  useEffect(() => {
    const controller = new AbortController()
    const captured = boundaryRef.current.capture()
    setOutcome({ type: 'loading' })
    void fetchPlanInputAvailability(companyId, planId, planVersion, controller.signal).then((result) => {
      if (controller.signal.aborted) return
      if (result.type === 'http' && (result.status === 401 || result.status === 403))
        boundaryRef.current.report(result.status, captured)
      setOutcome(result)
    })
    return () => controller.abort()
  }, [companyId, planId, planVersion, refreshGeneration, retry])
  const state = classifyOutcome(outcome) as RemoteViewState<PlanInputAvailability>
  return <section aria-label={T.heading}>
    <h3>{T.heading}</h3><p>{T.help}</p>
    {state.kind !== 'ready' ? <StateBlock state={state} subject={T.subject} onRetry={() => setRetry((n) => n + 1)} /> : <>
      <div className={styles.tableWrap}><table className={styles.table}>
        <caption>{T.caption}</caption>
        <thead><tr>{[T.label, T.requested, T.physical, T.blocked, T.allocated, T.free, T.missing, T.positions]
          .map((text) => <th key={text} scope="col">{text}</th>)}</tr></thead>
        <tbody>{state.data.inputs.map((input) => <tr key={input.labelId}>
          <th scope="row">{input.labelId}<br />{T.version}: {input.labelVersion}</th>
          <td>{input.requestedKg}</td><td>{input.physicalKg}</td><td>{input.blockedKg}</td>
          <td>{input.allocatedKg}</td><td>{input.freeKg}</td><td>{input.missingKg}</td>
          <td>{input.freePositionCount}{input.freePositionCount !== 1 ? ` — ${T.positionRefusal}` : ''}</td>
        </tr>)}</tbody>
      </table></div>
      <p>{T.readAt}: {state.data.readAtUtc}</p>
      <Button type="button" onClick={() => setRetry((n) => n + 1)}>{T.refresh}</Button>
      {state.data.cancelledOrders.length > 0 ? <>
        <h3>{T.cancelledHistory}</h3><ul>{state.data.cancelledOrders.map((entry) => <li key={entry.orderId}>
          {T.cancelledLine(entry.orderId, entry.planVersionAfterCancel)}
        </li>)}</ul>
      </> : null}
    </>}
  </section>
}
