/**
 * Source detail (`ReadResult<SourceDetail>`) with the two money commands kept APART:
 *  - Record (Draft only) — `{ draftId, expectedVersion }`; the server resolves the
 *    persisted draft. Its own key, its own confirmation.
 *  - Reverse (Recorded only) — `{ sourceId, expectedVersion, occurredOn, reason }`; the
 *    server resolves amount/currency/account from the original.
 *
 * After a `Succeeded` command the detail is re-read. If that re-read fails, the command
 * is NOT offered again in this view — the user gets a re-read, never a second Record
 * (#258 README "Success followed by failed GET does not resubmit Record").
 *
 * Whether the actor may record or reverse is not published as a read; the buttons are a
 * request, the server's `Denied` is the answer. UI visibility is not authorization.
 */

import { useEffect, useRef, useState, type FormEvent } from 'react'
import { StateBlock } from '../shell/StateBlock'
import { decodeBankAccountPage, decodeSourceDetail, type SourceDetail } from './contract'
import { useFinanceRead, useReceiptCommand, type ReceiptCommand } from './financePort'
import {
  BANK_NAME_UNAVAILABLE,
  CANCEL,
  DETAIL,
  RECORD_ACTION,
  RECORD_CONFIRM,
  RECORD_CONFIRM_TEXT,
  RECORD_CONFIRM_TITLE,
  RECORD_DONE,
  REREAD,
  REREAD_AFTER_SUCCESS,
  REVERSE_ACTION,
  REVERSE_CONFIRM,
  REVERSE_DATE,
  REVERSE_DONE,
  REVERSE_REASON,
  REVERSE_REASON_REQUIRED,
  SOURCE_SUBJECT,
  FIELD_ERROR,
} from './financeText'
import { isDateOnly } from './dateOnly'
import { isFrozen } from './receiptCommand'
import { statusViewOf } from './sourceView'
import { SourceStatus } from './SourceStatus'
import { CommandAttemptPanel } from './CommandAttemptPanel'
import styles from './Finance.module.css'

export function FinanceSourceDetail({ sourceId }: { readonly sourceId: string }) {
  const detail = useFinanceRead('source', [sourceId], (port, scope, signal) => port.sourceDetail(scope, { sourceId }, signal), decodeSourceDetail)
  const banks = useFinanceRead('bank-accounts', [], (port, scope, signal) => port.listBankAccounts(scope, signal), decodeBankAccountPage)
  const record = useReceiptCommand('record', sourceId)
  const reverse = useReceiptCommand('reverse', sourceId)

  // Re-read once per successful command — keyed by the attempt key, never repeated.
  const reread = useRef<string | null>(null)
  const { refetch } = detail
  useEffect(() => {
    for (const command of [record, reverse]) {
      if (command.state.phase === 'succeeded' && reread.current !== command.state.snapshot.key) {
        reread.current = command.state.snapshot.key
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
        <Fact term={DETAIL.sourceNumber} value={data.source.sourceNumber} />
        <Fact term={DETAIL.occurredOn} value={data.source.occurredOn} />
        <Fact term={DETAIL.valueOn} value={data.source.valueOn} />
        <Fact term={DETAIL.bankAccount} value={bankName} />
        <Fact term={DETAIL.bankReference} value={data.bankReference} />
        <Fact term={DETAIL.description} value={data.description} />
        <Fact term={DETAIL.recordedAt} value={data.recordedAtUtc} />
        <Fact term={DETAIL.reversedBy} value={data.reversalSourceId} />
        <Fact term={DETAIL.reverses} value={data.reversesSourceId} />
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

      {data.source.stage === 'Draft' && !recordDone ? <RecordAction command={record} source={data} /> : null}
      {data.source.stage === 'Recorded' && !reverseDone ? <ReverseAction command={reverse} source={data} /> : null}
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

function RecordAction({ command, source }: { readonly command: ReceiptCommand; readonly source: SourceDetail }) {
  const [confirming, setConfirming] = useState(false)
  const confirmRef = useRef<HTMLButtonElement | null>(null)
  useEffect(() => {
    if (confirming) confirmRef.current?.focus()
  }, [confirming])
  const frozen = isFrozen(command.state)

  return (
    <section className={styles.command} aria-label={RECORD_CONFIRM_TITLE}>
      <CommandAttemptPanel command={command} />
      {!confirming || frozen ? (
        <button type="button" disabled={frozen || !command.available} onClick={() => setConfirming(true)}>
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

function ReverseAction({ command, source }: { readonly command: ReceiptCommand; readonly source: SourceDetail }) {
  const [open, setOpen] = useState(false)
  const [occurredOn, setOccurredOn] = useState('')
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<{ occurredOn?: string; reason?: string }>({})
  const frozen = isFrozen(command.state)

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
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
      <CommandAttemptPanel command={command} />
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
            <button type="submit" disabled={frozen || !command.available}>
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
