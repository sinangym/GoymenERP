/**
 * Status presentation for one manual receipt source: stage, amount, unallocated amount
 * and the definitive-record gate. Presentational only — it renders a decoded
 * `SourceSummaryView` / `RecordGate` and derives nothing.
 *
 * Read-view states (loading, empty, forbidden, malformed, network…) are NOT redrawn
 * here; screens use the shared `shell/StateBlock` for those, so Finance never grows a
 * second set of state boxes.
 */

import { formatDecimalTextTr } from './decimalText'
import {
  AMOUNT_LABEL,
  AMOUNT_UNREADABLE,
  AMOUNT_WITHHELD,
  RECORD_GATE_REASON,
  RECORD_GATE_TITLE,
  STAGE_LABEL,
  STAGE_NOTICE,
  STAGE_TERM,
  UNALLOCATED_LABEL,
  UNALLOCATED_UNAVAILABLE,
} from './financeText'
import type { AmountView, RecordGate, SourceSummaryView, UnallocatedView } from './sourceView'
import styles from './SourceStatus.module.css'

function moneyText(amount: string, currencyLabel: string): string {
  const shown = formatDecimalTextTr(amount)
  return shown === null ? AMOUNT_UNREADABLE : `${shown} ${currencyLabel}`
}

function amountText(view: AmountView | UnallocatedView): string {
  switch (view.kind) {
    case 'visible':
    case 'server':
      return moneyText(view.amount, view.currencyLabel)
    case 'withheld':
      return AMOUNT_WITHHELD
    case 'unavailable':
      return UNALLOCATED_UNAVAILABLE
  }
}

export function SourceSummary({ view }: { readonly view: SourceSummaryView }) {
  return (
    <section className={styles.summary} data-stage={view.stage} aria-label={STAGE_LABEL[view.stage]}>
      <dl className={styles.facts}>
        <div>
          <dt>{STAGE_TERM}</dt>
          <dd>{STAGE_LABEL[view.stage]}</dd>
        </div>
        <div>
          <dt>{AMOUNT_LABEL}</dt>
          <dd data-amount-kind={view.amount.kind}>{amountText(view.amount)}</dd>
        </div>
        {view.stage === 'draft' ? null : (
          <div>
            <dt>{UNALLOCATED_LABEL}</dt>
            <dd data-amount-kind={view.unallocated.kind}>{amountText(view.unallocated)}</dd>
          </div>
        )}
      </dl>
      <p className={styles.notice} role="note">
        {STAGE_NOTICE[view.stage]}
      </p>
    </section>
  )
}

/** Rendered only while the gate is closed; an open gate needs no sentence. */
export function RecordGateNotice({ gate }: { readonly gate: RecordGate }) {
  if (gate.kind === 'open') return null
  return (
    <div className={styles.gate} role="status" aria-live="polite" data-gate="closed">
      <p className={styles.gateTitle}>{RECORD_GATE_TITLE}</p>
      <ul className={styles.gateReasons}>
        {gate.reasons.map((reason) => (
          <li key={reason}>{RECORD_GATE_REASON[reason]}</li>
        ))}
      </ul>
    </div>
  )
}
