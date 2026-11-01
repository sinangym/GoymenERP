/**
 * AT-061 / AT-062: the plan form's live theoretical preview, drawn from the server's answer only.
 *
 * The panel shows the last answered preview WITH the input snapshot it answered, and says plainly whether that is the
 * form's present content ("güncel") or an older one ("güncel değil"). A refused, failed or incomplete preview says why
 * and on which field; the older answer is then never presented as the current one. Missing parameters are listed by
 * the names the server gave; nothing is filled in.
 */

import type { PlanPreviewState } from './usePlanPreview'
import type { PlanFormulaAnswer, PlanPreview } from './planPreview'
import {
  OUTCOME_LABELS,
  PLAN_PREVIEW_TEXT,
  parameterLabel,
  previewFailure,
  previewRefusal,
  valueLabel,
} from './planPreviewText'
import { TARGET_BASIS_LABEL } from './manufacturingText'
import styles from './PlanPage.module.css'

function AnswerRow({ scope, answer }: { readonly scope: string; readonly answer: PlanFormulaAnswer }) {
  return (
    <tr>
      <th scope="row">{answer.formula}<div className={styles.muted}>{answer.formulaVersion}</div></th>
      <td>{scope}</td>
      <td>{OUTCOME_LABELS[answer.outcome] ?? answer.outcome}</td>
      <td>
        {answer.outcome === 'Computed' ? (
          <ul>
            {answer.values.map((value) => (
              <li key={value.name}>{valueLabel(value.name)}: <span className={styles.mono}>{value.value}</span></li>
            ))}
          </ul>
        ) : null}
        {answer.outcome === 'Unavailable'
          ? <>{PLAN_PREVIEW_TEXT.missing(answer.missingParameters.map(parameterLabel).join(', '))}</>
          : null}
        {answer.outcome === 'Refused'
          ? <span className={styles.error}>{answer.refusalCode} ({parameterLabel(answer.refusedParameter ?? '')})</span>
          : null}
      </td>
    </tr>
  )
}

/** The server-owned calculation projection, reused by live preview and a reopened stored plan. */
export function PlanCalculationDetails({
  preview,
  labelOf,
}: {
  readonly preview: PlanPreview
  readonly labelOf: (parentRollId: string) => string
}) {
  return (
    <>
      <dl className={styles.facts}>
        <div><dt>{PLAN_PREVIEW_TEXT.process}</dt><dd>{preview.process}</dd></div>
        <div><dt>{PLAN_PREVIEW_TEXT.plannedRollCount}</dt><dd>{preview.plannedRollCount}</dd></div>
        <div><dt>{PLAN_PREVIEW_TEXT.plannedInput}</dt><dd>{preview.plannedInputKg} kg</dd></div>
        <div>
          <dt>{PLAN_PREVIEW_TEXT.targetComparison}</dt>
          <dd>
            {preview.target === null
              ? PLAN_PREVIEW_TEXT.targetNone
              : preview.target.targetMinusPlannedInputKg !== null
                ? PLAN_PREVIEW_TEXT.targetMinusPlanned(preview.target.targetMinusPlannedInputKg)
                : `${TARGET_BASIS_LABEL[preview.target.basis] ?? preview.target.basis}: ${OUTCOME_LABELS[preview.target.outcome]} (${preview.target.missingParameters.map(parameterLabel).join(', ')})`}
          </dd>
        </div>
        <div><dt>{PLAN_PREVIEW_TEXT.calculator}</dt><dd className={styles.mono}>{preview.calculatorVersion}</dd></div>
      </dl>
      {preview.missingParameters.length > 0 ? (
        <>
          <h3>{PLAN_PREVIEW_TEXT.missingHeading}</h3>
          <p className={styles.muted}>{PLAN_PREVIEW_TEXT.missingNote}</p>
          <ul aria-label={PLAN_PREVIEW_TEXT.missingList}>
            {preview.missingParameters.map((name) => (
              <li key={name}>{parameterLabel(name)} <span className={styles.mono}>({name})</span></li>
            ))}
          </ul>
        </>
      ) : null}
      {preview.hasRefusal ? <p className={styles.error}>{PLAN_PREVIEW_TEXT.refusalNote}</p> : null}
      <div className={styles.tableWrap}>
        <table className={styles.table}>
          <thead><tr><th scope="col">{PLAN_PREVIEW_TEXT.formula}</th><th scope="col">{PLAN_PREVIEW_TEXT.scope}</th><th scope="col">{PLAN_PREVIEW_TEXT.status}</th><th scope="col">{PLAN_PREVIEW_TEXT.result}</th></tr></thead>
          <tbody>
            {preview.answers.map((answer) => <AnswerRow key={`plan:${answer.formula}`} scope={PLAN_PREVIEW_TEXT.planScope} answer={answer} />)}
            {preview.rolls.flatMap((roll) => roll.answers.map((answer) => (
              <AnswerRow
                key={`${roll.parentRollId}:${answer.formula}`}
                scope={`${labelOf(roll.parentRollId)} · ${roll.plannedKg} kg`}
                answer={answer}
              />
            )))}
          </tbody>
        </table>
      </div>
    </>
  )
}

