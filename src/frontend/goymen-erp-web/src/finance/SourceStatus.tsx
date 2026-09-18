/**
 * Status presentation for one source: stage, amount, unallocated amount and the stage
 * notice. Presentational only — renders a `SourceStatusView` and derives nothing.
 *
 * Read-view states (loading, forbidden, malformed, network…) are NOT redrawn here;
 * screens use the shared `shell/StateBlock`.
 */

import {
  AMOUNT_LABEL,
  STAGE_LABEL,
  STAGE_NOTICE,
  STAGE_TERM,
  UNALLOCATED_LABEL,
} from './financeText'
import { amountText, type SourceStatusView } from './sourceView'
import styles from './Finance.module.css'

export function SourceStatus({ view }: { readonly view: SourceStatusView }) {
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
