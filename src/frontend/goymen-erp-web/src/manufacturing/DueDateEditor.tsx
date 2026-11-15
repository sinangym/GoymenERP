import { useState } from 'react'
import { calendarDate, type ProductionPlanView } from './contract'
import { DUE_DATE_TEXT } from './dueDateText'
import styles from './PlanPage.module.css'
import { Button } from '../ui/Button'

/**
 * F-010a (URT-D29): the plan's hand-entered termin. It is shown to every reader of the plan; the change control is drawn
 * only where `canChange` (the plan save's grant, `manufacturing.plans.edit`) holds and the plan is not cancelled. The
 * server decides again either way (a closed order is its 422). Nothing here computes a day: the user types it.
 *
 * The control only produces the request — the day typed (or null to remove the termin). The command itself (key, pending
 * slot, unknown/resend, conflict re-read) is the plan detail screen's, like the approval; the screen draws this control
 * only while no other command of the plan is unknown, blocked or awaiting a re-read.
 */
export function DueDateFacts({ plan }: { readonly plan: ProductionPlanView }) {
  return (
    <div>
      <dt>{DUE_DATE_TEXT.label}</dt>
      <dd>
        {plan.requestedDueDate === null ? (
          <span className={styles.muted}>{DUE_DATE_TEXT.none}</span>
        ) : (
          <>
            <span className={styles.mono}>{plan.requestedDueDate}</span>
            {` · ${DUE_DATE_TEXT.revision} ${plan.dueDateRevision}`}
          </>
        )}
      </dd>
    </div>
  )
}

export function DueDateEditor({
  plan,
  canChange,
  busy,
  sending,
  onSubmit,
}: {
  readonly plan: ProductionPlanView
  readonly canChange: boolean
  readonly busy: boolean
  /** A termin change of this screen is in flight. */
  readonly sending: boolean
  readonly onSubmit: (requestedDueDate: string | null) => void
}) {
  const [open, setOpen] = useState(false)
  const [value, setValue] = useState(plan.requestedDueDate ?? '')
  if (!canChange) return null
  if (plan.status === 'Cancelled') return <p className={styles.note}>{DUE_DATE_TEXT.cancelledReadOnly}</p>

  const trimmed = value.trim()
  const requested = trimmed === '' ? null : trimmed
  const keepsTermin = plan.status === 'Approved'
  const valid = requested === null ? !keepsTermin : calendarDate(requested)
  const unchanged = valid && requested === plan.requestedDueDate
  const hintId = 'manufacturing-plan-due-date-hint'

  if (!open) {
    return (
      <div className={styles.actions}>
        <Button
          type="button"
          disabled={busy}
          aria-busy={sending}
          onClick={() => {
            setValue(plan.requestedDueDate ?? '')
            setOpen(true)
          }}
        >
          {sending ? DUE_DATE_TEXT.saving : DUE_DATE_TEXT.edit}
        </Button>
      </div>
    )
  }

  return (
    <div>
      <p className={styles.note}>{DUE_DATE_TEXT.manualNote}</p>
      {keepsTermin ? <p className={styles.note}>{DUE_DATE_TEXT.approvedKeepsTermin}</p> : null}
      <label htmlFor="manufacturing-plan-due-date">{DUE_DATE_TEXT.inputLabel}</label>
      <input
        id="manufacturing-plan-due-date"
        type="date"
        value={value}
        disabled={busy}
        aria-invalid={!valid}
        aria-describedby={hintId}
        onChange={(event) => setValue(event.target.value)}
      />
      <p id={hintId} className={valid ? styles.note : styles.error}>
        {!valid ? DUE_DATE_TEXT.invalid : unchanged ? DUE_DATE_TEXT.unchanged
          : keepsTermin ? DUE_DATE_TEXT.inputHintApproved : DUE_DATE_TEXT.inputHint}
      </p>
      <div className={styles.actions}>
        <Button
          type="button"
          disabled={busy || !valid || unchanged}
          aria-busy={sending}
          onClick={() => {
            if (!valid || unchanged) return
            onSubmit(requested)
            setOpen(false)
          }}
        >
          {sending ? DUE_DATE_TEXT.saving : DUE_DATE_TEXT.save}
        </Button>
        {/* URT-D66 (2): an approved plan keeps a termin; the server refuses its removal either way. */}
        {plan.requestedDueDate === null || keepsTermin ? null : (
          <Button
            type="button"
            disabled={busy}
            onClick={() => {
              onSubmit(null)
              setOpen(false)
            }}
          >
            {DUE_DATE_TEXT.remove}
          </Button>
        )}
        <Button type="button" disabled={busy} onClick={() => setOpen(false)}>
          {DUE_DATE_TEXT.dismiss}
        </Button>
      </div>
    </div>
  )
}
