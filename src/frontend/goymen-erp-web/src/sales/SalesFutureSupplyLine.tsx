import { useId, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { fetchFutureAllocations } from '../inventory/futureAllocationApi'
import { inventoryFutureAllocationPath } from '../inventory/capabilities'
import { FUTURE_ALLOCATION_TEXT as F } from '../inventory/futureAllocationText'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { SALES_FUTURE_SUPPLY_TEXT as T } from './salesFutureSupplyText'

interface Binding {
  readonly canRead: boolean; readonly companyId: string; readonly orderId: string; readonly lineId: string
  readonly namespace: string; readonly actorId: string; readonly routeEntry: number
}

/** F17-5 reads the actual Inventory owner. Expanding one line never queries all order lines. */
export function SalesFutureSupplyLine(props: Binding) {
  return props.canRead ? <AuthorizedLine key={`${props.namespace}:${props.actorId}:${props.routeEntry}:${props.orderId}:${props.lineId}`} {...props} /> : null
}

function AuthorizedLine({ companyId, orderId, lineId, namespace, actorId, routeEntry }: Binding) {
  const [open, setOpen] = useState(false)
  const regionId = useId()
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace, actorId, routeEntry, companyId, orderId, lineId, 'sales-future-supply'],
    enabled: open, retry: false, staleTime: 0, gcTime: 0, refetchOnWindowFocus: false,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchFutureAllocations(companyId, orderId, lineId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const data = query.data?.type === 'ok-single' ? query.data.data : null
  return <div>
    <Button type="button" aria-expanded={open} aria-controls={regionId} onClick={() => setOpen(value => !value)}>{open ? T.hide : T.show}</Button>
    {!open ? null : <section id={regionId} aria-label={F.title}>
      <p>{T.explanation}</p>
      <Link to={inventoryFutureAllocationPath(orderId, lineId)}>{T.choose}</Link>
      {data === null ? <StateBlock state={classifyOutcome(query.isPending ? { type: 'loading' } : query.data ?? { type: 'malformed' })} subject={F.subject} onRetry={() => void query.refetch()} /> : <>
        <p>{F.evaluatedOn}: {data.evaluatedOn}</p>
        {data.windowFull ? <p role="status">{F.window}</p> : null}
        {data.items.length === 0 ? <StateBlock state={{ kind: 'empty-no-data' }} subject={F.subject} /> : <ul>{data.items.map(item => {
          const schedule = data.schedules[item.allocationId]
          return <li key={item.allocationId}>
            <p>{F.status}: {F.statuses[item.status]} — {F.remaining}: {item.remainingQuantity} {data.unitCodes[item.unitId]}</p>
            <p>{F.delivery}: {schedule.status === 'Scheduled'
              ? <>{schedule.deliveryDate} — {schedule.isOverdue ? F.overdue : F.onTime}</>
              : schedule.status === 'Undated' ? F.undated : schedule.status === 'Inactive' ? F.inactiveSchedule : F.scheduleUnavailable}</p>
            {item.releaseReasonCode === null ? null : <p role="status">{T.warning}: {T.reasons[item.releaseReasonCode] ?? T.otherRelease}{item.releaseReason === null ? '' : ` — ${item.releaseReason}`}</p>}
          </li>
        })}</ul>}
      </>}
    </section>}
  </div>
}
