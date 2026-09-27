/**
 * DEC-SAL-003 ("auditlidir"): the delivery-schedule history of the draft — schedule 0 as created, then every recorded
 * change with who made it and when, as the server answers it. Read under the draft read; mounted only after the parent
 * detail was read in the same context, and re-read when the detail's version moves (a recorded change).
 */
import { useId } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchDraftDeliverySchedules } from './api'
import { SALES_CAPABILITY } from './capabilities'
import type { DeliverySchedule } from './contract'
import { formatBusinessDate, formatInstant } from './format'
import { ORDER_DETAIL_TEXT } from './salesText'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import { classifyOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import styles from './SalesPages.module.css'

const T = ORDER_DETAIL_TEXT.deliveryScheduleHistory

export function SalesDraftDeliveryScheduleHistory({ draftId, draftVersion }: {
  readonly draftId: string
  readonly draftVersion: string
}) {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  const companyId = shell.activeContext?.scopeMode === 'single' ? shell.activeContext.companyId : null
  const canRead = namespace !== null && companyId !== null && hasCapability(shell, SALES_CAPABILITY.draftsDetail)
  const boundary = useAuthBoundary()
  const headingId = useId()
  const query = useQuery({
    queryKey: [namespace ?? 'no-context', shell.routeEntryGeneration, 'sales-draft-delivery-schedules', draftId, draftVersion],
    enabled: canRead,
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: () => undefined,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftDeliverySchedules(draftId, companyId as string, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })
  if (!canRead) return null
  const outcome = query.data
  return (
    <section className={styles.section} aria-labelledby={headingId} data-testid="sales-delivery-schedule-history">
      <h2 id={headingId}>{T.heading}</h2>
      <p className={styles.muted}>{T.note}</p>
      {outcome?.type === 'ok-single' ? (
        outcome.data.items.map((schedule) => <Schedule key={schedule.scheduleNo} schedule={schedule} />)
      ) : (
        <StateBlock
          state={outcome === undefined ? { kind: 'initial-loading' } : classifyOutcome(outcome)}
          subject={T.subject}
          onRetry={() => void query.refetch()}
        />
      )}
    </section>
  )
}

const dateText = (value: string | null) => (value === null ? T.none : formatBusinessDate(value))

function Schedule({ schedule: s }: { readonly schedule: DeliverySchedule }) {
  const headingId = useId()
  return (
    <section className={styles.line} aria-labelledby={headingId} data-testid={`sales-delivery-schedule-${s.scheduleNo}`}>
      <h3 className={styles.lineTitle} id={headingId}>{s.scheduleNo === 0 ? T.original : T.change(s.scheduleNo)}</h3>
      <dl className={styles.facts}>
        <div><dt>{T.changedBy}</dt><dd>{s.changedBy ?? T.notChanged}</dd></div>
        <div>
          <dt>{T.changedAt}</dt>
          <dd>{s.changedAtUtc === null ? T.notChanged : <time dateTime={s.changedAtUtc}>{formatInstant(s.changedAtUtc)}</time>}</dd>
        </div>
        <div><dt>{T.headerDate}</dt><dd data-testid={`sales-delivery-schedule-${s.scheduleNo}-header`}>{dateText(s.requestedDeliveryDate)}</dd></div>
      </dl>
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <caption className={styles.caption}>{T.caption(s.scheduleNo)}</caption>
          <thead><tr>
            <th scope="col">{T.lineNo}</th><th scope="col">{T.lineDate}</th><th scope="col">{T.slices}</th>
          </tr></thead>
          <tbody>{s.lines.map((line) => (
            <tr key={line.lineId} data-testid={`sales-delivery-schedule-${s.scheduleNo}-line-${line.lineNo}`}>
              <th scope="row">{line.lineNo}</th>
              <td>{dateText(line.requestedDeliveryDate)}</td>
              <td>{line.slices.length === 0 ? T.noSlices
                : line.slices.map((slice) => `${slice.quantity} → ${formatBusinessDate(slice.requestedDeliveryDate)}`).join(' · ')}</td>
            </tr>
          ))}</tbody>
        </table>
      </div>
    </section>
  )
}
