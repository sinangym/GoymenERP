/**
 * URT-S17 (SAT-D317): the order's production cost, a read-only part of the order screen for a holder of
 * `manufacturing.orders.cost-trace` (order read + reports read).
 *
 * Keyed by the order's VERSION, so every re-read of the order after an accepted fact (a close among them) reads the
 * cost again. Every kg and TL figure is the server's recorded text, shown as it came — never summed, rounded or
 * recomputed here. Figures appear only when the server says `available`; an order that is not closed is `pending`
 * with its reason (SAT-D317 (1)), and every other answer says why, never a 0.
 */

import { useQuery } from '@tanstack/react-query'
import { fetchOrderCost } from './orderCostApi'
import type { OrderCostLine, OrderCostView } from './orderCostContract'
import { ORDER_COST_STATUS_TEXT, ORDER_COST_TEXT, orderCostCodeText } from './orderCostText'
import { ORDER_STATUS_LABEL, OUTPUT_CLASS_LABEL } from './manufacturingText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { StateBlock } from '../shell/StateBlock'
import styles from './OrderDetailPage.module.css'

function outputClassText(outputClass: string): string {
  return (OUTPUT_CLASS_LABEL as Record<string, string>)[outputClass] ?? outputClass
}

function orderStatusText(orderStatus: string): string {
  return (ORDER_STATUS_LABEL as Record<string, string>)[orderStatus] ?? orderStatus
}

function figure(value: string | null): string {
  return value ?? ORDER_COST_TEXT.notApplicable
}

