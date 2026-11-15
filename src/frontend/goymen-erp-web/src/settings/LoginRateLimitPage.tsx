/**
 * Ayarlar > Giriş deneme sınırı — KIM-D34 (`docs/decisions/SINAN_KARARLARI_2026-11-15_DALGA2_EK9.md`). Standing on
 * `settings.login-rate-limit.manage` (company-wide, not Critical, granted to no role here).
 *
 * The local-account login rate limit (KIM-D29) is one group-wide Settings record. The screen shows the stored versions as
 * the server sends them and records a new one: one authorized person, no second approval, a required reason, an expected
 * version and an Idempotency-Key; the server audits it under the company the person works in. The decision's starting
 * value (10 attempts per minute per IP + normalized user name) is shown as a reminder only and never pre-fills the form;
 * before the first version the host's configured starting value applies, which this screen does not see.
 *
 * Remounted per committed context and route entry; a group (multi-company) context shows the no-company state; a version
 * conflict closes the form and re-reads; an unknown outcome keeps the same Idempotency-Key for "retry".
 */

import { useState, type FormEvent } from 'react'
import { Button } from '../ui/Button'
import { Field } from '../ui/Field'
import { StateBlock } from '../shell/StateBlock'
import { useShellState } from '../shell/shellContext'
import { contextKey, contextKeyString, hasCapability } from '../shell/shellState'
import { SETTINGS_CAPABILITY } from './capabilities'
import type { CommandOutcome } from './discountLimitApi'
import { fetchLoginRateLimit, recordLoginRateLimitVersion } from './loginRateLimitApi'
import {
  LOGIN_RATE_LIMIT_INPUT,
  LOGIN_RATE_LIMIT_PARTITIONS,
  LOGIN_RATE_LIMIT_STALE_CODES,
  parseBoundedWhole,
  type LoginRateLimitPartition,
  type LoginRateLimitView,
} from './loginRateLimitContract'
import {
  LOGIN_RATE_LIMIT_TEXT as T,
  loginRateLimitRefusalText,
  partitionLabel,
  windowLabel,
} from './loginRateLimitText'
import { Frame, stateOf, useBoundaryRead } from './SalesPolicyParts'
import { useCommandAttempt, type AttemptStatus } from './useCommandAttempt'
import styles from './settings.module.css'

export function LoginRateLimitPage() {
  const shell = useShellState()
  const key = contextKey(shell)
  const namespace = key === null ? null : contextKeyString(key)
  return <LimitScreen key={`${namespace ?? 'no-context'}::${shell.routeEntryGeneration}`} namespace={namespace} />
}

function LimitScreen({ namespace }: { readonly namespace: string | null }) {
  const shell = useShellState()
  const companyId = shell.activeContext?.companyId ?? null
  const singleCompany = (shell.activeContext?.scopeMode ?? 'single') === 'single' && companyId !== null
  const ready = namespace !== null && singleCompany && hasCapability(shell, SETTINGS_CAPABILITY.loginRateLimit)
  const queryKey = [namespace ?? 'no-context', shell.routeEntryGeneration, 'settings-login-rate-limit']
  const view = useBoundaryRead(queryKey, ready, (signal) => fetchLoginRateLimit(companyId ?? '', signal))

  if (!singleCompany) {
    return (
      <Frame title={T.title} intro={T.intro}>
        <StateBlock state={{ kind: 'no-authorized-company' }} subject={T.subject} />
      </Frame>
    )
  }

  if (!ready || companyId === null) {
    return (
      <Frame title={T.title} intro={T.intro}>
        <StateBlock state={{ kind: 'not-found' }} subject={T.subject} />
      </Frame>
    )
  }

  const state = stateOf<LoginRateLimitView>(view.isPending, view.data)
  return (
    <Frame title={T.title} intro={T.intro}>
      {state.kind === 'ready' ? (
        <LimitSection data={state.data} companyId={companyId} onChanged={() => void view.refetch()} />
      ) : (
        <StateBlock state={state} subject={T.subject} onRetry={() => void view.refetch()} />
      )}
    </Frame>
  )
}

