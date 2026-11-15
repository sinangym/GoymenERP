/**
 * URT-S19 (B-27, DEC-PRDCT-009, API-42): the order's cost trace, a read-only control of the order screen for a holder
 * of `manufacturing.orders.cost-trace` (order read + reports read).
 *
 * Keyed by the order's VERSION, so every re-read of the order after an accepted fact reads the trace again. Every kg and
 * TL figure is the server's text, shown as it came — never summed, rounded or recomputed here. The cost part shows
 * figures only when the server says `available`; otherwise it says why, never a 0.
 */

import { useQuery } from '@tanstack/react-query'
import { fetchCostTrace } from './costTraceApi'
import type { CostTraceCost, CostTraceView } from './costTraceContract'
import {
  COST_TRACE_COST_STATUS_TEXT,
  COST_TRACE_TEXT,
  costTraceCodeText,
  costTraceMethodText,
} from './costTraceText'
import { OUTPUT_CLASS_LABEL } from './manufacturingText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import styles from './OrderDetailPage.module.css'

function when(value: string | null): string {
  if (value === null) return COST_TRACE_TEXT.noMoment
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString('tr-TR', { dateStyle: 'short', timeStyle: 'short', timeZone: 'UTC' }) + ' UTC'
}

function outputClassText(outputClass: string): string {
  return (OUTPUT_CLASS_LABEL as Record<string, string>)[outputClass] ?? outputClass
}

export function OrderCostTracePanel({
  namespace,
  routeEntryGeneration,
  companyId,
  orderId,
  orderVersion,
}: {
  readonly namespace: string
  readonly routeEntryGeneration: number
  readonly companyId: string
  readonly orderId: string
  readonly orderVersion: number
}) {
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace, routeEntryGeneration, 'manufacturing-cost-trace', orderId, orderVersion],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchCostTrace(companyId, orderId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<CostTraceView>

  return (
    <section aria-labelledby="mfg-cost-trace-heading" className={styles.section} data-testid="cost-trace">
      <h2 id="mfg-cost-trace-heading">{COST_TRACE_TEXT.heading}</h2>
      <p className={styles.help}>{COST_TRACE_TEXT.help}</p>
      {state.kind !== 'ready' ? (
        <StateBlock state={state} subject={COST_TRACE_TEXT.subject} onRetry={() => void query.refetch()} />
      ) : (
        <CostTraceBody trace={state.data} />
      )}
    </section>
  )
}

