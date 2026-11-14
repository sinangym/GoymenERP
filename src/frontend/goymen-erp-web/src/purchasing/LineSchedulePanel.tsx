import { useId, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import type { PurchaseOrderDeliverySlice, PurchaseOrderLine, PurchaseOrderSummary } from './contract'
import { fetchPurchaseOrderLineScheduleHistory } from './lineScheduleApi'
import type { PurchaseOrderLineScheduleHistory } from './lineScheduleContract'
import { LINE_SCHEDULE_TEXT as T } from './lineScheduleText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './PurchaseOrderDetailPage.module.css'

export interface LineSchedulePanelProps {
  /** The query namespace and route entry this panel was opened in; a change drops any late answer. */
  readonly fenceKey: string
  readonly companyId: string
  readonly order: PurchaseOrderSummary
  readonly lines: readonly PurchaseOrderLine[]
}

/** Presentation only: the wire's `yyyy-MM-dd` in Turkish order; anything else is shown as received. */
function formatDate(value: string): string {
  const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)
  return parts === null ? value : `${parts[3]}.${parts[2]}.${parts[1]}`
}

const SLICE_COLUMNS = [{ name: T.sliceNo }, { name: T.quantity, numeric: true }, { name: T.deliveryDate }] as const

function SliceTable({ caption, slices }: { readonly caption: string; readonly slices: readonly PurchaseOrderDeliverySlice[] }) {
  return <div className={styles.tableWrap}>
    <table className={styles.table} role="table" aria-label={caption}>
      <thead role="rowgroup"><tr role="row">{SLICE_COLUMNS.map((column) =>
        <th key={column.name} role="columnheader" scope="col" className={'numeric' in column ? styles.number : undefined}>{column.name}</th>)}</tr></thead>
      <tbody role="rowgroup">{slices.map((slice) => <tr key={slice.sliceId} role="row">
        <td role="cell" data-label={T.sliceNo}>{slice.sliceNo}</td>
        <td role="cell" data-label={T.quantity} className={styles.number}>{slice.quantity}</td>
        <td role="cell" data-label={T.deliveryDate}><time dateTime={slice.deliveryDate}>{formatDate(slice.deliveryDate)}</time></td>
      </tr>)}</tbody>
    </table>
  </div>
}

/**
 * F-027 (ALS-D94, ALS-D81): the read-only delivery schedule of each order line — the current slices from the order detail and, on
 * request, the line's recorded revisions oldest first with recorder and time, so a termin change on an approved order (no
 * re-approval) stays visible. Reading the history uses the order's read name, re-measured by the backend; 401/403 go to the auth
 * boundary. No edit control: the schedule command's screen control is a separate slice.
 */
export function LineSchedulePanel({ fenceKey, companyId, order, lines }: LineSchedulePanelProps) {
  const headingId = useId()
  return <section aria-labelledby={headingId} data-testid="purchase-order-line-schedules">
    <h2 id={headingId}>{T.heading}</h2>
    <p>{T.note}</p>
    {lines.map((line) => <LineSchedule key={line.lineId} fenceKey={fenceKey} companyId={companyId} order={order} line={line} />)}
  </section>
}

function LineSchedule({ fenceKey, companyId, order, line }: {
  readonly fenceKey: string
  readonly companyId: string
  readonly order: PurchaseOrderSummary
  readonly line: PurchaseOrderLine
}) {
  const [open, setOpen] = useState(false)
  const historyId = useId()
  const current = line.deliverySchedule
  return <div data-testid={`purchase-order-line-schedule-${line.lineNo}`}>
    <h3>{T.lineHeading(line.lineNo)}</h3>
    {current === undefined
      ? <p>{T.currentUnavailable}</p>
      : current.length === 0 ? <p>{T.currentEmpty}</p> : <SliceTable caption={T.currentCaption(line.lineNo)} slices={current} />}
    <Button type="button" variant="secondary" aria-expanded={open} aria-controls={historyId} onClick={() => setOpen((value) => !value)}>
      {open ? T.hideHistory(line.lineNo) : T.showHistory(line.lineNo)}
    </Button>
    <div id={historyId}>
      {open ? <LineScheduleHistory fenceKey={fenceKey} companyId={companyId} order={order} line={line} /> : null}
    </div>
  </div>
}

function LineScheduleHistory({ fenceKey, companyId, order, line }: {
  readonly fenceKey: string
  readonly companyId: string
  readonly order: PurchaseOrderSummary
  readonly line: PurchaseOrderLine
}) {
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [fenceKey, 'purchase-order-line-schedule-history', order.id, line.lineId, order.version],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchPurchaseOrderLineScheduleHistory(companyId, order.id, line.lineId, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as
    RemoteViewState<PurchaseOrderLineScheduleHistory>
  if (state.kind !== 'ready') return <StateBlock state={state} subject={T.historySubject} onRetry={() => void query.refetch()} />
  const history = state.data
  const newer = BigInt(history.orderVersion) > BigInt(order.version)
  return <div data-testid={`purchase-order-line-schedule-history-${line.lineNo}`}>
    <p>{T.historyReadAt(history.orderVersion)}</p>
    {newer ? <p role="status" className={styles.warning}>{T.historyNewer}</p> : null}
    {history.revisions.length === 0
      ? <p data-state="empty-no-data">{T.historyEmpty}</p>
      : <ol>{history.revisions.map((revision, index) => <li key={revision.orderVersion}
          data-testid={`purchase-order-line-schedule-revision-${line.lineNo}-${revision.orderVersion}`}>
          <h4>{T.revisionHeading(revision.orderVersion)}{index === history.revisions.length - 1 ? ` · ${T.currentMarker}` : ''}</h4>
          <dl className={styles.facts}>
            <div><dt>{T.recordedBy}</dt><dd className={styles.identifier}>{revision.recordedBy}</dd></div>
            <div><dt>{T.recordedAtUtc}</dt><dd><time dateTime={revision.recordedAtUtc}>{revision.recordedAtUtc}</time></dd></div>
          </dl>
          <SliceTable caption={T.revisionCaption(line.lineNo, revision.orderVersion)} slices={revision.slices} />
        </li>)}</ol>}
  </div>
}
