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
 * - Loading: per line, what was ordered and what Logistics has instructed, loaded, shipped and delivered,
 *   from the Sales loading-progress read (#684) — ONE request for the whole order under the draft read, the
 *   server asking the owner afresh each time. The answer is shown only when it describes exactly the detail
 *   on screen (its version and each line once); a zero is the owner's zero, never a default. Loading, no
 *   permission, not found, an unverifiable answer and a temporary failure each say so and show no number.
 *   Quantities are the server's exact text in each line's order unit; nothing is converted or summed here.
 *
 * Stale answers: every query is keyed by the shell context (company), the route entry generation and the
 * order — the allocation lines also by line and product version, the loading read also by the detail
 * version — and each response is decoded against that exact scope. A company, actor, order or route-entry
 * change mounts a new key; an answer of the old one is never shown.
 */

import { useQuery } from '@tanstack/react-query'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { classifyOutcome } from '../shell/uiState'
import { fetchDraftLoadingProgress } from './api'
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
  /** The version of the detail these lines came from: a loading answer of any other version is not this order's. */
  readonly draftVersion: string
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
          <dd data-testid="sales-linked-loading">
            {props.lines.length === 0 ? (
              <span>{T.loadingNoLines}</span>
            ) : (
              <LinkedLoading
                key={`${props.namespace}:${props.routeEntryGeneration}:${props.companyId}:${props.salesOrderId}:${props.draftVersion}`}
                {...props}
              />
            )}
          </dd>
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

/** The order's loading facts: one read for all lines, shown only as the facts of this exact detail. */
function LinkedLoading({ companyId, namespace, routeEntryGeneration, salesOrderId, draftVersion, lines }: SalesDraftLinkedEffectsProps) {
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace, routeEntryGeneration, 'sales-draft-loading-progress', salesOrderId, draftVersion] as const,
    // Read afresh on every open: a loading recorded in Logistics shows on the next visit, never from a cache.
    gcTime: 0,
    staleTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchDraftLoadingProgress(salesOrderId, { draftVersion, lines }, companyId, signal)
      if (outcome.type === 'http') boundary.report(outcome.status, snapshot)
      return signal.aborted ? { type: 'fence-stale' as const } : outcome
    },
  })

  const outcome = query.data
  if (outcome?.type === 'ok-single') {
    return (
      <div data-testid="sales-linked-loading-progress" data-state="ready">
        <p className={styles.muted}>{T.loadingNote}</p>
        <div className={styles.tableWrap}>
          <table className={styles.table}>
            <caption className={styles.caption}>{T.loadingCaption}</caption>
            <thead><tr>
              <th scope="col">{T.loadingLine}</th><th scope="col">{T.loadingOrdered}</th>
              <th scope="col">{T.loadingInstructed}</th><th scope="col">{T.loadingLoaded}</th>
              <th scope="col">{T.loadingShipped}</th><th scope="col">{T.loadingDelivered}</th>
            </tr></thead>
            <tbody>{outcome.data.lines.map((line) => (
              <tr key={line.lineId} data-testid={`sales-linked-loading-line-${line.lineId}`}
                data-instruction-lines={line.loadingInstructionLines}>
                <th scope="row">{line.lineNo}</th>
                <td data-fact="ordered">{line.ordered}</td>
                <td data-fact="instructed">{line.instructed}</td>
                <td data-fact="loaded">{line.loaded}</td>
                <td data-fact="shipped">{line.shipped}</td>
                <td data-fact="delivered">{line.delivered}</td>
              </tr>
            ))}</tbody>
          </table>
        </div>
      </div>
    )
  }

  const state = outcome === undefined ? { kind: 'initial-loading' as const } : classifyOutcome(outcome)
  let text: string
  let status: 'loading' | 'forbidden' | 'not-found' | 'malformed' | 'error'
  if (state.kind === 'initial-loading' || state.kind === 'stale-rejected') {
    text = T.loadingReading
    status = 'loading'
  } else if (state.kind === 'forbidden' || state.kind === 'unauthenticated') {
    text = T.loadingForbidden
    status = 'forbidden'
  } else if (state.kind === 'not-found') {
    text = T.loadingNotFound
    status = 'not-found'
  } else if (state.kind === 'malformed-response') {
    text = T.loadingMalformed
    status = 'malformed'
  } else {
    text = T.loadingUnavailable
    status = 'error'
  }
  // A refusal or a missing order is definite; an unverifiable or failed read may be asked again, the same way.
  const retryable = status === 'malformed' || status === 'error'
  return (
    <span data-testid="sales-linked-loading-progress" data-state={status} role={status === 'loading' ? 'status' : undefined}>
      {text}
      {retryable ? (
        <>
          {' '}
          <button type="button" className={styles.linkButton} onClick={() => void query.refetch()}>{T.retry}</button>
        </>
      ) : null}
    </span>
  )
}
