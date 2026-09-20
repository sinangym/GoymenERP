/**
 * M-07 — propose a period opening (ACC-V0). The proposal is stored but NOT open: another authorised
 * person approves it (Ö4). The client checks only the date grammar and the order of the two dates;
 * overlaps and everything else are the server's answer, shown by code.
 *
 * The period id and the Idempotency-Key are minted ONCE per snapshot; an unknown outcome freezes the
 * form until the same snapshot is re-sent (./usePeriodCommand.ts).
 */

import { useState } from 'react'
import { Link } from 'react-router'
import { ACCOUNTING_ROUTE } from './capabilities'
import { pendingPeriodKey, recallPendingPeriodCommand } from './pendingPeriodCommands'
import { newId, usePeriodCommand } from './usePeriodCommand'
import { PeriodAttemptNotice } from './PeriodAttemptNotice'
import {
  BACK_TO_PERIODS,
  DATE_ORDER,
  DATE_REQUIRED,
  ENDS_ON,
  PROPOSED_NOTICE,
  PROPOSE_INTRO,
  PROPOSE_SUBMIT,
  PROPOSE_TITLE,
  STARTS_ON,
} from './accountingText'
import { isDateOnly } from '../finance/dateOnly'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString } from '../shell/shellState'
import { StateBlock } from '../shell/StateBlock'
import styles from './Accounting.module.css'

const DIRTY_SCOPE = 'accounting.period.propose'

export function PeriodProposePage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <Propose key={namespace ?? 'no-context'} />
}

function Propose() {
  const shell = useShellState()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const storeKey = pendingPeriodKey(shell.sessionEpoch, shell.activeContext?.userId ?? null, companyId, 'propose')
  const command = usePeriodCommand(storeKey, companyId ?? '', DIRTY_SCOPE, () => undefined)

  // A restored snapshot shows the dates it was sent with, not an empty form.
  const [dates, setDates] = useState(() => {
    const pending = recallPendingPeriodCommand(storeKey)?.snapshot
    return pending?.operation === 'propose'
      ? { startsOn: pending.startsOn, endsOn: pending.endsOn }
      : { startsOn: '', endsOn: '' }
  })
  const [error, setError] = useState<string | null>(null)

  if (!singleCompany) {
    return (
      <Frame>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject="dönem önerisi için tek şirket bağlamı" />
      </Frame>
    )
  }

  const frozen = command.unresolved
  const done = command.attempt.phase === 'applied'

  const submit = (event: React.FormEvent) => {
    event.preventDefault()
    if (frozen || done) return
    // A submit refused here mints NO id and NO key: nothing was attempted.
    if (!isDateOnly(dates.startsOn) || !isDateOnly(dates.endsOn)) return setError(DATE_REQUIRED)
    if (dates.endsOn < dates.startsOn) return setError(DATE_ORDER)
    setError(null)
    command.submit({
      operation: 'propose',
      idempotencyKey: newId(),
      periodId: newId(),
      startsOn: dates.startsOn,
      endsOn: dates.endsOn,
    })
  }

  const update = (patch: Partial<typeof dates>) => {
    if (command.attempt.phase === 'failed') command.reset()
    setDates((current) => ({ ...current, ...patch }))
  }

  return (
    <Frame>
      <PeriodAttemptNotice attempt={command.attempt} appliedText={PROPOSED_NOTICE} onResend={command.resend} />
      {done ? (
        <div className={styles.actions}>
          <Link to={ACCOUNTING_ROUTE.periodsList}>{BACK_TO_PERIODS}</Link>
        </div>
      ) : (
        <form onSubmit={submit} noValidate className={styles.page}>
          <div className={styles.form}>
            <div className={styles.control}>
              <label htmlFor="accounting-period-starts">{STARTS_ON}</label>
              <input
                id="accounting-period-starts"
                type="date"
                value={dates.startsOn}
                disabled={frozen}
                onChange={(event) => update({ startsOn: event.target.value })}
              />
            </div>
            <div className={styles.control}>
              <label htmlFor="accounting-period-ends">{ENDS_ON}</label>
              <input
                id="accounting-period-ends"
                type="date"
                value={dates.endsOn}
                disabled={frozen}
                onChange={(event) => update({ endsOn: event.target.value })}
              />
            </div>
          </div>
          {error !== null ? (
            <p className={styles.error} role="alert">
              {error}
            </p>
          ) : null}
          <div className={styles.actions}>
            <button type="submit" disabled={frozen}>
              {PROPOSE_SUBMIT}
            </button>
            <Link to={ACCOUNTING_ROUTE.periodsList}>{BACK_TO_PERIODS}</Link>
          </div>
        </form>
      )}
    </Frame>
  )
}

function Frame({ children }: { readonly children: React.ReactNode }) {
  return (
    <section aria-labelledby="accounting-propose-title" className={styles.page}>
      <h1 id="accounting-propose-title">{PROPOSE_TITLE}</h1>
      <p className={styles.help}>{PROPOSE_INTRO}</p>
      {children}
    </section>
  )
}
