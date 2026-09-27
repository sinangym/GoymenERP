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
import { Link } from 'react-router'
import { StateBlock } from '../shell/StateBlock'
import { useShellDispatch } from '../shell/shellContext'
import { decodeBankAccountPage, decodeSourceDetailFor, type SourceDetail } from './contract'
import {
  useFinanceRead,
  useReceiptCommand,
  useReversalCommand,
  type ReceiptCommand,
  type ReversalCommand,
  type ReversalCommandState,
} from './financePort'
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
  UNKNOWN_TITLE,
  UNKNOWN_FROZEN,
  RESEND_SAME,
  ABANDON,
  CONFLICT_TITLE,
  CONFLICT_TEXT,
  REFUSAL_TITLE,
  COLLECTION_CHANNEL_LABEL,
  POS_BEARER_LABEL,
  POS_CUSTOMER_BEARER_PENDING,
  POS_DETAIL,
  POS_POSTING_PENDING,
  issueText,
  SOURCE_SUBJECT,
  financeRef,
  sourceNumberShown,
} from './financeText'
import { WithCurrencyCodes, currencyShown } from './currencyCodes'
import { isDateOnly } from './dateOnly'
import { isFrozen, type AttemptState } from './receiptCommand'
import { amountShown, amountText, statusViewOf } from './sourceView'
import { SourceStatus } from './SourceStatus'
import { CommandAttemptPanel } from './CommandAttemptPanel'
import styles from './Finance.module.css'
import { receiptDetailPath } from './capabilities'

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
 * `offerRecord` (FIN-T05a): the Record control, offered only when the caller holds the Record control's capability
 * (`finance.receipts.record`). The server decides the period from `occurredOn` (K1) and refuses definitively when it
 * must. `offerReverse` starts FIN-T06's persistent reversal-proposal flow; it does not expose the deliberately
 * unpublished legacy direct-Reverse action. `offerRecordReverse` turns both controls on in component tests.
 */
export function FinanceSourceDetail({
  sourceId,
  offerRecordReverse = false,
  offerRecord = offerRecordReverse,
  offerReverse = offerRecordReverse,
}: {
  readonly sourceId: string
  readonly offerRecordReverse?: boolean
  readonly offerRecord?: boolean
  readonly offerReverse?: boolean
}) {
  const detail = useFinanceRead('source', [sourceId], (port, scope, signal) => port.sourceDetail(scope, { sourceId }, signal), decodeSourceDetailFor(sourceId))
  const banks = useFinanceRead('bank-accounts', [], (port, scope, signal) => port.listBankAccounts(scope, signal), decodeBankAccountPage)
  const record = useReceiptCommand('record', sourceId)
  const reverse = useReversalCommand('propose', sourceId)

  // One re-read per settled attempt key (success or version conflict), for BOTH commands.
  // A Set, not a single slot: two settled keys must not take turns re-triggering each other.
  const reread = useRef(new Set<string>())
  const { refetch } = detail
  useEffect(() => {
    for (const { state } of [record]) {
      const key =
        state.phase === 'succeeded' || (state.phase === 'refused' && state.reason === 'version-conflict')
          ? state.snapshot.key
          : null
      if (key !== null && !reread.current.has(key)) {
        reread.current.add(key)
        refetch()
      }
    }
  }, [record, refetch])

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
        <SourceLinkFact term={DETAIL.reversedBy} sourceId={data.reversalSourceId} />
        <SourceLinkFact term={DETAIL.reverses} sourceId={data.reversesSourceId} />
      </dl>
      {data.pos === null ? null : <PosFacts source={data} />}

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

      {offerRecord && data.source.stage === 'Draft' && !recordDone ? (
        <RecordAction command={record} source={data} onReread={detail.refetch} />
      ) : null}
      {offerReverse && data.source.stage === 'Recorded' && !reverseDone ? (
        <ReverseAction command={reverse} source={data} onReread={detail.refetch} />
      ) : null}
    </div>
  )
}

