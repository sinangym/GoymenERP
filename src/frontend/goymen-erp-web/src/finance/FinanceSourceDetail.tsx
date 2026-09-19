/**
 * Source detail (`ReadResult<SourceDetail>`) with the two money commands kept APART:
 *  - Record (Draft only) — `{ draftId, expectedVersion }`; the server resolves the
 *    persisted draft. Its own key, its own confirmation.
 *  - Reverse (Recorded only) — `{ sourceId, expectedVersion, occurredOn, reason }`; the
 *    server resolves amount/currency/account from the original.
 *
 * Re-reads are driven by command OUTCOMES, once per attempt key:
 *  - after `Succeeded` the detail is re-read; if that fails the command is NOT offered
 *    again in this view (#258 README "Success followed by failed GET does not resubmit");
 *  - after a `VersionConflict` the detail is re-read, and the action stays closed while the
 *    shown version is still the one that conflicted. Only a fresh version the user has in
 *    front of them can be confirmed — never the stale one again.
 *
 * The read is bound to `sourceId`: a well-formed body about another source is malformed.
 * Whether the actor may record or reverse is not a published read; the buttons are a
 * request, the server's `Denied` is the answer.
 */

import { useEffect, useRef, useState, type FormEvent } from 'react'
import { StateBlock } from '../shell/StateBlock'
import { useShellDispatch } from '../shell/shellContext'
import { decodeBankAccountPage, decodeSourceDetailFor, type SourceDetail } from './contract'
import { useFinanceRead, useReceiptCommand, type ReceiptCommand } from './financePort'
import {
  BANK_NAME_UNAVAILABLE,
  CANCEL,
  DETAIL,
  FIELD_ERROR,
  RECORD_ACTION,
  RECORD_CONFIRM,
  RECORD_CONFIRM_TEXT,
  RECORD_CONFIRM_TITLE,
  RECORD_DONE,
  REREAD,
  REREAD_AFTER_CONFLICT,
  REREAD_AFTER_SUCCESS,
  REVERSE_ACTION,
  REVERSE_CONFIRM,
  REVERSE_DATE,
  REVERSE_DONE,
  REVERSE_REASON,
  REVERSE_REASON_REQUIRED,
  SOURCE_SUBJECT,
  financeRef,
  sourceNumberShown,
} from './financeText'
import { WithCurrencyCodes, currencyShown } from './currencyCodes'
import { isDateOnly } from './dateOnly'
import { isFrozen, type AttemptState } from './receiptCommand'
import { statusViewOf } from './sourceView'
import { SourceStatus } from './SourceStatus'
import { CommandAttemptPanel } from './CommandAttemptPanel'
import styles from './Finance.module.css'

export const FINANCE_RECORD_DIRTY_SCOPE = 'finance.source.record'
export const FINANCE_REVERSE_DIRTY_SCOPE = 'finance.source.reverse'

/** Register dirty work with the SHARED shell gate, so a company switch asks first. */
function useShellDirty(scope: string, dirty: boolean): void {
  const dispatch = useShellDispatch()
  useEffect(() => {
    dispatch({ type: dirty ? 'DIRTY_SET' : 'DIRTY_CLEAR', scope })
  }, [dirty, dispatch, scope])
  useEffect(() => () => dispatch({ type: 'DIRTY_CLEAR', scope }), [dispatch, scope])
}

/** The `expectedVersion` a refused-for-conflict attempt sent, if that is its state. */
function conflictedVersion(state: AttemptState): string | null {
  if (state.phase !== 'refused' || state.reason !== 'version-conflict') return null
  return state.snapshot.request.body.expectedVersion
}

/**
 * `offerRecordReverse`: Record and Reverse have no route yet (T05; their authority is OPEN-009), so the
 * mounted screen does NOT offer them — the HTTP port would only answer a local `Unavailable`, which
 * must never look like a sent command. Off by default; component tests of the actions turn it on.
 */
