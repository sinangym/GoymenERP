/**
 * MFG-PLAN-EDIT: the plan's history on its detail screen — revision 0 as created, then every recorded Draft edit with
 * its reason, who recorded it, when, the version it produced and what it changed against the entry before it. It
 * reads what the server stored (`GET /plans/{planId}/revisions`); the difference is computed here for reading only.
 * Newest first, so a reopened plan shows its latest change at the top.
 */

import { TARGET_BASIS_LABEL } from './manufacturingText'
import { describeRevisionChanges, PLAN_EDIT_TEXT } from './planEditText'
import type { PlanRevisionList } from './planRevisionContract'
import styles from './PlanPage.module.css'

function when(value: string): string {
  const date = new Date(value)
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString('tr-TR', { dateStyle: 'short', timeStyle: 'short', timeZone: 'UTC' }) + ' UTC'
}

export function PlanHistory({ history }: { readonly history: PlanRevisionList }) {
  const entries = [...history.revisions].reverse()
  return (
    <section aria-labelledby="manufacturing-plan-history-heading">
      <h2 id="manufacturing-plan-history-heading">{PLAN_EDIT_TEXT.historyHeading}</h2>
      {history.revisions.length === 1 ? <p className={styles.muted}>{PLAN_EDIT_TEXT.historyEmpty}</p> : null}
      <ol className={styles.history}>
        {entries.map((entry) => {
          const previous = history.revisions[entry.revision - 1]
          return (
            <li key={entry.revision} aria-label={PLAN_EDIT_TEXT.revision(entry.revision)}>
              <h3>
                {entry.revision === 0 ? PLAN_EDIT_TEXT.created : PLAN_EDIT_TEXT.revision(entry.revision)} · {PLAN_EDIT_TEXT.planVersion(entry.planVersion)}
                {' · '}{when(entry.recordedAtUtc)}
              </h3>
              {entry.reason === null ? null : <p>{PLAN_EDIT_TEXT.revisionReason(entry.reason)}</p>}
              {entry.revisedBy === null ? null : (
                <p className={styles.muted}>{PLAN_EDIT_TEXT.revisedBy} <span className={styles.mono}>{entry.revisedBy}</span></p>
              )}
              {previous === undefined ? (
                <p className={styles.muted}>
                  {PLAN_EDIT_TEXT.workCenter} <span className={styles.mono}>{entry.workCenterId}</span> ·{' '}
                  {PLAN_EDIT_TEXT.rollCount(entry.inputs.length)} ·{' '}
                  {entry.target === null
                    ? PLAN_EDIT_TEXT.targetNone
                    : `${TARGET_BASIS_LABEL[entry.target.basis] ?? entry.target.basis} ${entry.target.kg} kg`}
                </p>
              ) : (
                <ul>
                  {describeRevisionChanges(previous, entry).map((change) => <li key={change}>{change}</li>)}
                </ul>
              )}
            </li>
          )
        })}
      </ol>
    </section>
  )
}