/**
 * DEC-FIN-014: the three amounts kept apart — gross (the customer's debt closes at it), the bank's
 * commission and the net the bank settles — each with the gross's own disclosure. What is not done
 * yet is said, not implied: no journal until the chart of accounts is decided.
 */
function PosFacts({ source }: { readonly source: SourceDetail }) {
  const pos = source.pos
  if (pos === null) return null
  return (
    <section className={styles.summary} aria-label={POS_DETAIL.title} data-collection-channel="pos">
      <dl className={styles.facts}>
        <Fact term={POS_DETAIL.channel} value={COLLECTION_CHANNEL_LABEL.pos} />
        <Fact term={POS_DETAIL.gross} value={amountText(amountShown(source.source.amount))} />
        <Fact term={POS_DETAIL.commission} value={amountText(amountShown(pos.commission))} />
        <Fact term={POS_DETAIL.bankNet} value={amountText(amountShown(pos.bankNet))} />
        <Fact term={POS_DETAIL.bearer} value={POS_BEARER_LABEL[pos.commissionBearer]} />
      </dl>
      <p className={styles.notice} role="note">
        {POS_POSTING_PENDING}
      </p>
      {pos.commissionBearer === 'Customer' ? (
        <p className={styles.notice} role="note">
          {POS_CUSTOMER_BEARER_PENDING}
        </p>
      ) : null}
    </section>
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

function SourceLinkFact({ term, sourceId }: { readonly term: string; readonly sourceId: string | null }) {
  return (
    <div>
      <dt>{term}</dt>
      <dd>{sourceId === null ? DETAIL.none : <Link to={receiptDetailPath(sourceId)}>{financeRef(sourceId)}</Link>}</dd>
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
  readonly command: ReversalCommand
  readonly source: SourceDetail
  readonly onReread: () => void
}) {
  const [open, setOpen] = useState(false)
  const [occurredOn, setOccurredOn] = useState('')
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<{ occurredOn?: string; reason?: string }>({})
  const frozen = command.state.phase === 'submitting' || command.state.phase === 'unknown'
    || command.state.phase === 'conflicted'
  const stale = command.state.phase === 'refused' && command.state.reason === 'version-conflict'
    && command.state.request.body.expectedVersion === source.source.version
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
      operation: 'propose',
      body: { sourceId: source.source.id, expectedVersion: source.source.version, occurredOn: occurredOn.trim(), reason: reason.trim() },
    })
  }

  return (
    <section className={styles.command} aria-label={REVERSE_ACTION}>
      {stale ? <StaleVersionNotice onReread={onReread} /> : <ReversalAttemptPanel command={command} />}
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

function ReversalAttemptPanel({ command }: { readonly command: ReversalCommand }) {
  const state: ReversalCommandState = command.state
  if (state.phase === 'idle' || state.phase === 'succeeded') return null
  if (state.phase === 'submitting') return <p role="status">İşlem gönderiliyor…</p>
  if (state.phase === 'unknown') {
    return (
      <div className={styles.attempt} role="status">
        <p className={styles.attemptTitle}>{UNKNOWN_TITLE}</p>
        <p>{UNKNOWN_FROZEN}</p>
        <div className={styles.actions}>
          <button type="button" onClick={command.resend}>{RESEND_SAME}</button>
          <button type="button" onClick={command.abandon}>{ABANDON}</button>
        </div>
      </div>
    )
  }
  if (state.phase === 'conflicted') {
    return (
      <div className={styles.attempt} role="status">
        <p className={styles.attemptTitle}>{CONFLICT_TITLE}</p>
        <p>{CONFLICT_TEXT}</p>
        <button type="button" onClick={command.abandon}>{ABANDON}</button>
      </div>
    )
  }
  return (
    <div className={styles.attempt} role="status">
      <p className={styles.attemptTitle}>{REFUSAL_TITLE[state.reason]}</p>
      {state.issues.map((issue, index) => (
        <p key={`${issue.field}-${issue.code}-${index}`}>{issueText(issue.code)}</p>
      ))}
    </div>
  )
}
