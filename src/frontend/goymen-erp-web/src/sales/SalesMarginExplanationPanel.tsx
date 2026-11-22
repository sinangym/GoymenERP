/**
 * WP11 (DALGA_PLANI_v2 1.12): the "Açıkla" panel beside one line's margin on S-05. It reads the server's explanation only
 * when opened, shows the calculator's own steps, the records they came from and the rules and goldens they follow. Nothing
 * is computed here; a withheld margin is 403 (the forbidden state), never an empty trace.
 */

import { useQuery } from '@tanstack/react-query'
import { fetchMarginExplanation, type MarginExplanation, type MarginTraceStep } from './marginExplanation'
import { MARGIN_EXPLANATION_TEXT as T } from './marginExplanationText'
import { PRICE_CONTROL_TEXT } from './priceControlText'
import { classifyOutcome, type RemoteViewState } from '../shell/uiState'
import { StateBlock } from '../shell/StateBlock'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import { ExplainToggle, ExplainTraceTable, type ExplainStepRow } from '../ui/ExplainTrace'
import { EXPLAIN_TEXT } from '../ui/explainText'

export function SalesMarginExplanation(props: {
  readonly namespace: string
  readonly routeEntryGeneration: number
  readonly companyId: string
  readonly draftId: string
  readonly lineId: string
  readonly lineNo: number
  readonly version: string
}) {
  return (
    <ExplainToggle testId={`sales-margin-explain-${props.lineNo}`} label={T.rowLabel(props.lineNo)}>
      <MarginExplanationBody {...props} />
    </ExplainToggle>
  )
}

function MarginExplanationBody({ namespace, routeEntryGeneration, companyId, draftId, lineId, lineNo, version }: {
  readonly namespace: string
  readonly routeEntryGeneration: number
  readonly companyId: string
  readonly draftId: string
  readonly lineId: string
  readonly lineNo: number
  readonly version: string
}) {
  const boundary = useAuthBoundary()
  const query = useQuery({
    queryKey: [namespace, routeEntryGeneration, 'sales-margin-explanation', draftId, lineId, version],
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await fetchMarginExplanation(draftId, lineId, companyId, signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) boundary.report(outcome.status, snapshot)
      return outcome
    },
  })
  const state = (
    query.isPending ? { kind: 'initial-loading' } : classifyOutcome(query.data ?? { type: 'malformed' })
  ) as RemoteViewState<MarginExplanation>
  if (state.kind !== 'ready') return <StateBlock state={state} subject={T.subject} onRetry={() => void query.refetch()} />
  const explanation = state.data
  return (
    <div data-testid={`sales-margin-explain-${lineNo}-body`}>
      <p>{explanation.kind === 'Snapshot' ? T.kindSnapshot : T.kindPreview}</p>
      {explanation.margin.state === 'Unavailable' ? (
        <p data-testid={`sales-margin-explain-${lineNo}-reason`}>
          {`${T.unavailable} ${PRICE_CONTROL_TEXT.margin.reason[explanation.margin.unavailableReason]}`}
        </p>
      ) : null}
      <ExplainTraceTable caption={T.caption(lineNo)} rows={explanation.steps.map(row)}
        testId={`sales-margin-explain-${lineNo}-steps`} />
      <h4>{EXPLAIN_TEXT.sources}</h4>
      <ul data-testid={`sales-margin-explain-${lineNo}-sources`}>
        {explanation.sources.map((source, index) => (
          <li key={`${index}:${source.kind}`} data-source={source.kind}>
            {[T.source[source.kind] ?? source.kind, source.reference, source.recordId,
              source.revision === null ? null : T.revision(source.revision),
              source.recordedAtUtc === null ? null : T.recordedAt(source.recordedAtUtc)].filter(Boolean).join(' · ')}
          </li>
        ))}
      </ul>
      <h4>{EXPLAIN_TEXT.rules}</h4>
      <ul data-testid={`sales-margin-explain-${lineNo}-rules`}>
        <li>{`${EXPLAIN_TEXT.calculator}: ${explanation.calculatorVersion}`}</li>
        {explanation.ruleReferences.map((rule) => <li key={rule}>{rule}</li>)}
      </ul>
      <p>{EXPLAIN_TEXT.note}</p>
    </div>
  )
}

function row(step: MarginTraceStep): ExplainStepRow {
  return {
    code: step.code,
    label: T.step[step.code] ?? step.code,
    operation: step.operation,
    operandLabels: step.operands.map((operand) => T.step[operand] ?? operand),
    value: step.value,
    unit: null,
    scale: step.scale,
  }
}