function LimitSection({
  data,
  companyId,
  onChanged,
}: {
  readonly data: LoginRateLimitView
  readonly companyId: string
  readonly onChanged: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [notice, setNotice] = useState<string | null>(null)
  const current = data.versions.find((entry) => entry.isCurrent) ?? null
  const close = (saved: string | null) => {
    setEditing(false)
    setNotice(saved)
    if (saved !== null) onChanged()
  }
  const stale = (text: string) => {
    setEditing(false)
    setNotice(text)
    onChanged()
  }

  return (
    <section aria-labelledby="login-rate-limit-heading" className={styles.section}>
      {notice !== null ? (
        <p role="status" className={styles.notice}>
          {notice}
        </p>
      ) : null}
      <h2 id="login-rate-limit-heading">{T.tableLabel}</h2>
      <p className={styles.muted}>
        {current === null
          ? T.noneCurrent
          : `${T.currentHeading}: ${current.permitLimit} ${T.columnPermit.toLocaleLowerCase('tr-TR')} / ` +
            `${windowLabel(current.windowSeconds)} · ${partitionLabel(current.partitionBy)}`}
      </p>
      {data.versions.length === 0 ? (
        <p className={styles.muted}>{T.empty}</p>
      ) : (
        <table className={styles.table}>
          <caption>{T.tableLabel}</caption>
          <thead>
            <tr>
              <th scope="col">{T.columnVersion}</th>
              <th scope="col">{T.columnPermit}</th>
              <th scope="col">{T.columnWindow}</th>
              <th scope="col">{T.columnPartition}</th>
              <th scope="col">{T.columnCurrent}</th>
              <th scope="col">{T.columnRecorded}</th>
              <th scope="col">{T.columnRecordedIn}</th>
              <th scope="col">{T.columnReason}</th>
            </tr>
          </thead>
          <tbody>
            {data.versions.map((entry) => (
              <tr key={entry.version}>
                <td>{entry.version}</td>
                <td>{entry.permitLimit}</td>
                <td>{windowLabel(entry.windowSeconds)}</td>
                <td>{partitionLabel(entry.partitionBy)}</td>
                <td>{entry.isCurrent ? T.isCurrent : T.isPrevious}</td>
                <td>
                  <code>{entry.recordedBy}</code> · <time dateTime={entry.recordedAtUtc}>{entry.recordedAtUtc}</time>
                </td>
                <td>
                  <code>{entry.recordedInCompanyId}</code>
                </td>
                <td>{entry.reason}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {editing ? (
        <LimitForm data={data} companyId={companyId} onDone={close} onStale={stale} />
      ) : (
        <div className={styles.actions}>
          <Button
            type="button"
            variant="primary"
            onClick={() => {
              setNotice(null)
              setEditing(true)
            }}
          >
            {T.newVersion}
          </Button>
        </div>
      )}
    </section>
  )
}

function LimitForm({
  data,
  companyId,
  onDone,
  onStale,
}: {
  readonly data: LoginRateLimitView
  readonly companyId: string
  readonly onDone: (notice: string | null) => void
  readonly onStale: (notice: string) => void
}) {
  // The form starts from the current stored version, never from a value the screen invents (KIM-D34's starting value is
  // a reminder text only).
  const current = data.versions.find((entry) => entry.isCurrent) ?? null
  const [permitText, setPermitText] = useState(current === null ? '' : String(current.permitLimit))
  const [windowText, setWindowText] = useState(current === null ? '' : String(current.windowSeconds))
  const [partitionBy, setPartitionBy] = useState<LoginRateLimitPartition | null>(current?.partitionBy ?? null)
  const [reason, setReason] = useState('')
  const attempt = useCommandAttempt<true>()
  const permitLimit = parseBoundedWhole(
    permitText,
    LOGIN_RATE_LIMIT_INPUT.minimumPermitLimit,
    LOGIN_RATE_LIMIT_INPUT.maximumPermitLimit,
  )
  const windowSeconds = parseBoundedWhole(
    windowText,
    LOGIN_RATE_LIMIT_INPUT.minimumWindowSeconds,
    LOGIN_RATE_LIMIT_INPUT.maximumWindowSeconds,
  )

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (permitLimit === null || windowSeconds === null || partitionBy === null) return
    const body = { expectedVersion: data.version, permitLimit, windowSeconds, partitionBy, reason: reason.trim() }
    const outcome = await attempt.run(body, (idempotencyKey) =>
      recordLoginRateLimitVersion({ companyId, idempotencyKey }, body),
    )
    if (outcome.type === 'applied') {
      onDone(outcome.replayed ? T.savedReplay : T.saved)
      return
    }
    const stale = staleRefusal(outcome)
    if (stale !== null) onStale(stale)
  }

  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} aria-labelledby="login-rate-limit-edit-heading">
      <h3 id="login-rate-limit-edit-heading">{T.newVersion}</h3>
      <p className={styles.muted}>{T.groupWideNote}</p>
      <p className={styles.muted}>{T.decisionHint}</p>
      <Field
        label={T.permitLabel}
        help={T.permitHelp}
        error={permitText.trim().length > 0 && permitLimit === null ? T.permitError : undefined}
      >
        {(control) => (
          <input
            {...control}
            type="text"
            inputMode="numeric"
            required
            autoComplete="off"
            value={permitText}
            onChange={(event) => setPermitText(event.target.value)}
          />
        )}
      </Field>
      <Field
        label={T.windowLabel}
        help={T.windowHelp}
        error={windowText.trim().length > 0 && windowSeconds === null ? T.windowError : undefined}
      >
        {(control) => (
          <input
            {...control}
            type="text"
            inputMode="numeric"
            required
            autoComplete="off"
            value={windowText}
            onChange={(event) => setWindowText(event.target.value)}
          />
        )}
      </Field>
      <fieldset className={styles.fieldset}>
        <legend>{T.partitionLegend}</legend>
        {LOGIN_RATE_LIMIT_PARTITIONS.map((partition) => (
          <label key={partition} className={styles.choice}>
            <input
              type="radio"
              name="login-rate-limit-partition"
              required
              checked={partitionBy === partition}
              onChange={() => setPartitionBy(partition)}
            />
            {partitionLabel(partition)}
          </label>
        ))}
      </fieldset>
      <Field label={T.reasonLabel} help={T.reasonHelp}>
        {(control) => (
          <textarea
            {...control}
            required
            rows={3}
            maxLength={LOGIN_RATE_LIMIT_INPUT.reasonMaximumLength}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
          />
        )}
      </Field>
      <Footer status={attempt.status} hasPendingRetry={attempt.hasPendingRetry} onCancel={() => onDone(null)} />
    </form>
  )
}

/** A refusal after which the screen's copy is stale (the version moved): close and re-read. */
function staleRefusal<D>(outcome: CommandOutcome<D>): string | null {
  if (outcome.type !== 'refused' || outcome.code === null) return null
  return LOGIN_RATE_LIMIT_STALE_CODES.includes(outcome.code) ? loginRateLimitRefusalText(outcome.status, outcome.code) : null
}

function Footer<D>({
  status,
  hasPendingRetry,
  onCancel,
}: {
  readonly status: AttemptStatus<D>
  readonly hasPendingRetry: boolean
  readonly onCancel: () => void
}) {
  return (
    <>
      {status.kind === 'refused' ? (
        <p role="alert" className={styles.error}>
          {loginRateLimitRefusalText(status.status, status.code)}
        </p>
      ) : null}
      {status.kind === 'unknown' ? (
        <p role="alert" className={styles.error}>
          {T.unknownOutcome}
        </p>
      ) : null}
      <div className={styles.actions}>
        <Button type="submit" variant="primary" disabled={status.kind === 'busy'} aria-busy={status.kind === 'busy'}>
          {status.kind === 'busy' ? T.saving : hasPendingRetry ? T.retrySame : T.save}
        </Button>
        <Button type="button" variant="ghost" onClick={onCancel} disabled={status.kind === 'busy'}>
          {T.cancel}
        </Button>
      </div>
    </>
  )
}
