/**
 * S-04 "Bağlı etkiler" — what the order is already bound to, read from each owner and never assumed
 * (DEC-SAL-001 separate axes; DEC-SAL-005 and Business 5737989917 item 4: an order's cancellation or
 * revision shows its active allocations and loading effects).
 *
 * - Physical allocation: Inventory's server truth, the SAME authorized read and query key the allocation
 *   panel uses (`fetchPhysicalAllocations`), so a create/release there refreshes this line as well. Only
 *   `Active` allocations are counted. A count is shown only after a successful read; loading, no
 *   permission, an error or an unverifiable answer say so and show no number. A full search window is a
 *   lower bound ("en az"), never an exact count.
 * - Loading instruction: Logistics publishes no per-order read Sales could use, so the screen says it
 *   cannot be read yet — never 0.
 *
 * Stale answers: every line query is keyed by the shell context (company), the route entry generation, the
 * order, the line and its product version, and the response is decoded against that exact scope — the
 * allocation panel's pattern. A company or order change mounts a new key; an answer of the old one is
 * never shown.
 */

import { useQuery } from '@tanstack/react-query'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome } from '../shell/uiState'
import type { DraftLine } from './contract'
import { fetchPhysicalAllocations } from './allocationApi'
import type { AllocationScope } from './allocationContract'
import { ORDER_DETAIL_TEXT } from './salesText'
import styles from './SalesPages.module.css'

const T = ORDER_DETAIL_TEXT.approval.linkedEffects

export interface SalesDraftLinkedEffectsProps {
  /** The shell publishes Inventory's allocation capability; the read needs it on the server as well. */
  readonly allocationReadable: boolean
  readonly companyId: string
  readonly namespace: string
  readonly routeEntryGeneration: number
  readonly salesOrderId: string
  readonly lines: readonly DraftLine[]
}

export function SalesDraftLinkedEffects(props: SalesDraftLinkedEffectsProps) {
  return (
    <div data-testid="sales-approval-linked-effects">
      <h3 className={styles.lineTitle}>{T.heading}</h3>
      <dl className={styles.facts}>
        <div>
          <dt>{T.allocationTitle}</dt>
          <dd data-testid="sales-linked-allocation">
            {!props.allocationReadable ? (
              <span data-testid="sales-linked-allocation-no-access">{T.allocationNoAccess}</span>
            ) : props.lines.length === 0 ? (
              <span>{T.noLines}</span>
            ) : (
              <ul>
                {props.lines.map((line) => (
                  <LinkedAllocationLine
                    key={`${props.namespace}:${props.routeEntryGeneration}:${props.companyId}:${props.salesOrderId}:${line.lineId}:${line.productId}:${line.productVersion}`}
                    {...props}
                    line={line}
                  />
                ))}
              </ul>
            )}
          </dd>
        </div>
        <div>
          <dt>{T.loadingTitle}</dt>
          <dd data-testid="sales-linked-loading">{T.loadingNotReadable}</dd>
        </div>
      </dl>
    </div>
  )
}

/** The unavailable fallback when the page has no bound company: nothing is read, nothing is counted. */
export function SalesDraftLinkedEffectsUnbound() {
  return (
    <div data-testid="sales-approval-linked-effects">
      <h3 className={styles.lineTitle}>{T.heading}</h3>
      <dl className={styles.facts}>
        <div>
          <dt>{T.allocationTitle}</dt>
          <dd data-testid="sales-linked-allocation">{T.unavailable}</dd>
        </div>
        <div>
          <dt>{T.loadingTitle}</dt>
          <dd data-testid="sales-linked-loading">{T.loadingNotReadable}</dd>
        </div>
      </dl>
    </div>
  )
}

function LinkedAllocationLine({ companyId, namespace, routeEntryGeneration, salesOrderId, line }: SalesDraftLinkedEffectsProps & {
  readonly line: DraftLine
}) {
  const boundary = useAuthBoundary()
  const scope: AllocationScope = {
    companyId,
    salesOrderId,
    salesOrderLineId: line.lineId,
    productId: line.productId,
    productVersion: line.productVersion,
  }
  // The allocation panel's exact key: one cached server answer for both views of the same line.
  const queryKey = [namespace, routeEntryGeneration, 'physical-allocations', salesOrderId, line.lineId,
    line.productId, line.productVersion] as const
  const query = useQuery({
    queryKey,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchPhysicalAllocations(scope, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return outcome
    },
  })

  const outcome = query.data
  const state = outcome === undefined ? { kind: 'initial-loading' as const } : classifyOutcome(outcome)
  const page = outcome?.type === 'ok-single' ? outcome.data : null
  let text: string
  let status: string
  if (state.kind === 'initial-loading') {
    text = T.loading
    status = 'loading'
  } else if (state.kind === 'ready' && page !== null) {
    const active = page.items.filter((item) => item.allocation.status === 'Active').length
    text = page.searchWindowFull ? T.activeAtLeast(active) : T.active(active)
    status = page.searchWindowFull ? 'lower-bound' : 'ready'
  } else if (state.kind === 'forbidden' || state.kind === 'unauthenticated') {
    text = T.forbidden
    status = 'forbidden'
  } else if (state.kind === 'malformed-response') {
    text = T.malformed
    status = 'error'
  } else {
    text = T.unavailable
    status = 'error'
  }
  return (
    <li data-testid={`sales-linked-allocation-line-${line.lineId}`} data-state={status}>
      {T.lineLabel(line.lineNo)}: {text}
      {status === 'error' ? (
        <>
          {' '}
          <button type="button" className={styles.linkButton} onClick={() => void query.refetch()}>{T.retry}</button>
        </>
      ) : null}
    </li>
  )
}
