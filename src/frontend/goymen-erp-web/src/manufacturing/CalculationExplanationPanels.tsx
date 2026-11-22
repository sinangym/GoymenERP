/**
 * WP11 (DALGA_PLANI_v2 1.12): the "Açıkla" panels beside the order's kg cost and beside each roll's mass difference. Each
 * reads its explanation only when opened, under the same grant as the figure it explains (reports read / order read), and
 * shows the server calculator's steps and the records they came from. Nothing is computed here.
 */

import { useQuery } from '@tanstack/react-query'
import {
  fetchMassBalanceExplanation,
  fetchProductionCostExplanation,
  type ManufacturingTraceStep,
  type MassBalanceExplanation,
  type ProductionCostExplanation,
} from './calculationExplanation'
import { CALCULATION_EXPLANATION_TEXT as T } from './calculationExplanationText'
import { orderCostCodeText } from './orderCostText'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { ExplainToggle, ExplainTraceTable, type ExplainStepRow } from '../ui/ExplainTrace'
import { EXPLAIN_TEXT } from '../ui/explainText'
import styles from './OrderDetailPage.module.css'

export interface OrderExplainContext {
  readonly namespace: string
  readonly routeEntryGeneration: number
  readonly companyId: string
  readonly orderId: string
  readonly orderVersion: number
}

function row(step: ManufacturingTraceStep): ExplainStepRow {
  return {
    code: step.code,
    label: T.step[step.code] ?? step.code,
    operation: step.operation,
    operandLabels: step.operands.map((operand) => T.step[operand] ?? operand),
    value: step.value,
    unit: step.unit,
    scale: step.scale,
  }
}

