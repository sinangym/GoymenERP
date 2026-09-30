/**
 * AT-009 — the VKN/TCKN block of the Cari card's identity section (user decision 2026-09-30).
 *
 * What the user can do here, and nothing more:
 *  - READ the party's number (VKN or TCKN) as the server stored it; "Girilmemiş" when there is none.
 *  - ENTER the first number. The client refuses a number whose check digits fail before any request exists; the
 *    server refuses it again on its own. Before saving, the same company's other cards carrying the number are
 *    looked up and shown as a WARNING; the user may still save ("Uyarıya rağmen kaydet").
 *  - REQUEST A CHANGE of an existing number, with a reason. It is never applied here: it waits until a DIFFERENT
 *    person with the approve permission decides it. The proposer sees that it is pending and is offered no decision.
 *  - DECIDE (approve / reject) someone else's pending request.
 *
 * What is offered comes from the read's `viewer` block, measured by the server for this actor and company; the server
 * re-authorizes every command anyway. Every command carries one Idempotency-Key per attempt: an answer the client
 * could not read keeps the SAME key and body for an explicit retry, and a definite answer ends the attempt. After any
 * applied command the card is re-read — a 204 carries no body, so nothing is painted from what was sent.
 */

import { useRef, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { generateAttemptKey } from './commandAttempt'
import type { CommandContext, CommandOutcome } from './commandApi'
import type { ContractContext } from './clientContract'
import {
  KIND_LABEL,
  TAX_IDENTITY_TEXT as T,
  TAX_PROBLEM_CODE,
  checkTaxIdentity,
  taxProblemCodeOf,
  type TaxChange,
  type TaxDuplicate,
  type TaxIdentityView,
} from './taxIdentity'
import {
  checkTaxIdentityDuplicates,
  decideTaxIdentityChange,
  enterTaxIdentity,
  fetchTaxIdentity,
  requestTaxIdentityChange,
} from './taxIdentityApi'
import styles from './CariTaxIdentityPanel.module.css'

interface Props {
  readonly partyId: string
  readonly companyId: string
  readonly namespace: string
  readonly contractContext: ContractContext | null
}

interface Notice {
  readonly tone: 'ok' | 'error'
  readonly text: string
  readonly retry?: () => void
}

type Sender = (context: CommandContext) => Promise<CommandOutcome<null>>

const REFETCH_CODES = new Set<string>([
  TAX_PROBLEM_CODE.versionConflict,
  TAX_PROBLEM_CODE.changeStale,
  TAX_PROBLEM_CODE.changeNotPending,
  TAX_PROBLEM_CODE.changePending,
  TAX_PROBLEM_CODE.requiresApproval,
  TAX_PROBLEM_CODE.notSet,
])

function refusalText(status: number, body: unknown): string {
  const code = taxProblemCodeOf(body)
  if (code !== null && code in T.refusal) return T.refusal[code]
  if (status === 403) return T.forbidden
  return T.genericRefusal
}

export function CariTaxIdentityPanel({ partyId, companyId, namespace, contractContext }: Props) {
  const queryClient = useQueryClient()
  const queryKey = [namespace, 'counterparty-tax-identity', partyId, companyId]
  const query = useQuery({
    queryKey,
    queryFn: ({ signal }) => fetchTaxIdentity(partyId, companyId, signal),
  })
  // A ref, not state: a retry closure must see the key of the attempt it retries, not a stale render's.
  const attempt = useRef<{ readonly fingerprint: string; readonly key: string } | null>(null)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState<Notice | null>(null)
  const [formKey, setFormKey] = useState(0)

  const refresh = () => void queryClient.invalidateQueries({ queryKey })

  /** One command attempt: the same fingerprint reuses its key until a definite answer ends it. */
  const run = async (fingerprint: string, send: Sender, okText: string): Promise<boolean> => {
    if (contractContext === null) {
      setNotice({ tone: 'error', text: T.contractNotReady })
      return false
    }
    const current = attempt.current
    const key = current !== null && current.fingerprint === fingerprint ? current.key : generateAttemptKey()
    attempt.current = { fingerprint, key }
    setBusy(true)
    setNotice(null)
    let outcome: CommandOutcome<null>
    try {
      outcome = await send({ companyId, idempotencyKey: key, contract: contractContext })
    } finally {
      setBusy(false)
    }
    switch (outcome.type) {
      case 'applied':
        attempt.current = null
        setNotice({ tone: 'ok', text: okText })
        setFormKey((value) => value + 1)
        refresh()
        return true
      case 'http': {
        attempt.current = null
        setNotice({ tone: 'error', text: refusalText(outcome.status, outcome.body) })
        const code = taxProblemCodeOf(outcome.body)
        if (code !== null && REFETCH_CODES.has(code)) refresh()
        return false
      }
      case 'contract-not-ready':
        attempt.current = null
        setNotice({ tone: 'error', text: T.contractNotReady })
        return false
      case 'unfenced':
        attempt.current = null
        setNotice({ tone: 'error', text: T.genericRefusal })
        return false
      default:
        // Unknown: the command may have been applied. Same key, same body, only on an explicit retry.
        setNotice({
          tone: 'error',
          text: T.unknownOutcome,
          retry: () => void run(fingerprint, send, okText),
        })
        return false
    }
  }

  const outcome = query.data
  if (query.isPending) {
    return (
      <div className={styles.panel} data-testid="cari-tax-identity" data-state="loading">
        <h3 className={styles.title}>{T.heading}</h3>
        <p className={styles.muted}>{T.loading}</p>
      </div>
    )
  }
  if (outcome === undefined || outcome.type !== 'ok') {
    return (
      <div className={styles.panel} data-testid="cari-tax-identity" data-state="error">
        <h3 className={styles.title}>{T.heading}</h3>
        <p className={styles.error} role="alert">
          {outcome?.type === 'http' && outcome.status === 403 ? T.forbidden : T.loadFailed}
        </p>
        <div className={styles.actions}>
          <button type="button" className={styles.button} onClick={() => void query.refetch()}>
            {T.retry}
          </button>
        </div>
      </div>
    )
  }

  const view = outcome.data
  const pending = view.pendingChange
  return (
    <div className={styles.panel} data-testid="cari-tax-identity" data-state="ready">
      <h3 className={styles.title}>{T.heading}</h3>
      <p>
        <span className={styles.muted}>{T.factLabel}: </span>
        {view.taxIdentityNumber === null || view.kind === null ? (
          <span data-tax-identity="none">{T.none}</span>
        ) : (
          <>
            <span className={styles.value} data-tax-identity={view.taxIdentityNumber}>
              {view.taxIdentityNumber}
            </span>{' '}
            <span className={styles.muted}>({KIND_LABEL[view.kind]})</span>
          </>
        )}
      </p>

      {view.duplicates.length > 0 ? (
        <DuplicateWarning title={T.duplicateTitle} duplicates={view.duplicates} state="tax-identity-duplicate" />
      ) : null}

      {view.lastDecidedChange !== null && pending === null ? <LastDecision change={view.lastDecidedChange} /> : null}

      {pending !== null ? (
        <PendingChange
          key={`${pending.id}:${formKey}`}
          view={view}
          change={pending}
          busy={busy}
          onDecide={(decision, note) =>
            run(
              `decide:${pending.id}:${decision}:${note ?? ''}:${view.version}`,
              (context) => decideTaxIdentityChange(context, partyId, view.version, pending.id, decision, note),
              decision === 'approve' ? T.approved : T.rejected,
            )
          }
        />
      ) : null}

      {view.viewer.canUpdate && pending === null ? (
        view.taxIdentityNumber === null ? (
          <NumberForm
            key={`enter:${formKey}`}
            mode="enter"
            busy={busy}
            partyId={partyId}
            companyId={companyId}
            onSend={(number) =>
              run(
                `enter:${number}:${view.version}`,
                (context) => enterTaxIdentity(context, partyId, view.version, number),
                T.saved,
              )
            }
          />
        ) : (
          <ChangeRequest
            key={`request:${formKey}`}
            busy={busy}
            partyId={partyId}
            companyId={companyId}
            onSend={(number, reason) =>
              run(
                `request:${number}:${reason}:${view.version}`,
                (context) => requestTaxIdentityChange(context, partyId, view.version, number, reason),
                T.requested,
              )
            }
          />
        )
      ) : null}

      {notice !== null ? (
        <div role={notice.tone === 'error' ? 'alert' : 'status'} data-state={`tax-identity-notice-${notice.tone}`}>
          <p className={notice.tone === 'error' ? styles.error : styles.ok}>{notice.text}</p>
          {notice.retry !== undefined ? (
            <div className={styles.actions}>
              <button type="button" className={styles.button} disabled={busy} onClick={notice.retry}>
                {T.retrySame}
              </button>
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  )
}

function DuplicateWarning({
  title,
  duplicates,
  state,
}: {
  readonly title: string
  readonly duplicates: readonly TaxDuplicate[]
  readonly state: string
}) {
  return (
    <div className={styles.warning} role="status" data-state={state}>
      <p>
        <strong>{title}</strong>
      </p>
      <p>{T.duplicateDetail}</p>
      <ul>
        {duplicates.map((duplicate) => (
          <li key={duplicate.partyId}>
            <Link to={`/cari/${encodeURIComponent(duplicate.partyId)}`}>
              {duplicate.code} — {duplicate.legalName}
            </Link>{' '}
            {duplicate.accountIsActive ? null : <span>{T.inactiveAccount}</span>}
          </li>
        ))}
      </ul>
    </div>
  )
}

function LastDecision({ change }: { readonly change: TaxChange }) {
  return (
    <p className={styles.muted} data-state={`tax-identity-last-${change.status}`}>
      {change.status === 'approved' ? T.lastDecisionApproved : T.lastDecisionRejected}:{' '}
      {change.currentTaxIdentityNumber} → {change.proposedTaxIdentityNumber}
      {change.decisionNote !== null ? ` (${change.decisionNote})` : ''}
    </p>
  )
}

function PendingChange({
  view,
  change,
  busy,
  onDecide,
}: {
  readonly view: TaxIdentityView
  readonly change: TaxChange
  readonly busy: boolean
  readonly onDecide: (decision: 'approve' | 'reject', note: string | null) => Promise<boolean>
}) {
  const [note, setNote] = useState('')
  const noteId = `tax-identity-note-${change.id}`
  const mayDecide = view.viewer.canApprove && !view.viewer.isRequesterOfPending
  const trimmed = note.trim()
  return (
    <div className={styles.pending} data-state="tax-identity-pending">
      <strong>{T.pendingTitle}</strong>
      <dl>
        <dt>{T.pendingCurrent}</dt>
        <dd>{change.currentTaxIdentityNumber}</dd>
        <dt>{T.pendingProposed}</dt>
        <dd data-proposed-tax-identity={change.proposedTaxIdentityNumber}>
          {change.proposedTaxIdentityNumber} ({KIND_LABEL[change.proposedKind]})
        </dd>
        <dt>{T.pendingReason}</dt>
        <dd>{change.reason}</dd>
        <dt>{T.pendingRequestedAt}</dt>
        <dd>{change.requestedAtUtc}</dd>
      </dl>
      {change.proposedDuplicates.length > 0 ? (
        <DuplicateWarning
          title={T.proposedDuplicateTitle}
          duplicates={change.proposedDuplicates}
          state="tax-identity-proposed-duplicate"
        />
      ) : null}
      {view.viewer.isRequesterOfPending ? (
        <p className={styles.muted} data-state="tax-identity-own-request">
          {T.pendingBySelf}
        </p>
      ) : mayDecide ? (
        <>
          <p className={styles.muted}>{T.pendingByOther}</p>
          <div className={styles.form}>
            <label htmlFor={noteId}>{T.noteLabel}</label>
            <textarea id={noteId} value={note} maxLength={1000} rows={2} onChange={(e) => setNote(e.target.value)} />
          </div>
          <div className={styles.actions}>
            <button
              type="button"
              className={styles.button}
              disabled={busy}
              onClick={() => void onDecide('approve', trimmed.length > 0 ? trimmed : null)}
            >
              {T.approve}
            </button>
            <button
              type="button"
              className={styles.button}
              disabled={busy}
              onClick={() => void onDecide('reject', trimmed.length > 0 ? trimmed : null)}
            >
              {T.reject}
            </button>
          </div>
        </>
      ) : (
        <p className={styles.muted}>{T.pendingNoApprover}</p>
      )}
    </div>
  )
}

/**
 * The number field with the client check and the duplicate warning in front of the send. A warning never blocks: it
 * asks for one more, explicit click. A failed look-up is said out loud and asks for the same click.
 */
function useCheckedNumber(partyId: string, companyId: string) {
  const [text, setText] = useState('')
  const [fieldError, setFieldError] = useState<string | null>(null)
  const [warning, setWarning] = useState<{
    readonly number: string
    readonly duplicates: readonly TaxDuplicate[] | null
  } | null>(null)
  const [checking, setChecking] = useState(false)

  /** Returns the number when it may be sent now, or null when the user must act first. */
  const prepare = async (): Promise<string | null> => {
    const check = checkTaxIdentity(text)
    if (!check.ok) {
      setFieldError(check.message)
      setWarning(null)
      return null
    }
    setFieldError(null)
    if (warning !== null && warning.number === check.value) return check.value
    setChecking(true)
    const outcome = await checkTaxIdentityDuplicates(partyId, companyId, check.value).finally(() => setChecking(false))
    if (outcome.type === 'ok') {
      if (outcome.data.duplicates.length === 0) return check.value
      setWarning({ number: check.value, duplicates: outcome.data.duplicates })
      return null
    }
    if (outcome.type === 'http' && outcome.code === TAX_PROBLEM_CODE.invalid) {
      setFieldError(T.refusal[TAX_PROBLEM_CODE.invalid])
      return null
    }
    setWarning({ number: check.value, duplicates: null })
    return null
  }

  const onChange = (value: string) => {
    setText(value)
    setFieldError(null)
    setWarning(null)
  }

  return { text, onChange, fieldError, warning, checking, prepare }
}

function NumberForm({
  mode,
  busy,
  partyId,
  companyId,
  onSend,
}: {
  readonly mode: 'enter'
  readonly busy: boolean
  readonly partyId: string
  readonly companyId: string
  readonly onSend: (number: string) => Promise<boolean>
}) {
  const field = useCheckedNumber(partyId, companyId)
  const inputId = `tax-identity-${mode}-${partyId}`
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const number = await field.prepare()
    if (number !== null) await onSend(number)
  }
  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} noValidate>
      <label htmlFor={inputId}>{T.inputLabel}</label>
      <input
        id={inputId}
        inputMode="numeric"
        autoComplete="off"
        maxLength={32}
        value={field.text}
        aria-invalid={field.fieldError !== null}
        aria-describedby={field.fieldError !== null ? `${inputId}-error` : undefined}
        onChange={(e) => field.onChange(e.target.value)}
      />
      {field.fieldError !== null ? (
        <p id={`${inputId}-error`} className={styles.error} role="alert">
          {field.fieldError}
        </p>
      ) : null}
      <CheckWarning warning={field.warning} />
      <div className={styles.actions}>
        <button type="submit" className={styles.button} disabled={busy || field.checking}>
          {field.checking ? T.checking : busy ? T.sending : field.warning !== null ? T.saveAnyway : T.save}
        </button>
      </div>
    </form>
  )
}

function ChangeRequest({
  busy,
  partyId,
  companyId,
  onSend,
}: {
  readonly busy: boolean
  readonly partyId: string
  readonly companyId: string
  readonly onSend: (number: string, reason: string) => Promise<boolean>
}) {
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState('')
  const [reasonError, setReasonError] = useState<string | null>(null)
  const field = useCheckedNumber(partyId, companyId)
  const inputId = `tax-identity-request-${partyId}`
  const reasonId = `${inputId}-reason`
  if (!open) {
    return (
      <div className={styles.actions}>
        <p className={styles.muted}>{T.changeNote}</p>
        <button type="button" className={styles.button} onClick={() => setOpen(true)}>
          {T.requestChange}
        </button>
      </div>
    )
  }
  const submit = async (event: FormEvent) => {
    event.preventDefault()
    const trimmed = reason.trim()
    if (trimmed.length === 0 || trimmed.length > 1000) {
      setReasonError(T.reasonRequired)
      return
    }
    setReasonError(null)
    const number = await field.prepare()
    if (number !== null) await onSend(number, trimmed)
  }
  return (
    <form className={styles.form} onSubmit={(event) => void submit(event)} noValidate>
      <p className={styles.muted}>{T.changeNote}</p>
      <label htmlFor={inputId}>{T.proposedLabel}</label>
      <input
        id={inputId}
        inputMode="numeric"
        autoComplete="off"
        maxLength={32}
        value={field.text}
        aria-invalid={field.fieldError !== null}
        onChange={(e) => field.onChange(e.target.value)}
      />
      {field.fieldError !== null ? (
        <p className={styles.error} role="alert">
          {field.fieldError}
        </p>
      ) : null}
      <label htmlFor={reasonId}>{T.reasonLabel}</label>
      <textarea
        id={reasonId}
        value={reason}
        rows={2}
        maxLength={1000}
        aria-invalid={reasonError !== null}
        onChange={(e) => {
          setReason(e.target.value)
          setReasonError(null)
        }}
      />
      {reasonError !== null ? (
        <p className={styles.error} role="alert">
          {reasonError}
        </p>
      ) : null}
      <CheckWarning warning={field.warning} />
      <div className={styles.actions}>
        <button type="submit" className={styles.button} disabled={busy || field.checking}>
          {field.checking ? T.checking : busy ? T.sending : field.warning !== null ? T.sendAnyway : T.sendRequest}
        </button>
        <button type="button" className={styles.button} disabled={busy} onClick={() => setOpen(false)}>
          {T.cancel}
        </button>
      </div>
    </form>
  )
}

function CheckWarning({
  warning,
}: {
  readonly warning: { readonly number: string; readonly duplicates: readonly TaxDuplicate[] | null } | null
}) {
  if (warning === null) return null
  if (warning.duplicates === null) {
    return (
      <div className={styles.warning} role="status" data-state="tax-identity-check-failed">
        <p>{CHECK_FAILED}</p>
      </div>
    )
  }
  return (
    <DuplicateWarning title={T.duplicateTitle} duplicates={warning.duplicates} state="tax-identity-duplicate-before-save" />
  )
}

const CHECK_FAILED =
  'Mükerrer kontrolü yapılamadı. Numarayı yine de kaydedebilirsiniz; kayıttan sonra kart uyarıyı gösterir.'