export function OrderCostPanel({
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
    queryKey: [namespace, routeEntryGeneration, 'manufacturing-order-cost', orderId, orderVersion],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchOrderCost(companyId, orderId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<OrderCostView>

  return (
    <section aria-labelledby="mfg-order-cost-heading" className={styles.section} data-testid="order-cost">
      <h2 id="mfg-order-cost-heading">{ORDER_COST_TEXT.heading}</h2>
      <p className={styles.help}>{ORDER_COST_TEXT.help}</p>
      {state.kind !== 'ready' ? (
        <StateBlock state={state} subject={ORDER_COST_TEXT.subject} onRetry={() => void query.refetch()} />
      ) : (
        <OrderCostBody cost={state.data} />
      )}
    </section>
  )
}

function OrderCostBody({ cost }: { readonly cost: OrderCostView }) {
  return (
    <div data-testid="order-cost-body" data-cost-status={cost.status}>
      <p role="status">{ORDER_COST_STATUS_TEXT[cost.status]}</p>
      {cost.status === 'available' ? <AvailableCost cost={cost} /> : <CostWithoutFigures cost={cost} />}
      {cost.noteCodes.length > 0 ? (
        <>
          <p className={styles.help}>{ORDER_COST_TEXT.notes}</p>
          <ul data-testid="order-cost-notes">
            {cost.noteCodes.map((note, index) => (
              <li key={`${index}:${note}`}>{orderCostCodeText(note)}</li>
            ))}
          </ul>
        </>
      ) : null}
      <p className={styles.help}>{ORDER_COST_TEXT.neverZero}</p>
    </div>
  )
}

function CostWithoutFigures({ cost }: { readonly cost: OrderCostView }) {
  return (
    <>
      <p data-testid="order-cost-reason">
        {ORDER_COST_TEXT.reason}: {orderCostCodeText(cost.reasonCode ?? '')}
      </p>
      {cost.issueReasons.length > 0 ? (
        <div className={styles.tableWrap}>
          <table className={styles.table} data-testid="order-cost-issue-reasons">
            <caption>{ORDER_COST_TEXT.issueReasonsCaption}</caption>
            <thead>
              <tr>
                <th scope="col">{ORDER_COST_TEXT.issueMovement}</th>
                <th scope="col">{ORDER_COST_TEXT.issueReason}</th>
              </tr>
            </thead>
            <tbody>
              {cost.issueReasons.map((reason, index) => (
                <tr key={`${index}:${reason.issueMovementId}:${reason.reasonCode}`}>
                  <th scope="row" className={styles.mono}>
                    {reason.issueMovementId}
                  </th>
                  <td>{orderCostCodeText(reason.reasonCode)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </>
  )
}

function AvailableCost({ cost }: { readonly cost: OrderCostView }) {
  return (
    <>
      <dl data-testid="order-cost-figures">
        <dt>{ORDER_COST_TEXT.orderStatus}</dt>
        <dd>{orderStatusText(cost.orderStatus)}</dd>
        {cost.costBasis !== null ? (
          <>
            <dt>{ORDER_COST_TEXT.costBasis}</dt>
            <dd>{orderCostCodeText(cost.costBasis)}</dd>
          </>
        ) : null}
        <dt>{ORDER_COST_TEXT.costPerKg}</dt>
        <dd data-testid="order-cost-per-kg">{cost.costPerKgTl}</dd>
        <dt>{ORDER_COST_TEXT.rawMaterialCostPerKg}</dt>
        <dd data-testid="order-raw-material-cost-per-kg">{cost.rawMaterialCostPerKgTl}</dd>
        <dt>{ORDER_COST_TEXT.inputTotal}</dt>
        <dd data-testid="order-cost-input-total">{cost.inputTotalTl}</dd>
        {cost.weighedScrapExpenseTl !== null ? (
          <>
            <dt>{ORDER_COST_TEXT.weighedScrapExpense}</dt>
            <dd data-testid="order-cost-weighed-scrap">{cost.weighedScrapExpenseTl}</dd>
          </>
        ) : null}
        <dt>{ORDER_COST_TEXT.distributedTotal}</dt>
        <dd data-testid="order-cost-distributed-total">{cost.distributedTotalTl}</dd>
        <dt>{ORDER_COST_TEXT.remainder}</dt>
        <dd data-testid="order-cost-remainder">{cost.remainderTl}</dd>
        <dt>{ORDER_COST_TEXT.remainderLine}</dt>
        <dd className={styles.mono} data-testid="order-cost-remainder-line">
          {cost.remainderLabelId ?? ORDER_COST_TEXT.noRemainderLine}
        </dd>
      </dl>
      <div className={styles.tableWrap}>
        <table className={styles.table} data-testid="order-cost-lines">
          <caption>{ORDER_COST_TEXT.linesCaption}</caption>
          <thead>
            <tr>
              <th scope="col">{ORDER_COST_TEXT.outputLabel}</th>
              <th scope="col">{ORDER_COST_TEXT.outputClass}</th>
              <th scope="col">{ORDER_COST_TEXT.kilograms}</th>
              <th scope="col">{ORDER_COST_TEXT.unitCostPerKg}</th>
              <th scope="col">{ORDER_COST_TEXT.amount}</th>
              <th scope="col">{ORDER_COST_TEXT.separateScrapExpense}</th>
              <th scope="col">{ORDER_COST_TEXT.lineRemainder}</th>
              <th scope="col">{ORDER_COST_TEXT.allocationBasis}</th>
            </tr>
          </thead>
          <tbody>
            {cost.lines.length === 0 ? (
              <tr>
                <td colSpan={8}>{ORDER_COST_TEXT.noLines}</td>
              </tr>
            ) : (
              cost.lines.map((line) => <CostLineRow key={line.outputLabelId} line={line} />)
            )}
          </tbody>
        </table>
      </div>
      {cost.currencyLines.length > 0 ? (
        <div className={styles.tableWrap}>
          <table className={styles.table} data-testid="order-cost-currency-lines">
            <caption>{ORDER_COST_TEXT.currencyCaption}</caption>
            <thead>
              <tr>
                <th scope="col">{ORDER_COST_TEXT.issueMovement}</th>
                <th scope="col">{ORDER_COST_TEXT.currency}</th>
                <th scope="col">{ORDER_COST_TEXT.foreignUnitCostPerKg}</th>
                <th scope="col">{ORDER_COST_TEXT.exchangeRate}</th>
                <th scope="col">{ORDER_COST_TEXT.tlUnitCostPerKg}</th>
                <th scope="col">{ORDER_COST_TEXT.keptKilograms}</th>
                <th scope="col">{ORDER_COST_TEXT.foreignAmount}</th>
              </tr>
            </thead>
            <tbody>
              {cost.currencyLines.map((line, index) => (
                <tr key={`${index}:${line.issueMovementId}`}>
                  <th scope="row" className={styles.mono}>
                    {line.issueMovementId}
                  </th>
                  <td className={styles.mono}>{line.currencyId}</td>
                  <td>{line.foreignUnitCostRatio ? `${line.foreignUnitCostRatio.numerator} / ${line.foreignUnitCostRatio.denominator}` : line.foreignUnitCostPerKg}</td>
                  <td>{line.exchangeRate}</td>
                  <td>{line.tlUnitCostPerKg}</td>
                  <td>{line.keptKilograms}</td>
                  <td>{line.foreignAmountRatio ? `${line.foreignAmountRatio.numerator} / ${line.foreignAmountRatio.denominator}` : line.foreignAmount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </>
  )
}

function CostLineRow({ line }: { readonly line: OrderCostLine }) {
  return (
    <tr data-cost-line={line.outputLabelId}>
      <th scope="row" className={styles.mono}>
        {line.outputLabelId}
      </th>
      <td>{outputClassText(line.outputClass)}</td>
      <td>{line.kilograms}</td>
      <td>{figure(line.unitCostPerKgTl)}</td>
      <td data-testid="order-cost-line-amount">
        {line.amountTl === null
          ? `${ORDER_COST_TEXT.notAllocated}: ${orderCostCodeText(line.notAllocatedCode ?? '')}`
          : line.amountTl}
      </td>
      <td>{figure(line.separateScrapExpenseTl)}</td>
      <td data-testid="order-cost-line-remainder">{line.remainderTl}</td>
      <td>{line.allocationBasis === null ? ORDER_COST_TEXT.notApplicable : orderCostCodeText(line.allocationBasis)}</td>
    </tr>
  )
}