export function FinanceSourceDetail({
  sourceId,
  offerRecordReverse = false,
}: {
  readonly sourceId: string
  readonly offerRecordReverse?: boolean
}) {
  const detail = useFinanceRead('source', [sourceId], (port, scope, signal) => port.sourceDetail(scope, { sourceId }, signal), decodeSourceDetailFor(sourceId))
  const banks = useFinanceRead('bank-accounts', [], (port, scope, signal) => port.listBankAccounts(scope, signal), decodeBankAccountPage)
  const record = useReceiptCommand('record', sourceId)
  const reverse = useReceiptCommand('reverse', sourceId)

  // One re-read per settled attempt key (success or version conflict), for BOTH commands.
  // A Set, not a single slot: two settled keys must not take turns re-triggering each other.
  const reread = useRef(new Set<string>())
  const { refetch } = detail
  useEffect(() => {
    for (const { state } of [record, reverse]) {
      const key =
        state.phase === 'succeeded' || (state.phase === 'refused' && state.reason === 'version-conflict')
          ? state.snapshot.key
          : null
      if (key !== null && !reread.current.has(key)) {
        reread.current.add(key)
        refetch()
      }
    }
  }, [record, reverse, refetch])

  const recordDone = record.state.phase === 'succeeded'
  const reverseDone = reverse.state.phase === 'succeeded'

  if (detail.state.kind !== 'ready') {
    return (
      <div className={styles.detail}>
        {recordDone || reverseDone ? (
          <p className={styles.notice} role="status">
            {REREAD_AFTER_SUCCESS}
          </p>
        ) : null}
        <StateBlock state={detail.state} subject={SOURCE_SUBJECT} onRetry={detail.refetch} />
        {recordDone || reverseDone ? (
          <button type="button" onClick={detail.refetch}>
            {REREAD}
          </button>
        ) : null}
      </div>
    )
  }

  const data = detail.state.data
  const bankName =
    banks.state.kind === 'ready'
      ? (banks.state.data.items.find((account) => account.id === data.source.treasuryAccountId)?.displayName ?? BANK_NAME_UNAVAILABLE)
      : BANK_NAME_UNAVAILABLE

  return (
    <div className={styles.detail}>
      <SourceStatus view={statusViewOf(data)} />
      <dl className={styles.facts}>
        <Fact term={DETAIL.sourceNumber} value={sourceNumberShown(data.source.sourceNumber, data.source.id)} />
        <Fact term={DETAIL.occurredOn} value={data.source.occurredOn} />
        <Fact term={DETAIL.valueOn} value={data.source.valueOn} />
        <Fact term={DETAIL.bankAccount} value={bankName} />
        <WithCurrencyCodes>{(codes) => <Fact term={DETAIL.currency} value={currencyShown(data.source.currencyId, codes)} />}</WithCurrencyCodes>
        <Fact term={DETAIL.bankReference} value={data.bankReference} />
        <Fact term={DETAIL.description} value={data.description} />
        <Fact term={DETAIL.recordedAt} value={data.recordedAtUtc} />
        <Fact term={DETAIL.reversedBy} value={data.reversalSourceId === null ? null : financeRef(data.reversalSourceId)} />
        <Fact term={DETAIL.reverses} value={data.reversesSourceId === null ? null : financeRef(data.reversesSourceId)} />
      </dl>

      {recordDone ? (
        <p className={styles.notice} role="status">
          {RECORD_DONE}
        </p>
      ) : null}
      {reverseDone ? (
        <p className={styles.notice} role="status">
          {REVERSE_DONE}
        </p>
      ) : null}

      {offerRecordReverse && data.source.stage === 'Draft' && !recordDone ? (
        <RecordAction command={record} source={data} onReread={detail.refetch} />
      ) : null}
      {offerRecordReverse && data.source.stage === 'Recorded' && !reverseDone ? (
        <ReverseAction command={reverse} source={data} onReread={detail.refetch} />
      ) : null}
    </div>
  )
}

function Fact({ term, value }: { readonly term: string; readonly value: string | null }) {
  return (
    <div>
      <dt>{term}</dt>
      <dd>{value ?? DETAIL.none}</dd>
    </div>
  )
}

/** Shown while the displayed version is the one that just conflicted. */
function StaleVersionNotice({ onReread }: { readonly onReread: () => void }) {
  return (
    <div className={styles.attempt} role="status" data-attempt="stale-version">
      <p>{REREAD_AFTER_CONFLICT}</p>
      <button type="button" onClick={onReread}>
        {REREAD}
      </button>
    </div>
  )
}

