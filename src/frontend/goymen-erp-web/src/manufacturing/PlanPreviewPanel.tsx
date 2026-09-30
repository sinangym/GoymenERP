/**
 * AT-061 / AT-062: the plan form's live theoretical preview, drawn from the server's answer only.
 *
 * The panel shows the last answered preview WITH the input snapshot it answered, and says plainly whether that is the
 * form's present content ("güncel") or an older one ("güncel değil"). A refused, failed or incomplete preview says why
 * and on which field; the older answer is then never presented as the current one. Missing parameters are listed by
 * the names the server gave; nothing is filled in.
 */

import type { PlanPreviewState } from './usePlanPreview'
import type { PlanFormulaAnswer } from './planPreview'
import {
  OUTCOME_LABELS,
  PLAN_PREVIEW_TEXT,
  parameterLabel,
  previewFailure,
  previewRefusal,
  valueLabel,
} from './planPreviewText'
import styles from './PlanPage.module.css'

const BASIS_LABELS: Readonly<Record<string, string>> = {
  InputMass: 'Girdi kütlesi',
  GoodOutputMass: 'İyi ürün kütlesi',
}

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
          ? <>Eksik: {answer.missingParameters.map(parameterLabel).join(', ')}</>
          : null}
        {answer.outcome === 'Refused'
          ? <span className={styles.error}>{answer.refusalCode} ({parameterLabel(answer.refusedParameter ?? '')})</span>
          : null}
      </td>
    </tr>
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
          Önizleme hesaplanmadı: {previewRefusal(status.status, status.code, status.field)}
        </p>
      ) : null}
      {status.type === 'failed' ? <p className={styles.error} role="alert">{previewFailure(status.reason)}</p> : null}

      {last === null ? null : (
        <div aria-label={current ? 'Güncel önizleme' : 'Önceki girdilere ait önizleme'} className={current ? undefined : styles.muted}>
          {current ? null : <p className={styles.note}><strong>{PLAN_PREVIEW_TEXT.stale}</strong></p>}
          <h3>Yanıtlanan girdiler</h3>
          <ul>
            <li>İş merkezi: {workCenterLabel(last.request.workCenterId)}</li>
            {last.request.inputs.map((input) => (
              <li key={input.parentRollId}>{labelOf(input.parentRollId)}: {input.plannedKg} kg (etiket sürümü {input.expectedLabelVersion})</li>
            ))}
            <li>Hedef: {last.request.target === null ? 'yok' : `${BASIS_LABELS[last.request.target.basis]} ${last.request.target.kg} kg`}</li>
          </ul>
          <dl className={styles.facts}>
            <div><dt>Süreç</dt><dd>{last.preview.process}</dd></div>
            <div><dt>Planlanan rulo</dt><dd>{last.preview.plannedRollCount}</dd></div>
            <div><dt>Toplam planlanan girdi</dt><dd>{last.preview.plannedInputKg} kg</dd></div>
            <div>
              <dt>Hedef karşılaştırması</dt>
              <dd>
                {last.preview.target === null
                  ? 'Hedef yok'
                  : last.preview.target.targetMinusPlannedInputKg !== null
                    ? `Hedef − planlanan girdi: ${last.preview.target.targetMinusPlannedInputKg} kg`
                    : `${BASIS_LABELS[last.preview.target.basis] ?? last.preview.target.basis}: ${OUTCOME_LABELS[last.preview.target.outcome]} (${last.preview.target.missingParameters.map(parameterLabel).join(', ')})`}
              </dd>
            </div>
            <div><dt>Hesaplayıcı</dt><dd className={styles.mono}>{last.preview.calculatorVersion}</dd></div>
          </dl>
          {last.preview.missingParameters.length > 0 ? (
            <>
              <h3>{PLAN_PREVIEW_TEXT.missingHeading}</h3>
              <p className={styles.muted}>{PLAN_PREVIEW_TEXT.missingNote}</p>
              <ul aria-label="Eksik parametreler">
                {last.preview.missingParameters.map((name) => (
                  <li key={name}>{parameterLabel(name)} <span className={styles.mono}>({name})</span></li>
                ))}
              </ul>
            </>
          ) : null}
          {last.preview.hasRefusal ? <p className={styles.error}>Bir hesap girdiyi reddetti; ayrıntı aşağıdadır.</p> : null}
          <div className={styles.tableWrap}>
            <table className={styles.table}>
              <thead><tr><th scope="col">Formül</th><th scope="col">Kapsam</th><th scope="col">Durum</th><th scope="col">Sonuç</th></tr></thead>
              <tbody>
                {last.preview.answers.map((answer) => <AnswerRow key={`plan:${answer.formula}`} scope="Plan" answer={answer} />)}
                {last.preview.rolls.flatMap((roll) => roll.answers.map((answer) => (
                  <AnswerRow
                    key={`${roll.parentRollId}:${answer.formula}`}
                    scope={`${labelOf(roll.parentRollId)} · ${roll.plannedKg} kg`}
                    answer={answer}
                  />
                )))}
              </tbody>
            </table>
          </div>
        </div>
      )}
      <p className={styles.muted}>{PLAN_PREVIEW_TEXT.saveNote}</p>
    </section>
  )
}