function useExplanation<T>(context: OrderExplainContext, key: string,
  fetcher: (companyId: string, orderId: string, signal?: AbortSignal) => Promise<ViewOutcome<T>>) {
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [context.namespace, context.routeEntryGeneration, key, context.orderId, context.orderVersion],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetcher(context.companyId, context.orderId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<T>
  return { state, retry: () => void query.refetch() }
}

export function ProductionCostExplanationToggle({ context }: { readonly context: OrderExplainContext }) {
  return (
    <ExplainToggle testId="order-cost-explain" label={T.costLabel}>
      <ProductionCostExplanationBody context={context} />
    </ExplainToggle>
  )
}

function ProductionCostExplanationBody({ context }: { readonly context: OrderExplainContext }) {
  const { state, retry } = useExplanation<ProductionCostExplanation>(context, 'manufacturing-production-cost-explanation',
    fetchProductionCostExplanation)
  if (state.kind !== 'ready') return <StateBlock state={state} subject={T.costSubject} onRetry={retry} />
  const explanation = state.data
  if (explanation.status !== 'available') {
    return <p data-testid="order-cost-explain-reason">{`${T.reason}: ${orderCostCodeText(explanation.reasonCode ?? '')}`}</p>
  }
  return (
    <div data-testid="order-cost-explain-body">
      <div className={styles.tableWrap}>
        <ExplainTraceTable caption={T.costCaption} rows={explanation.steps.map(row)} testId="order-cost-explain-steps" />
      </div>
      <div className={styles.tableWrap}>
        <table className={styles.table} data-testid="order-cost-explain-issues">
          <caption>{T.issuesCaption}</caption>
          <thead>
            <tr>
              <th scope="col">{T.issueMovement}</th>
              <th scope="col">{T.issuedKg}</th>
              <th scope="col">{T.returnedKg}</th>
              <th scope="col">{T.keptKg}</th>
              <th scope="col">{T.tlUnitCostPerKg}</th>
              <th scope="col">{T.amountTl}</th>
            </tr>
          </thead>
          <tbody>
            {explanation.issues.map((issue) => (
              <tr key={issue.issueMovementId}>
                <th scope="row" className={styles.mono}>{issue.issueMovementId}</th>
                <td>{issue.issuedKg}</td>
                <td>{issue.returnedKg}</td>
                <td>{issue.keptKg}</td>
                <td>{issue.tlUnitCostPerKg}</td>
                <td>{issue.amountTl}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {explanation.scrapWeighings.length > 0 ? (
        <div className={styles.tableWrap}>
          <table className={styles.table} data-testid="order-cost-explain-scrap">
            <caption>{T.scrapCaption}</caption>
            <thead>
              <tr>
                <th scope="col">{T.scrapOutput}</th>
                <th scope="col">{T.kilograms}</th>
                <th scope="col">{T.tlPerKg}</th>
                <th scope="col">{T.valueTl}</th>
              </tr>
            </thead>
            <tbody>
              {explanation.scrapWeighings.map((weighing) => (
                <tr key={weighing.scrapOutputId}>
                  <th scope="row" className={styles.mono}>{weighing.scrapOutputId}</th>
                  <td>{weighing.kilograms}</td>
                  <td>{weighing.tlPerKg ?? T.noPrice}</td>
                  <td>{weighing.valueTl}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
      {explanation.currencyLines.length > 0 ? (
        <ul data-testid="order-cost-explain-rates" aria-label={T.currencyCaption}>
          {explanation.currencyLines.map((line) => (
            <li key={line.issueMovementId}>
              {`${line.issueMovementId} · ${T.currency} ${line.currencyId} · ${T.exchangeRate} ${line.exchangeRate
                ?? (line.exchangeRateRatio ? `${line.exchangeRateRatio.numerator}/${line.exchangeRateRatio.denominator}` : T.none)}`}
            </li>
          ))}
        </ul>
      ) : null}
      <Rules calculator={explanation.calculatorVersion} rules={explanation.ruleReferences} testId="order-cost-explain-rules" />
    </div>
  )
}

export function MassBalanceExplanationToggle({ context, parentRollId }: {
  readonly context: OrderExplainContext
  readonly parentRollId: string
}) {
  return (
    <ExplainToggle testId={`mass-balance-explain-${parentRollId}`} label={T.balanceLabel(parentRollId)}>
      <MassBalanceExplanationBody context={context} parentRollId={parentRollId} />
    </ExplainToggle>
  )
}

function MassBalanceExplanationBody({ context, parentRollId }: {
  readonly context: OrderExplainContext
  readonly parentRollId: string
}) {
  const { state, retry } = useExplanation<MassBalanceExplanation>(context, 'manufacturing-mass-balance-explanation',
    fetchMassBalanceExplanation)
  if (state.kind !== 'ready') return <StateBlock state={state} subject={T.balanceSubject} onRetry={retry} />
  const roll = state.data.rolls.find((item) => item.parentRollId === parentRollId)
  if (roll === undefined) return <StateBlock state={{ kind: 'malformed-response' }} subject={T.balanceSubject} onRetry={retry} />
  const ids = (list: readonly string[]) => (list.length === 0 ? T.none : list.join(', '))
  return (
    <div data-testid={`mass-balance-explain-${parentRollId}-body`}>
      <ExplainTraceTable caption={T.balanceCaption} rows={roll.steps.map(row)} testId={`mass-balance-explain-${parentRollId}-steps`} />
      <dl>
        <dt>{T.issueIds}</dt>
        <dd className={styles.mono}>{ids(roll.issueIds)}</dd>
        <dt>{T.returnIds}</dt>
        <dd className={styles.mono}>{ids(roll.returnIds)}</dd>
        <dt>{T.outputIds}</dt>
        <dd className={styles.mono}>{ids(roll.outputIds)}</dd>
      </dl>
      <Rules calculator={state.data.formulaVersion} rules={state.data.ruleReferences} testId={`mass-balance-explain-${parentRollId}-rules`} />
    </div>
  )
}

function Rules({ calculator, rules, testId }: { readonly calculator: string; readonly rules: readonly string[]; readonly testId: string }) {
  return (
    <>
      <p>{EXPLAIN_TEXT.rules}</p>
      <ul data-testid={testId}>
        <li>{`${EXPLAIN_TEXT.calculator}: ${calculator}`}</li>
        {rules.map((rule) => <li key={rule}>{rule}</li>)}
      </ul>
      <p>{EXPLAIN_TEXT.note}</p>
    </>
  )
}
