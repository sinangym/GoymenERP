import { useId, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchGoodsReceiptBatches } from './goodsReceiptBatchApi'
import type { GoodsReceiptBatch, GoodsReceiptBatchPage } from './goodsReceiptBatchContract'
import {
  GOODS_RECEIPT_BATCH_STATUS_TEXT,
  GOODS_RECEIPT_BATCH_TEXT as T,
  GOODS_RECEIPT_LABEL_CODE_SOURCE_TEXT,
  GOODS_RECEIPT_VARIANCE_TEXT,
} from './goodsReceiptBatchText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import styles from './PurchaseOrderDetailPage.module.css'

export interface GoodsReceiptBatchPanelProps {
  /** The query namespace and route entry this panel was opened in; a change drops any late answer. */
  readonly fenceKey: string
  readonly companyId: string
  readonly orderId: string
  readonly orderVersion: string
}

/**
 * F-027 (ALC-S16; KLN-D41, KLN-D43, KLN-D45): the order's bulk goods receipts, read only and loaded on request — each batch's
 * total kg against the expected kg, the server's variance outcome and Ayarlar tolerance snapshot, the preparer and the decision,
 * and its labels with our code, its source and the supplier's original number. 401/403 go to the auth boundary.
 */
export function GoodsReceiptBatchPanel({ fenceKey, companyId, orderId, orderVersion }: GoodsReceiptBatchPanelProps) {
  const headingId = useId()
  const listId = useId()
  const [open, setOpen] = useState(false)
  return <section aria-labelledby={headingId} data-testid="purchase-order-goods-receipt-batches">
    <h2 id={headingId}>{T.heading}</h2>
    <p>{T.note}</p>
    <Button type="button" variant="secondary" aria-expanded={open} aria-controls={listId} onClick={() => setOpen((value) => !value)}>
      {open ? T.hide : T.show}
    </Button>
    <div id={listId}>
      {open ? <GoodsReceiptBatchList fenceKey={fenceKey} companyId={companyId} orderId={orderId} orderVersion={orderVersion} /> : null}
    </div>
  </section>
}

function GoodsReceiptBatchList({ fenceKey, companyId, orderId, orderVersion }: GoodsReceiptBatchPanelProps) {
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [fenceKey, 'purchase-order-goods-receipt-batches', orderId, orderVersion],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchGoodsReceiptBatches(companyId, orderId, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as
    RemoteViewState<GoodsReceiptBatchPage>
  if (state.kind !== 'ready') return <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
  const page = state.data
  if (page.items.length === 0) return <p data-state="empty-no-data">{T.empty}</p>
  return <div data-testid="purchase-order-goods-receipt-batch-list">
    {page.searchWindowFull ? <p role="status" className={styles.warning}>{T.windowFull}</p> : null}
    <ol>{page.items.map((batch, index) => <li key={batch.batchId}>
      <BatchFacts batch={batch} index={index + 1} />
    </li>)}</ol>
  </div>
}

const LABEL_COLUMNS = [
  { name: T.labelNo }, { name: T.labelCode }, { name: T.labelCodeSource }, { name: T.supplierLabelNumber },
  { name: T.measuredKg, numeric: true }, { name: T.heatNumber }, { name: T.coilNumber },
] as const

function BatchFacts({ batch, index }: { readonly batch: GoodsReceiptBatch; readonly index: number }) {
  return <article data-testid={`purchase-order-goods-receipt-batch-${index}`} data-status={batch.status}>
    <h3>{T.batchHeading(index)}</h3>
    <dl className={styles.facts}>
      <div><dt>{T.status}</dt><dd>{GOODS_RECEIPT_BATCH_STATUS_TEXT[batch.status] ?? batch.status}</dd></div>
      <div><dt>{T.version}</dt><dd>{batch.version}</dd></div>
      <div><dt>{T.expectedKg}</dt><dd className={styles.number}>{batch.expectedQuantityKg}</dd></div>
      <div><dt>{T.receivedKg}</dt><dd className={styles.number}>{batch.receivedQuantityKg}</dd></div>
      <div><dt>{T.differenceKg}</dt><dd className={styles.number}>{batch.differenceKg}</dd></div>
      <div><dt>{T.variance}</dt><dd>{GOODS_RECEIPT_VARIANCE_TEXT[batch.varianceOutcome] ?? batch.varianceOutcome}</dd></div>
      <div><dt>{T.tolerance}</dt><dd>{batch.tolerancePercent ?? T.toleranceNotSet}</dd></div>
      <div><dt>{T.acknowledged}</dt><dd>{batch.varianceAcknowledged ? T.yes : T.no}</dd></div>
      <div><dt>{T.occurredAtUtc}</dt><dd><time dateTime={batch.occurredAtUtc}>{batch.occurredAtUtc}</time></dd></div>
      <div><dt>{T.preparedBy}</dt><dd className={styles.identifier}>{batch.preparedBy}</dd></div>
      {batch.decidedBy !== null && batch.decidedAtUtc !== null ? <>
        <div><dt>{T.decidedBy}</dt><dd className={styles.identifier}>{batch.decidedBy}</dd></div>
        <div><dt>{T.decidedAtUtc}</dt><dd><time dateTime={batch.decidedAtUtc}>{batch.decidedAtUtc}</time></dd></div>
      </> : null}
      {batch.decisionReason !== null ? <div><dt>{T.decisionReason}</dt><dd>{batch.decisionReason}</dd></div> : null}
    </dl>
    <div className={styles.tableWrap}>
      <table className={styles.table} role="table" aria-label={T.labelsCaption(index)}>
        <thead role="rowgroup"><tr role="row">{LABEL_COLUMNS.map((column) =>
          <th key={column.name} role="columnheader" scope="col" className={'numeric' in column ? styles.number : undefined}>{column.name}</th>)}</tr></thead>
        <tbody role="rowgroup">{batch.labels.map((label) => <tr key={label.labelId} role="row">
          <td role="cell" data-label={T.labelNo}>{label.lineNo}</td>
          <td role="cell" data-label={T.labelCode} className={styles.identifier}>{label.labelCode}</td>
          <td role="cell" data-label={T.labelCodeSource}>{GOODS_RECEIPT_LABEL_CODE_SOURCE_TEXT[label.labelCodeSource] ?? label.labelCodeSource}</td>
          <td role="cell" data-label={T.supplierLabelNumber}>{label.supplierLabelNumber ?? T.none}</td>
          <td role="cell" data-label={T.measuredKg} className={styles.number}>{label.measuredQuantityKg}</td>
          <td role="cell" data-label={T.heatNumber}>{label.heatNumber ?? T.none}</td>
          <td role="cell" data-label={T.coilNumber}>{label.coilNumber ?? T.none}</td>
        </tr>)}</tbody>
      </table>
    </div>
  </article>
}