export function PlanPreviewPanel({
  state,
  labelOf,
  workCenterLabel,
}: {
  readonly state: PlanPreviewState
  readonly labelOf: (parentRollId: string) => string
  readonly workCenterLabel: (workCenterId: string) => string
}) {
  const { status, last, current } = state
  return (
    <section className={styles.commandCard} aria-labelledby="manufacturing-plan-preview-heading" data-testid="plan-preview">
      <h2 id="manufacturing-plan-preview-heading">{PLAN_PREVIEW_TEXT.heading}</h2>
      <p className={styles.muted}>{PLAN_PREVIEW_TEXT.note}</p>
      <div role="status" aria-live="polite">
        {status.type === 'waiting' || status.type === 'loading' ? <p>{PLAN_PREVIEW_TEXT.waiting}</p> : null}
        {status.type === 'answered' && current ? <p className={styles.success}>{PLAN_PREVIEW_TEXT.current}</p> : null}
        {status.type === 'incomplete' ? (
          <>
            <p>{PLAN_PREVIEW_TEXT.incomplete}</p>
            <ul>{status.reasons.map((reason) => <li key={reason}>{reason}</li>)}</ul>
          </>
        ) : null}
      </div>
      {status.type === 'refused' ? (
        <p className={styles.error} role="alert">
          {PLAN_PREVIEW_TEXT.refused(previewRefusal(status.status, status.code, status.field))}
        </p>
      ) : null}
      {status.type === 'failed' ? <p className={styles.error} role="alert">{previewFailure(status.reason)}</p> : null}

      {last === null ? null : (
        <div aria-label={current ? PLAN_PREVIEW_TEXT.currentLabel : PLAN_PREVIEW_TEXT.staleLabel} className={current ? undefined : styles.muted}>
          {current ? null : <p className={styles.note}><strong>{PLAN_PREVIEW_TEXT.stale}</strong></p>}
          <h3>{PLAN_PREVIEW_TEXT.answeredHeading}</h3>
          <ul>
            <li>{PLAN_PREVIEW_TEXT.answeredWorkCenter(workCenterLabel(last.request.workCenterId))}</li>
            {last.request.inputs.map((input) => (
              <li key={input.parentRollId}>{PLAN_PREVIEW_TEXT.answeredInput(labelOf(input.parentRollId), input.plannedKg, input.expectedLabelVersion)}</li>
            ))}
            <li>{PLAN_PREVIEW_TEXT.answeredTarget(last.request.target === null ? PLAN_PREVIEW_TEXT.answeredTargetNone : `${TARGET_BASIS_LABEL[last.request.target.basis]} ${last.request.target.kg} kg`)}</li>
          </ul>
          <PlanCalculationDetails preview={last.preview} labelOf={labelOf} />
        </div>
      )}
      <p className={styles.muted}>{PLAN_PREVIEW_TEXT.saveNote}</p>
    </section>
  )
}