function CostTraceBody({ trace }: { readonly trace: CostTraceView }) {
  return (
    <>
      <p className={styles.help} data-testid="cost-trace-method">
        {COST_TRACE_TEXT.method}: {costTraceMethodText(trace.methodCode)}
      </p>
      <div className={styles.tableWrap}>
        <table className={styles.table} data-testid="cost-trace-lots">
          <caption>{COST_TRACE_TEXT.lotsCaption}</caption>
          <thead>
            <tr>
              <th scope="col">{COST_TRACE_TEXT.parentRoll}</th>
              <th scope="col">{COST_TRACE_TEXT.plannedKg}</th>
              <th scope="col">{COST_TRACE_TEXT.issuedKg}</th>
              <th scope="col">{COST_TRACE_TEXT.returnedKg}</th>
              <th scope="col">{COST_TRACE_TEXT.consumedKg}</th>
              <th scope="col">{COST_TRACE_TEXT.issueCount}</th>
              <th scope="col">{COST_TRACE_TEXT.returnCount}</th>
              <th scope="col">{COST_TRACE_TEXT.firstIssuedAt}</th>
              <th scope="col">{COST_TRACE_TEXT.lastIssuedAt}</th>
              <th scope="col">{COST_TRACE_TEXT.owner}</th>
            </tr>
          </thead>
          <tbody>
            {trace.lots.length === 0 ? (
              <tr>
                <td colSpan={10}>{COST_TRACE_TEXT.noLots}</td>
              </tr>
            ) : (
              trace.lots.map((lot) => (
                <tr key={lot.parentRollId} data-cost-lot={lot.parentRollId}>
                  <th scope="row" className={styles.mono}>
                    {lot.parentRollId}
                  </th>
                  <td>{lot.plannedKg}</td>
                  <td>{lot.issuedKg}</td>
                  <td>{lot.returnedKg}</td>
                  <td data-testid="cost-lot-consumed">{lot.consumedKg}</td>
                  <td>{lot.issueCount}</td>
                  <td>{lot.returnCount}</td>
                  <td>{when(lot.firstIssuedAtUtc)}</td>
                  <td>{when(lot.lastIssuedAtUtc)}</td>
                  <td>{lot.commercialPartyId === null ? COST_TRACE_TEXT.companyOwned : COST_TRACE_TEXT.customerOwned}</td>
                </tr>
              ))
            )}
          </tbody>
          <tfoot>
            <tr data-testid="cost-trace-totals">
              <th scope="row">{COST_TRACE_TEXT.totals}</th>
              <td />
              <td>{trace.totalIssuedKg}</td>
              <td>{trace.totalReturnedKg}</td>
              <td>{trace.totalConsumedKg}</td>
              <td colSpan={5} />
            </tr>
          </tfoot>
        </table>
      </div>
      <div className={styles.tableWrap}>
        <table className={styles.table} data-testid="cost-trace-outputs">
          <caption>{COST_TRACE_TEXT.outputsCaption}</caption>
          <thead>
            <tr>
              <th scope="col">{COST_TRACE_TEXT.outputClass}</th>
              <th scope="col">{COST_TRACE_TEXT.netKg}</th>
              <th scope="col">{COST_TRACE_TEXT.declarationCount}</th>
              <th scope="col">{COST_TRACE_TEXT.reversalCount}</th>
            </tr>
          </thead>
          <tbody>
            {trace.outputs.length === 0 ? (
              <tr>
                <td colSpan={4}>{COST_TRACE_TEXT.noOutputs}</td>
              </tr>
            ) : (
              trace.outputs.map((output) => (
                <tr key={output.outputClass} data-cost-output={output.outputClass}>
                  <th scope="row">{outputClassText(output.outputClass)}</th>
                  <td>{output.netKg}</td>
                  <td>{output.declarationCount}</td>
                  <td>{output.reversalCount}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
      <h3>{COST_TRACE_TEXT.timelineHeading}</h3>
      <dl data-testid="cost-trace-timeline">
        <dt>{COST_TRACE_TEXT.firstFactAt}</dt>
        <dd>{when(trace.timeline.firstFactAtUtc)}</dd>
        <dt>{COST_TRACE_TEXT.lastFactAt}</dt>
        <dd>{when(trace.timeline.lastFactAtUtc)}</dd>
        <dt>{COST_TRACE_TEXT.closedAt}</dt>
        <dd>{when(trace.timeline.closedAtUtc)}</dd>
        <dt>{COST_TRACE_TEXT.stoppage}</dt>
        <dd>{costTraceCodeText(trace.timeline.stoppageCode)}</dd>
        <dt>{COST_TRACE_TEXT.duration}</dt>
        <dd>{costTraceCodeText(trace.timeline.durationCode)}</dd>
      </dl>
      <h3>{COST_TRACE_TEXT.salesHeading}</h3>
      {trace.salesOrderLink === null ? (
        <p className={styles.help} data-testid="cost-trace-sales-link">
          {COST_TRACE_TEXT.noSalesLink}
        </p>
      ) : (
        <dl data-testid="cost-trace-sales-link">
          <dt>{COST_TRACE_TEXT.salesOrder}</dt>
          <dd className={styles.mono}>{trace.salesOrderLink.salesOrderId}</dd>
          <dt>{COST_TRACE_TEXT.salesOrderLine}</dt>
          <dd className={styles.mono}>{trace.salesOrderLink.salesOrderLineId}</dd>
          <dt>{COST_TRACE_TEXT.customer}</dt>
          <dd className={styles.mono}>{trace.salesOrderLink.customerPartyId}</dd>
          <dt>{COST_TRACE_TEXT.product}</dt>
          <dd className={styles.mono}>{trace.salesOrderLink.productId}</dd>
          <dt>{COST_TRACE_TEXT.lineRevision}</dt>
          <dd>{trace.salesOrderLink.lineRevision}</dd>
          <dt>{COST_TRACE_TEXT.linkedAt}</dt>
          <dd>{when(trace.salesOrderLink.linkedAtUtc)}</dd>
        </dl>
      )}
      <CostBlock cost={trace.cost} />
    </>
  )
}

function CostBlock({ cost }: { readonly cost: CostTraceCost }) {
  return (
    <div data-testid="cost-trace-cost" data-cost-status={cost.status}>
      <h3>{COST_TRACE_TEXT.costHeading}</h3>
      <p role="status">{COST_TRACE_COST_STATUS_TEXT[cost.status]}</p>
      {cost.status === 'available' ? (
        <dl>
          {cost.costBasis !== null ? (
            <>
              <dt>{COST_TRACE_TEXT.costBasis}</dt>
              <dd>{costTraceCodeText(cost.costBasis)}</dd>
            </>
          ) : null}
          <dt>{COST_TRACE_TEXT.costPerKg}</dt>
          <dd data-testid="cost-per-kg">{cost.costPerKgTl}</dd>
          <dt>{COST_TRACE_TEXT.rawMaterialCostPerKg}</dt>
          <dd data-testid="raw-material-cost-per-kg">{cost.rawMaterialCostPerKgTl}</dd>
          {cost.weighedScrapExpenseTl !== null ? (
            <>
              <dt>{COST_TRACE_TEXT.weighedScrapExpense}</dt>
              <dd>{cost.weighedScrapExpenseTl}</dd>
            </>
          ) : null}
        </dl>
      ) : (
        <p data-testid="cost-reason">
          {COST_TRACE_TEXT.reason}: {costTraceCodeText(cost.reasonCode ?? '')}
        </p>
      )}
      {cost.noteCodes.length > 0 ? (
        <>
          <p className={styles.help}>{COST_TRACE_TEXT.notes}</p>
          <ul data-testid="cost-notes">
            {cost.noteCodes.map((note, index) => (
              <li key={`${index}:${note}`}>{costTraceCodeText(note)}</li>
            ))}
          </ul>
        </>
      ) : null}
      <p className={styles.help}>{COST_TRACE_TEXT.neverZero}</p>
    </div>
  )
}
