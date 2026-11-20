import { useId, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { fetchSupplierReturns } from './supplierReturnApi'
import type { SupplierReturn, SupplierReturnPage } from './supplierReturnContract'
import {
  SUPPLIER_RETURN_COST_BASIS_TEXT,
  SUPPLIER_RETURN_SOURCE_BLOCK_TEXT,
  SUPPLIER_RETURN_STATUS_TEXT,
  SUPPLIER_RETURN_TEXT as T,
} from './supplierReturnText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { Button } from '../ui/Button'
import { SupplierReturnSourceSummary } from './SupplierReturnSourceSummary'
import styles from './PurchaseOrderDetailPage.module.css'

export interface SupplierReturnPanelProps {
  /** The query namespace and route entry this panel was opened in; a change drops any late answer. */
  readonly fenceKey: string
  readonly companyId: string
  readonly orderId: string
  readonly orderVersion: string
}

/**
 * B-19 (ALC-S5; SAT-D306, SAT-D392, SAT-D393, SAT-D396): the order's returns to supplier, read only and loaded on request — each
 * return's kg and source block, the cost basis and unit cost the server recorded, the requester and the exception decision.
 * 401/403 go to the auth boundary; a 503 (the return flow not wired) is the service-unavailable state.
 */
export function SupplierReturnPanel({ fenceKey, companyId, orderId, orderVersion }: SupplierReturnPanelProps) {
  const headingId = useId()
  const listId = useId()
  const [open, setOpen] = useState(false)
  return <section aria-labelledby={headingId} data-testid="purchase-order-supplier-returns">
    <h2 id={headingId}>{T.heading}</h2>
    <p>{T.note}</p>
    <Button type="button" variant="secondary" aria-expanded={open} aria-controls={listId} onClick={() => setOpen((value) => !value)}>
      {open ? T.hide : T.show}
    </Button>
    <div id={listId}>
      {open ? <SupplierReturnList fenceKey={fenceKey} companyId={companyId} orderId={orderId} orderVersion={orderVersion} /> : null}
    </div>
  </section>
}

function SupplierReturnList({ fenceKey, companyId, orderId, orderVersion }: SupplierReturnPanelProps) {
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [fenceKey, 'purchase-order-supplier-returns', orderId, orderVersion],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const result = await fetchSupplierReturns(companyId, orderId, signal)
      if (result.type === 'http' && (result.status === 401 || result.status === 403)) boundary.report(result.status, snapshot)
      return result
    },
  })
  const state = (query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })) as
    RemoteViewState<SupplierReturnPage>
  if (state.kind !== 'ready') return <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
  const page = state.data
  if (page.items.length === 0) return <p data-state="empty-no-data">{T.empty}</p>
  return <div data-testid="purchase-order-supplier-return-list">
    {page.searchWindowFull ? <p role="status" className={styles.warning}>{T.windowFull}</p> : null}
    <ol>{page.items.map((item, index) => <li key={item.returnId}>
      <ReturnFacts item={item} index={index + 1} />
    </li>)}</ol>
  </div>
}

function ReturnFacts({ item, index }: { readonly item: SupplierReturn; readonly index: number }) {
  return <article data-testid={`purchase-order-supplier-return-${index}`} data-status={item.status}>
    <h3>{T.returnHeading(index)}</h3>
    <dl className={styles.facts}>
      <div><dt>{T.status}</dt><dd>{SUPPLIER_RETURN_STATUS_TEXT[item.status] ?? item.status}</dd></div>
      <div><dt>{T.version}</dt><dd>{item.version}</dd></div>
      <div><dt>{T.sourceBlock}</dt><dd>{SUPPLIER_RETURN_SOURCE_BLOCK_TEXT[item.sourceBlock] ?? item.sourceBlock}</dd></div>
      <div><dt>{T.quantityKg}</dt><dd className={styles.number}>{item.quantityKg}</dd></div>
      {item.undoQuantity !== undefined ? <>
        <div><dt>{T.undoQuantity}</dt><dd className={styles.number}>{item.undoQuantity}</dd></div>
        <div><dt>{T.expectedOrderVersion}</dt><dd>{item.expectedPurchaseOrderVersion}</dd></div>
        <div><dt>{T.replacementExpected}</dt><dd>{item.replacementExpected ? T.replacementExpected : T.definitiveReturn}</dd></div>
      </> : null}
      <div><dt>{T.costBasis}</dt><dd>{SUPPLIER_RETURN_COST_BASIS_TEXT[item.costBasis] ?? item.costBasis}</dd></div>
      <div><dt>{T.unitCost}</dt><dd className={styles.number}>{item.priceDisclosure === 'Withheld' ? T.priceWithheld : item.unitCostTryPerKg}</dd></div>
      <div><dt>{T.reason}</dt><dd>{item.reason}</dd></div>
      <div><dt>{T.occurredAtUtc}</dt><dd><time dateTime={item.occurredAtUtc}>{item.occurredAtUtc}</time></dd></div>
      <div><dt>{T.requestedBy}</dt><dd className={styles.identifier}>{item.requestedBy}</dd></div>
      <div><dt>{T.requestedAtUtc}</dt><dd><time dateTime={item.requestedAtUtc}>{item.requestedAtUtc}</time></dd></div>
      {item.decidedBy !== null && item.decidedAtUtc !== null ? <>
        <div><dt>{T.decidedBy}</dt><dd className={styles.identifier}>{item.decidedBy}</dd></div>
        <div><dt>{T.decidedAtUtc}</dt><dd><time dateTime={item.decidedAtUtc}>{item.decidedAtUtc}</time></dd></div>
      </> : null}
      {item.decisionReason !== null ? <div><dt>{T.decisionReason}</dt><dd>{item.decisionReason}</dd></div> : null}
    </dl>
    {item.costAllocation !== undefined ? <SupplierReturnSourceSummary allocation={item.costAllocation} labels={item.costAllocationSources} /> : null}
  </article>
}
