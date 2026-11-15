/**
 * Pieces shared by the two F-029 Settings sales policy screens (delivery tolerance, approval amount thresholds): the page
 * frame, the bounded read, the reason / date fields, the command footer, the one-time baseline transfer form and the
 * version columns. The screens differ only in their values; the timeline, the audit columns and the command rules are
 * the same (URN-D3: one manager records, no second approval, every change audited; URN-D4: never before today).
 */

import { useState, type FormEvent, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { classifyOutcome, type RemoteViewState, type ViewOutcome } from '../shell/uiState'
import { useAuthBoundary } from '../shell/useAuthBoundary'
import type { CommandContext, CommandOutcome } from './discountLimitApi'
import { isDateText } from './discountLimitContract'
import { businessDateLabel } from './discountLimitText'
import {
  SALES_POLICY_INPUT,
  SALES_POLICY_STALE_CODES,
  type SalesPolicyVersionMeta,
  type TransferBaselineBody,
} from './salesPolicyContract'
import { POLICY_TEXT, refusalText, sourceLabel, stateLabel } from './salesPolicyText'
import { useCommandAttempt, type AttemptStatus } from './useCommandAttempt'
import styles from './settings.module.css'

export function Frame({
  title,
  intro,
  children,
}: {
  readonly title: string
  readonly intro: string
  readonly children: ReactNode
}) {
  return (
    <div className={styles.page}>
      <h1>{title}</h1>
      <p className={styles.muted}>{intro}</p>
      {children}
    </div>
  )
}

/** Runs one read through the auth boundary, so a real 401/403 is reported like everywhere else in the shell. */
export function useBoundaryRead<T>(
  queryKey: readonly unknown[],
  enabled: boolean,
  read: (signal: AbortSignal) => Promise<ViewOutcome<T>>,
) {
  const boundary = useAuthBoundary()
  return useQuery({
    queryKey,
    enabled,
    queryFn: async ({ signal }) => {
      const snapshot = boundary.capture()
      const outcome = await read(signal)
      if (outcome.type === 'http' && (outcome.status === 401 || outcome.status === 403)) {
        boundary.report(outcome.status, snapshot)
      }
      return outcome
    },
  })
}

export function stateOf<T>(pending: boolean, data: ViewOutcome<T> | undefined): RemoteViewState<T> {
  return (pending ? { kind: 'initial-loading' } : classifyOutcome(data ?? { type: 'malformed' })) as RemoteViewState<T>
}

/** The notice after a definite "applied": a replay says so (the first attempt's result, not a second change). */
export function savedNotice<T>(outcome: CommandOutcome<T>, appliedText: string = POLICY_TEXT.saved): string | null {
  if (outcome.type !== 'applied') return null
  return outcome.replayed ? POLICY_TEXT.savedReplay : appliedText
}

/** A refusal after which the screen's copy is stale (version moved, baseline state changed): close and re-read. */
export function staleRefusal<T>(outcome: CommandOutcome<T>): string | null {
  if (outcome.type !== 'refused' || outcome.code === null) return null
  return SALES_POLICY_STALE_CODES.includes(outcome.code) ? refusalText(outcome.status, outcome.code) : null
}

export function ReasonField({ value, onChange }: { readonly value: string; readonly onChange: (value: string) => void }) {
  return (
    <Field label={POLICY_TEXT.reasonLabel} help={POLICY_TEXT.reasonHelp}>
      {(control) => (
        <textarea
          {...control}
          required
          rows={3}
          maxLength={SALES_POLICY_INPUT.reasonMaximumLength}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
    </Field>
  )
}

/** A date from today on; an earlier or unreadable date is held back here and judged again by the server. */
export function isEffectiveFromValid(value: string, today: string): boolean {
  return isDateText(value) && value >= today
}

/** Today travels as "omitted" (the server's business date), another date as itself. */
export function effectiveFromBody(value: string, today: string): string | null {
  return value === today ? null : value
}

export function EffectiveFromField({
  value,
  today,
  onChange,
}: {
  readonly value: string
  readonly today: string
  readonly onChange: (value: string) => void
}) {
  return (
    <Field
      label={POLICY_TEXT.effectiveFromLabel}
      help={`${POLICY_TEXT.effectiveFromHelp} En erken: ${businessDateLabel(today)}.`}
      error={isEffectiveFromValid(value, today) ? undefined : POLICY_TEXT.effectiveFromError}
    >
      {(control) => (
        <input
          {...control}
          type="date"
          required
          min={today}
          value={value}
          onChange={(event) => onChange(event.target.value)}
        />
      )}
    </Field>
  )
}

/** Shared tail of every command form: the attempt's message, and the submit/retry/cancel controls. */
export function AttemptFooter<T>({
  status,
  hasPendingRetry,
  submitLabel,
  onCancel,
}: {
  readonly status: AttemptStatus<T>
  readonly hasPendingRetry: boolean
  readonly submitLabel: string
  readonly onCancel?: () => void
}) {
  return (
    <>
      {status.kind === 'refused' ? (
        <p role="alert" className={styles.error}>
          {refusalText(status.status, status.code)}
        </p>
      ) : null}
      {status.kind === 'unknown' ? (
        <p role="alert" className={styles.error}>
          {POLICY_TEXT.unknownOutcome}
        </p>
      ) : null}
      <div className={styles.actions}>
        <Button type="submit" variant="primary" disabled={status.kind === 'busy'} aria-busy={status.kind === 'busy'}>
          {status.kind === 'busy' ? POLICY_TEXT.saving : hasPendingRetry ? POLICY_TEXT.retrySame : submitLabel}
        </Button>
        {onCancel === undefined ? null : (
          <Button type="button" variant="ghost" onClick={onCancel} disabled={status.kind === 'busy'}>
            {POLICY_TEXT.cancel}
          </Button>
        )}
      </div>
    </>
  )
}

/**
 * The one-time transfer of the compiled catalog's entries into Settings records. The values do not change; from then on
 * the timeline is the stored records and new versions can be recorded.
 */
export function BaselineTransferForm({
  companyId,
  headingId,
  transfer,
  onApplied,
  onStale,
}: {
  readonly companyId: string
  readonly headingId: string
  readonly transfer: (context: CommandContext, body: TransferBaselineBody) => Promise<CommandOutcome<true>>
  readonly onApplied: (notice: string | null) => void
  readonly onStale: (notice: string) => void
}) {
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()

  async function submit(event: FormEvent) {
    event.preventDefault()
    const body = { reason: reason.trim() }
    const outcome = await attempt.run({ transfer: headingId, ...body }, (idempotencyKey) =>
      transfer({ companyId, idempotencyKey }, body),
    )
    if (outcome.type === 'applied') {
      onApplied(savedNotice(outcome, POLICY_TEXT.baselineDone))
      return
    }
    const stale = staleRefusal(outcome)
    if (stale !== null) onStale(stale)
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby={headingId}>
      <h3 id={headingId}>{POLICY_TEXT.baselineHeading}</h3>
      <p className={styles.muted}>{POLICY_TEXT.baselinePending}</p>
      <ReasonField value={reason} onChange={setReason} />
      <AttemptFooter
        status={attempt.status}
        hasPendingRetry={attempt.hasPendingRetry}
        submitLabel={POLICY_TEXT.baselineSubmit}
      />
    </form>
  )
}

/** The column headers every version table ends with. */
export function MetaHeaders() {
  return (
    <>
      <th scope="col">{POLICY_TEXT.columnFrom}</th>
      <th scope="col">{POLICY_TEXT.columnThrough}</th>
      <th scope="col">{POLICY_TEXT.columnState}</th>
      <th scope="col">{POLICY_TEXT.columnSource}</th>
      <th scope="col">{POLICY_TEXT.columnRecorded}</th>
      <th scope="col">{POLICY_TEXT.columnReason}</th>
    </>
  )
}

/** One version's dates, state and audit trail, as the server sent them (the screen derives nothing from the dates). */
export function MetaCells({ entry }: { readonly entry: SalesPolicyVersionMeta }) {
  return (
    <>
      <td>{businessDateLabel(entry.effectiveFrom)}</td>
      <td>{entry.effectiveThrough === null ? POLICY_TEXT.openEnded : businessDateLabel(entry.effectiveThrough)}</td>
      <td>{stateLabel(entry.state)}</td>
      <td>
        {sourceLabel(entry.sourceKind)}
        {entry.decisionReference === null ? null : (
          <>
            {' '}
            <small>({entry.decisionReference})</small>
          </>
        )}
      </td>
      <td>
        {entry.recordedBy === null || entry.recordedAtUtc === null ? (
          POLICY_TEXT.notRecorded
        ) : (
          <>
            <code>{entry.recordedBy}</code> · <time dateTime={entry.recordedAtUtc}>{entry.recordedAtUtc}</time>
          </>
        )}
      </td>
      <td>{entry.reason ?? POLICY_TEXT.notRecorded}</td>
    </>
  )
}