function RecordAction({
  command,
  source,
  onReread,
}: {
  readonly command: ReceiptCommand
  readonly source: SourceDetail
  readonly onReread: () => void
}) {
  const [confirming, setConfirming] = useState(false)
  const confirmRef = useRef<HTMLButtonElement | null>(null)
  useEffect(() => {
    if (confirming) confirmRef.current?.focus()
  }, [confirming])
  const frozen = isFrozen(command.state)
  const stale = conflictedVersion(command.state) === source.source.version
  useShellDirty(FINANCE_RECORD_DIRTY_SCOPE, frozen)

  return (
    <section className={styles.command} aria-label={RECORD_CONFIRM_TITLE}>
      {stale ? <StaleVersionNotice onReread={onReread} /> : <CommandAttemptPanel command={command} />}
      {!confirming || frozen || stale ? (
        <button type="button" disabled={frozen || stale || !command.available} onClick={() => setConfirming(true)}>
          {RECORD_ACTION}
        </button>
      ) : (
        <div role="group" aria-labelledby="finance-record-confirm-title">
          <p id="finance-record-confirm-title" className={styles.attemptTitle}>
            {RECORD_CONFIRM_TITLE}
          </p>
          <p>{RECORD_CONFIRM_TEXT}</p>
          <div className={styles.actions}>
            <button
              ref={confirmRef}
              type="button"
              onClick={() => {
                setConfirming(false)
                command.submit({ operation: 'record', body: { draftId: source.source.id, expectedVersion: source.source.version } })
              }}
            >
              {RECORD_CONFIRM}
            </button>
            <button type="button" onClick={() => setConfirming(false)}>
              {CANCEL}
            </button>
          </div>
        </div>
      )}
    </section>
  )
}

function ReverseAction({
  command,
  source,
  onReread,
}: {
  readonly command: ReceiptCommand
  readonly source: SourceDetail
  readonly onReread: () => void
}) {
  const [open, setOpen] = useState(false)
  const [occurredOn, setOccurredOn] = useState('')
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<{ occurredOn?: string; reason?: string }>({})
  const frozen = isFrozen(command.state)
  const stale = conflictedVersion(command.state) === source.source.version
  useShellDirty(FINANCE_REVERSE_DIRTY_SCOPE, frozen || (open && (occurredOn.trim().length > 0 || reason.trim().length > 0)))

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    if (stale) return
    const next: { occurredOn?: string; reason?: string } = {}
    if (occurredOn.trim().length === 0) next.occurredOn = FIELD_ERROR.required
    else if (!isDateOnly(occurredOn.trim())) next.occurredOn = FIELD_ERROR['date-format']
    if (reason.trim().length === 0) next.reason = REVERSE_REASON_REQUIRED
    setErrors(next)
    if (Object.keys(next).length > 0) return
    command.submit({
      operation: 'reverse',
      body: { sourceId: source.source.id, expectedVersion: source.source.version, occurredOn: occurredOn.trim(), reason: reason.trim() },
    })
  }

  return (
    <section className={styles.command} aria-label={REVERSE_ACTION}>
      {stale ? <StaleVersionNotice onReread={onReread} /> : <CommandAttemptPanel command={command} />}
      {!open && !frozen ? (
        <button type="button" disabled={!command.available} onClick={() => setOpen(true)}>
          {REVERSE_ACTION}
        </button>
      ) : (
        <form onSubmit={onSubmit} noValidate className={styles.form}>
          <div className={styles.field}>
            <label htmlFor="finance-reverse-date">{REVERSE_DATE}</label>
            <input
              id="finance-reverse-date"
              type="date"
              value={occurredOn}
              disabled={frozen}
              aria-invalid={errors.occurredOn !== undefined}
              aria-describedby={errors.occurredOn ? 'finance-reverse-date-error' : undefined}
              onChange={(event) => setOccurredOn(event.target.value)}
            />
            {errors.occurredOn ? (
              <p id="finance-reverse-date-error" className={styles.error}>
                {errors.occurredOn}
              </p>
            ) : null}
          </div>
          <div className={styles.field}>
            <label htmlFor="finance-reverse-reason">{REVERSE_REASON}</label>
            <textarea
              id="finance-reverse-reason"
              value={reason}
              disabled={frozen}
              rows={3}
              aria-invalid={errors.reason !== undefined}
              aria-describedby={errors.reason ? 'finance-reverse-reason-error' : undefined}
              onChange={(event) => setReason(event.target.value)}
            />
            {errors.reason ? (
              <p id="finance-reverse-reason-error" className={styles.error}>
                {errors.reason}
              </p>
            ) : null}
          </div>
          <div className={styles.actions}>
            <button type="submit" disabled={frozen || stale || !command.available}>
              {REVERSE_CONFIRM}
            </button>
            {frozen ? null : (
              <button type="button" onClick={() => setOpen(false)}>
                {CANCEL}
              </button>
            )}
          </div>
        </form>
      )}
    </section>
  )
}
